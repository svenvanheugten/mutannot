module Mutannot.IntegrationTests.MtpNUnitTests

open Xunit
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

// Standard MTP NUnit scratch library source: the one Add the target test pins.
let private nunitCalc =
    "namespace ScratchNu;\n"
    + "public static class Calc\n"
    + "{\n"
    + "    public static int Add(int x, int y) => x + y;\n"
    + "}\n"

// Flips Add to subtraction; caught by the target test.
let private addSubPatch =
    diff
        "Nu/Calc.cs"
        "@@ -1,5 +1,5 @@"
        [ " namespace ScratchNu;"
          " public static class Calc"
          " {"
          "-    public static int Add(int x, int y) => x + y;"
          "+    public static int Add(int x, int y) => x - y;"
          " }" ]

// A single-project MTP NUnit graph: `sources` in one executable test project under
// <scratch>/<dir>, run-targeted at its .csproj.
let private nunitGraph (dir: string) (sources: SourceFile list) =
    { Projects = [ testProject Csharp MtpNUnit dir [] sources ]
      RunTarget = $"{dir}/{dir}.csproj" }

type MtpNUnitTests() =
    // The straight end-to-end path: a green NUnit-on-MTP project whose only test
    // pins Add and carries a ShouldCatch flipping it, so a real run establishes the
    // baseline, applies the patch, and the now-failing test kills the mutant.
    //
    // A killed mutant on MTP is recognized by its target test running and *failing*,
    // which the platform (and so NUnit's runner) signals with exit code 2. The
    // ShouldCatch below drops MtpNUnit from that classification, so a NUnit mutant's
    // failing run (exit 2) is scored as an error rather than a kill -- the inner run
    // then exits 3 (not all killed) instead of 0, so the mutant survives unless
    // MtpNUnit is classified exactly like the other MTP runner.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -62,7 +62,7 @@
             match runnerKind, exitCode with
             | _, 0 -> Survived
             | VSTest, 1 -> Killed
    -        | (MtpXunitV3 | MtpNUnit), 2 -> Killed
    +        | MtpXunitV3, 2 -> Killed
             | _, code -> Errored code

         // What a mutation's test should be narrowed to when run. The concrete filter
    """)>]
    member _.``mutannot kills mutants in a Microsoft.Testing.Platform NUnit project``() =
        withScratch (fun scratch ->
            let tests =
                "using Mutannot.Annotations;\n"
                + "using NUnit.Framework;\n"
                + "namespace ScratchNu;\n"
                + "public class Tests\n"
                + "{\n"
                + "    "
                + csharpShouldCatch addSubPatch
                + "\n"
                + "    [Test]\n"
                + "    public void AddWorks() => Assert.That(Calc.Add(2, 3), Is.EqualTo(5));\n"
                + "}\n"

            let exitCode =
                nunitGraph "Nu" [ file "Calc.cs" nunitCalc; file "Tests.cs" tests ]
                |> runIn scratch

            Assert.Equal(0, exitCode))

    // Runner detection is the gate to the whole MTP path, and it can't be guarded by
    // a plain end-to-end run: `dotnet test` (the VSTest path) runs an MTP NUnit
    // project's tests fine, it just ignores the --filter, so on a single-test project
    // a downgrade to VSTest would still kill the mutant -- only the runner
    // *selection* differs, which the exit code doesn't reveal.
    //
    // So this makes the selection observable: the scratch project carries a *second,
    // always-failing* test with no ShouldCatch. With correct MtpNUnit detection the
    // run filters the baseline down to the annotated target alone, never touches the
    // failing test, and exits 0. Downgraded to VSTest the --filter is ignored, so the
    // baseline runs every test -- including the failing one -- fails, and the run
    // exits 4. The two ShouldCatch patches each force exactly that downgrade -- the
    // first slips the case of the EnableNUnitRunner probe, the second stops the test
    // assembly's nunit.framework reference from being recognized as NUnit -- so a
    // surviving mutant here shows up as exit 4 <> 0.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -342,7 +342,7 @@

             match testFramework with
             | XunitV3 when getProperty "IsTestingPlatformApplication" = "true" -> MtpXunitV3
    -        | NUnit when getProperty "EnableNUnitRunner" = "true" -> MtpNUnit
    +        | NUnit when getProperty "EnableNUnitRunner" = "True" -> MtpNUnit
             | XunitV3
             | NUnit -> VSTest
             | OtherFramework ->
    """)>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -433,7 +433,7 @@
                         && a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

                 if references "xunit.v3" then XunitV3
    -            elif references "nunit.framework" then NUnit
    +            elif references "nunit.framework" then OtherFramework
                 else OtherFramework

             let mutations =
    """)>]
    member _.``detects the runner as Microsoft.Testing.Platform NUnit``() =
        withScratch (fun scratch ->
            let tests =
                "using Mutannot.Annotations;\n"
                + "using NUnit.Framework;\n"
                + "namespace ScratchNu;\n"
                + "public class Tests\n"
                + "{\n"
                + "    "
                + csharpShouldCatch addSubPatch
                + "\n"
                + "    [Test]\n"
                + "    public void AddWorks() => Assert.That(Calc.Add(2, 3), Is.EqualTo(5));\n"
                + "\n"
                + "    [Test]\n"
                + "    public void AlwaysFails() => Assert.That(false);\n"
                + "}\n"

            let exitCode =
                nunitGraph "Nu" [ file "Calc.cs" nunitCalc; file "Tests.cs" tests ]
                |> runIn scratch

            Assert.Equal(0, exitCode))
