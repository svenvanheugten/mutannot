namespace Mutannot

open System
open System.IO
open System.Xml.Linq
open Fli

module Mutator =
    // Resolve an Include attribute value (authored relative to the project dir,
    // possibly with Windows-style '\' separators) to an absolute path. The '\'
    // must be normalized to '/' first: on non-Windows platforms neither
    // Path.Combine nor Path.GetFullPath treats '\' as a separator, so such a
    // path never matches the real file on disk. '/' is understood by every
    // platform's Path APIs, so this keeps ownership matching working regardless
    // of how the project was authored.
    let private includeAbsPath (dir: string) (includeValue: string) =
        Path.GetFullPath(Path.Combine(dir, includeValue.Replace('\\', '/')))

    let private getPatchedRelativePaths (patch: string) =
        patch.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
        |> Array.choose (fun line ->
            if line.StartsWith("--- a/") then
                Some(line.Substring(6).Trim())
            else
                None)
        |> Array.toList

    // Mutated project files stay next to the originals but carry a
    // .<segment>.mutated suffix. The segment keeps concurrent `run --jobs` workers
    // from clobbering one another's mutated project files (each owns a segment).
    let private toMutatedProjectPath (segment: int) (path: string) =
        let dir = Path.GetDirectoryName path
        let name = Path.GetFileNameWithoutExtension path
        let ext = Path.GetExtension path
        Path.Combine(dir, $"{name}.{segment}.mutated{ext}")

    // Mutated source files live under .mutannot/<segment>/ at the git root so they
    // never land inside a project directory and can't be accidentally picked up by
    // SDK implicit globs or other tooling. The per-segment subdirectory isolates
    // concurrent `run --jobs` workers, which each own a segment, so their mutated
    // sources never collide. The path is built with '/' rather than Path.Combine:
    // this path goes straight into the patch we hand to `git apply`, which rejects
    // a path that mixes separators, and on Windows Path.Combine would prefix a
    // backslash onto the forward slashes that the rest of the path inherits from
    // the git patch.
    let private toMutatedSourceRelPath (segment: int) (relPath: string) = $".mutannot/{segment}/" + relPath

    let private toMutatedSourceAbsPath (gitRoot: string) (segment: int) (absPath: string) =
        Path.Combine(gitRoot, ".mutannot", string segment, Path.GetRelativePath(gitRoot, absPath))

    let private rewritePatchForMutated (segment: int) (patchedRelPaths: string list) (patch: string) =
        patchedRelPaths
        |> List.fold
            (fun (acc: string) relPath ->
                let mutated = toMutatedSourceRelPath segment relPath
                acc.Replace($"--- a/{relPath}", $"--- a/{mutated}").Replace($"+++ b/{relPath}", $"+++ b/{mutated}"))
            patch

    let private applyPatch (gitRoot: string) (patch: string) =
        Git.apply gitRoot [] patch |> Output.throwIfErrored |> ignore

    type private ProjectKind =
        | FSharp
        | CSharp

    type private ProjectInfo =
        { AbsolutePath: string
          Kind: ProjectKind
          OwnsFile: string -> bool
          ProjectRefs: string list }

    let private parseProject (absolutePath: string) : ProjectInfo =
        let dir = Path.GetDirectoryName absolutePath
        let doc = XDocument.Load absolutePath

        let kind =
            match Path.GetExtension absolutePath with
            | ".fsproj" -> FSharp
            | ".csproj" -> CSharp
            | ext -> failwith $"Unsupported project extension '{ext}': {absolutePath}"

        let getIncludes elementName =
            doc.Descendants(XName.Get elementName)
            |> Seq.choose (fun e ->
                match e.Attribute(XName.Get "Include") with
                | null -> None
                | attr -> Some(includeAbsPath dir attr.Value))
            |> Seq.toList

        let ownsFile =
            match kind with
            | FSharp ->
                let sources = getIncludes "Compile"
                fun filePath -> List.contains filePath sources
            | CSharp ->
                // C# projects use an implicit glob; ownership is directory containment.
                let sep = Path.DirectorySeparatorChar.ToString()

                fun filePath ->
                    Path.GetExtension filePath = ".cs"
                    && filePath.StartsWith(dir + sep)
                    && not (filePath.Contains(sep + "obj" + sep))
                    && not (filePath.Contains(sep + "bin" + sep))

        { AbsolutePath = absolutePath
          Kind = kind
          OwnsFile = ownsFile
          ProjectRefs = getIncludes "ProjectReference" }

    let private collectProjectTree (testProjectPath: string) : ProjectInfo list =
        let rec collect (path: string) =
            let info = parseProject path
            (info.ProjectRefs |> List.collect collect) @ [ info ]

        collect testProjectPath |> List.distinctBy _.AbsolutePath

    let private findProjectsNeedingMutation (testProjectPath: string) (patchedFiles: Set<string>) : ProjectInfo list =
        let allProjects = collectProjectTree testProjectPath

        let rec propagate (mutSet: Set<string>) =
            let newMutSet =
                allProjects
                |> List.fold
                    (fun acc p ->
                        if Set.contains p.AbsolutePath acc then
                            acc
                        elif patchedFiles |> Set.exists p.OwnsFile then
                            Set.add p.AbsolutePath acc
                        elif p.ProjectRefs |> List.exists (fun r -> Set.contains r acc) then
                            Set.add p.AbsolutePath acc
                        else
                            acc)
                    mutSet

            if newMutSet = mutSet then mutSet else propagate newMutSet

        let mutSet = propagate Set.empty
        allProjects |> List.filter (fun p -> Set.contains p.AbsolutePath mutSet)

    let private createMutatedProject
        (segment: int)
        (projectInfo: ProjectInfo)
        (mutatedSourceMap: Map<string, string>)
        (mutatedProjectMap: Map<string, string>)
        =
        let dir = Path.GetDirectoryName projectInfo.AbsolutePath
        let doc = XDocument.Load projectInfo.AbsolutePath

        let updateIncludes elementName lookupMap =
            for element in doc.Descendants(XName.Get elementName) do
                match element.Attribute(XName.Get "Include") with
                | null -> ()
                | attr ->
                    let absPath = includeAbsPath dir attr.Value

                    match Map.tryFind absPath lookupMap with
                    | None -> ()
                    | Some mutatedAbsPath -> attr.Value <- Path.GetRelativePath(dir, mutatedAbsPath)

        match projectInfo.Kind with
        | FSharp -> updateIncludes "Compile" mutatedSourceMap
        | CSharp ->
            // SDK-style C# projects glob *.cs implicitly, so inject a Remove/Include
            // pair for each patched file this project owns.
            let owned =
                mutatedSourceMap
                |> Map.toList
                |> List.filter (fun (orig, _) -> projectInfo.OwnsFile orig)

            if owned <> [] then
                let itemGroup = XElement(XName.Get "ItemGroup")

                for orig, mutated in owned do
                    itemGroup.Add(
                        XElement(XName.Get "Compile", XAttribute(XName.Get "Remove", Path.GetRelativePath(dir, orig)))
                    )

                    itemGroup.Add(
                        XElement(
                            XName.Get "Compile",
                            XAttribute(XName.Get "Include", Path.GetRelativePath(dir, mutated))
                        )
                    )

                doc.Root.Add itemGroup

        updateIncludes "ProjectReference" mutatedProjectMap

        let mutatedPath = toMutatedProjectPath segment projectInfo.AbsolutePath

        // The mutated project sits next to the original but, being named
        // X.mutated, would default to an assembly name of "X.mutated". Keep the
        // original assembly name: anything keyed on it otherwise breaks -- most
        // visibly [InternalsVisibleTo("X.Tests")], which stops granting access
        // once the mutated test assembly is named "X.Tests.mutated", so the
        // mutated build fails to compile. An explicit <AssemblyName> is already
        // carried over from the original; when there is none, the name defaults
        // to the file name, so add one pinned to the original project's name.
        // (The build output is kept from colliding with the original's by
        // redirecting it on the dotnet command line; see Runner.mutatedBuildArgs.)
        if doc.Descendants(XName.Get "AssemblyName") |> Seq.isEmpty then
            doc.Root.Add(
                XElement(
                    XName.Get "PropertyGroup",
                    XElement(XName.Get "AssemblyName", Path.GetFileNameWithoutExtension projectInfo.AbsolutePath)
                )
            )

        doc.Save mutatedPath

    // Returns the path to the mutated test project. `segment` names the .mutannot
    // subtree this mutation's sources, projects and build output land in, so
    // concurrent `run --jobs` workers (each with its own segment) never collide.
    let internal applyMutation (testProjectPath: string) (segment: int) (patch: string) : string =
        let gitRoot = Git.root (Path.GetDirectoryName testProjectPath)
        let patchedRelPaths = getPatchedRelativePaths patch

        let patchedAbsPaths =
            patchedRelPaths
            |> List.map (fun p -> Path.GetFullPath(Path.Combine(gitRoot, p)))
            |> Set.ofList

        let projectsToMutate = findProjectsNeedingMutation testProjectPath patchedAbsPaths

        let mutatedSourceMap =
            patchedAbsPaths
            |> Set.toSeq
            |> Seq.map (fun p -> p, toMutatedSourceAbsPath gitRoot segment p)
            |> Map.ofSeq

        let mutatedProjectMap =
            projectsToMutate
            |> List.map (fun p -> p.AbsolutePath, toMutatedProjectPath segment p.AbsolutePath)
            |> Map.ofList

        for KeyValue(origPath, mutatedPath) in mutatedSourceMap do
            Directory.CreateDirectory(Path.GetDirectoryName mutatedPath) |> ignore
            File.Copy(origPath, mutatedPath, overwrite = true)

        applyPatch gitRoot (rewritePatchForMutated segment patchedRelPaths patch)

        for project in projectsToMutate do
            createMutatedProject segment project mutatedSourceMap mutatedProjectMap

        toMutatedProjectPath segment testProjectPath
