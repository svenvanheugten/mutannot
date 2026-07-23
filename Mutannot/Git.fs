namespace Mutannot

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
