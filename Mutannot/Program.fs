open System
open System.IO
open Fli

[<EntryPoint>]
let main argv =
    if argv.Length <> 1 then
        eprintfn "Usage: mutannot <path/to/project.csproj|fsproj>"
        exit 1

    let gitState =
        cli {
            Exec "git"
            Arguments [ "status"; "--porcelain" ]
        }
        |> Command.execute
        |> Output.throwIfErrored

    if gitState.Text <> None then
        eprintfn "Uncommitted changes. Refusing to run."
        exit 2

    cli {
        Exec "dotnet"
        Arguments [ "build"; argv[0] ]
        Output(new StreamWriter(Console.OpenStandardOutput()))
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

    0
