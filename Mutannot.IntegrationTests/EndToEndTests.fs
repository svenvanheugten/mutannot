module Mutannot.IntegrationTests.EndToEndTests

open System.IO
open Xunit
open Mutannot
open Mutannot.Annotations
open Mutannot.IntegrationTests.TestSupport

[<Fact>]
let ``mutannot kills mutants in an fsproj project`` () =
    let exitCode =
        Program.main
            [| "run"
               Path.Combine(repoRoot, "Example.FSharp.Tests", "Example.FSharp.Tests.fsproj") |]

    Assert.Equal(0, exitCode)

[<Fact>]
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
let ``mutannot kills mutants in a csproj project`` () =
    let exitCode =
        Program.main
            [| "run"
               Path.Combine(repoRoot, "Example.CSharp.Tests", "Example.CSharp.Tests.csproj") |]

    Assert.Equal(0, exitCode)
