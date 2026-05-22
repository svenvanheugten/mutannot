namespace Example

open Example
open Mutannot
open Xunit
open System

[<ShouldCatch("""
--- a/Example/Validator.fs
+++ b/Example/Validator.fs
@@ -3,4 +3,4 @@ namespace Example
 open System

 module Validator =
-    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date
+    let isAllowed (now: DateTime) (date: DateTime) = now <= date
""")>]
type ValidatorTests() =
    [<Fact>]
    member _.``You're allowed to pick the current day``() =
        let now = DateTime(2026, 5, 12, 17, 17, 13)
        let date = DateTime(2026, 5, 12)
        Assert.True <| Validator.isAllowed now date

    [<Fact>]
    member _.``You're allowed to pick the current day even late at night``() =
        let now = DateTime(2026, 5, 12, 23, 59, 59)
        let date = DateTime(2026, 5, 12)
        Assert.True <| Validator.isAllowed now date

    [<Fact>]
    [<ShouldCatch("""
    --- a/Example/Validator.fs
    +++ b/Example/Validator.fs
    @@ -3,4 +3,4 @@ namespace Example
     open System

     module Validator =
    -    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date
    +    let isAllowed (now: DateTime) (date: DateTime) = now.Date >= date
    """)>]
    member _.``You're not allowed to pick the previous day``() =
        let now = DateTime(2026, 5, 12, 17, 17, 13)
        let date = DateTime(2026, 5, 11)
        Assert.False <| Validator.isAllowed now date
