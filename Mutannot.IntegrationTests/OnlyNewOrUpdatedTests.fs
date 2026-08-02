module Mutannot.IntegrationTests.OnlyNewOrUpdatedTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport
open Fli

// `run --only-new-or-updated-since <base branch>` narrows mutation testing to the
// ShouldCatch patches a branch adds or changes relative to a base branch, so a PR
// pipeline can check just the mutations it touches without running the whole suite.
// These tests build real scratch projects and commit them into the scratch
// repository, so a base actually exists to diff against; they lean on a
// deliberately *surviving* mutation (one the test does not catch, which would make
// a full `run` exit 3) to prove that filtering really skipped or kept a mutation
// rather than the run merely happening to pass. Because the diff against base runs
// through Vcs, each scenario is a [<Theory>] over both backends: `jj = false` runs
// it in a git repository (withScratch), `jj = true` in a jj repository not
// co-located with git (withJjScratch). markBaseAndStartFeature and commitFeature
// hide the per-backend mechanics of recording the base and moving onto the feature.

// The library under test: two independent functions so a patch can mutate one
// without the test covering the other, giving us a surviving mutant on demand.
let private calcSource =
    "namespace Calc;\n"
    + "public static class Calc\n"
    + "{\n"
    + "    public static int Add(int x, int y) => x + y;\n"
    + "    public static int Sub(int x, int y) => x - y;\n"
    + "}\n"

// A patch that flips `Add`'s operator. The tests below assert on `Add`, so this
// mutant is caught (killed).
let private mutateAdd =
    diff
        "Calc/Calc.cs"
        "@@ -2,4 +2,4 @@"
        [ " public static class Calc"
          " {"
          "-    public static int Add(int x, int y) => x + y;"
          "+    public static int Add(int x, int y) => x - y;"
          "     public static int Sub(int x, int y) => x - y;" ]

// A patch that flips `Sub`'s operator. The tests only assert on `Add`, so this
// mutant goes undetected (survives), which is what lets us tell "this mutation was
// run" apart from "the run passed".
let private mutateSub =
    diff
        "Calc/Calc.cs"
        "@@ -3,4 +3,4 @@"
        [ " {"
          "     public static int Add(int x, int y) => x + y;"
          "-    public static int Sub(int x, int y) => x - y;"
          "+    public static int Sub(int x, int y) => x + y;"
          " }" ]

// A C# test class carrying one ShouldCatch patch, formatted so the raw string
// literal reflection reads matches, patch-for-patch, what extractPatches scans out
// of the file (see PatchValidator.newOrUpdatedPatches) -- the closing `"""` sits at
// column 0, exactly as the other run tests write it.
let private testClass (className: string) (methodName: string) (patch: string) =
    "using Mutannot.Annotations;\n"
    + "using Xunit;\n"
    + "namespace CalcTests;\n"
    + $"public class {className}\n"
    + "{\n"
    + "    "
    + csharpShouldCatch patch
    + "\n"
    + "    [Fact]\n"
    + $"    public void {methodName}() => Assert.Equal(5, Calc.Calc.Add(2, 3));\n"
    + "}\n"

let private runGit (dir: string) (args: string list) =
    cli {
        Exec "git"
        Arguments args
        WorkingDirectory dir
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

// Stages everything and commits it under a fixed identity, so these tests don't
// depend on the machine's global git config having a user configured (it may not,
// e.g. in the Nix sandbox).
let private commit (dir: string) (message: string) =
    runGit dir [ "add"; "-A" ]

    runGit
        dir
        [ "-c"
          "user.email=test@example.com"
          "-c"
          "user.name=Test"
          "-c"
          "commit.gpgsign=false"
          "commit"
          "-m"
          message ]

let private runJj (dir: string) (args: string list) =
    cli {
        Exec "jj"
        Arguments args
        WorkingDirectory dir
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

// Records the base files just written as the base to diff against, then moves onto a
// fresh feature line whose later edits are what `--only-new-or-updated-since base`
// sees. Under git that means committing the base and branching off for the feature;
// under jj, bookmarking the auto-snapshotted working copy and `jj new`ing onto an
// empty child change.
let private markBaseAndStartFeature (jj: bool) (dir: string) =
    if jj then
        runJj dir [ "bookmark"; "create"; "base"; "-r"; "@" ]
        runJj dir [ "new" ]
    else
        commit dir "base"
        runGit dir [ "branch"; "-M"; "base" ]
        runGit dir [ "checkout"; "-b"; "feature" ]

// Records the feature edits so `--only-new-or-updated-since` can diff them against
// base. git needs an explicit commit; jj auto-snapshots the working copy, so there
// is nothing to do.
let private commitFeature (jj: bool) (dir: string) (message: string) =
    if not jj then
        commit dir message

// Writes the shared Calc library and an xunit v2 test project into `scratch`,
// returning (libDir, testDir, testProjectPath). The test project globs *.cs
// implicitly, so a test can add or edit .cs files without touching the .csproj --
// keeping the .csproj out of the diff the feature inspects.
let private scaffold (scratch: string) =
    let libDir = Path.Combine(scratch, "Calc")
    let testDir = Path.Combine(scratch, "Calc.Tests")
    Directory.CreateDirectory libDir |> ignore
    Directory.CreateDirectory testDir |> ignore

    File.WriteAllText(Path.Combine(libDir, "Calc.cs"), calcSource)
    File.WriteAllText(Path.Combine(libDir, "Calc.csproj"), sdkProject [ "<Nullable>enable</Nullable>" ] [])

    File.WriteAllText(
        Path.Combine(testDir, "Calc.Tests.csproj"),
        xunitV2TestProject
            [ "<IsPackable>false</IsPackable>"
              "<Nullable>enable</Nullable>"
              "<ImplicitUsings>enable</ImplicitUsings>" ]
            []
            [ "../Calc/Calc.csproj" ]
    )

    libDir, testDir, Path.Combine(testDir, "Calc.Tests.csproj")

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
[<ShouldCatch("""
--- a/Mutannot/Runner.fs
+++ b/Mutannot/Runner.fs
@@ -425,7 +425,7 @@
             |> List.filter (fun mutation ->
                 match maybeAllowedPatches with
                 | None -> true
-                | Some allowedPatches -> Set.contains mutation.Patch allowedPatches)
+                | Some allowedPatches -> true)

         // The MtpXunitV3 control runs use `dotnet run --no-build`, so the original
         // must first be built with the MTP runner entry point. Do it once, up front,
""")>]
let ``run --only-new-or-updated-since runs a newly added mutation and skips an unchanged one`` (jj: bool) =
    withScratchFor jj (fun scratch ->
        let _, testDir, testProject = scaffold scratch

        // Base: a surviving mutation (mutates Sub, which no test catches). A full
        // `run` would exit 3 because of it -- so if the feature keeps it, we'll see
        // that.
        File.WriteAllText(Path.Combine(testDir, "OldTests.cs"), testClass "OldTests" "OldPasses" mutateSub)
        markBaseAndStartFeature jj scratch

        // Feature: add a new test file with a killed mutation (mutates Add, which its
        // test catches). Only this file appears in the diff against base.
        File.WriteAllText(Path.Combine(testDir, "NewTests.cs"), testClass "NewTests" "NewKilled" mutateAdd)
        commitFeature jj scratch "add new mutation"

        // Only the new mutation is run: it is killed, and the pre-existing surviving
        // mutation is skipped because its file didn't change, so the run succeeds.
        let scopedExit =
            Program.main [| "run"; testProject; "--only-new-or-updated-since"; "base" |]

        Assert.Equal(0, scopedExit)

        // Without the flag every mutation runs, including the surviving one, so the
        // run fails (exit 3). This is what proves the scoped run above actually
        // skipped it rather than passing for some unrelated reason.
        let fullExit = Program.main [| "run"; testProject |]
        Assert.Equal(3, fullExit))

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``run --only-new-or-updated-since reruns an updated mutation, not its base version`` (jj: bool) =
    withScratchFor jj (fun scratch ->
        let _, testDir, testProject = scaffold scratch

        let testFile = Path.Combine(testDir, "Tests.cs")

        // Base: a mutation that its test catches (mutates Add), so on base this
        // mutation is killed.
        File.WriteAllText(testFile, testClass "Tests" "AddTest" mutateAdd)
        markBaseAndStartFeature jj scratch

        // Feature: update the same mutation to a surviving one (now mutates Sub,
        // which the test doesn't catch).
        File.WriteAllText(testFile, testClass "Tests" "AddTest" mutateSub)
        commitFeature jj scratch "update mutation"

        // The updated (surviving) patch is what gets run, so the run fails (exit 3).
        // Had it run the base (killed) patch, or filtered the change out entirely,
        // the run would have exited 0 -- so exit 3 confirms the updated patch, and
        // only it, was run.
        let scopedExit =
            Program.main [| "run"; testProject; "--only-new-or-updated-since"; "base" |]

        Assert.Equal(3, scopedExit))

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -89,7 +89,6 @@
         |> Array.filter (String.IsNullOrWhiteSpace >> not)
         // A modified-then-deleted file still shows in the diff but no longer exists
         // on disk, so it carries no working-tree patches; drop it.
-        |> Array.filter (fun relativePath -> File.Exists(Path.Combine(gitRoot, relativePath)))
         |> Array.toList

     // The contents of `relativePath` as committed at `baseBranch`, read with
""")>]
[<ShouldCatch("""
--- a/Mutannot/Jj.fs
+++ b/Mutannot/Jj.fs
@@ -68,7 +68,6 @@
         |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
         // A modified-then-deleted file still shows in the diff but no longer exists
         // on disk, so it carries no working-copy patches; drop it.
-        |> Array.filter (fun relativePath -> File.Exists(Path.Combine(root, relativePath)))
         |> Array.toList

     // The contents of `relativePath` as committed at `baseRevision`, read with
""")>]
let ``run --only-new-or-updated-since drops a source file the feature deleted`` (jj: bool) =
    withScratchFor jj (fun scratch ->
        let libDir, testDir, testProject = scaffold scratch

        // Base: an extra source file (matching the *.cs filter) that exists only to be
        // removed on the feature. A deletion still shows up in the diff against base
        // even though the file no longer exists on disk.
        let doomed = Path.Combine(libDir, "Doomed.cs")

        File.WriteAllText(
            doomed,
            "namespace Calc;\npublic static class Doomed { public static int Id(int x) => x; }\n"
        )

        markBaseAndStartFeature jj scratch

        // Feature: add a new killed mutation and delete the base-only source file. The
        // deleted file appears in the diff but is dropped rather than read off disk --
        // reading it would throw FileNotFoundException (see PatchValidator, which
        // File.ReadAllTexts every changed path). Without that drop the run below would
        // blow up instead of exiting 0.
        File.WriteAllText(Path.Combine(testDir, "NewTests.cs"), testClass "NewTests" "NewKilled" mutateAdd)
        File.Delete doomed
        commitFeature jj scratch "add mutation and delete file"

        // The deletion is skipped, the remaining new mutation is killed, so the run
        // succeeds.
        let scopedExit =
            Program.main [| "run"; testProject; "--only-new-or-updated-since"; "base" |]

        Assert.Equal(0, scopedExit))
