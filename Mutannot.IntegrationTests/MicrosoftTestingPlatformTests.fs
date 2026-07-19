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

    // Runner detection is the gate to the whole MTP path, but the end-to-end run
    // above can't guard it: `dotnet test` (the VSTest path) runs an MTP project's
    // tests fine, it just silently ignores the filter, so on a single-test
    // project a downgrade to VSTest still kills the mutant -- only the runner
    // *selection* differs. So pin the detection directly. The patch makes the
    // IsTestingPlatformApplication check miss on a case slip, downgrading every
    // MTP project to VSTest.
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
        let projectPath =
            Path.Combine(repoRoot, "Example.Mtp.Tests", "Example.Mtp.Tests.csproj")

        // getMutations builds the project and reports that its assembly references
        // xunit v3; getRunnerKind then reads the platform properties (which only
        // exist once restored) and classifies an MTP xunit v3 project as MtpXunitV3.
        let _, referencesXunitV3 = Runner.getMutations projectPath
        Assert.Equal(Runner.MtpXunitV3, Runner.getRunnerKind projectPath referencesXunitV3)
