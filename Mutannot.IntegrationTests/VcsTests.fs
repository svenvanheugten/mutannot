module Mutannot.IntegrationTests.VcsTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

// The `sourceFiles` scan behaves identically under git and under a jj repository not
// co-located with git, so each shared behaviour is a [<Theory>] over both backends.
// Both backends' mutations are stacked: a failing row fails the whole theory, so the
// Git.fs mutation is caught by the git row and the Jj.fs one by the jj row.

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -36,6 +36,6 @@
             .Split('\n')
         |> Array.map (fun line -> line.Trim())
         |> Array.filter (String.IsNullOrWhiteSpace >> not)
-        |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
+        |> Array.map (fun relativePath -> Path.GetFullPath(relativePath))
         |> Array.filter File.Exists
         |> Array.toList
""")>]
[<ShouldCatch("""
--- a/Mutannot/Jj.fs
+++ b/Mutannot/Jj.fs
@@ -39,7 +39,7 @@
             .Split('\n')
         |> Array.map (fun line -> line.Trim())
         |> Array.filter (String.IsNullOrWhiteSpace >> not)
-        |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
+        |> Array.map (fun relativePath -> Path.GetFullPath(relativePath))
         |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
         |> Array.filter File.Exists
         |> Array.toList
""")>]
let ``sourceFiles returns absolute paths for untracked files, recursing into subdirectories`` (jj: bool) =
    withScratchFor jj (fun scratch ->
        Directory.CreateDirectory(Path.Combine(scratch, "Nested")) |> ignore
        File.WriteAllText(Path.Combine(scratch, "Foo.cs"), "public class Foo {}\n")
        File.WriteAllText(Path.Combine(scratch, "Nested", "Bar.fs"), "module Bar\n")

        let result = (if jj then Jj.sourceFiles else Git.sourceFiles) scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Foo.cs")), result)
        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Nested", "Bar.fs")), result))

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -33,10 +33,7 @@
                 [ "ls-files"
                   "--cached"
                   "--others"
-                  "--exclude-standard"
-                  "--"
-                  "*.cs"
-                  "*.fs" ]
+                  "--exclude-standard" ]

             WorkingDirectory directory
          }
""")>]
[<ShouldCatch("""
--- a/Mutannot/Jj.fs
+++ b/Mutannot/Jj.fs
@@ -40,6 +40,5 @@
         |> Array.map (fun line -> line.Trim())
         |> Array.filter (String.IsNullOrWhiteSpace >> not)
         |> Array.map (fun relativePath -> Path.GetFullPath(Path.Combine(directory, relativePath)))
-        |> Array.filter (fun path -> Path.GetExtension path = ".cs" || Path.GetExtension path = ".fs")
         |> Array.filter File.Exists
         |> Array.toList
""")>]
let ``sourceFiles ignores files that are not C# or F#`` (jj: bool) =
    withScratchFor jj (fun scratch ->
        File.WriteAllText(Path.Combine(scratch, "Keep.cs"), "public class Keep {}\n")
        File.WriteAllText(Path.Combine(scratch, "Skip.txt"), "not source\n")
        File.WriteAllText(Path.Combine(scratch, "Data.json"), "{}\n")

        let result = (if jj then Jj.sourceFiles else Git.sourceFiles) scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Keep.cs")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "Skip.txt")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "Data.json")), result))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -33,7 +33,6 @@
                 [ "ls-files"
                   "--cached"
                   "--others"
-                  "--exclude-standard"
                   "--"
                   "*.cs"
                   "*.fs" ]
""")>]
let ``sourceFiles excludes gitignored files`` () =
    withScratch (fun scratch ->
        // `obj/` is gitignored (see .gitignore), so its source must be skipped even
        // though it is an untracked .cs file the scan would otherwise pick up.
        Directory.CreateDirectory(Path.Combine(scratch, "obj")) |> ignore
        File.WriteAllText(Path.Combine(scratch, "Keep.cs"), "public class Keep {}\n")
        File.WriteAllText(Path.Combine(scratch, "obj", "Ignored.cs"), "public class Ignored {}\n")

        let result = Git.sourceFiles scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Keep.cs")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "obj", "Ignored.cs")), result))
