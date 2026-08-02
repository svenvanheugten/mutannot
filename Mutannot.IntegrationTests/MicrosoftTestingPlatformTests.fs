module Mutannot.IntegrationTests.MicrosoftTestingPlatformTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

type MicrosoftTestingPlatformTests() =
    [<Fact>]
    member _.``mutannot kills mutants in a Microsoft.Testing.Platform xunit v3 project``() =
        withScratch (fun scratch ->
            let projDir = Path.Combine(scratch, "Mtp")
            Directory.CreateDirectory projDir |> ignore

            File.WriteAllText(
                Path.Combine(projDir, "Calc.cs"),
                "namespace ScratchMtp;\n"
                + "public static class Calc\n"
                + "{\n"
                + "    public static int Add(int x, int y) => x + y;\n"
                + "}\n"
            )

            // The Microsoft.Testing.Platform + xunit v3 setup lives here rather than
            // in the .csproj, so mutannot must detect the runner through msbuild
            // evaluation instead of parsing the project file.
            File.WriteAllText(
                Path.Combine(projDir, "Directory.Build.props"),
                "<Project>\n"
                + "  <PropertyGroup>\n"
                + "    <OutputType>Exe</OutputType>\n"
                + "    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"xunit.v3\" Version=\"3.1.0\" />\n"
                + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
                + "      <PrivateAssets>all</PrivateAssets>\n"
                + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                + "    </PackageReference>\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            File.WriteAllText(
                Path.Combine(projDir, "Mtp.csproj"),
                sdkProject
                    [ "<IsPackable>false</IsPackable>"
                      "<Nullable>enable</Nullable>"
                      "<ImplicitUsings>enable</ImplicitUsings>" ]
                    [ itemGroup [ annotationsReference () ] ]
            )

            let patch =
                String.concat
                    "\n"
                    [ $"--- a/Mtp/Calc.cs"
                      $"+++ b/Mtp/Calc.cs"
                      "@@ -1,5 +1,5 @@"
                      " namespace ScratchMtp;"
                      " public static class Calc"
                      " {"
                      "-    public static int Add(int x, int y) => x + y;"
                      "+    public static int Add(int x, int y) => x - y;"
                      " }" ]

            File.WriteAllText(
                Path.Combine(projDir, "Tests.cs"),
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "namespace ScratchMtp;\n"
                + "public class Tests\n"
                + "{\n"
                + "    [ShouldCatch(\"\"\"\n"
                + patch
                + "\n\"\"\")]\n"
                + "    [Fact]\n"
                + "    public void Add_Works() => Assert.Equal(5, Calc.Add(2, 3));\n"
                + "}\n"
            )

            let exitCode = Program.main [| "run"; Path.Combine(projDir, "Mtp.csproj") |]
            Assert.Equal(0, exitCode))

    // A killed mutant is recognized by its target test running and *failing*, which
    // MTP signals with the single exit code 2. Every other non-zero code means the
    // run neither cleanly passed nor cleanly failed -- a crash, an invalid filter
    // that matched zero tests, an infrastructure error -- and must not be miscounted
    // as a kill.
    //
    // This pins down the sharpest such case: a mutation whose patch renames the very
    // test method the run filters to. The original (baseline) build still has the
    // method, so the green baseline passes; the mutated build no longer does, so MTP
    // filters down to zero tests and exits 8 *without any test having failed*.
    // Scoring "non-zero == killed" would call this a kill even though nothing ran, so
    // `run` must instead surface it as an error (exit 3, its not-all-killed code)
    // rather than success (0). The ShouldCatch reintroduces exactly that miscount --
    // any MtpXunitV3 exit code counts as a kill -- which makes this test's inner run
    // exit 0 instead of 3, so the mutant survives unless the classifier is exact.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -53,6 +53,6 @@
             match runnerKind, exitCode with
             | _, 0 -> Survived
             | VSTest, 1 -> Killed
    -        | MtpXunitV3, 2 -> Killed
    +        | MtpXunitV3, _ -> Killed
             | _, code -> Errored code
    """)>]
    member _.``a non-failure error exit code is not counted as a killed mutant``() =
        withScratch (fun scratch ->
            let projDir = Path.Combine(scratch, "Mtp")
            Directory.CreateDirectory projDir |> ignore

            File.WriteAllText(
                Path.Combine(projDir, "Calc.cs"),
                "namespace ScratchMtp;\n"
                + "public static class Calc\n"
                + "{\n"
                + "    public static int Add(int x, int y) => x + y;\n"
                + "}\n"
            )

            // A real Microsoft.Testing.Platform xunit v3 project (see the first test
            // for the shape).
            File.WriteAllText(
                Path.Combine(projDir, "Mtp.csproj"),
                sdkProject
                    [ "<IsPackable>false</IsPackable>"
                      "<Nullable>enable</Nullable>"
                      "<ImplicitUsings>enable</ImplicitUsings>"
                      "<OutputType>Exe</OutputType>"
                      "<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>" ]
                    [ "  <ItemGroup>\n"
                      + "    <PackageReference Include=\"xunit.v3\" Version=\"3.1.0\" />\n"
                      + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
                      + "      <PrivateAssets>all</PrivateAssets>\n"
                      + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                      + "    </PackageReference>\n"
                      + "    "
                      + annotationsReference ()
                      + "\n"
                      + "  </ItemGroup>" ]
            )

            // The ShouldCatch patch renames the target test method itself. mutannot
            // reads the target's name from the original assembly and filters the run
            // to it (--filter-method ...Target); after this patch the mutated build
            // has only Renamed, so that filter matches zero tests. The '-'/'+' diff
            // markers keep these lines distinct from the real code lines below, so the
            // patch's own context matches only the actual method declaration.
            let patch =
                String.concat
                    "\n"
                    [ "--- a/Mtp/Tests.cs"
                      "+++ b/Mtp/Tests.cs"
                      "@@ -15,3 +15,3 @@"
                      "     [Fact]"
                      "-    public void Target() => Assert.Equal(4, Calc.Add(2, 2));"
                      "+    public void Renamed() => Assert.Equal(4, Calc.Add(2, 2));"
                      " }" ]

            File.WriteAllText(
                Path.Combine(projDir, "Tests.cs"),
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "namespace ScratchMtp;\n"
                + "public class Tests\n"
                + "{\n"
                + "    [ShouldCatch(\"\"\"\n"
                + patch
                + "\n\"\"\")]\n"
                + "    [Fact]\n"
                + "    public void Target() => Assert.Equal(4, Calc.Add(2, 2));\n"
                + "}\n"
            )

            let exitCode = Program.main [| "run"; Path.Combine(projDir, "Mtp.csproj") |]
            // Not 0: the run must not report success when no test ever failed. 3 is
            // `run`'s "not all mutants killed" code.
            Assert.Equal(3, exitCode))

    // Runner detection is the gate to the whole MTP path. A plain end-to-end run
    // can't guard it on its own: `dotnet test` (the VSTest path) runs an MTP
    // project's tests fine, it just silently ignores the --filter, so on a
    // single-test project a downgrade to VSTest would still kill the mutant --
    // only the runner *selection* differs, which the exit code doesn't reveal.
    //
    // So this makes the selection observable: the scratch MTP project carries a
    // *second, always-failing* test with no ShouldCatch. With correct MTP
    // detection the run filters the baseline down to the annotated target alone
    // (--filter-class/--filter-method), never touches the failing test, and exits
    // 0. Downgraded to VSTest the --filter is ignored, so the baseline runs every
    // test -- including the failing one -- fails, and the run exits 4. The
    // ShouldCatch below makes the IsTestingPlatformApplication check miss on a
    // case slip, forcing exactly that downgrade, so a surviving mutant here shows
    // up as exit 4 <> 0.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -175,3 +175,3 @@ let getRunnerKind projectPath referencesXunitV3 =
             match getProperty "IsTestingPlatformApplication" with
    -        | "true" ->
    +        | "True" ->
                 if referencesXunitV3 then
    """)>]
    member _.``detects the runner as Microsoft.Testing.Platform xunit v3``() =
        withScratch (fun scratch ->
            let projDir = Path.Combine(scratch, "Mtp")
            Directory.CreateDirectory projDir |> ignore

            // The production code the target test pins down, mutated by the
            // ShouldCatch patch below so the target genuinely kills its mutant.
            File.WriteAllText(
                Path.Combine(projDir, "Calc.cs"),
                "namespace ScratchMtp;\n"
                + "public static class Calc\n"
                + "{\n"
                + "    public static int Add(int x, int y) => x + y;\n"
                + "}\n"
            )

            // A real Microsoft.Testing.Platform xunit v3 project: an executable
            // with the platform's dotnet test support and xunit.v3, so the SDK
            // reports IsTestingPlatformApplication and mutannot must detect it as
            // MtpXunitV3 (see the module comment).
            File.WriteAllText(
                Path.Combine(projDir, "Mtp.csproj"),
                sdkProject
                    [ "<IsPackable>false</IsPackable>"
                      "<Nullable>enable</Nullable>"
                      "<ImplicitUsings>enable</ImplicitUsings>"
                      "<OutputType>Exe</OutputType>"
                      "<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>" ]
                    [ "  <ItemGroup>\n"
                      + "    <PackageReference Include=\"xunit.v3\" Version=\"3.1.0\" />\n"
                      + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
                      + "      <PrivateAssets>all</PrivateAssets>\n"
                      + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                      + "    </PackageReference>\n"
                      + "    "
                      + annotationsReference ()
                      + "\n"
                      + "  </ItemGroup>" ]
            )

            // The patch that the target test's ShouldCatch applies: it breaks Add
            // so the target fails once mutated, i.e. the mutant is killed.
            let patch =
                String.concat
                    "\n"
                    [ $"--- a/Mtp/Calc.cs"
                      $"+++ b/Mtp/Calc.cs"
                      "@@ -1,5 +1,5 @@"
                      " namespace ScratchMtp;"
                      " public static class Calc"
                      " {"
                      "-    public static int Add(int x, int y) => x + y;"
                      "+    public static int Add(int x, int y) => x - y;"
                      " }" ]

            // Two tests: the annotated target (green, and killed by the patch
            // above), plus an always-failing test with no ShouldCatch. Only a
            // downgrade to VSTest -- which ignores the filter and so runs both --
            // lets the failing test into the baseline.
            File.WriteAllText(
                Path.Combine(projDir, "Tests.cs"),
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "namespace ScratchMtp;\n"
                + "public class Tests\n"
                + "{\n"
                + "    [ShouldCatch(\"\"\"\n"
                + patch
                + "\n\"\"\")]\n"
                + "    [Fact]\n"
                + "    public void Target() => Assert.Equal(5, Calc.Add(2, 3));\n"
                + "\n"
                + "    [Fact]\n"
                + "    public void AlwaysFails() => Assert.True(false);\n"
                + "}\n"
            )

            let exitCode = Program.main [| "run"; Path.Combine(projDir, "Mtp.csproj") |]
            Assert.Equal(0, exitCode))

    // The control (baseline) run tests every target scope in one runner invocation so
    // the runner can parallelise them itself (see Runner.combinedMtpFilterArgs). That
    // is subtle for the MTP xunit v3 runner: it ORs repeated values of one filter kind
    // but ANDs across kinds, so a baseline mixing a method scope (--filter-method) with
    // a class scope (--filter-class) would intersect to zero tests, fail the baseline,
    // and abort with exit 4. mutannot instead expresses every scope as a --filter-method
    // value (a class scope as the wildcard Type.*), keeping the whole set OR-combined.
    //
    // This pins that down with a project carrying both scope kinds at once: a
    // method-level ShouldCatch (TestMethod scope) and a class-level ShouldCatch
    // (TestClass scope), each green at baseline and killed by its patch. A regression
    // to mixing filter kinds would empty the combined baseline and surface as exit 4.
    // The two ShouldCatch patches reintroduce exactly such regressions in
    // combinedMtpFilterArgs -- the first mixes --filter-class with --filter-method (so
    // the two categories intersect to zero tests), the second drops the --filter-method
    // switch (so the values are no longer recognized as a filter) -- and this test must
    // catch both.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -162,12 +162,7 @@ module Runner =
         // of the class's methods -- keeping the whole set in the single (OR-combined)
         // method category.
         let private combinedMtpFilterArgs scopes =
    -        let methodPattern scope =
    -            match scope with
    -            | TestMethod fqn -> fqn
    -            | TestClass fqn -> $"{fqn}.*"
    -
    -        "--filter-method" :: (scopes |> List.map methodPattern)
    +        scopes |> List.collect mtpFilterArgs

         // Human-readable description of what a control run targets, for its header.
         let private describeScope scope =
    """)>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -167,7 +167,7 @@ module Runner =
                 | TestMethod fqn -> fqn
                 | TestClass fqn -> $"{fqn}.*"

    -        "--filter-method" :: (scopes |> List.map methodPattern)
    +        scopes |> List.map methodPattern

         // Human-readable description of what a control run targets, for its header.
         let private describeScope scope =
    """)>]
    member _.``runs a combined baseline across both method and class scopes``() =
        withScratch (fun scratch ->
            let projDir = Path.Combine(scratch, "MtpMulti")
            Directory.CreateDirectory projDir |> ignore

            File.WriteAllText(
                Path.Combine(projDir, "Calc.cs"),
                "namespace ScratchMtpMulti;\n"
                + "public static class Calc\n"
                + "{\n"
                + "    public static int Add(int x, int y) => x + y;\n"
                + "    public static int Sub(int x, int y) => x - y;\n"
                + "}\n"
            )

            File.WriteAllText(
                Path.Combine(projDir, "MtpMulti.csproj"),
                sdkProject
                    [ "<IsPackable>false</IsPackable>"
                      "<Nullable>enable</Nullable>"
                      "<ImplicitUsings>enable</ImplicitUsings>"
                      "<OutputType>Exe</OutputType>"
                      "<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>" ]
                    [ "  <ItemGroup>\n"
                      + "    <PackageReference Include=\"xunit.v3\" Version=\"3.1.0\" />\n"
                      + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
                      + "      <PrivateAssets>all</PrivateAssets>\n"
                      + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                      + "    </PackageReference>\n"
                      + "    "
                      + annotationsReference ()
                      + "\n"
                      + "  </ItemGroup>" ]
            )

            // Breaks Add; caught by the method-scoped test.
            let addPatch =
                String.concat
                    "\n"
                    [ "--- a/MtpMulti/Calc.cs"
                      "+++ b/MtpMulti/Calc.cs"
                      "@@ -2,4 +2,4 @@"
                      " public static class Calc"
                      " {"
                      "-    public static int Add(int x, int y) => x + y;"
                      "+    public static int Add(int x, int y) => x - y;"
                      "     public static int Sub(int x, int y) => x - y;" ]

            // Breaks Sub; caught by the class-scoped test.
            let subPatch =
                String.concat
                    "\n"
                    [ "--- a/MtpMulti/Calc.cs"
                      "+++ b/MtpMulti/Calc.cs"
                      "@@ -3,4 +3,4 @@"
                      " {"
                      "     public static int Add(int x, int y) => x + y;"
                      "-    public static int Sub(int x, int y) => x - y;"
                      "+    public static int Sub(int x, int y) => x + y;"
                      " }" ]

            // Two test classes: one whose method carries the ShouldCatch (a TestMethod
            // scope) and one carrying it on the class itself (a TestClass scope).
            File.WriteAllText(
                Path.Combine(projDir, "Tests.cs"),
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "namespace ScratchMtpMulti;\n"
                + "public class MethodScoped\n"
                + "{\n"
                + "    [ShouldCatch(\"\"\"\n"
                + addPatch
                + "\n\"\"\")]\n"
                + "    [Fact]\n"
                + "    public void AddWorks() => Assert.Equal(5, Calc.Add(2, 3));\n"
                + "}\n"
                + "[ShouldCatch(\"\"\"\n"
                + subPatch
                + "\n\"\"\")]\n"
                + "public class ClassScoped\n"
                + "{\n"
                + "    [Fact]\n"
                + "    public void SubWorks() => Assert.Equal(2, Calc.Sub(5, 3));\n"
                + "}\n"
            )

            let exitCode = Program.main [| "run"; Path.Combine(projDir, "MtpMulti.csproj") |]
            Assert.Equal(0, exitCode))
