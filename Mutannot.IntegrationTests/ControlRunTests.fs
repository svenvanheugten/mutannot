module Mutannot.IntegrationTests.ControlRunTests

open Xunit
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

// mutannot establishes a green baseline before mutating anything: because a
// failing test run is how it recognizes a killed mutant, it can't tell a
// genuinely failing mutant from a broken build or runner unless the unmutated
// suite is known to pass first. When a target test fails on the unmutated
// build, `run` refuses to proceed and exits 4 rather than running mutations.
type ControlRunTests() =
    [<Fact>]
    member _.``run refuses to proceed when the unmutated target test fails``() =
        withScratch (fun scratch ->
            // A test that fails on the unmutated build, carrying a ShouldCatch
            // annotation only so a mutation exists and the baseline actually
            // runs. The patch is never applied -- the run aborts at the baseline
            // before mutating -- so its contents are irrelevant.
            let source =
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "[ShouldCatch(\"unused: run aborts at the baseline before applying it\")]\n"
                + "public class RedTests { [Fact] public void Fails() => Assert.True(false); }\n"

            let graph =
                { Projects = [ testProject Csharp XunitV2 "Red" [] [ file "RedTests.cs" source ] ]
                  RunTarget = "Red/Red.csproj" }

            Assert.Equal(4, graph |> runIn scratch))
