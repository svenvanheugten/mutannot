module Mutannot.IntegrationTests.CSharpTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
[<ShouldCatch("""
--- a/Mutannot/Vcs.fs
+++ b/Mutannot/Vcs.fs
@@ -46,7 +46,7 @@
     let root (directory: string) =
         match backend directory with
         | Git -> Git.root directory
-        | Jj -> Jj.root directory
+        | Jj -> Git.root directory

     let sourceFiles (directory: string) =
         match backend directory with
""")>]
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
let ``mutannot kills mutants in a csproj project`` (jj: bool) =
    withScratchFor jj (fun scratch ->
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
                [ $"--- a/Calc/Calc.cs"
                  $"+++ b/Calc/Calc.cs"
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
            xunitV2TestProject
                [ "<IsPackable>false</IsPackable>"
                  "<Nullable>enable</Nullable>"
                  "<ImplicitUsings>enable</ImplicitUsings>" ]
                []
                [ "../Calc/Calc.csproj" ]
        )

        let exitCode = Program.main [| "run"; Path.Combine(testDir, "Calc.Tests.csproj") |]
        Assert.Equal(0, exitCode))
