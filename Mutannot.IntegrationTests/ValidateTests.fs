module Mutannot.IntegrationTests.ValidateTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

// `validate` checks that a source file's ShouldCatch patches still apply to the
// working tree with `git apply --check`. Unlike `run` it never builds or executes
// anything, so these tests point it at real files under the git root and assert on
// the exit code alone -- there is no scratch project to compile.

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -54,7 +54,7 @@
     let private checkPatch (gitRoot: string) (patch: string) =
         let output = Vcs.apply gitRoot [ "--check" ] patch

-        if Output.toExitCode output = 0 then
+        if Output.toExitCode output <> 0 then
             None
         else
             Some(Output.toError output)
""")>]
let ``validate accepts patches that still apply in an fsproj test file`` () =
    withScratch (fun scratch ->
        File.WriteAllText(
            Path.Combine(scratch, "Validator.fs"),
            String.concat
                "\n"
                [ "namespace Example"
                  ""
                  "open System"
                  ""
                  "module Validator ="
                  "    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                  "" ]
        )

        File.WriteAllText(
            Path.Combine(scratch, "ValidatorTests.fs"),
            String.concat
                "\n"
                [ "namespace Example"
                  ""
                  "open Mutannot.Annotations"
                  ""
                  "[<ShouldCatch(\"\"\""
                  $"--- a/Validator.fs"
                  $"+++ b/Validator.fs"
                  "@@ -3,4 +3,4 @@ namespace Example"
                  " open System"
                  ""
                  " module Validator ="
                  "-    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                  "+    let isAllowed (now: DateTime) (date: DateTime) = now <= date"
                  "\"\"\")>]"
                  "type ValidatorTests() = class end"
                  "" ]
        )

        let exitCode =
            Program.main [| "validate"; Path.Combine(scratch, "ValidatorTests.fs") |]

        Assert.Equal(0, exitCode))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -86,4 +86,4 @@
                 Console.ForegroundColor <- ConsoleColor.Green
                 printf "Success: All patches apply\n"
                 Console.ResetColor()
-                0
+                3
""")>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -123,7 +123,7 @@
             let gitRoot =
                 Vcs.root (
                     if Directory.Exists fullPath then
                         fullPath
                     else
-                        Path.GetDirectoryName fullPath
+                        "."
                 )
""")>]
let ``validate accepts patches that still apply in a csproj test file`` () =
    withScratch (fun scratch ->
        File.WriteAllText(
            Path.Combine(scratch, "Calculator.cs"),
            "namespace Example;\n"
            + "\n"
            + "public static class Calculator\n"
            + "{\n"
            + "    public static int Add(int x, int y) => x + y;\n"
            + "}\n"
        )

        File.WriteAllText(
            Path.Combine(scratch, "CalculatorTests.cs"),
            "using Mutannot.Annotations;\n"
            + "\n"
            + "[ShouldCatch(\"\"\"\n"
            + $"--- a/Calculator.cs\n"
            + $"+++ b/Calculator.cs\n"
            + "@@ -1,6 +1,6 @@\n"
            + " namespace Example;\n"
            + "\n"
            + " public static class Calculator\n"
            + " {\n"
            + "-    public static int Add(int x, int y) => x + y;\n"
            + "+    public static int Add(int x, int y) => x - y;\n"
            + " }\n"
            + "\"\"\")]\n"
            + "public class CalculatorTests {}\n"
        )

        let exitCode =
            Program.main [| "validate"; Path.Combine(scratch, "CalculatorTests.cs") |]

        Assert.Equal(0, exitCode))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -81,7 +81,7 @@
                 Console.ForegroundColor <- ConsoleColor.Red
                 eprintf "ERROR: Some patches do not apply\n"
                 Console.ResetColor()
-                3
+                0
             else
                 Console.ForegroundColor <- ConsoleColor.Green
                 printf "Success: All patches apply\n"
""")>]
let ``validate rejects a patch whose context no longer matches`` () =
    withScratch (fun scratch ->
        // The target file exists but its source (`x + y`) does not match the patch's
        // removed line (`x * y`), so `git apply --check` refuses the patch and
        // validate exits 3.
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
                  "-    public static int Add(int x, int y) => x * y;"
                  "+    public static int Add(int x, int y) => x - y;"
                  " }"
                  "\"\"\")]"
                  "public class Foo {}" ]

        let file = Path.Combine(scratch, "Stale.cs")
        File.WriteAllText(file, source)

        let exitCode = Program.main [| "validate"; file |]
        Assert.Equal(3, exitCode))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -118,6 +118,6 @@

         if List.isEmpty filesWithPatches then
             printfn "No ShouldCatch attributes found in '%s'." path
-            0
+            3
         else
             let gitRoot =
""")>]
let ``validate succeeds when the file has no ShouldCatch attributes`` () =
    withScratch (fun scratch ->
        let file = Path.Combine(scratch, "Plain.cs")
        File.WriteAllText(file, "public class Plain {}\n")

        let exitCode = Program.main [| "validate"; file |]
        Assert.Equal(0, exitCode))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -32,7 +32,6 @@
             Arguments
                 [ "ls-files"
                   "--cached"
-                  "--others"
                   "--exclude-standard"
                   "--"
                   "*.cs"
""")>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -99,7 +99,7 @@
         let fullPath = Path.GetFullPath path

         let sourceFiles =
             if Directory.Exists fullPath then
-                Vcs.sourceFiles fullPath
+                []
             else
                 [ fullPath ]
""")>]
let ``validate scans a directory, including newly created untracked files`` () =
    withScratch (fun scratch ->
        // The scratch directory and everything in it is untracked and not
        // gitignored, so validate can only reach this file if the ls-files scan
        // includes untracked files (--others). Its patch is stale (Calc.cs below
        // has `x + y`, the patch removes `x * y`), so a scan that finds it exits 3 --
        // dropping --others would instead miss the file entirely and exit 0.
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
                  "-    public static int Add(int x, int y) => x * y;"
                  "+    public static int Add(int x, int y) => x - y;"
                  " }"
                  "\"\"\")]"
                  "public class Foo {}" ]

        File.WriteAllText(Path.Combine(scratch, "Stale.cs"), source)

        let exitCode = Program.main [| "validate"; scratch |]
        Assert.Equal(3, exitCode))
