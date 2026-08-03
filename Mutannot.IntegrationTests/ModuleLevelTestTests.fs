module Mutannot.IntegrationTests.ModuleLevelTestTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

type ModuleLevelTestTests() =
    // An F# test authored as a module-level `let` (rather than a member of a type)
    // compiles to a *static* method on the module class. mutannot has to scan static
    // methods too, or such a test's ShouldCatch is never discovered and its mutation
    // silently skipped. This drives a full `run` over a scratch project whose only
    // test is a module-level `let` carrying a ShouldCatch: a green run must find and
    // kill that mutant. Exit code alone can't prove it -- a run that discovers *no*
    // mutations also succeeds -- so this asserts the run actually reported killing
    // one. The ShouldCatch below drops BindingFlags.Static from that scan, which
    // makes the module-level test go undiscovered; the mutated run then kills
    // nothing, the "Mutant killed" line never appears, and this test fails.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -281,3 +281,2 @@
                                 ||| BindingFlags.Instance
    -                            ||| BindingFlags.Static
                                 ||| BindingFlags.DeclaredOnly
    """)>]
    member _.``discovers a ShouldCatch on a module-level let test, not just members``() =
        withScratch (fun scratch ->
            // A trivial library whose one function the scratch test pins and mutates.
            let libSource =
                String.concat "\n" [ "namespace ModLib"; ""; "module Calc ="; "    let answer () = 41"; "" ]

            // The test lives directly under a `module` as a `let`, so it compiles to a
            // static method -- exactly the shape that used to slip past discovery. It
            // carries a ShouldCatch flipping the pinned value, so a green run has to
            // kill the mutant.
            let patch =
                diff
                    "ModLib/Calc.fs"
                    "@@ -3,2 +3,2 @@ namespace ModLib"
                    [ " module Calc ="; "-    let answer () = 41"; "+    let answer () = 42" ]

            let testSource =
                String.concat
                    "\n"
                    [ "module ModLib.Tests.Tests"
                      ""
                      "open Xunit"
                      "open Mutannot.Annotations"
                      "open ModLib"
                      ""
                      "[<Fact>]"
                      fsharpShouldCatch patch
                      "let ``answer is 41`` () ="
                      "    Assert.Equal(41, Calc.answer ())"
                      "" ]

            let graph =
                { Projects =
                    [ library Fsharp "ModLib" [ file "Calc.fs" libSource ]
                      testProject
                          Fsharp
                          XunitV2
                          "ModLib.Tests"
                          [ "../ModLib/ModLib.fsproj" ]
                          [ file "Tests.fs" testSource ] ]
                  RunTarget = "ModLib.Tests/ModLib.Tests.fsproj" }

            let target = writeGraph scratch graph

            // Capture mutannot's own output: the run succeeds whether it kills a mutant
            // or finds none at all, so the exit code can't tell the two apart. Its
            // per-mutant "Mutant killed" line can. Child process output goes straight
            // to the real stdout handle, so only mutannot's messages land here.
            let output = new StringWriter()
            let original = Console.Out
            Console.SetOut output

            let exitCode =
                try
                    Program.main [| "run"; target |]
                finally
                    Console.SetOut original

            Assert.Equal(0, exitCode)
            Assert.Contains("Mutant killed", output.ToString()))
