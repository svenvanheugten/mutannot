namespace Mutannot

open System
open System.IO
open System.Xml.Linq
open Fli

module Mutator =
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
            if line.StartsWith("--- a/") then Some(line.Substring(6).Trim()) else None)
        |> Array.toList

    let private toMutatedPath (path: string) =
        let dir = Path.GetDirectoryName path
        let name = Path.GetFileNameWithoutExtension path
        let ext = Path.GetExtension path
        Path.Combine(dir, $"{name}.mutated{ext}")

    let private rewritePatchForMutated (patchedRelPaths: string list) (patch: string) =
        patchedRelPaths
        |> List.fold
            (fun (acc: string) relPath ->
                let mutated = toMutatedPath relPath
                acc
                    .Replace($"--- a/{relPath}", $"--- a/{mutated}")
                    .Replace($"+++ b/{relPath}", $"+++ b/{mutated}"))
            patch

    let private applyPatch (patch: string) =
        cli {
            Exec "git"
            Arguments [ "apply"; "-" ]
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
                | attr -> Some(Path.GetFullPath(Path.Combine(dir, attr.Value))))
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
                        if Set.contains p.AbsolutePath acc then acc
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
                    let absPath = Path.GetFullPath(Path.Combine(dir, attr.Value))

                    match Map.tryFind absPath lookupMap with
                    | None -> ()
                    | Some mutatedAbsPath -> attr.Value <- Path.GetRelativePath(dir, mutatedAbsPath)

        match projectInfo.Kind with
        | FSharp -> updateIncludes "Compile" mutatedSourceMap
        | CSharp ->
            // SDK-style C# projects glob *.cs implicitly, so inject a Remove/Include
            // pair for each patched file this project owns.
            let owned =
                mutatedSourceMap |> Map.toList |> List.filter (fun (orig, _) -> projectInfo.OwnsFile orig)

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
        doc.Save(toMutatedPath projectInfo.AbsolutePath)

    // Returns the path to the mutated test project.
    let applyMutation (testProjectPath: string) (patch: string) : string =
        let gitRoot = getGitRoot ()
        let patchedRelPaths = getPatchedRelativePaths patch

        let patchedAbsPaths =
            patchedRelPaths
            |> List.map (fun p -> Path.GetFullPath(Path.Combine(gitRoot, p)))
            |> Set.ofList

        let projectsToMutate = findProjectsNeedingMutation testProjectPath patchedAbsPaths

        let mutatedSourceMap =
            patchedAbsPaths |> Set.toSeq |> Seq.map (fun p -> p, toMutatedPath p) |> Map.ofSeq

        let mutatedProjectMap =
            projectsToMutate
            |> List.map (fun p -> p.AbsolutePath, toMutatedPath p.AbsolutePath)
            |> Map.ofList

        for KeyValue(origPath, mutatedPath) in mutatedSourceMap do
            File.Copy(origPath, mutatedPath, overwrite = true)

        applyPatch (rewritePatchForMutated patchedRelPaths patch)

        for project in projectsToMutate do
            createMutatedProject project mutatedSourceMap mutatedProjectMap

        toMutatedPath testProjectPath
