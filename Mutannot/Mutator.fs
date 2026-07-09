namespace Mutannot

open System
open System.IO
open System.Xml.Linq
open Fli

module Mutator =
    // Include paths in project files may be authored with Windows-style
    // backslash separators. On non-Windows platforms neither Path.Combine nor
    // Path.GetFullPath treats '\' as a separator, so such a path never matches
    // the real file on disk: the owning project is left out of the mutation
    // set, no *.mutated project is written, and the run fails hard when dotnet
    // is pointed at the missing project (MSBUILD error MSB1009). Normalizing to
    // '/' (which every platform's Path APIs understand) keeps ownership
    // matching working regardless of how the project was authored.
    let private normalizeSeparators (path: string) = path.Replace('\\', '/')

    let private getGitRoot () =
        (cli {
            Exec "git"
            Arguments [ "rev-parse"; "--show-toplevel" ]
         }
         |> Command.execute
         |> Output.toText)
            .Trim()

    let private getPatchedRelativePaths (patch: string) =
        patch.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
        |> Array.choose (fun line ->
            if line.StartsWith("--- a/") then
                Some(line.Substring(6).Trim())
            else
                None)
        |> Array.toList

    // Mutated project files stay next to the originals with a .mutated suffix.
    let private toMutatedProjectPath (path: string) =
        let dir = Path.GetDirectoryName path
        let name = Path.GetFileNameWithoutExtension path
        let ext = Path.GetExtension path
        Path.Combine(dir, $"{name}.mutated{ext}")

    // Mutated source files live under .mutannot/ at the git root so they never
    // land inside a project directory and can't be accidentally picked up by
    // SDK implicit globs or other tooling. The path is built with '/' rather
    // than Path.Combine: this path goes straight into the patch we hand to
    // `git apply`, which rejects a path that mixes separators, and on Windows
    // Path.Combine would prefix a backslash onto the forward slashes that the
    // rest of the path inherits from the git patch.
    let private toMutatedSourceRelPath (relPath: string) =
        ".mutannot/" + normalizeSeparators relPath

    let private toMutatedSourceAbsPath (gitRoot: string) (absPath: string) =
        Path.Combine(gitRoot, ".mutannot", Path.GetRelativePath(gitRoot, absPath))

    let private rewritePatchForMutated (patchedRelPaths: string list) (patch: string) =
        patchedRelPaths
        |> List.fold
            (fun (acc: string) relPath ->
                let mutated = toMutatedSourceRelPath relPath
                acc.Replace($"--- a/{relPath}", $"--- a/{mutated}").Replace($"+++ b/{relPath}", $"+++ b/{mutated}"))
            patch

    let private applyPatch (gitRoot: string) (patch: string) =
        cli {
            Exec "git"
            Arguments [ "apply"; "-" ]
            WorkingDirectory gitRoot
            Input patch
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

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
                | attr -> Some(Path.GetFullPath(Path.Combine(dir, normalizeSeparators attr.Value))))
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
                    let absPath = Path.GetFullPath(Path.Combine(dir, normalizeSeparators attr.Value))

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
        doc.Save(toMutatedProjectPath projectInfo.AbsolutePath)

    // Returns the path to the mutated test project.
    let applyMutation (testProjectPath: string) (patch: string) : string =
        let gitRoot = getGitRoot ()
        // A patch may use either separator in its paths (a git diff produced on
        // Windows still uses '/', but a hand-written ShouldCatch might not).
        // Normalize before resolving them against the file system; the patch
        // text is rewritten below using its original paths so the string
        // replacement still lands.
        let rawRelPaths = getPatchedRelativePaths patch
        let patchedRelPaths = rawRelPaths |> List.map normalizeSeparators

        let patchedAbsPaths =
            patchedRelPaths
            |> List.map (fun p -> Path.GetFullPath(Path.Combine(gitRoot, p)))
            |> Set.ofList

        let projectsToMutate = findProjectsNeedingMutation testProjectPath patchedAbsPaths

        let mutatedSourceMap =
            patchedAbsPaths
            |> Set.toSeq
            |> Seq.map (fun p -> p, toMutatedSourceAbsPath gitRoot p)
            |> Map.ofSeq

        let mutatedProjectMap =
            projectsToMutate
            |> List.map (fun p -> p.AbsolutePath, toMutatedProjectPath p.AbsolutePath)
            |> Map.ofList

        for KeyValue(origPath, mutatedPath) in mutatedSourceMap do
            Directory.CreateDirectory(Path.GetDirectoryName mutatedPath) |> ignore
            File.Copy(origPath, mutatedPath, overwrite = true)

        applyPatch gitRoot (rewritePatchForMutated rawRelPaths patch)

        for project in projectsToMutate do
            createMutatedProject project mutatedSourceMap mutatedProjectMap

        toMutatedProjectPath testProjectPath
