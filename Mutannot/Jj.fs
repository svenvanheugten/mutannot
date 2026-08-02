namespace Mutannot

open System
open System.IO
open Fli

module Jj =
    // The absolute path of the jj workspace root that contains `directory`. Patch
    // paths (a/..., b/...) are relative to it, so anything invoking `git apply` has to
    // resolve against and run from here. A jj repository need not be co-located with
    // git -- it may carry no .git at all -- so `jj root` is what reports this.
    let root (directory: string) =
        (cli {
            Exec "jj"
            Arguments [ "root" ]
            WorkingDirectory directory
         }
         |> Command.execute
         |> Output.throwIfErrored
         |> Output.toText)
            .Trim()

    // The C#/F# source files under `directory` that validate should scan, as absolute
    // paths. `jj file list` reports the working copy's tracked files; jj auto-snapshots
    // the working copy, so newly created files are tracked too, and the listing honours
    // .gitignore, so ignored build output stays out. The trailing `.` scopes the
    // listing to `directory`, and paths come out relative to it, so they are resolved
    // back against `directory`. `jj file list` has no per-type filter, so the C#/F#
    // restriction is applied here.
    let sourceFiles (directory: string) =
        (cli {
            Exec "jj"
            Arguments [ "file"; "list"; "." ]
            WorkingDirectory directory
         }
         |> Command.execute
         |> Output.throwIfErrored
         |> Output.toText)
            .Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
        |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
        |> Array.filter File.Exists
        |> Array.toList

    // The C#/F# source files that differ between `baseRevision` and the working copy,
    // as paths relative to `root` exactly as jj reports them (forward slashes,
    // root-relative -- the form `jj file show -r base path` also expects). jj snapshots
    // the working copy, so with `--to` left to default to the working-copy commit,
    // `--from base` picks up uncommitted edits too. Both added and modified files
    // appear; deleted files are dropped since they hold no current patches to run.
    // `jj diff --name-only` has no per-type filter, so the C#/F# restriction is applied
    // here. Used by `run --only-new-or-updated-since` to look at just the files a branch
    // touched.
    let changedSourceFiles (root: string) (baseRevision: string) =
        (cli {
            Exec "jj"
            Arguments [ "diff"; "--from"; baseRevision; "--name-only" ]
            WorkingDirectory root
         }
         |> Command.execute
         |> Output.throwIfErrored
         |> Output.toText)
            .Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
        // A modified-then-deleted file still shows in the diff but no longer exists
        // on disk, so it carries no working-copy patches; drop it.
        |> Array.filter (fun relativePath -> File.Exists(Path.Combine(root, relativePath)))
        |> Array.toList

    // The contents of `relativePath` as committed at `baseRevision`, read with
    // `jj file show -r base path` so the base version never has to be checked out.
    // Returns None when the file does not exist at the base (a file the branch newly
    // added), in which case all of its current patches count as new.
    let showAtBase (root: string) (baseRevision: string) (relativePath: string) =
        let output =
            cli {
                Exec "jj"
                Arguments [ "file"; "show"; "-r"; baseRevision; relativePath ]
                WorkingDirectory root
            }
            |> Command.execute

        if Output.toExitCode output = 0 then
            Some(Output.toText output)
        else
            None
