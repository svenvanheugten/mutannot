module Mutannot.IntegrationTests.ValidateTests

open System.IO
open Xunit
open Mutannot
open Mutannot.IntegrationTests.TestSupport

// `validate` checks that a source file's ShouldCatch patches still apply to the
// working tree with `git apply --check`. Unlike `run` it never builds or executes
// anything, so these tests point it at real files under the git root and assert on
// the exit code alone -- there is no scratch project to compile.

[<Fact>]
let ``validate accepts patches that still apply in an fsproj test file`` () =
    let exitCode =
        Program.main
            [| "validate"
               Path.Combine(repoRoot, "Example.FSharp.Tests", "ValidatorTests.fs") |]

    Assert.Equal(0, exitCode)

[<Fact>]
let ``validate accepts patches that still apply in a csproj test file`` () =
    let exitCode =
        Program.main
            [| "validate"
               Path.Combine(repoRoot, "Example.CSharp.Tests", "CalculatorTests.cs") |]

    Assert.Equal(0, exitCode)

[<Fact>]
let ``validate rejects a patch whose context no longer matches`` () =
    withScratch (fun _ scratch ->
        // The removed line (`x * y`) does not match the real Calculator.cs source
        // (`x + y`), so `git apply --check` refuses the patch and validate exits 3.
        let source =
            String.concat
                "\n"
                [ "using Mutannot.Annotations;"
                  "[ShouldCatch(\"\"\""
                  "--- a/Example.CSharp/Calculator.cs"
                  "+++ b/Example.CSharp/Calculator.cs"
                  "@@ -1,6 +1,6 @@"
                  " namespace Example;"
                  ""
                  " public static class Calculator"
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
let ``validate succeeds when the file has no ShouldCatch attributes`` () =
    withScratch (fun _ scratch ->
        let file = Path.Combine(scratch, "Plain.cs")
        File.WriteAllText(file, "public class Plain {}\n")

        let exitCode = Program.main [| "validate"; file |]
        Assert.Equal(0, exitCode))
