namespace Mutannot

open System
open System.IO
open Fli

module Git =
    // The absolute path of the working tree's root that contains `directory`. Patch
    // paths (a/..., b/...) are relative to it, so anything invoking `git apply` has
    // to resolve against and run from here.
    let root (directory: string) =
        (cli {
            Exec "git"
            Arguments [ "rev-parse"; "--show-toplevel" ]
            WorkingDirectory directory
         }
         |> Command.execute
         |> Output.throwIfErrored
         |> Output.toText)
            .Trim()

    // Runs `git apply <extraArgs> -` from the root, with the patch on stdin. The
    // root is fixed because patch paths (a/..., b/...) are relative to it.
    let apply (gitRoot: string) (extraArgs: string list) (patch: string) =
        cli {
            Exec "git"
            Arguments([ "apply" ] @ extraArgs @ [ "-" ])
            WorkingDirectory gitRoot
            // Fli uses `WriteLine` to write to stdin, which means that the patch
            // is suffixed with `\r\n` on Windows, which `git apply` considers to
            // be part of the patch, which causes it to break.
            //
            // Inserting a newline to "terminate" the patch right before that
            // `\r\n` seems to prevent that problem.
            Input $"{patch}\n"
        }
        |> Command.execute

    // The C#/F# source files under `directory` that validate should scan, as
    // absolute paths. `--cached` lists tracked files and `--others` untracked ones,
    // so newly created files are candidates too; but `--cached` is *not* implied
    // once `--others` is given, hence both are named. `--exclude-standard` makes the
    // untracked half honor .gitignore, so ignored build output stays out. ls-files
    // reports paths relative to its working directory, so they are resolved back
    // against `directory`; and `--cached` can list a file that has been deleted from
    // disk but not yet staged as removed, so those are dropped here.
    let sourceFiles (directory: string) =
        (cli {
            Exec "git"

            Arguments
                [ "ls-files"
                  "--cached"
                  "--others"
                  "--exclude-standard"
                  "--"
                  "*.cs"
                  "*.fs" ]

            WorkingDirectory directory
         }
         |> Command.execute
         |> Output.throwIfErrored
         |> Output.toText)
            .Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
        |> Array.filter File.Exists
        |> Array.toList

    // The C#/F# source files that differ between `baseBranch` and the working tree,
    // as paths relative to `gitRoot` exactly as git reports them (forward slashes,
    // repository-root-relative -- the form `git show base:path` also expects). Both
    // added and modified files appear; deleted files are dropped since they hold no
    // current patches to run. Used by `run --only-new-or-updated-since` to look at
    // just the files a branch touched.
    let changedSourceFiles (gitRoot: string) (baseBranch: string) =
        (cli {
            Exec "git"
            Arguments [ "diff"; "--name-only"; baseBranch; "--"; "*.cs"; "*.fs" ]
            WorkingDirectory gitRoot
         }
         |> Command.execute
         |> Output.throwIfErrored
         |> Output.toText)
            .Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        // A modified-then-deleted file still shows in the diff but no longer exists
        // on disk, so it carries no working-tree patches; drop it.
        |> Array.filter (fun relativePath -> File.Exists(Path.Combine(gitRoot, relativePath)))
        |> Array.toList

    // The contents of `relativePath` as committed at `baseBranch`, read with
    // `git show base:path` so the base version never has to be checked out. Returns
    // None when the file does not exist at the base (a file the branch newly added),
    // in which case all of its current patches count as new.
    let showAtBase (gitRoot: string) (baseBranch: string) (relativePath: string) =
        let output =
            cli {
                Exec "git"
                Arguments [ "show"; $"{baseBranch}:{relativePath}" ]
                WorkingDirectory gitRoot
            }
            |> Command.execute

        if Output.toExitCode output = 0 then
            Some(Output.toText output)
        else
            None
