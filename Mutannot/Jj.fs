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
