module Mutannot.IntegrationTests.ControlRunTests

open System.IO
open Xunit
open Mutannot
open Mutannot.IntegrationTests.TestSupport

// mutannot establishes a green baseline before mutating anything: because a
// failing test run is how it recognizes a killed mutant, it can't tell a
// genuinely failing mutant from a broken build or runner unless the unmutated
// suite is known to pass first. When a target test fails on the unmutated
// build, `run` refuses to proceed and exits 4 rather than running mutations.
// The passing-baseline path is exercised by EndToEndTests, whose green result
// is only reachable once the baseline has passed.
[<Collection(ExampleProjectsCollection)>]
type ControlRunTests() =
    [<Fact>]
    member _.``run refuses to proceed when the unmutated target test fails``() =
        withScratch (fun _ scratch ->
            let projDir = Path.Combine(scratch, "Red")
            Directory.CreateDirectory projDir |> ignore

            // A test that fails on the unmutated build, carrying a ShouldCatch
            // annotation only so a mutation exists and the baseline actually
            // runs. The patch is never applied -- the run aborts at the baseline
            // before mutating -- so its contents are irrelevant.
            File.WriteAllText(
                Path.Combine(projDir, "RedTests.cs"),
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "[ShouldCatch(\"unused: run aborts at the baseline before applying it\")]\n"
                + "public class RedTests { [Fact] public void Fails() => Assert.True(false); }\n"
            )

            File.WriteAllText(
                Path.Combine(projDir, "Red.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <ProjectReference Include=\"../../Mutannot.Annotations/Mutannot.Annotations.fsproj\" />\n"
                + "  </ItemGroup>\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />\n"
                + "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n"
                + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            let projPath = Path.Combine(projDir, "Red.csproj")
            let exitCode = Program.main [| "run"; projPath |]
            Assert.Equal(4, exitCode))
