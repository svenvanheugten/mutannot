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
            let projDir = Path.Combine(scratch, "Example.FSharp")
            Directory.CreateDirectory(Path.Combine(projDir, "Sub")) |> ignore

            // A copy of the example project, referenced with a Windows-style
            // backslash separator like a project authored on Windows would have.
            File.Copy(
                Path.Combine(gitRoot, "Example.FSharp", "Validator.fs"),
                Path.Combine(projDir, "Sub", "Validator.fs")
            )

            File.WriteAllText(
                Path.Combine(projDir, "Example.FSharp.fsproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <ItemGroup>\n"
                + "    <Compile Include=\"Sub\\Validator.fs\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            let patch =
                String.concat
                    "\n"
                    [ $"--- a/{name}/Example.FSharp/Sub/Validator.fs"
                      $"+++ b/{name}/Example.FSharp/Sub/Validator.fs"
                      "@@ -3,4 +3,4 @@ namespace Example"
                      " open System"
                      ""
                      " module Validator ="
                      "-    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                      "+    let isAllowed (now: DateTime) (date: DateTime) = now <= date"
                      "" ]

            Mutator.applyMutation (Path.Combine(projDir, "Example.FSharp.fsproj")) patch
            |> ignore

            Assert.True(
                File.Exists(Path.Combine(projDir, "Example.FSharp.mutated.fsproj")),
                "expected the project that references its source with backslashes to be mutated"
            ))

    // A patch whose paths use backslash separators (as a hand-written ShouldCatch
    // authored on Windows might) must still be applied. mutannot has to resolve
    // the paths on disk and hand `git apply` a `.mutannot/` path that does not
    // mix separators; otherwise the copy or the apply fails hard.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Mutator.fs
    +++ b/Mutannot/Mutator.fs
    @@ -46,7 +46,7 @@ module Mutator =
         // Path.Combine would prefix a backslash onto the forward slashes that the
         // rest of the path inherits from the git patch.
         let private toMutatedSourceRelPath (relPath: string) =
    -        ".mutannot/" + normalizeSeparators relPath
    +        Path.Combine(".mutannot", relPath)

         let private toMutatedSourceAbsPath (gitRoot: string) (absPath: string) =
             Path.Combine(gitRoot, ".mutannot", Path.GetRelativePath(gitRoot, absPath))
    """)>]
    member _.``applies a patch whose paths use backslash separators``() =
        withScratch (fun name scratch ->
            let projDir = Path.Combine(scratch, "Example.FSharp")
            Directory.CreateDirectory(Path.Combine(projDir, "Sub")) |> ignore

            File.Copy(
                Path.Combine(gitRoot, "Example.FSharp", "Validator.fs"),
                Path.Combine(projDir, "Sub", "Validator.fs")
            )

            File.WriteAllText(
                Path.Combine(projDir, "Example.FSharp.fsproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <ItemGroup>\n"
                + "    <Compile Include=\"Sub/Validator.fs\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n"
            )

            // The patch references the file with backslash separators.
            let relPath = $"{name}/Example.FSharp/Sub/Validator.fs".Replace('/', '\\')

            let patch =
                String.concat
                    "\n"
                    [ $"--- a/{relPath}"
                      $"+++ b/{relPath}"
                      "@@ -3,4 +3,4 @@ namespace Example"
                      " open System"
                      ""
                      " module Validator ="
                      "-    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date"
                      "+    let isAllowed (now: DateTime) (date: DateTime) = now <= date"
                      "" ]

            Mutator.applyMutation (Path.Combine(projDir, "Example.FSharp.fsproj")) patch
            |> ignore

            let mutatedSource =
                Path.Combine(gitRoot, ".mutannot", name, "Example.FSharp", "Sub", "Validator.fs")

            Assert.True(File.Exists mutatedSource, "expected the patched source to be mirrored under .mutannot/")
            Assert.DoesNotContain("now.Date", File.ReadAllText mutatedSource))
