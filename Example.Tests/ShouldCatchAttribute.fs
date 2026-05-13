namespace Mutannot

open System

/// <summary>
/// Patch, generated with `git diff`, that should cause the test to fail.
/// You can verify that the test _actually_ fails when the patch is applied with `mutannot` (https://codeberg.org/svenvanheugten/mutannot).
/// </summary>
[<AttributeUsage(AttributeTargets.Method, AllowMultiple = true)>]
type ShouldCatchAttribute(patch: string) =
    inherit Attribute()

    member _.Patch = patch
