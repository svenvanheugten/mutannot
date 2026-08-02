module Mutannot.IntegrationTests.CSharpTests

open Xunit
open Mutannot.Annotations
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
[<ShouldCatch("""
--- a/Mutannot/Vcs.fs
+++ b/Mutannot/Vcs.fs
@@ -46,7 +46,7 @@
     let root (directory: string) =
         match backend directory with
         | Git -> Git.root directory
-        | Jj -> Jj.root directory
+        | Jj -> Git.root directory

     let sourceFiles (directory: string) =
         match backend directory with
""")>]
#if WINDOWS // the `\n` terminator only matters on Windows; without it patches from C# raw string literals fail to apply
[<ShouldCatch("""
--- a/Mutannot/Git.fs
+++ b/Mutannot/Git.fs
@@ -30,6 +30,6 @@
             //
             // Inserting a newline to "terminate" the patch right before that
             // `\r\n` seems to prevent that problem.
-            Input $"{patch}\n"
+            Input patch
         }
         |> Command.execute
""")>]
#endif
let ``mutannot kills mutants in a csproj project`` (jj: bool) =
    withScratchFor jj (fun scratch -> Assert.Equal(0, graphWithKillableMutant Csharp |> runIn scratch))
