namespace Mutannot

open System

/// <summary>
/// Patch that should cause the test to fail.
/// Use https://codeberg.org/svenvanheugten/mutannot to verify.
/// </summary>
[<AttributeUsage(AttributeTargets.Method, AllowMultiple = true)>]
type ShouldCatchAttribute(patch: string) =
    inherit Attribute()

    member _.Patch = patch
