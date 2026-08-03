module Mutannot.IntegrationTests.InternalsVisibleToTests

open Xunit
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
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
        withScratch (fun scratch ->
            // A library that exposes an internal member to its test assembly by
            // name. It pins no explicit <AssemblyName>, so the assembly name is
            // the project file name -- exactly what mutating would rename.
            let secret =
                "namespace IvtLib;\ninternal class Secret { public static int Answer => 41; }\n"

            // Flips the internal's value; caught by a test that can only reach it
            // while the mutated test assembly keeps its IVT-granted name.
            let patch =
                diff
                    "IvtLib/Secret.cs"
                    "@@ -1,2 +1,2 @@"
                    [ " namespace IvtLib;"
                      "-internal class Secret { public static int Answer => 41; }"
                      "+internal class Secret { public static int Answer => 42; }" ]

            let testSource =
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "public class SecretTests\n"
                + "{\n"
                + "    [Fact]\n"
                + "    [ShouldCatch(@\"\n"
                + patch
                + "\n\")]\n"
                + "    public void Answer_is_41() => Assert.Equal(41, IvtLib.Secret.Answer);\n"
                + "}\n"

            let graph =
                { Projects =
                    [ { library Csharp "IvtLib" [ file "Secret.cs" secret ] with
                          Items = [ "<InternalsVisibleTo Include=\"IvtLib.Tests\" />" ] }
                      testProject
                          Csharp
                          XunitV2
                          "IvtLib.Tests"
                          [ "../IvtLib/IvtLib.csproj" ]
                          [ file "SecretTests.cs" testSource ] ]
                  RunTarget = "IvtLib.Tests/IvtLib.Tests.csproj" }

            Assert.Equal(0, graph |> runIn scratch))
