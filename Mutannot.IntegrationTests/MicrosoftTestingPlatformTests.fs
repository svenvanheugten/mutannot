module Mutannot.IntegrationTests.MicrosoftTestingPlatformTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

[<Collection(ExampleProjectsCollection)>]
type MicrosoftTestingPlatformTests() =
    // End to end against a real Microsoft.Testing.Platform xunit v3 project. Its
    // MTP + xunit.v3 configuration lives in a Directory.Build.props, so this only
    // passes if the runner is detected through msbuild evaluation rather than by
    // reading the project file, and if the xunit-native filter actually isolates
    // the mutated test.
    [<Fact>]
    member _.``mutannot kills mutants in a Microsoft.Testing.Platform xunit v3 project``() =
        let exitCode =
            Program.main
                [| "run"
                   Path.Combine(repoRoot, "Example.Mtp.Tests", "Example.Mtp.Tests.csproj") |]

        Assert.Equal(0, exitCode)

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
        withScratch (fun name scratch ->
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
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "    <IsPackable>false</IsPackable>\n"
                + "    <Nullable>enable</Nullable>\n"
                + "    <ImplicitUsings>enable</ImplicitUsings>\n"
                + "    <OutputType>Exe</OutputType>\n"
                + "    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"xunit.v3\" Version=\"3.1.0\" />\n"
                + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
                + "      <PrivateAssets>all</PrivateAssets>\n"
                + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                + "    </PackageReference>\n"
                + "    <ProjectReference Include=\"../../Mutannot.Annotations/Mutannot.Annotations.fsproj\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            // The patch that the target test's ShouldCatch applies: it breaks Add
            // so the target fails once mutated, i.e. the mutant is killed.
            let patch =
                String.concat
                    "\n"
                    [ $"--- a/{name}/Mtp/Calc.cs"
                      $"+++ b/{name}/Mtp/Calc.cs"
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
