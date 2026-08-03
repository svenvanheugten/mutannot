namespace Mutannot

open System
open System.IO
open System.Text.RegularExpressions
open Fli

module PatchValidator =
    // The two languages whose triple-quoted string literals trim differently (see
    // extractPatches). Determined from the source file's extension.
    type Language =
        | CSharp
        | FSharp

    // ShouldCatch patches are embedded as string literals, in one of two shapes: a
    // triple-quoted literal (C# raw string / F# verbatim triple-quoted) or a verbatim
    // string (@"..."). We extract them purely with a regex so validation stays fast and
    // never needs a dotnet build: the point of `validate` is a quick "do these patches
    // still apply?" check. The alternation captures each shape into its own group so
    // extractPatches can reproduce how each language's compiler interprets it.
    //
    // The raw body is non-greedy so each match stops at its own closing """. The
    // verbatim body matches a doubled quote ("") -- verbatim strings' sole escape for a
    // literal quote -- and otherwise any non-quote char, so it stops at the first lone
    // quote that closes the literal. Singleline so '.' and [^"] span the newlines a
    // multi-line patch contains.
    let private shouldCatchPattern =
        Regex(
            @"ShouldCatch\s*\(\s*(?:""""""(?<raw>.*?)""""""|@""(?<verbatim>(?:[^""]|"""")*)"")",
            RegexOptions.Singleline ||| RegexOptions.Compiled
        )

    // The regex above captures the string literals exactly as they appear in the file.
    //
    // The `run` command, however, uses reflection to read these strings, which means
    // that it follows the language's own rules for interpreting string literals
    // instead.
    //
    // For triple-quoted literals, C#
    // "The newline before the closing quotes isn't included in the literal string."
    // (from https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/raw-string)
    // In F#, that isn't the case.
    //
    // A verbatim string (@"...") is simpler: neither language trims or dedents it, and
    // its only escape is a doubled quote ("") standing for a single quote -- so the same
    // handling works for both languages.
    //
    // We want `validate` to check exactly the same patches as `run`, so we need to mirror
    // the language's own behaviour here.
    let extractPatches (language: Language) (sourceText: string) =
        shouldCatchPattern.Matches sourceText
        |> Seq.map (fun m ->
            let rawGroup = m.Groups["raw"]

            let body =
                if rawGroup.Success then
                    match language with
                    | CSharp -> Regex.Replace(rawGroup.Value, @"\n[ \t]*$", "")
                    | FSharp -> rawGroup.Value
                else
                    m.Groups["verbatim"].Value.Replace("\"\"", "\"")

            Runner.unindentPatch body)
        |> Seq.toList

    let private languageOf (path: string) =
        match Path.GetExtension path with
        | ".fs" -> FSharp
        | ".cs" -> CSharp
        | ext -> failwithf "Unsupported file extension '%s' for file '%s'." ext path

    // The ShouldCatch patches that are new or updated in the working tree relative to
    // `baseBranch`: for every source file that differs from the base, the patches its
    // current version declares that its base version did not. Used by
    // `run --only-new-or-updated-since` to narrow mutation testing to the patches a
    // branch actually adds or changes. Reuses the same extractPatches logic as
    // `validate`, so a patch counts as "the same" exactly when both commands would
    // treat it identically. The base version of each file is read through the VCS
    // (see Vcs.showAtBase) so it never has to be checked out, which keeps this working
    // for both git and jj repositories.
    let internal newOrUpdatedPatches (gitRoot: string) (baseBranch: string) =
        Vcs.changedSourceFiles gitRoot baseBranch
        |> List.collect (fun relativePath ->
            let language = languageOf relativePath

            let currentPatches =
                extractPatches language (File.ReadAllText(Path.Combine(gitRoot, relativePath)))

            let basePatches =
                match Vcs.showAtBase gitRoot baseBranch relativePath with
                | Some baseText -> extractPatches language baseText
                | None -> []

            currentPatches
            |> List.filter (fun patch -> not (List.contains patch basePatches)))
        |> Set.ofList

    // `git apply --check` reports whether the patch would apply to the working tree
    // without touching any files. Returns None on success, or Some error text
    // describing why it doesn't apply.
    let private checkPatch (gitRoot: string) (patch: string) =
        let output = Vcs.apply gitRoot [ "--check" ] patch

        if Output.toExitCode output = 0 then
            None
        else
            Some(Output.toError output)

    // Checks every patch extracted from one source file, printing each patch and
    // whether it applies. Returns true if any patch in the file failed to apply.
    let private validateFile (gitRoot: string) (sourceFilePath: string) (patches: string list) =
        Console.ForegroundColor <- ConsoleColor.Cyan
        printf "%s\n" sourceFilePath
        Console.ResetColor()

        patches
        |> List.indexed
        |> List.fold
            (fun anyInvalid (index, patch) ->
                Console.ForegroundColor <- ConsoleColor.Green
                printf $"PATCH {index + 1}\n"

                Console.ForegroundColor <- ConsoleColor.Magenta
                printf "Patch:\n"
                Console.ResetColor()
                printf "%s\n" patch

                match checkPatch gitRoot patch with
                | None ->
                    Console.ForegroundColor <- ConsoleColor.Green
                    printf "✓ Applies cleanly\n\n"
                    Console.ResetColor()
                    anyInvalid
                | Some error ->
                    Console.ForegroundColor <- ConsoleColor.Red
                    printf "✗ Does not apply\n"
                    eprintf "%s\n" (error.TrimEnd())
                    Console.ResetColor()
                    printf "\n"
                    true)
            false

    // `path` is either a single source file or a directory to scan for C#/F# source
    // files (see Vcs.sourceFiles).
    let internal validate (path: string) =
        let fullPath = Path.GetFullPath path

        let sourceFiles =
            if Directory.Exists fullPath then
                Vcs.sourceFiles fullPath
            else
                [ fullPath ]

        let filesWithPatches =
            sourceFiles
            |> List.map (fun file -> file, extractPatches (languageOf file) (File.ReadAllText file))
            |> List.filter (snd >> List.isEmpty >> not)

        if List.isEmpty filesWithPatches then
            printfn "No ShouldCatch attributes found in '%s'." path
            0
        else
            let gitRoot =
                Vcs.root (
                    if Directory.Exists fullPath then
                        fullPath
                    else
                        Path.GetDirectoryName fullPath
                )

            let anyInvalid =
                filesWithPatches
                |> List.fold (fun anyInvalid (file, patches) -> validateFile gitRoot file patches || anyInvalid) false

            if anyInvalid then
                Console.ForegroundColor <- ConsoleColor.Red
                eprintf "ERROR: Some patches do not apply\n"
                Console.ResetColor()
                3
            else
                Console.ForegroundColor <- ConsoleColor.Green
                printf "Success: All patches apply\n"
                Console.ResetColor()
                0
