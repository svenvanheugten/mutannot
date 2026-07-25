module Mutannot.IntegrationTests.EndToEndTests

open System.IO
open Xunit
open Mutannot
open Mutannot.IntegrationTests.TestSupport

[<Fact>]
let ``mutannot kills mutants in an fsproj project`` () =
    let exitCode =
        Program.main
            [| "run"
               Path.Combine(repoRoot, "Example.FSharp.Tests", "Example.FSharp.Tests.fsproj") |]

    Assert.Equal(0, exitCode)

[<Fact>]
let ``mutannot kills mutants in a csproj project`` () =
    let exitCode =
        Program.main
            [| "run"
               Path.Combine(repoRoot, "Example.CSharp.Tests", "Example.CSharp.Tests.csproj") |]

    Assert.Equal(0, exitCode)
