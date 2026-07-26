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
    withScratch (fun name scratch ->
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
                  $"--- a/{name}/Calc/Calc.fs"
                  $"+++ b/{name}/Calc/Calc.fs"
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
            xunitTestProject [] [ "Tests.fs" ] [ "../Calc/Calc.fsproj" ]
        )

        let exitCode = Program.main [| "run"; Path.Combine(testDir, "Calc.Tests.fsproj") |]
        Assert.Equal(0, exitCode))
