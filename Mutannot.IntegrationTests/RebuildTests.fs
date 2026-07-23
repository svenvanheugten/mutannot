module Mutannot.IntegrationTests.RebuildTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

type RebuildTests() =
    // Mutated projects are written next to the originals and keep the original
    // assembly name (so InternalsVisibleTo and friends keep working), so their
    // build output has to be redirected elsewhere (--artifacts-path). Without that
    // redirect the mutated build emits the same-named assembly into the shared
    // bin/obj, clobbering the real one -- and because that file is now newer than
    // its sources, even rebuilding the original project leaves the stale, mutated
    // assembly in place. This runs a mutation and proves that a rebuild of the
    // original library afterwards still yields the original assembly.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Runner.fs
    +++ b/Mutannot/Runner.fs
    @@ -41,7 +41,7 @@ type Mutation =
         // --artifacts-path redirects both bin/ and obj/ into a separate tree keyed by
         // project file name, so X.mutated lands apart from X. It is passed to both the
         // build and the (--no-build) test run so the runner looks where the build wrote.
    -    let private mutatedBuildArgs = [ "--artifacts-path"; ".mutannot/artifacts" ]
    +    let private mutatedBuildArgs = []

         // Building an MTP xunit v3 project with UseMicrosoftTestingPlatformRunner=true
         // gives its executable the MTP runner entry point, which mutannot filters with
    """)>]
    member _.``a rebuild after mutating still produces the original assembly``() =
        withScratch (fun name scratch ->
            let libDir = Path.Combine(scratch, "Widget")
            let testDir = Path.Combine(scratch, "Widget.Tests")
            Directory.CreateDirectory libDir |> ignore
            Directory.CreateDirectory testDir |> ignore

            // The library under test pins an explicit assembly name; the mutated
            // build keeps that name and so would collide with the real assembly
            // unless mutannot redirects its output elsewhere.
            File.WriteAllText(
                Path.Combine(libDir, "Calc.cs"),
                "namespace Widget;\n"
                + "public static class Calc\n"
                + "{\n"
                + "    public static int Add(int x, int y) => x + y;\n"
                + "}\n"
            )

            File.WriteAllText(
                Path.Combine(libDir, "Widget.csproj"),
                sdkProject
                    [ "<Nullable>enable</Nullable>"
                      "<AssemblyName>PinnedAssemblyName</AssemblyName>" ]
                    []
            )

            // A test project whose ShouldCatch mutates the library, so a run
            // exercises the mutated-build path (getMutations -> applyMutation ->
            // ensureBuilt with mutatedBuildArgs).
            let patch =
                String.concat
                    "\n"
                    [ $"--- a/{name}/Widget/Calc.cs"
                      $"+++ b/{name}/Widget/Calc.cs"
                      "@@ -1,5 +1,5 @@"
                      " namespace Widget;"
                      " public static class Calc"
                      " {"
                      "-    public static int Add(int x, int y) => x + y;"
                      "+    public static int Add(int x, int y) => x - y;"
                      " }" ]

            File.WriteAllText(
                Path.Combine(testDir, "Tests.cs"),
                "using Mutannot.Annotations;\n"
                + "using Xunit;\n"
                + "namespace WidgetTests;\n"
                + "public class CalcTests\n"
                + "{\n"
                + "    [ShouldCatch(\"\"\"\n"
                + patch
                + "\n\"\"\")]\n"
                + "    [Fact]\n"
                + "    public void Add_Works() => Assert.Equal(5, Widget.Calc.Add(2, 3));\n"
                + "}\n"
            )

            File.WriteAllText(
                Path.Combine(testDir, "Widget.Tests.csproj"),
                xunitTestProject
                    [ "<IsPackable>false</IsPackable>"
                      "<Nullable>enable</Nullable>"
                      "<ImplicitUsings>enable</ImplicitUsings>" ]
                    []
                    [ "../Widget/Widget.csproj" ]
            )

            let testProjPath = Path.Combine(testDir, "Widget.Tests.csproj")
            let libProjPath = Path.Combine(libDir, "Widget.csproj")

            let assemblyPath =
                Path.Combine(libDir, "bin", "Debug", "net10.0", "PinnedAssemblyName.dll")

            // Build the real project and remember exactly what the library produced.
            build testProjPath
            let originalHash = sha256 (File.ReadAllBytes assemblyPath)

            // A full run: mutannot builds the mutant the way it really does (output
            // redirected away from the shared bin/obj), kills it, and exits 0.
            Assert.Equal(0, Program.main [| "run"; testProjPath |])

            // Rebuild the original library. If the mutated build had clobbered its
            // assembly, MSBuild would now see that (newer) file as up to date and
            // silently leave the stale, mutated assembly in place -- the exact bug
            // this guards.
            build libProjPath

            Assert.Equal(originalHash, sha256 (File.ReadAllBytes assemblyPath)))
