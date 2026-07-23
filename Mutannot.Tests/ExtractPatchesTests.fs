namespace Mutannot.Tests

open Xunit

module ExtractPatchesTests =
    // `validate` finds ShouldCatch patches by regex rather than by loading a built
    // assembly, so the extraction has to reproduce, on the raw source, the trimming
    // the compilers would otherwise do. extractPatches is language-agnostic -- it
    // regexes out the triple-quoted body and hands it to unindentPatch -- so the
    // cases below are really "column-zero" vs "indented" rather than C# vs F#. They
    // are named after a language only because that is how each shape shows up in
    // practice: an F# literal keeps its source indentation, a top-level C# raw string
    // sits at column zero, and a nested (e.g. method-level) C# raw string is indented
    // to the closing """ -- which unindentPatch strips just as it does for F#.

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

        let patches = Mutannot.PatchValidator.extractPatches source

        Assert.Equal<string list>(
            [ "--- a/f.txt\n+++ b/f.txt\n@@ -1,1 +1,1 @@\n-a\n+b\n" ],
            patches
        )

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

        let patches = Mutannot.PatchValidator.extractPatches source

        Assert.Equal<string list>(
            [ "--- a/f.txt\n+++ b/f.txt\n@@ -1,1 +1,1 @@\n-a\n+b\n" ],
            patches
        )

    [<Fact>]
    let ``extracts and unindents a nested, indented C# raw-string patch`` () =
        // A method-level [ShouldCatch(...)] nested in a class, indented to align with
        // the method it annotates. The raw string is indented to its closing """ (the
        // method's column); the C# compiler would dedent by that column, and
        // extractPatches must reach the same result via unindentPatch.
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

        let patches = Mutannot.PatchValidator.extractPatches source

        Assert.Equal<string list>(
            [ "--- a/f.txt\n+++ b/f.txt\n@@ -1,1 +1,1 @@\n-a\n+b\n" ],
            patches
        )

    [<Fact>]
    let ``extracts every ShouldCatch attribute in the file`` () =
        let attribute body =
            "[<ShouldCatch(\"\"\"\n" + body + "\n\"\"\")>]"

        let source = attribute "--- a/one\n+++ b/one" + "\n" + attribute "--- a/two\n+++ b/two"

        let patches = Mutannot.PatchValidator.extractPatches source

        Assert.Equal(2, List.length patches)
        Assert.Contains("--- a/one", List.head patches)
        Assert.Contains("--- a/two", List.last patches)

    [<Fact>]
    let ``returns nothing when there are no ShouldCatch attributes`` () =
        Assert.Empty(Mutannot.PatchValidator.extractPatches "public class Foo {}")
