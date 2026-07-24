module Mutannot.IntegrationTests.PathSeparatorTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

type PathSeparatorTests() =

    // A project authored on Windows lists its sources with backslash separators.
    // mutannot must still recognize that the project owns the patched file;
    // otherwise no *.mutated project is produced and the build fails hard.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Mutator.fs
    +++ b/Mutannot/Mutator.fs
    @@ -91,7 +91,7 @@ module Mutator =
                 |> Seq.choose (fun e ->
                     match e.Attribute(XName.Get "Include") with
                     | null -> None
    -                | attr -> Some(Path.GetFullPath(Path.Combine(dir, normalizeSeparators attr.Value))))
    +                | attr -> Some(Path.GetFullPath(Path.Combine(dir, attr.Value))))
                 |> Seq.toList

             let ownsFile =
    """)>]
    member _.``mutates a project that references its source with backslashes``() =
        withScratch (fun name scratch ->
            let libDir = Path.Combine(scratch, "BackslashSource")
            let testDir = Path.Combine(scratch, "BackslashSource.Tests")
            Directory.CreateDirectory(Path.Combine(libDir, "Sub")) |> ignore
            Directory.CreateDirectory testDir |> ignore

            // A copy of the example validator, referenced with a Windows-style
            // backslash separator like a project authored on Windows would have.
            File.Copy(
                Path.Combine(gitRoot, "Example.FSharp", "Validator.fs"),
                Path.Combine(libDir, "Sub", "Validator.fs")
            )

            File.WriteAllText(
                Path.Combine(libDir, "BackslashSource.fsproj"),
                sdkProject [] [ itemGroup [ compileInclude "Sub\\Validator.fs" ] ]
            )

            // A test that pins the validator's behaviour and carries a ShouldCatch
            // mutating the backslash-referenced source. A green run must kill that
            // mutant, which mutannot can only do if it recognizes -- despite the
            // backslash -- that the library owns the patched file and writes a
            // *.mutated project for it. The patch is generated here so the scratch
            // directory's runtime name can be embedded in its paths.
            File.WriteAllText(
                Path.Combine(testDir, "ValidatorTests.fs"),
                String.concat
                    "\n"
                    [ "namespace Example"
                      ""
                      "open Example"
                      "open Mutannot.Annotations"
                      "open Xunit"
                      "open System"
                      ""
                      "[<ShouldCatch(\"\"\""
                      $"--- a/{name}/BackslashSource/Sub/Validator.fs"
                      $"+++ b/{name}/BackslashSource/Sub/Validator.fs"
                      "@@ -3,4 +3,4 @@ namespace Example"
                      " open System"
                      ""
                      " module Validator ="
                      "-    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                      "+    let isAllowed (now: DateTime) (date: DateTime) = now <= date"
                      "\"\"\")>]"
                      "type ValidatorTests() ="
                      "    [<Fact>]"
                      "    member _.``You're allowed to pick the current day``() ="
                      "        let now = DateTime(2026, 5, 12, 17, 17, 13)"
                      "        let date = DateTime(2026, 5, 12)"
                      "        Assert.True <| Validator.isAllowed now date"
                      "" ]
            )

            File.WriteAllText(
                Path.Combine(testDir, "BackslashSource.Tests.fsproj"),
                xunitTestProject [] [ "ValidatorTests.fs" ] [ "../BackslashSource/BackslashSource.fsproj" ]
            )

            let exitCode =
                Program.main [| "run"; Path.Combine(testDir, "BackslashSource.Tests.fsproj") |]

            Assert.Equal(0, exitCode))
