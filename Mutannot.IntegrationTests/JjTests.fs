module Mutannot.IntegrationTests.JjTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

// These tests drive a jj repository that is *not* co-located with git, so
// `git rev-parse`/`git ls-files` cannot see it and jj is what answers instead.
// withJjScratch builds exactly such a repository under the system temp path, outside
// any ambient git tree.

[<ShouldCatch("""
--- a/Mutannot/Jj.fs
+++ b/Mutannot/Jj.fs
@@ -39,7 +39,7 @@
             .Split('\n')
         |> Array.map (fun line -> line.Trim())
         |> Array.filter (String.IsNullOrWhiteSpace >> not)
-        |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
+        |> Array.map (fun relativePath -> Path.GetFullPath(relativePath))
         |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
         |> Array.filter File.Exists
         |> Array.toList
""")>]
[<Fact>]
let ``sourceFiles returns absolute paths for untracked files, recursing into subdirectories`` () =
    withJjScratch (fun scratch ->
        Directory.CreateDirectory(Path.Combine(scratch, "Nested")) |> ignore
        File.WriteAllText(Path.Combine(scratch, "Foo.cs"), "public class Foo {}\n")
        File.WriteAllText(Path.Combine(scratch, "Nested", "Bar.fs"), "module Bar\n")

        let result = Jj.sourceFiles scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Foo.cs")), result)
        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Nested", "Bar.fs")), result))

[<ShouldCatch("""
--- a/Mutannot/Jj.fs
+++ b/Mutannot/Jj.fs
@@ -40,6 +40,5 @@
         |> Array.map (fun line -> line.Trim())
         |> Array.filter (String.IsNullOrWhiteSpace >> not)
         |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
-        |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
         |> Array.filter File.Exists
         |> Array.toList
""")>]
[<Fact>]
let ``sourceFiles ignores files that are not C# or F#`` () =
    withJjScratch (fun scratch ->
        File.WriteAllText(Path.Combine(scratch, "Keep.cs"), "public class Keep {}\n")
        File.WriteAllText(Path.Combine(scratch, "Skip.txt"), "not source\n")
        File.WriteAllText(Path.Combine(scratch, "Data.json"), "{}\n")

        let result = Jj.sourceFiles scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Keep.cs")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "Skip.txt")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "Data.json")), result))

[<ShouldCatch("""
--- a/Mutannot/Vcs.fs
+++ b/Mutannot/Vcs.fs
@@ -39,7 +39,7 @@
         if succeeds "git" [ "rev-parse"; "--show-toplevel" ] directory then
             Git
         elif succeeds "jj" [ "root" ] directory then
-            Jj
+            Git
         else
             Git
""")>]
[<Fact>]
let ``validate applies patches through the jj backend when git cannot see the repo`` () =
    // End-to-end: `git rev-parse` fails in this non-co-located jj repo, so `validate`
    // only reaches a green result if Vcs falls through to the jj backend for both the
    // source scan and the root that anchors `git apply --check`. Force the dispatch to
    // git instead (the mutation) and those git calls fail in a repo git cannot see, so
    // validate never returns 0.
    withJjScratch (fun scratch ->
        File.WriteAllText(
            Path.Combine(scratch, "Calc.cs"),
            "public static class Calc\n"
            + "{\n"
            + "    public static int Add(int x, int y) => x + y;\n"
            + "}\n"
        )

        let source =
            String.concat
                "\n"
                [ "using Mutannot.Annotations;"
                  "[ShouldCatch(\"\"\""
                  $"--- a/Calc.cs"
                  $"+++ b/Calc.cs"
                  "@@ -1,4 +1,4 @@"
                  " public static class Calc"
                  " {"
                  "-    public static int Add(int x, int y) => x + y;"
                  "+    public static int Add(int x, int y) => x - y;"
                  " }"
                  "\"\"\")]"
                  "public class Foo {}" ]

        File.WriteAllText(Path.Combine(scratch, "Tests.cs"), source)

        let exitCode = Program.main [| "validate"; scratch |]
        Assert.Equal(0, exitCode))

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
[<Fact>]
let ``run kills a mutant in a jj repository that is not co-located with git`` () =
    // The full run path, not just validate: this actually builds the mutated project
    // and executes its test. `run` resolves the root (to redirect the mutated build's
    // output and anchor `git apply`) via Vcs, which must reach the jj backend here --
    // `git rev-parse` cannot see this non-co-located repo. Route the root to git
    // instead (the mutation) and that lookup fails, so run never reaches a clean kill.
    withJjScratch (fun scratch ->
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
