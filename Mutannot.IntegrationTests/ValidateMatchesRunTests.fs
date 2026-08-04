module Mutannot.IntegrationTests.ValidateMatchesRunTests

open System.IO
open Xunit
open Mutannot.Annotations
open Mutannot
open Mutannot.IntegrationTests.ScratchFixtures
open Mutannot.IntegrationTests.TestSupport

// `run` reads a ShouldCatch patch out of the built assembly, so the patch text it
// mutates with is whatever the C#/F# compiler made of the triple-quoted string
// literal (see Runner.getMutations). `validate` never builds: it recovers the patch
// from raw source with a regex and reproduces the compiler's trimming itself (see
// PatchValidator.extractPatches). The two interpretations of a multi-line literal
// must stay byte-for-byte identical, or a patch `run` applies could differ from the
// one `validate` checks -- the mismatch #1 had to fix.
//
// These tests build a source file full of the awkward cases -- a column-0 literal and
// a deeply-indented one, at both class/type and method/member level -- then assert
// that the patches `run` reads from the assembly are exactly the set `validate`
// extracts from the source. C# drops the newline before its closing """, F# keeps it;
// both languages are exercised so either language's regex drifting from its compiler
// is caught.

// A C# source with two ShouldCatch attributes. The first literal sits at column 0
// (closing """ flush left); the second is indented to a nested method, so the compiler
// dedents by that column. Both patches must survive the round trip identically.
let private csharpSource =
    String.concat
        "\n"
        [ "using Mutannot.Annotations;"
          ""
          "namespace Subtlety;"
          ""
          "[ShouldCatch(\"\"\""
          "--- a/Calc.cs"
          "+++ b/Calc.cs"
          "@@ -1,3 +1,3 @@"
          " public static class Calc"
          " {"
          "-    public static int Add(int x, int y) => x + y;"
          "+    public static int Add(int x, int y) => x - y;"
          " }"
          "\"\"\")]"
          "public class Subtle"
          "{"
          "    [ShouldCatch(\"\"\""
          "        --- a/Calc.cs"
          "        +++ b/Calc.cs"
          "        @@ -5,1 +5,1 @@"
          "        -    public static int Mul(int x, int y) => x * y;"
          "        +    public static int Mul(int x, int y) => x + y;"
          "        \"\"\")]"
          "    public void MethodLevel() {}"
          "}"
          "" ]

// An F# source with the same two shapes: a column-0 literal on a module-level `let`
// (which compiles to a static method) and an indented one on a member.
let private fsharpSource =
    String.concat
        "\n"
        [ "module Subtlety.Sub"
          ""
          "open Mutannot.Annotations"
          ""
          "[<ShouldCatch(\"\"\""
          "--- a/Calc.fs"
          "+++ b/Calc.fs"
          "@@ -3,1 +3,1 @@"
          "-    let add x y = x + y"
          "+    let add x y = x - y"
          "\"\"\")>]"
          "let moduleLevel () = ()"
          ""
          "type Holder() ="
          "    [<ShouldCatch(\"\"\""
          "    --- a/Calc.fs"
          "    +++ b/Calc.fs"
          "    @@ -4,1 +4,1 @@"
          "    -    let mul x y = x * y"
          "    +    let mul x y = x + y"
          "    \"\"\")>]"
          "    member _.MemberLevel() = ()"
          "" ]

let private assertValidateMatchesRun
    (language: Language)
    (validatorLanguage: PatchValidator.Language)
    (sourceName: string)
    (source: string)
    =
    withScratch (fun scratch ->
        // A test project -- the shape `run` targets -- carrying the ShouldCatch
        // attributes. No tests run: both commands only read the patches from its source.
        let project = testProject language XunitV2 "Subtle" [] [ file sourceName source ]

        let extension =
            match language with
            | Csharp -> ".csproj"
            | Fsharp -> ".fsproj"

        let target =
            writeGraph
                scratch
                { Projects = [ project ]
                  RunTarget = "Subtle/Subtle" + extension }

        build target

        let runPatches =
            Runner.getMutations target |> fst |> List.map (fun m -> m.Patch) |> Set.ofList

        let validatePatches =
            PatchValidator.extractPatches
                validatorLanguage
                (File.ReadAllText(Path.Combine(scratch, "Subtle", sourceName)))
            |> Set.ofList

        // Guard against a fixture that silently yields nothing: two empty sets would
        // match vacuously and prove neither path saw either patch.
        Assert.Equal(2, Set.count runPatches)
        Assert.Equal<Set<string>>(runPatches, validatePatches))

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -42,7 +42,7 @@
 
             let body =
                 match language with
-                | CSharp -> Regex.Replace(body, @"\n[ \t]*$", "")
+                | CSharp -> body
                 | FSharp -> body
 
             Runner.unindentPatch body)
""")>]
let ``validate extracts the same C# patches run reads from the assembly`` () =
    assertValidateMatchesRun Csharp PatchValidator.CSharp "Subtle.cs" csharpSource

[<Fact>]
[<ShouldCatch("""
--- a/Mutannot/PatchValidator.fs
+++ b/Mutannot/PatchValidator.fs
@@ -43,7 +43,7 @@
             let body =
                 match language with
                 | CSharp -> Regex.Replace(body, @"\n[ \t]*$", "")
-                | FSharp -> body
+                | FSharp -> body.TrimEnd()
 
             Runner.unindentPatch body)
         |> Seq.toList
""")>]
let ``validate extracts the same F# patches run reads from the assembly`` () =
    assertValidateMatchesRun Fsharp PatchValidator.FSharp "Subtle.fs" fsharpSource
