module Mutannot.IntegrationTests.OutsideGitTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations

// mutannot's commands resolve the git root (see the `Git` module) before doing their
// work. Run outside any git repository that lookup fails, and the error must surface
// rather than collapsing into an empty root that leaks downstream and resurfaces as a
// cryptic failure. Each test drives a command against a scratch directory under the
// system temp path -- outside mutannot's own tree and any repository -- so the
// underlying `git` invocation fails.

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
[<Fact>]
let ``validate on a directory outside any git repository surfaces git's error`` () =
    // Unlike withScratch's fixtures (which live under mutannot's own git root), this
    // scratch directory sits under the system temp path, outside any repository.
    let scratch =
        Path.Combine(Path.GetTempPath(), "mutannot-nogit-" + System.Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory scratch |> ignore

    try
        File.WriteAllText(Path.Combine(scratch, "Foo.cs"), "public class Foo {}\n")

        let error =
            Assert.ThrowsAny<exn>(fun () -> Program.main [| "validate"; scratch |] |> ignore)

        Assert.Contains("not a git repository", error.Message)
    finally
        Directory.Delete(scratch, true)
