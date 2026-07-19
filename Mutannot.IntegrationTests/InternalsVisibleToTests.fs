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
    // build fails to compile. This builds a real IVT pair and proves the mutated
    // build still compiles.
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

            // The consumer reaches into that internal, so it only compiles while
            // its assembly is still named IvtLib.Tests.
            File.WriteAllText(
                Path.Combine(testDir, "Consumer.cs"),
                "namespace Consumers;\npublic class Consumer { public int Get() => IvtLib.Secret.Answer; }\n"
            )

            File.WriteAllText(
                Path.Combine(testDir, "IvtLib.Tests.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <ProjectReference Include=\"../IvtLib/IvtLib.csproj\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            let patch =
                String.concat
                    "\n"
                    [ $"--- a/{name}/IvtLib/Secret.cs"
                      $"+++ b/{name}/IvtLib/Secret.cs"
                      "@@ -1,2 +1,2 @@"
                      " namespace IvtLib;"
                      "-internal class Secret { public static int Answer => 41; }"
                      "+internal class Secret { public static int Answer => 42; }"
                      "" ]

            let mutatedTestProject =
                Mutator.applyMutation (Path.Combine(testDir, "IvtLib.Tests.csproj")) patch

            // Builds the mutated library and test project together. This throws on
            // a compile error (CS0122) if the mutated assemblies were renamed.
            Runner.ensureBuilt Runner.mutatedBuildArgs mutatedTestProject)
