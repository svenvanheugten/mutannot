module Mutannot.IntegrationTests.OutsideGitTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

// mutannot's commands resolve the git root (see the `Git` module) before doing their
// work. Run outside any git repository that lookup fails, and the error must surface
// rather than collapsing into an empty root that leaks downstream and resurfaces as a
// cryptic failure. Each test drives a command against a withTempScratch directory --
// under the system temp path, outside mutannot's own tree and any repository -- so the
// underlying `git` invocation fails.

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -58,7 +58,6 @@
             WorkingDirectory directory
          }
          |> Command.execute
-         |> Output.throwIfErrored
          |> Output.toText)
             .Split('\n')
         |> Array.map (fun line -> line.Trim())
""")>]
let ``validate on a directory outside any git repository surfaces git's error`` () =
    withTempScratch (fun scratch ->
        File.WriteAllText(Path.Combine(scratch, "Foo.cs"), "public class Foo {}\n")

        let error =
            Assert.ThrowsAny<exn>(fun () -> Program.main [| "validate"; scratch |] |> ignore)

        Assert.Contains("not a git repository", error.Message))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -15,6 +15,5 @@ module Git =
             WorkingDirectory directory
          }
          |> Command.execute
-         |> Output.throwIfErrored
          |> Output.toText)
             .Trim()
""")>]
let ``run outside any git repository surfaces git's error`` () =
    withTempScratch (fun scratch ->
        let testProj = writeGraph scratch (graphWithKillableMutant Csharp)

        let error =
            Assert.ThrowsAny<exn>(fun () -> Program.main [| "run"; testProj |] |> ignore)

        Assert.Contains("not a git repository", error.Message))
