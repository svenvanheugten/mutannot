namespace Mutannot.UnitTests

open Mutannot
open Xunit

module TypeAnnotatorTests =
    let joinLines lines = String.concat "\n" lines + "\n"

    [<Fact>]
    let ``annotateTypeWithPatch inserts a type attribute`` () =
        let patch =
            joinLines
                [ "--- a/Example.FSharp/Validator.fs"
                  "+++ b/Example.FSharp/Validator.fs"
                  "@@ -1,1 +1,1 @@"
                  "-let before = 1"
                  "+let after = 2" ]

        let source =
            joinLines
                [ "namespace Example"
                  ""
                  "type ValidatorTests() ="
                  "    member _.Test() = ()" ]

        let updatedSource =
            TypeAnnotator.annotateTypeWithPatch "ValidatorTests" patch source
            |> Result.defaultWith failwith

        let expected =
            joinLines
                [ "namespace Example"
                  ""
                  "[<ShouldCatch(\"\"\""
                  "--- a/Example.FSharp/Validator.fs"
                  "+++ b/Example.FSharp/Validator.fs"
                  "@@ -1,1 +1,1 @@"
                  "-let before = 1"
                  "+let after = 2"
                  "\"\"\")>]"
                  "type ValidatorTests() ="
                  "    member _.Test() = ()" ]

        Assert.Equal(expected, updatedSource)

    [<Fact>]
    let ``annotateTypeWithPatch keeps existing ShouldCatch attributes and adds another one`` () =
        let patch =
            joinLines
                [ "--- a/Example.FSharp/Validator.fs"
                  "+++ b/Example.FSharp/Validator.fs"
                  "@@ -1,1 +1,1 @@"
                  "-let before = 1"
                  "+let after = 2" ]

        let source =
            joinLines
                [ "namespace Example"
                  ""
                  "[<ShouldCatch(\"\"\""
                  "--- old patch"
                  "\"\"\")>]"
                  "type ValidatorTests() ="
                  "    member _.Test() = ()" ]

        let updatedSource =
            TypeAnnotator.annotateTypeWithPatch "ValidatorTests" patch source
            |> Result.defaultWith failwith

        let expected =
            joinLines
                [ "namespace Example"
                  ""
                  "[<ShouldCatch(\"\"\""
                  "--- old patch"
                  "\"\"\")>]"
                  "[<ShouldCatch(\"\"\""
                  "--- a/Example.FSharp/Validator.fs"
                  "+++ b/Example.FSharp/Validator.fs"
                  "@@ -1,1 +1,1 @@"
                  "-let before = 1"
                  "+let after = 2"
                  "\"\"\")>]"
                  "type ValidatorTests() ="
                  "    member _.Test() = ()" ]

        Assert.Equal(expected, updatedSource)

    [<Fact>]
    let ``annotateTypeWithPatch preserves other type attributes`` () =
        let patch =
            joinLines
                [ "--- a/Example.FSharp/Validator.fs"
                  "+++ b/Example.FSharp/Validator.fs"
                  "@@ -1,1 +1,1 @@"
                  "-let before = 1"
                  "+let after = 2" ]

        let source =
            joinLines
                [ "namespace Example"
                  ""
                  "[<CLIMutable>]"
                  "[<ShouldCatch(\"\"\""
                  "--- old patch"
                  "\"\"\")>]"
                  "type ValidatorRecord ="
                  "    { Value: int }" ]

        let updatedSource =
            TypeAnnotator.annotateTypeWithPatch "ValidatorRecord" patch source
            |> Result.defaultWith failwith

        let expected =
            joinLines
                [ "namespace Example"
                  ""
                  "[<CLIMutable>]"
                  "[<ShouldCatch(\"\"\""
                  "--- old patch"
                  "\"\"\")>]"
                  "[<ShouldCatch(\"\"\""
                  "--- a/Example.FSharp/Validator.fs"
                  "+++ b/Example.FSharp/Validator.fs"
                  "@@ -1,1 +1,1 @@"
                  "-let before = 1"
                  "+let after = 2"
                  "\"\"\")>]"
                  "type ValidatorRecord ="
                  "    { Value: int }" ]

        Assert.Equal(expected, updatedSource)
