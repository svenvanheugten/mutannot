module Mutannot.IntegrationTests.EndToEndTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

[<Fact>]
let ``mutannot kills mutants in an fsproj project`` () =
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

[<Fact>]
#if WINDOWS // the `\n` terminator only matters on Windows; without it patches from C# raw string literals fail to apply
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -30,6 +30,6 @@
             //
             // Inserting a newline to "terminate" the patch right before that
             // `\r\n` seems to prevent that problem.
-            Input $"{patch}\n"
+            Input patch
         }
         |> Command.execute
""")>]
#endif
let ``mutannot kills mutants in a csproj project`` () =
    withScratch (fun name scratch ->
        let libDir = Path.Combine(scratch, "Calc")
        let testDir = Path.Combine(scratch, "Calc.Tests")
        Directory.CreateDirectory libDir |> ignore
        Directory.CreateDirectory testDir |> ignore

        File.WriteAllText(
            Path.Combine(libDir, "Calc.cs"),
            "namespace Calc;\n"
            + "public static class Calc\n"
            + "{\n"
            + "    public static int Add(int x, int y) => x + y;\n"
            + "}\n"
        )

        File.WriteAllText(Path.Combine(libDir, "Calc.csproj"), sdkProject [ "<Nullable>enable</Nullable>" ] [])

        let patch =
            String.concat
                "\n"
                [ $"--- a/{name}/Calc/Calc.cs"
                  $"+++ b/{name}/Calc/Calc.cs"
                  "@@ -1,5 +1,5 @@"
                  " namespace Calc;"
                  " public static class Calc"
                  " {"
                  "-    public static int Add(int x, int y) => x + y;"
                  "+    public static int Add(int x, int y) => x - y;"
                  " }" ]

        File.WriteAllText(
            Path.Combine(testDir, "Tests.cs"),
            "using Mutannot.Annotations;\n"
            + "using Xunit;\n"
            + "namespace CalcTests;\n"
            + "public class CalcTests\n"
            + "{\n"
            + "    [ShouldCatch(\"\"\"\n"
            + patch
            + "\n\"\"\")]\n"
            + "    [Fact]\n"
            + "    public void Add_Works() => Assert.Equal(5, Calc.Calc.Add(2, 3));\n"
            + "}\n"
        )

        File.WriteAllText(
            Path.Combine(testDir, "Calc.Tests.csproj"),
            xunitTestProject
                [ "<IsPackable>false</IsPackable>"
                  "<Nullable>enable</Nullable>"
                  "<ImplicitUsings>enable</ImplicitUsings>" ]
                []
                [ "../Calc/Calc.csproj" ]
        )

        let exitCode = Program.main [| "run"; Path.Combine(testDir, "Calc.Tests.csproj") |]
        Assert.Equal(0, exitCode))
