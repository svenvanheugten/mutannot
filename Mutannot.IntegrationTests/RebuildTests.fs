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
    // assembly in place. This test builds for real and proves that a rebuild of
    // the original after a mutation still yields the original assembly.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Program.fs
    +++ b/Mutannot/Program.fs
    @@ -40,7 +40,7 @@ type Mutation =
     // --artifacts-path redirects both bin/ and obj/ into a separate tree keyed by
     // project file name, so X.mutated lands apart from X. It is passed to both the
     // build and the (--no-build) test run so the runner looks where the build wrote.
    -let mutatedBuildArgs = [ "--artifacts-path"; ".mutannot/artifacts" ]
    +let mutatedBuildArgs = []

     // Building an MTP xunit v3 project with UseMicrosoftTestingPlatformRunner=true
     // gives its executable the MTP runner entry point, which mutannot filters with
    """)>]
    member _.``a rebuild after mutating still produces the original assembly``() =
        withScratch (fun name scratch ->
            let projDir = Path.Combine(scratch, "Widget")
            Directory.CreateDirectory projDir |> ignore

            File.WriteAllText(Path.Combine(projDir, "Calc.fs"), "module Calc\nlet value = 1\n")

            // The project pins an explicit assembly name; the mutated build keeps
            // that name and so would collide with the real assembly unless
            // mutannot redirects its output elsewhere.
            File.WriteAllText(
                Path.Combine(projDir, "Widget.fsproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net10.0</TargetFramework>\n"
                + "    <AssemblyName>PinnedAssemblyName</AssemblyName>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <Compile Include=\"Calc.fs\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            let projPath = Path.Combine(projDir, "Widget.fsproj")

            let assemblyPath =
                Path.Combine(projDir, "bin", "Debug", "net10.0", "PinnedAssemblyName.dll")

            // Build the real project and remember exactly what it produced.
            build projPath
            let originalHash = sha256 (File.ReadAllBytes assemblyPath)

            let patch =
                String.concat
                    "\n"
                    [ $"--- a/{name}/Widget/Calc.fs"
                      $"+++ b/{name}/Widget/Calc.fs"
                      "@@ -1,2 +1,2 @@"
                      " module Calc"
                      "-let value = 1"
                      "+let value = 2"
                      "" ]

            Mutator.applyMutation projPath patch |> ignore

            // Build the mutated project the way mutannot does (its output
            // redirected away from the shared bin/obj), then rebuild the original.
            // If the mutated build clobbered the original's assembly, MSBuild now
            // sees that (newer) file as up to date, so this rebuild silently
            // leaves the stale, mutated assembly in place -- the exact bug this
            // guards.
            Program.ensureBuilt Program.mutatedBuildArgs (Path.Combine(projDir, "Widget.mutated.fsproj"))
            build projPath

            Assert.Equal(originalHash, sha256 (File.ReadAllBytes assemblyPath)))
