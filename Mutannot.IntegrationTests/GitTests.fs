module Mutannot.IntegrationTests.GitTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

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
[<Fact>]
let ``sourceFiles returns absolute paths for untracked files, recursing into subdirectories`` () =
    withScratch (fun _ scratch ->
        Directory.CreateDirectory(Path.Combine(scratch, "Nested")) |> ignore
        File.WriteAllText(Path.Combine(scratch, "Foo.cs"), "public class Foo {}\n")
        File.WriteAllText(Path.Combine(scratch, "Nested", "Bar.fs"), "module Bar\n")

        let result = Git.sourceFiles scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Foo.cs")), result)
        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Nested", "Bar.fs")), result))

[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -28,7 +28,7 @@
     let sourceFiles (directory: string) =
         (cli {
             Exec "git"
-            Arguments [ "ls-files"; "--cached"; "--others"; "--exclude-standard"; "--"; "*.cs"; "*.fs" ]
+            Arguments [ "ls-files"; "--cached"; "--others"; "--exclude-standard" ]
             WorkingDirectory directory
          }
          |> Command.execute
""")>]
[<Fact>]
let ``sourceFiles ignores files that are not C# or F#`` () =
    withScratch (fun _ scratch ->
        File.WriteAllText(Path.Combine(scratch, "Keep.cs"), "public class Keep {}\n")
        File.WriteAllText(Path.Combine(scratch, "Skip.txt"), "not source\n")
        File.WriteAllText(Path.Combine(scratch, "Data.json"), "{}\n")

        let result = Git.sourceFiles scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Keep.cs")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "Skip.txt")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "Data.json")), result))

[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -28,7 +28,7 @@
     let sourceFiles (directory: string) =
         (cli {
             Exec "git"
-            Arguments [ "ls-files"; "--cached"; "--others"; "--exclude-standard"; "--"; "*.cs"; "*.fs" ]
+            Arguments [ "ls-files"; "--cached"; "--others"; "--"; "*.cs"; "*.fs" ]
             WorkingDirectory directory
          }
          |> Command.execute
""")>]
[<Fact>]
let ``sourceFiles excludes gitignored files`` () =
    withScratch (fun _ scratch ->
        // `obj/` is gitignored (see .gitignore), so its source must be skipped even
        // though it is an untracked .cs file the scan would otherwise pick up.
        Directory.CreateDirectory(Path.Combine(scratch, "obj")) |> ignore
        File.WriteAllText(Path.Combine(scratch, "Keep.cs"), "public class Keep {}\n")
        File.WriteAllText(Path.Combine(scratch, "obj", "Ignored.cs"), "public class Ignored {}\n")

        let result = Git.sourceFiles scratch

        Assert.Contains(Path.GetFullPath(Path.Combine(scratch, "Keep.cs")), result)
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(scratch, "obj", "Ignored.cs")), result))
