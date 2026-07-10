namespace Mutannot.Tests

open Xunit

module UnindentPatchTests =
    // A ShouldCatch attribute embeds the patch as an indented, triple-quoted
    // string literal. The line separator that literal carries depends on how
    // the source file was checked out: LF everywhere, or CRLF on a Windows
    // checkout (the F# compiler preserves the source file's endings verbatim).
    // unindentPatch must strip the shared indentation without changing the line
    // endings, because `git apply` matches the patch against the target file
    // byte-for-byte.
    let private indentedLines =
        [ "    diff --git a/f.txt b/f.txt"
          "    --- a/f.txt"
          "    +++ b/f.txt"
          "    @@ -1,2 +1,2 @@"
          "     line one"
          "    -line two"
          "    +line TWO" ]

    let private expectedLines =
        [ "diff --git a/f.txt b/f.txt"
          "--- a/f.txt"
          "+++ b/f.txt"
          "@@ -1,2 +1,2 @@"
          " line one"
          "-line two"
          "+line TWO" ]

    [<Fact>]
    let ``strips the shared indentation from an LF patch and keeps it LF`` () =
        let input = String.concat "\n" indentedLines
        let expected = String.concat "\n" expectedLines
        Assert.Equal(expected, Program.unindentPatch input)

    [<Fact>]
    let ``preserves CRLF line endings so the patch still matches a CRLF file`` () =
        // Simulates the attribute string as compiled from a Windows/CRLF checkout.
        let input = String.concat "\r\n" indentedLines
        let expected = String.concat "\r\n" expectedLines
        Assert.Equal(expected, Program.unindentPatch input)

    [<Fact>]
    let ``does not introduce carriage returns into an LF patch`` () =
        // Regression guard for the Environment.NewLine bug: on Windows the old
        // join forced CRLF onto every line, corrupting patches for LF files.
        let input = String.concat "\n" indentedLines
        Assert.DoesNotContain("\r", Program.unindentPatch input)
