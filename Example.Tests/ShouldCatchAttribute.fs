namespace Mutannot

open System

[<AttributeUsage(AttributeTargets.Method, AllowMultiple = true)>]
type ShouldCatchAttribute(patch: string) =
    inherit Attribute()

    member _.Patch = patch
