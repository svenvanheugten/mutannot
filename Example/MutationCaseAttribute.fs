namespace Mutannot

open System

[<AttributeUsage(AttributeTargets.Method, AllowMultiple = true)>]
type MutationCaseAttribute(patch: string) =
    inherit Attribute()

    member _.Patch = patch
