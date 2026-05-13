namespace Example

open Example
open Mutannot
open Xunit

type CalculatorTests() =
    [<Fact>]
    [<ShouldCatch("""
    diff --git a/Example/Calculator.fs b/Example/Calculator.fs
    index 6f0c515..030e391 100644
    --- a/Example/Calculator.fs
    +++ b/Example/Calculator.fs
    @@ -1,4 +1,4 @@
     namespace Example

     module Calculator =
    -    let addOne value = value + 1
    +    let addOne value = value - 1
    """)>]
    member _.AddOne_increments() = Assert.Equal(42, Calculator.addOne 41)
