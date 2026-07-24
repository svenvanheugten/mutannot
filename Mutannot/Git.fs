namespace Mutannot

open System
open System.IO
open Fli

module Git =
    // The absolute path of the working tree's root. Patch paths (a/..., b/...) are
    // relative to it, so anything invoking `git apply` has to resolve against and
    // run from here.
    let root () =
        (cli {
            Exec "git"
            Arguments [ "rev-parse"; "--show-toplevel" ]
         }
         |> Command.execute
         |> Output.toText)
            .Trim()

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
         |> Output.toText)
            .Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
        |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
        |> Array.filter File.Exists
        |> Array.toList
