open Fli

[<EntryPoint>]
let main argv =
    let gitState =
        cli {
            Exec "git"
            Arguments [ "status"; "--porcelain" ]
        }
        |> Command.execute
        |> Output.throwIfErrored

    if gitState.Text <> None then
        eprintfn "Uncommitted changes. Refusing to run."
        exit 1

    0
