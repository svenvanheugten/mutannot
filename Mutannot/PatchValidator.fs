namespace Mutannot

open System
open System.IO
open System.Text.RegularExpressions
open Fli

module PatchValidator =
    // ShouldCatch patches are embedded as triple-quoted string literals in both C#
    // (raw string) and F# (verbatim triple-quoted) source. We extract them purely
    // with a regex so validation stays fast and never needs a dotnet build: the
    // point of `validate` is a quick "do these patches still apply?" check.
    // Non-greedy so each match stops at its own closing """, and Singleline so '.'
    // spans the newlines a multi-line patch contains.
    let private shouldCatchPattern =
        Regex(@"ShouldCatch\s*\(\s*""""""(?<patch>.*?)""""""", RegexOptions.Singleline ||| RegexOptions.Compiled)

    let extractPatches (sourceText: string) =
        shouldCatchPattern.Matches sourceText
        |> Seq.map (fun m -> m.Groups["patch"].Value |> Runner.unindentPatch)
        |> Seq.toList

    // `git apply --check` reports whether the patch would apply to the working tree
    // without touching any files. The patch paths (a/..., b/...) are relative to the
    // git root, so it has to run from there. Returns None on success, or Some error
    // text describing why it doesn't apply.
    let private checkPatch (gitRoot: string) (patch: string) =
        let output =
            cli {
                Exec "git"
                Arguments [ "apply"; "--check"; "-" ]
                WorkingDirectory gitRoot
                Input patch
            }
            |> Command.execute

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
    // files (see Git.sourceFiles).
    let internal validate (path: string) =
        let fullPath = Path.GetFullPath path

        let sourceFiles =
            if Directory.Exists fullPath then
                Git.sourceFiles fullPath
            else
                [ fullPath ]

        let filesWithPatches =
            sourceFiles
            |> List.map (fun file -> file, extractPatches (File.ReadAllText file))
            |> List.filter (snd >> List.isEmpty >> not)

        if List.isEmpty filesWithPatches then
            printfn "No ShouldCatch attributes found in '%s'." path
            0
        else
            let gitRoot = Git.root ()

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
