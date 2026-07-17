module Mutannot.IntegrationTests.TestSupport

open System
open System.IO
open Fli

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

// The mutator resolves patched paths against the git root (via `git rev-parse`),
// so the throwaway fixtures below have to live under it too.
let gitRoot =
    (cli {
        Exec "git"
        Arguments [ "rev-parse"; "--show-toplevel" ]
     }
     |> Command.execute
     |> Output.toText)
        .Trim()

// Runs `body relName scratchAbs` against a unique, self-cleaning scratch
// directory under the git root. `.mutannot/<relName>` (where the mutator mirrors
// the sources it patches) is cleaned up alongside it.
let withScratch (body: string -> string -> unit) =
    let name = ".inttest-" + Guid.NewGuid().ToString("N")
    let scratch = Path.Combine(gitRoot, name)

    try
        Directory.CreateDirectory scratch |> ignore
        body name scratch
    finally
        for dir in [ scratch; Path.Combine(gitRoot, ".mutannot", name) ] do
            if Directory.Exists dir then
                Directory.Delete(dir, true)

let build (projectPath: string) =
    cli {
        Exec "dotnet"
        Arguments [ "build"; projectPath; "-c"; "Debug" ]
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let sha256 (bytes: byte[]) =
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes)
