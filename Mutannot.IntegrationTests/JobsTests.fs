module Mutannot.IntegrationTests.JobsTests

open System
open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

// --- Scratch fixtures -----------------------------------------------------
//
// Each fixture is a tiny two-project graph (a library plus an xunit v2 test
// project that references it) carrying two ShouldCatch mutations on the library.
// Two mutations run under `--jobs 2` deterministically land in separate workers
// (round-robin: index 0 -> segment 1, index 1 -> segment 2), so both segments are
// always exercised regardless of the order getMutations returns them in.

let private writeProject (scratch: string) (libSource: string) (testSource: string) =
    let libDir = Path.Combine(scratch, "Lib")
    let testDir = Path.Combine(scratch, "Lib.Tests")
    Directory.CreateDirectory libDir |> ignore
    Directory.CreateDirectory testDir |> ignore

    File.WriteAllText(Path.Combine(libDir, "Lib.fs"), libSource)
    File.WriteAllText(Path.Combine(libDir, "Lib.fsproj"), sdkProject [] [ itemGroup [ compileInclude "Lib.fs" ] ])

    File.WriteAllText(Path.Combine(testDir, "Tests.fs"), testSource)

    File.WriteAllText(
        Path.Combine(testDir, "Lib.Tests.fsproj"),
        xunitV2TestProject [] [ "Tests.fs" ] [ "../Lib/Lib.fsproj" ]
    )

    Path.Combine(testDir, "Lib.Tests.fsproj")

// A library with two independently-mutable functions.
let private killableLib =
    String.concat "\n" [ "module Lib"; "let add x y = x + y"; "let sub x y = x - y"; "" ]

// Two tests, each pinning its own function with a ShouldCatch that flips the
// operator, so a green run must kill both mutants.
let private killableTests =
    String.concat
        "\n"
        [ "module Tests"
          ""
          "open Xunit"
          "open Mutannot.Annotations"
          "open Lib"
          ""
          "[<Fact>]"
          "[<ShouldCatch(\"\"\""
          "--- a/Lib/Lib.fs"
          "+++ b/Lib/Lib.fs"
          "@@ -1,3 +1,3 @@"
          " module Lib"
          "-let add x y = x + y"
          "+let add x y = x - y"
          " let sub x y = x - y"
          "\"\"\")>]"
          "let ``add sums`` () = Assert.Equal(5, add 2 3)"
          ""
          "[<Fact>]"
          "[<ShouldCatch(\"\"\""
          "--- a/Lib/Lib.fs"
          "+++ b/Lib/Lib.fs"
          "@@ -1,3 +1,3 @@"
          " module Lib"
          " let add x y = x + y"
          "-let sub x y = x - y"
          "+let sub x y = x + y"
          "\"\"\")>]"
          "let ``sub subtracts`` () = Assert.Equal(2, sub 5 3)"
          "" ]

// A library where one function is genuinely caught by its test and the other is
// only mutated commutatively, so its test still passes and the mutant survives.
let private survivorLib =
    String.concat "\n" [ "module Lib"; "let add x y = x + y"; "let keep x y = x + y"; "" ]

// `add` is killed (flipped to subtraction); `keep` is mutated to `y + x`, which
// its `keep 2 3 = 5` test cannot tell apart -- that mutant survives.
let private survivorTests =
    String.concat
        "\n"
        [ "module Tests"
          ""
          "open Xunit"
          "open Mutannot.Annotations"
          "open Lib"
          ""
          "[<Fact>]"
          "[<ShouldCatch(\"\"\""
          "--- a/Lib/Lib.fs"
          "+++ b/Lib/Lib.fs"
          "@@ -1,3 +1,3 @@"
          " module Lib"
          "-let add x y = x + y"
          "+let add x y = x - y"
          " let keep x y = x + y"
          "\"\"\")>]"
          "let ``add sums`` () = Assert.Equal(5, add 2 3)"
          ""
          "[<Fact>]"
          "[<ShouldCatch(\"\"\""
          "--- a/Lib/Lib.fs"
          "+++ b/Lib/Lib.fs"
          "@@ -1,3 +1,3 @@"
          " module Lib"
          " let add x y = x + y"
          "-let keep x y = x + y"
          "+let keep x y = y + x"
          "\"\"\")>]"
          "let ``keep is stable`` () = Assert.Equal(5, keep 2 3)"
          "" ]

// --- Tests ----------------------------------------------------------------

// Concurrent `--jobs` workers must not clobber one another's mutated sources,
// project files or build output. The isolation is structural: each worker owns a
// .mutannot segment, and every path the mutator/runner builds carries that
// segment. Rather than run the workers concurrently and hope a broken segment
// surfaces as a race, this asserts the invariant directly -- after a two-mutation
// run under --jobs 2, every per-segment path is on disk. Each ShouldCatch drops
// the segment from one of those paths; the matching assertion then fails
// deterministically.
[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Mutator.fs
+++ b/Mutannot/Mutator.fs
@@ -34,3 +34,3 @@ module Mutator =
         let ext = Path.GetExtension path
-        Path.Combine(dir, $"{name}.{segment}.mutated{ext}")
+        Path.Combine(dir, $"{name}.mutated{ext}")

""")>]
[<ShouldCatch("""
--- a/Mutannot/Mutator.fs
+++ b/Mutannot/Mutator.fs
@@ -48,3 +48,3 @@ module Mutator =
     let private toMutatedSourceAbsPath (gitRoot: string) (segment: int) (absPath: string) =
-        Path.Combine(gitRoot, ".mutannot", string segment, Path.GetRelativePath(gitRoot, absPath))
+        Path.Combine(gitRoot, ".mutannot", Path.GetRelativePath(gitRoot, absPath))

""")>]
[<ShouldCatch("""
--- a/Mutannot/Runner.fs
+++ b/Mutannot/Runner.fs
@@ -48,3 +48,3 @@ module Runner =
         [ "--artifacts-path"
-          Path.Combine(gitRoot, ".mutannot", string segment, "artifacts") ]
+          Path.Combine(gitRoot, ".mutannot", "artifacts") ]

""")>]
let ``run --jobs gives each worker its own .mutannot segment`` () =
    withScratch (fun scratch ->
        let testProj = writeProject scratch killableLib killableTests

        let exitCode = Program.main [| "run"; testProj; "--jobs"; "2" |]

        Assert.Equal(0, exitCode)

        // Per-segment build output (mutatedBuildArgs): dropping the segment routes
        // both workers into a shared .mutannot/artifacts.
        Assert.True(Directory.Exists(Path.Combine(scratch, ".mutannot", "1", "artifacts")))
        Assert.True(Directory.Exists(Path.Combine(scratch, ".mutannot", "2", "artifacts")))

        // Per-segment mutated sources (toMutatedSourceAbsPath): dropping the segment
        // routes both workers' copies into a shared .mutannot/Lib/Lib.fs.
        Assert.True(File.Exists(Path.Combine(scratch, ".mutannot", "1", "Lib", "Lib.fs")))
        Assert.True(File.Exists(Path.Combine(scratch, ".mutannot", "2", "Lib", "Lib.fs")))

        // Per-segment mutated project files (toMutatedProjectPath): dropping the
        // segment collapses both workers onto a single Lib.Tests.mutated.fsproj.
        Assert.True(File.Exists(Path.Combine(scratch, "Lib.Tests", "Lib.Tests.1.mutated.fsproj")))
        Assert.True(File.Exists(Path.Combine(scratch, "Lib.Tests", "Lib.Tests.2.mutated.fsproj"))))

// --jobs below 1 is rejected up front with exit 2, before any build. Widening the
// guard would let a zero-worker run through, which vacuously "kills" everything.
[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Program.fs
+++ b/Mutannot/Program.fs
@@ -53,3 +53,3 @@
-    if jobs < 1 then
+    if jobs < 0 then
         eprintfn "--jobs must be at least 1."
         2
""")>]
let ``run rejects --jobs below 1`` () =
    withScratch (fun scratch ->
        let testProj = writeProject scratch killableLib killableTests
        Assert.Equal(2, Program.main [| "run"; testProj; "--jobs"; "0" |]))

// A surviving mutant must still be reported (exit 3) when the mutations are spread
// across workers. One mutant is killed and one survives; under --jobs 2 they land
// in different workers, so the overall result comes from aggregating per-worker
// outcomes with `forall` (every worker killed all of its mutants). Relaxing that to
// `exists` would let the killed worker mask the surviving one.
[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Runner.fs
+++ b/Mutannot/Runner.fs
@@ -439,4 +439,4 @@ module Runner =
                 |> Async.Parallel
                 |> Async.RunSynchronously
-                |> Array.forall id
+                |> Array.exists id

""")>]
let ``run --jobs reports a surviving mutant across workers`` () =
    withScratch (fun scratch ->
        let testProj = writeProject scratch survivorLib survivorTests
        Assert.Equal(3, Program.main [| "run"; testProj; "--jobs"; "2" |]))
