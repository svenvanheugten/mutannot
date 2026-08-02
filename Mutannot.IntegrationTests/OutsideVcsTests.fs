module Mutannot.IntegrationTests.OutsideGitTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

// mutannot's commands resolve the git root (see the `Git` module) before doing their
// work. Run outside any git repository that lookup fails, and the error must surface
// rather than collapsing into an empty root that leaks downstream and resurfaces as a
// cryptic failure. Each test drives a command against a scratch directory under the
// system temp path -- outside mutannot's own tree and any repository -- so the
// underlying `git` invocation fails.

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -58,7 +58,6 @@
             WorkingDirectory directory
          }
          |> Command.execute
-         |> Output.throwIfErrored
          |> Output.toText)
             .Split('\n')
         |> Array.map (fun line -> line.Trim())
""")>]
let ``validate on a directory outside any git repository surfaces git's error`` () =
    // Unlike withScratch's fixtures (which live under mutannot's own git root), this
    // scratch directory sits under the system temp path, outside any repository.
    let scratch =
        Path.Combine(Path.GetTempPath(), "mutannot-nogit-" + System.Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory scratch |> ignore

    try
        File.WriteAllText(Path.Combine(scratch, "Foo.cs"), "public class Foo {}\n")

        let error =
            Assert.ThrowsAny<exn>(fun () -> Program.main [| "validate"; scratch |] |> ignore)

        Assert.Contains("not a git repository", error.Message)
    finally
        Directory.Delete(scratch, true)

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -15,6 +15,5 @@ module Git =
             WorkingDirectory directory
          }
          |> Command.execute
-         |> Output.throwIfErrored
          |> Output.toText)
             .Trim()
""")>]
let ``run outside any git repository surfaces git's error`` () =
    // Unlike withScratch's fixtures (which live under mutannot's own git root), this
    // scratch directory sits under the system temp path, outside any repository.
    let scratch =
        Path.Combine(Path.GetTempPath(), "mutannot-run-nogit-" + Guid.NewGuid().ToString("N"))

    let libDir = Path.Combine(scratch, "Calc")
    let testDir = Path.Combine(scratch, "Calc.Tests")
    Directory.CreateDirectory libDir |> ignore
    Directory.CreateDirectory testDir |> ignore

    try
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

        let error =
            Assert.ThrowsAny<exn>(fun () ->
                Program.main [| "run"; Path.Combine(testDir, "Calc.Tests.csproj") |] |> ignore)

        Assert.Contains("not a git repository", error.Message)
    finally
        if Directory.Exists scratch then
            Directory.Delete(scratch, true)
