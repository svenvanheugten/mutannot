module Mutannot.IntegrationTests.ControlRunTests

open System.IO
open Xunit
open Mutannot
open Mutannot.IntegrationTests.TestSupport

// runControl establishes the green baseline the whole run depends on: because a
// failing test run is how mutannot recognizes a killed mutant, it can't tell a
// genuinely failing mutant from a broken build or runner unless the unmutated
// suite is known to pass first.
[<Collection(ExampleProjectsCollection)>]
type ControlRunTests() =
    [<Fact>]
    member _.``control run passes when the target test passes``() =
        let projectPath =
            Path.Combine(repoRoot, "Example.CSharp.Tests", "Example.CSharp.Tests.csproj")

        // runControl runs against an existing build (--no-build), so build first.
        build projectPath
        Assert.Equal(0, Runner.runControl Runner.VSTest projectPath (Runner.TestClass "Example.CalculatorTests"))

    [<Fact>]
    member _.``control run fails when the target test fails``() =
        withScratch (fun _ scratch ->
            let projDir = Path.Combine(scratch, "Red")
            Directory.CreateDirectory projDir |> ignore

            File.WriteAllText(
                Path.Combine(projDir, "RedTests.cs"),
                "using Xunit;\npublic class RedTests { [Fact] public void Fails() => Assert.True(false); }\n"
            )

            File.WriteAllText(
                Path.Combine(projDir, "Red.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />\n"
                + "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n"
                + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            let projPath = Path.Combine(projDir, "Red.csproj")
            build projPath
            Assert.NotEqual(0, Runner.runControl Runner.VSTest projPath (Runner.TestClass "RedTests")))
