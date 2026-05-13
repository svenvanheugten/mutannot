namespace Example

open Example
open Mutannot
open Xunit
open System

type ValidatorTests() =
    [<Fact>]
    [<ShouldCatch("""
    diff --git a/Example/Validator.fs b/Example/Validator.fs
    index 0881bd8..eaf6036 100644
    --- a/Example/Validator.fs
    +++ b/Example/Validator.fs
    @@ -3,4 +3,4 @@ namespace Example
     open System

     module Validator =
    -    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date
    +    let isAllowed (now: DateTime) (date: DateTime) = now <= date
    """)>]
    member _.``You're allowed to pick the current day``() =
        let now = DateTime(2026, 5, 12, 17, 17, 13)
        let date = DateTime(2026, 5, 12)
        Assert.True <| Validator.isAllowed now date
