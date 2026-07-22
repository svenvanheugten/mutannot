module Mutannot.IntegrationTests.InternalsVisibleToTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

type InternalsVisibleToTests() =
    // A test project reaching a library's internals through InternalsVisibleTo
    // only compiles while both assemblies keep their original names. Mutating
    // renames the project files to X.mutated, so mutannot has to pin the assembly
    // names back to the originals -- otherwise the mutated test assembly becomes
    // "X.Tests.mutated", the library no longer grants it access, and the mutated
    // build fails to compile. This drives a full `run` over a real IVT pair whose
    // test reaches an internal and whose ShouldCatch mutates that internal: a green
    // run has to kill the mutant, which is only possible if the mutated test
    // assembly keeps its name and thus its IVT access. If assembly-name pinning
    // broke, the mutated build wouldn't compile, the run would fail, and the exit
    // code would not be 0.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Mutator.fs
    +++ b/Mutannot/Mutator.fs
    @@ -217,3 +217,3 @@ module Mutator =
                         XName.Get "PropertyGroup",
    -                    XElement(XName.Get "AssemblyName", Path.GetFileNameWithoutExtension projectInfo.AbsolutePath)
    +                    XElement(XName.Get "AssemblyName", Path.GetFileNameWithoutExtension mutatedPath)
                     )
    """)>]
    member _.``a mutated build preserves assembly names so InternalsVisibleTo keeps working``() =
        withScratch (fun name scratch ->
            let libDir = Path.Combine(scratch, "IvtLib")
            let testDir = Path.Combine(scratch, "IvtLib.Tests")
            Directory.CreateDirectory libDir |> ignore
            Directory.CreateDirectory testDir |> ignore

            // A library that exposes an internal member to its test assembly by
            // name. It pins no explicit <AssemblyName>, so the assembly name is
            // the project file name -- exactly what mutating would rename.
            File.WriteAllText(
                Path.Combine(libDir, "Secret.cs"),
                "namespace IvtLib;\ninternal class Secret { public static int Answer => 41; }\n"
            )

            File.WriteAllText(
                Path.Combine(libDir, "IvtLib.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <InternalsVisibleTo Include=\"IvtLib.Tests\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            // A real xunit test that reaches into the library's internal (so its
            // assembly only compiles while still named IvtLib.Tests) and carries a
            // ShouldCatch that mutates that internal's value. Running mutannot over
            // it must kill the mutant: the baseline passes on 41, the mutated build
            // flips it to 42 and the assertion fails. The patch is generated here so
            // the scratch directory's runtime name can be embedded in its paths.
            File.WriteAllText(
                Path.Combine(testDir, "SecretTests.cs"),
                String.concat
                    "\n"
                    [ "using Mutannot.Annotations;"
                      "using Xunit;"
                      "public class SecretTests"
                      "{"
                      "    [Fact]"
                      "    [ShouldCatch(@\""
                      $"--- a/{name}/IvtLib/Secret.cs"
                      $"+++ b/{name}/IvtLib/Secret.cs"
                      "@@ -1,2 +1,2 @@"
                      " namespace IvtLib;"
                      "-internal class Secret { public static int Answer => 41; }"
                      "+internal class Secret { public static int Answer => 42; }"
                      "\")]"
                      "    public void Answer_is_41() => Assert.Equal(41, IvtLib.Secret.Answer);"
                      "}"
                      "" ]
            )

            File.WriteAllText(
                Path.Combine(testDir, "IvtLib.Tests.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <ProjectReference Include=\"../IvtLib/IvtLib.csproj\" />\n"
                + "    <ProjectReference Include=\"../../Mutannot.Annotations/Mutannot.Annotations.fsproj\" />\n"
                + "  </ItemGroup>\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />\n"
                + "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n"
                + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
                + "      <PrivateAssets>all</PrivateAssets>\n"
                + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
                + "    </PackageReference>\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            let exitCode =
                Program.main [| "run"; Path.Combine(testDir, "IvtLib.Tests.csproj") |]

            Assert.Equal(0, exitCode))
