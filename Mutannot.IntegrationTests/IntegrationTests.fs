module Mutannot.IntegrationTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Fli

let private repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

[<Fact>]
let ``mutannot kills mutants in an fsproj project`` () =
    let exitCode =
        Program.main
            [| "run"
               Path.Combine(repoRoot, "Example.FSharp.Tests", "Example.FSharp.Tests.fsproj") |]

    Assert.Equal(0, exitCode)

[<Fact>]
let ``mutannot kills mutants in a csproj project`` () =
    let exitCode =
        Program.main
            [| "run"
               Path.Combine(repoRoot, "Example.CSharp.Tests", "Example.CSharp.Tests.csproj") |]

    Assert.Equal(0, exitCode)

// The mutator resolves patched paths against the git root (via `git rev-parse`),
// so the throwaway fixtures below have to live under it too.
let private gitRoot =
    (cli {
        Exec "git"
        Arguments [ "rev-parse"; "--show-toplevel" ]
     }
     |> Command.execute
     |> Output.toText)
        .Trim()

// Runs `body relName scratchAbs` against a unique, self-cleaning scratch
// directory under the git root. `.mutannot/<relName>` (where the mutator mirrors
// the sources it patches) is cleaned up alongside it.
let private withScratch (body: string -> string -> unit) =
    let name = ".inttest-" + Guid.NewGuid().ToString("N")
    let scratch = Path.Combine(gitRoot, name)

    try
        Directory.CreateDirectory scratch |> ignore
        body name scratch
    finally
        for dir in [ scratch; Path.Combine(gitRoot, ".mutannot", name) ] do
            if Directory.Exists dir then
                Directory.Delete(dir, true)

let private build (projectPath: string) =
    cli {
        Exec "dotnet"
        Arguments [ "build"; projectPath; "-c"; "Debug" ]
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let private sha256 (bytes: byte[]) =
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes)

// runControl establishes the green baseline the whole run depends on: because a
// failing test run is how mutannot recognizes a killed mutant, it can't tell a
// genuinely failing mutant from a broken build or runner unless the unmutated
// suite is known to pass first.
type ControlRunTests() =
    [<Fact>]
    member _.``control run passes when the target test passes``() =
        let projectPath =
            Path.Combine(repoRoot, "Example.CSharp.Tests", "Example.CSharp.Tests.csproj")

        // runControl runs against an existing build (--no-build), so build first.
        build projectPath
        Assert.Equal(0, Program.runControl Program.VSTest projectPath (Program.TestClass "Example.CalculatorTests"))

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
            Assert.NotEqual(0, Program.runControl Program.VSTest projPath (Program.TestClass "RedTests")))

type MicrosoftTestingPlatformTests() =
    // End to end against a real Microsoft.Testing.Platform xunit v3 project. Its
    // MTP + xunit.v3 configuration lives in a Directory.Build.props, so this only
    // passes if the runner is detected through msbuild evaluation rather than by
    // reading the project file, and if the xunit-native filter actually isolates
    // the mutated test.
    [<Fact>]
    member _.``mutannot kills mutants in a Microsoft.Testing.Platform xunit v3 project``() =
        let exitCode =
            Program.main
                [| "run"
                   Path.Combine(repoRoot, "Example.Mtp.Tests", "Example.Mtp.Tests.csproj") |]

        Assert.Equal(0, exitCode)

    // Runner detection is the gate to the whole MTP path, but the end-to-end run
    // above can't guard it: `dotnet test` (the VSTest path) runs an MTP project's
    // tests fine, it just silently ignores the filter, so on a single-test
    // project a downgrade to VSTest still kills the mutant -- only the runner
    // *selection* differs. So pin the detection directly. The patch makes the
    // IsTestingPlatformApplication check miss on a case slip, downgrading every
    // MTP project to VSTest.
    [<Fact>]
    [<ShouldCatch("""
    --- a/Mutannot/Program.fs
    +++ b/Mutannot/Program.fs
    @@ -155,3 +155,3 @@ let getRunnerKind projectPath referencesXunitV3 =
         match getProperty "IsTestingPlatformApplication" with
    -    | "true" ->
    +    | "True" ->
             if referencesXunitV3 then
    """)>]
    member _.``detects the runner as Microsoft.Testing.Platform xunit v3``() =
        let projectPath =
            Path.Combine(repoRoot, "Example.Mtp.Tests", "Example.Mtp.Tests.csproj")

        // getMutations builds the project and reports that its assembly references
        // xunit v3; getRunnerKind then reads the platform properties (which only
        // exist once restored) and classifies an MTP xunit v3 project as MtpXunitV3.
        let _, referencesXunitV3 = Program.getMutations projectPath
        Assert.Equal(Program.MtpXunitV3, Program.getRunnerKind projectPath referencesXunitV3)

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
            Program.ensureBuilt Program.mutatedBuildArgs mutatedTestProject)
