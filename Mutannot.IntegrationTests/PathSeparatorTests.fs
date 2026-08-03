module Mutannot.IntegrationTests.PathSeparatorTests

open Xunit
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

type PathSeparatorTests() =

    // A project authored on Windows lists its sources with backslash separators.
    // mutannot must still recognize that the project owns the patched file;
    // otherwise no *.mutated project is produced and the build fails hard.
    [<Fact>]
#if LINUX // backslashes in paths work fine on Windows
    [<ShouldCatch("""
    --- a/Mutannot/Mutator.fs
    +++ b/Mutannot/Mutator.fs
    @@ -15,5 +15,5 @@ module Mutator =
         // of how the project was authored.
         let private includeAbsPath (dir: string) (includeValue: string) =
    -        Path.GetFullPath(Path.Combine(dir, includeValue.Replace('\\', '/')))
    +        Path.GetFullPath(Path.Combine(dir, includeValue))

         let private getPatchedRelativePaths (patch: string) =
    """)>]
#endif
    member _.``mutates a project that references its source with backslashes``() =
        withScratch (fun scratch ->
            let validatorSource =
                String.concat
                    "\n"
                    [ "namespace Example"
                      ""
                      "open System"
                      ""
                      "module Validator ="
                      "    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                      "" ]

            // A test that pins the validator's behaviour and carries a ShouldCatch
            // mutating the backslash-referenced source. A green run must kill that
            // mutant, which mutannot can only do if it recognizes -- despite the
            // backslash -- that the library owns the patched file and writes a
            // *.mutated project for it.
            let patch =
                diff
                    "BackslashSource/Sub/Validator.fs"
                    "@@ -3,4 +3,4 @@ namespace Example"
                    [ " open System"
                      ""
                      " module Validator ="
                      "-    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                      "+    let isAllowed (now: DateTime) (date: DateTime) = now <= date" ]

            let testSource =
                String.concat
                    "\n"
                    [ "namespace Example"
                      ""
                      "open Example"
                      "open Mutannot.Annotations"
                      "open Xunit"
                      "open System"
                      ""
                      fsharpShouldCatch patch
                      "type ValidatorTests() ="
                      "    [<Fact>]"
                      "    member _.``You're allowed to pick the current day``() ="
                      "        let now = DateTime(2026, 5, 12, 17, 17, 13)"
                      "        let date = DateTime(2026, 5, 12)"
                      "        Assert.True <| Validator.isAllowed now date"
                      "" ]

            // The library authors its <Compile> with a backslash separator (the whole
            // point of the test), while the source is written at Sub/Validator.fs.
            let graph =
                { Projects =
                    [ { library Fsharp "BackslashSource" [ file "Sub/Validator.fs" validatorSource ] with
                          Compiles = Some [ "Sub\\Validator.fs" ] }
                      testProject
                          Fsharp
                          XunitV2
                          "BackslashSource.Tests"
                          [ "../BackslashSource/BackslashSource.fsproj" ]
                          [ file "ValidatorTests.fs" testSource ] ]
                  RunTarget = "BackslashSource.Tests/BackslashSource.Tests.fsproj" }

            Assert.Equal(0, graph |> runIn scratch))
