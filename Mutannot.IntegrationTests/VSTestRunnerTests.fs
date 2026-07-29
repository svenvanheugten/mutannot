module Mutannot.IntegrationTests.VSTestRunnerTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Runner.fs
+++ b/Mutannot/Runner.fs
@@ -190,7 +190,7 @@ module Runner =
                     $"Project '{projectPath}' uses Microsoft.Testing.Platform but its tests are not xunit v3. mutannot only supports xunit v3 on Microsoft.Testing.Platform."

                 exit 2
-        | _ -> VSTest
+        | _ -> MtpXunitV3

     let private getMetadataLoadContext (assemblyPath: string) =
         // This allows us to inspect assemblies regardless of the platform that they were built for
""")>]
let ``mutannot kills mutants in a vstest project`` () =
    withScratch (fun scratch ->
        let libDir = Path.Combine(scratch, "Calc")
        let testDir = Path.Combine(scratch, "Calc.Tests")
        Directory.CreateDirectory libDir |> ignore
        Directory.CreateDirectory testDir |> ignore

        File.WriteAllText(
            Path.Combine(libDir, "Calc.fs"),
            String.concat "\n" [ "namespace Calc"; ""; "module Calc ="; "    let add x y = x + y"; "" ]
        )

        File.WriteAllText(
            Path.Combine(libDir, "Calc.fsproj"),
            sdkProject [] [ itemGroup [ compileInclude "Calc.fs" ] ]
        )

        // The test pins Calc.add and carries a ShouldCatch flipping it, so a green
        // run must kill the mutant. The patch is generated here so the scratch
        // directory's runtime name can be embedded in its paths.
        File.WriteAllText(
            Path.Combine(testDir, "Tests.fs"),
            String.concat
                "\n"
                [ "module Calc.Tests.Tests"
                  ""
                  "open Xunit"
                  "open Mutannot.Annotations"
                  "open Calc"
                  ""
                  "[<Fact>]"
                  "[<ShouldCatch(\"\"\""
                  $"--- a/Calc/Calc.fs"
                  $"+++ b/Calc/Calc.fs"
                  "@@ -3,2 +3,2 @@ namespace Calc"
                  " module Calc ="
                  "-    let add x y = x + y"
                  "+    let add x y = x - y"
                  "\"\"\")>]"
                  "let ``add sums`` () ="
                  "    Assert.Equal(5, Calc.add 2 3)"
                  "" ]
        )

        File.WriteAllText(
            Path.Combine(testDir, "Calc.Tests.fsproj"),
            xunitV2TestProject [] [ "Tests.fs" ] [ "../Calc/Calc.fsproj" ]
        )

        let exitCode = Program.main [| "run"; Path.Combine(testDir, "Calc.Tests.fsproj") |]
        Assert.Equal(0, exitCode))

// mutannot recognizes a killed mutant by its target test *running and failing*. A
// mutant whose patch produces code that won't compile never gets that far: the
// mutated build fails, so no test runs and nothing establishes that the mutation
// was caught. Scoring such a build failure as a kill would report false success.
// This drives a mutation that applies cleanly but breaks compilation and asserts
// the run does not report success -- the mutant must not be counted as killed.
[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Runner.fs
+++ b/Mutannot/Runner.fs
@@ -88,2 +88,2 @@ module Runner =
-        output |> Output.throwIfErrored |> ignore
+        output |> ignore
         captureOutput output
""")>]
let ``a mutant that fails to compile is not counted as killed`` () =
    withScratch (fun scratch ->
        let libDir = Path.Combine(scratch, "Broken")
        let testDir = Path.Combine(scratch, "Broken.Tests")
        Directory.CreateDirectory libDir |> ignore
        Directory.CreateDirectory testDir |> ignore

        File.WriteAllText(
            Path.Combine(libDir, "Broken.fs"),
            String.concat "\n" [ "namespace Broken"; ""; "module Broken ="; "    let add x y = x + y"; "" ]
        )

        File.WriteAllText(
            Path.Combine(libDir, "Broken.fsproj"),
            sdkProject [] [ itemGroup [ compileInclude "Broken.fs" ] ]
        )

        // The test passes on the unmutated build, so the green baseline is
        // established and the mutation actually runs. Its ShouldCatch patch applies
        // cleanly but replaces the addend with an undefined identifier, so the
        // *mutated* build fails to compile. The patch is generated here so the
        // scratch directory's runtime name can be embedded in its paths.
        File.WriteAllText(
            Path.Combine(testDir, "Tests.fs"),
            String.concat
                "\n"
                [ "module Broken.Tests.Tests"
                  ""
                  "open Xunit"
                  "open Mutannot.Annotations"
                  "open Broken"
                  ""
                  "[<Fact>]"
                  "[<ShouldCatch(\"\"\""
                  $"--- a/Broken/Broken.fs"
                  $"+++ b/Broken/Broken.fs"
                  "@@ -3,2 +3,2 @@ namespace Broken"
                  " module Broken ="
                  "-    let add x y = x + y"
                  "+    let add x y = x + doesNotCompile"
                  "\"\"\")>]"
                  "let ``add sums`` () ="
                  "    Assert.Equal(5, Broken.add 2 3)"
                  "" ]
        )

        File.WriteAllText(
            Path.Combine(testDir, "Broken.Tests.fsproj"),
            xunitV2TestProject [] [ "Tests.fs" ] [ "../Broken/Broken.fsproj" ]
        )

        // A build failure means no test ran, so nothing established that the mutation
        // was caught: the run must not report success (exit 0). Whether mutannot
        // signals that with a non-zero exit code or by failing outright, a compile
        // failure must never be counted as a kill.
        let reportedSuccess =
            try
                Program.main [| "run"; Path.Combine(testDir, "Broken.Tests.fsproj") |] = 0
            with _ ->
                false

        Assert.False(reportedSuccess, "a mutant that fails to compile must not be scored as killed"))
