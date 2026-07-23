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

    let private getGitRoot () =
        (cli {
            Exec "git"
            Arguments [ "rev-parse"; "--show-toplevel" ]
         }
         |> Command.execute
         |> Output.toText)
            .Trim()

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

    let internal validate (sourceFilePath: string) =
        let sourceText = File.ReadAllText(Path.GetFullPath sourceFilePath)
        let patches = extractPatches sourceText

        if List.isEmpty patches then
            printfn "No ShouldCatch attributes found in '%s'." sourceFilePath
            0
        else
            let gitRoot = getGitRoot ()

            let anyInvalid =
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
