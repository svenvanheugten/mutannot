namespace Mutannot.UnitTests

open Xunit

module ExtractPatchesTests =
    // `validate` finds ShouldCatch patches by regex rather than by loading a built
    // assembly, so the extraction has to reproduce, on the raw source, the trimming
    // the compilers would otherwise do.

    [<Fact>]
    let ``extracts and unindents an indented F# triple-quoted patch`` () =
        let source =
            String.concat
                "\n"
                [ "    [<ShouldCatch(\"\"\""
                  "    --- a/f.txt"
                  "    +++ b/f.txt"
                  "    @@ -1,1 +1,1 @@"
                  "    -a"
                  "    +b"
                  "    \"\"\")>]" ]

        let patches =
            Mutannot.PatchValidator.extractPatches Mutannot.PatchValidator.FSharp source

        Assert.Equal<string list>([ "--- a/f.txt\n+++ b/f.txt\n@@ -1,1 +1,1 @@\n-a\n+b\n" ], patches)

    [<Fact>]
    let ``extracts a column-zero C# raw-string patch`` () =
        let source =
            String.concat
                "\n"
                [ "[ShouldCatch(\"\"\""
                  "--- a/f.txt"
                  "+++ b/f.txt"
                  "@@ -1,1 +1,1 @@"
                  "-a"
                  "+b"
                  "\"\"\")]" ]

        let patches =
            Mutannot.PatchValidator.extractPatches Mutannot.PatchValidator.CSharp source

        // C# raw strings drop the newline before the closing """, so no trailing newline.
        Assert.Equal<string list>([ "--- a/f.txt\n+++ b/f.txt\n@@ -1,1 +1,1 @@\n-a\n+b" ], patches)

    [<Fact>]
    let ``extracts and unindents a nested, indented C# raw-string patch`` () =
        // A method-level [ShouldCatch(...)] nested in a class, indented to align with
        // the method it annotates. The raw string is indented to its closing """ (the
        // method's column); the C# compiler would dedent by that column and drop the
        // trailing newline, and extractPatches must reach the same result.
        let source =
            String.concat
                "\n"
                [ "public class Tests"
                  "{"
                  "    [ShouldCatch(\"\"\""
                  "    --- a/f.txt"
                  "    +++ b/f.txt"
                  "    @@ -1,1 +1,1 @@"
                  "    -a"
                  "    +b"
                  "    \"\"\")]"
                  "    public void Test() {}"
                  "}" ]

        let patches =
            Mutannot.PatchValidator.extractPatches Mutannot.PatchValidator.CSharp source

        Assert.Equal<string list>([ "--- a/f.txt\n+++ b/f.txt\n@@ -1,1 +1,1 @@\n-a\n+b" ], patches)

    [<Fact>]
    let ``extracts every ShouldCatch attribute in a F# file`` () =
        let attribute body =
            "[<ShouldCatch(\"\"\"\n" + body + "\n\"\"\")>]"

        let source =
            attribute "--- a/one\n+++ b/one" + "\n" + attribute "--- a/two\n+++ b/two"

        let patches =
            Mutannot.PatchValidator.extractPatches Mutannot.PatchValidator.FSharp source

        Assert.Equal(2, List.length patches)
        Assert.Contains("--- a/one", List.head patches)
        Assert.Contains("--- a/two", List.last patches)

    [<Fact>]
    let ``returns nothing when there are no ShouldCatch attributes`` () =
        Assert.Empty(Mutannot.PatchValidator.extractPatches Mutannot.PatchValidator.CSharp "public class Foo {}")
