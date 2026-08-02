---
name: write-or-update-mutations
description: Write or update `mutannot` mutation annotations.
---

# What are mutations?
You can annotate tests with a mutation that is supposed to make the test fail:

```cs
    [Fact]
    [ShouldCatch("""
    --- a/Example.CSharp/Calculator.cs
    +++ b/Example.CSharp/Calculator.cs
    @@ -1,6 +1,6 @@
     namespace Example;

     public static class Calculator
     {
    -    public static int Add(int x, int y) => x + y;
    +    public static int Add(int x, int y) => x - y;
     }
    """)]
    public void Add_Returns_Sum()
    {
        Assert.Equal(5, Calculator.Add(2, 3));
    }
```

The `ShouldCatch` type is provided by the `Mutannot.Annotations` namespace, so import it if isn't already imported.

# Writing mutations
To write a mutation, follow the following recipe:

1. Write a `sed` command that mutates the code and writes its output to a temporary file. Do not use `sed -i`.
2. Generate the patch by running `diff -u --label a/<file relative to VCS root> --label b/<file relative to VCS root> <file> <the temporary file>`.
3. Wrap the patch in `[ShouldCatch("""[...]""")]` (C#) or `[<ShouldCatch("""[...]""">]` (F#) as shown above, indenting the whole patch to the level of the first `[`.

Never hand-write mutations, not even when you're just updating an existing mutation. Always follow the recipe.

# Verification
You can verify that the mutation patches are valid by running `dotnet run --project Mutannot/Mutannot.fsproj -- validate <path/to/testfile.cs|fs>`.

You can run a mutation with `dotnet run --project Mutannot/Mutannot.fsproj -- run <path/to/testproject.csproj|fsproj --filter <filter string>`, where `<filter string>` is an arbitrary string from the patch, e.g. `a/Example.CSharp/Calculator.cs`.
