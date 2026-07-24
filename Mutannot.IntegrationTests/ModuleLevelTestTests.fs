module Mutannot.IntegrationTests.ModuleLevelTestTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
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
        withScratch (fun name scratch ->
            let libDir = Path.Combine(scratch, "ModLib")
            let testDir = Path.Combine(scratch, "ModLib.Tests")
            Directory.CreateDirectory libDir |> ignore
            Directory.CreateDirectory testDir |> ignore

            // A trivial library whose one function the scratch test pins and mutates.
            File.WriteAllText(
                Path.Combine(libDir, "Calc.fs"),
                String.concat "\n" [ "namespace ModLib"; ""; "module Calc ="; "    let answer () = 41"; "" ]
            )

            File.WriteAllText(
                Path.Combine(libDir, "ModLib.fsproj"),
                sdkProject [] [ itemGroup [ compileInclude "Calc.fs" ] ]
            )

            // The test lives directly under a `module` as a `let`, so it compiles to a
            // static method -- exactly the shape that used to slip past discovery. It
            // carries a ShouldCatch flipping the pinned value, so a green run has to
            // kill the mutant. The patch is generated here so the scratch directory's
            // runtime name can be embedded in its paths.
            File.WriteAllText(
                Path.Combine(testDir, "Tests.fs"),
                String.concat
                    "\n"
                    [ "module ModLib.Tests.Tests"
                      ""
                      "open Xunit"
                      "open Mutannot.Annotations"
                      "open ModLib"
                      ""
                      "[<Fact>]"
                      "[<ShouldCatch(\"\"\""
                      $"--- a/{name}/ModLib/Calc.fs"
                      $"+++ b/{name}/ModLib/Calc.fs"
                      "@@ -3,2 +3,2 @@ namespace ModLib"
                      " module Calc ="
                      "-    let answer () = 41"
                      "+    let answer () = 42"
                      "\"\"\")>]"
                      "let ``answer is 41`` () ="
                      "    Assert.Equal(41, Calc.answer ())"
                      "" ]
            )

            File.WriteAllText(
                Path.Combine(testDir, "ModLib.Tests.fsproj"),
                xunitTestProject [] [ "Tests.fs" ] [ "../ModLib/ModLib.fsproj" ]
            )

            // Capture mutannot's own output: the run succeeds whether it kills a mutant
            // or finds none at all, so the exit code can't tell the two apart. Its
            // per-mutant "Mutant killed" line can. Child process output goes straight
            // to the real stdout handle, so only mutannot's messages land here.
            let output = new StringWriter()
            let original = Console.Out
            Console.SetOut output

            let exitCode =
                try
                    Program.main [| "run"; Path.Combine(testDir, "ModLib.Tests.fsproj") |]
                finally
                    Console.SetOut original

            Assert.Equal(0, exitCode)
            Assert.Contains("Mutant killed", output.ToString()))
