module Mutannot.IntegrationTests.RebuildTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
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
    @@ -45,7 +45,6 @@ module Runner =
         // The tree is keyed by segment too, so concurrent `run --jobs` workers (each
         // owning a segment) build into separate bin/obj and never race one another.
         let private mutatedBuildArgs gitRoot segment =
    -        [ "--artifacts-path"
    -          Path.Combine(gitRoot, ".mutannot", string segment, "artifacts") ]
    +        []

         // Building an MTP xunit v3 project with UseMicrosoftTestingPlatformRunner=true
    """)>]
    member _.``a rebuild after mutating still produces the original assembly``() =
        withScratch (fun scratch ->
            // The canonical killable-mutant project, but with an explicit assembly name: the
            // mutated build keeps that name and so would collide with the real assembly
            // unless mutannot redirects its output elsewhere.
            let graph = graphWithKillableMutant Csharp |> pinAssemblyName "PinnedAssemblyName"
            let testProj = writeGraph scratch graph
            let libProj = Path.Combine(scratch, "Calc", "Calc.csproj")

            let assemblyPath =
                Path.Combine(scratch, "Calc", "bin", "Debug", "net10.0", "PinnedAssemblyName.dll")

            // Build the real project and remember exactly what the library produced.
            build testProj
            let originalHash = sha256 (File.ReadAllBytes assemblyPath)

            // A full run: mutannot builds the mutant the way it really does (output
            // redirected away from the shared bin/obj), kills it, and exits 0.
            Assert.Equal(0, Program.main [| "run"; testProj |])

            // Rebuild the original library. If the mutated build had clobbered its
            // assembly, MSBuild would now see that (newer) file as up to date and
            // silently leave the stale, mutated assembly in place -- the exact bug
            // this guards.
            build libProj

            Assert.Equal(originalHash, sha256 (File.ReadAllBytes assemblyPath)))
