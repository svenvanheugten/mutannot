# mutannot

[![mutannot on NuGet](https://img.shields.io/nuget/v/mutannot?label=mutannot)](https://www.nuget.org/packages/mutannot)
[![Mutannot.Annotations on NuGet](https://img.shields.io/nuget/v/Mutannot.Annotations?label=Mutannot.Annotations)](https://www.nuget.org/packages/Mutannot.Annotations)

This will let you write the [mutations](https://en.wikipedia.org/wiki/Mutation_testing) that should cause a test to fail [directly into your test code](https://sven.memcmp.org/2026-05-13-encoding-mutations-directly-into-the-test-code/).

It can help you make sure that a test _actually_ tests what you _think_ that it is testing, and that the test isn't just turning green for some other reason (for example because it goes down a different branch entirely that just _happens_ to lead to the expected result, or because the assertions are too weak to discover anything).

Currently, only .NET is supported.

## Installation

`mutannot` is a [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) and requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

Install it globally:

```text
dotnet tool install --global mutannot
```

Or add it to your repository's [tool manifest](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools#install-a-local-tool):

```text
dotnet tool install mutannot
```

## Usage

You add the [`Mutannot.Annotations`](https://www.nuget.org/packages/Mutannot.Annotations) NuGet package to your test project (or [add a copy of the attribute manually](https://github.com/svenvanheugten/mutannot/blob/main/Mutannot.Annotations/ShouldCatchAttribute.fs) if you prefer to not have a dependency), and then you annotate tests with `git` patches which, when applied, should cause the test to fail:

```fs
open Xunit
open Mutannot.Annotations

[<Fact>]
[<ShouldCatch("""
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
    Assert.True (Validator.isAllowed now date)
```

To run your mutations, you use `dotnet tool run mutannot -- run [path/to/testproject.csproj|fsproj]`. It will do a control run of the original test cases, and then it will run the same tests again with the patches applied, to confirm that they now fail. Doing so takes a while, so if you just want to quickly check if your git patches are (still) technically valid without running the mutation, use `dotnet tool run mutannot -- validate [path/to/directory|path/to/testfile.cs|path/to/testfile.fs]`.

Use `dotnet tool run mutannot -- --help` to list all commands and options.

## Examples

A simple C# example is available [here](https://github.com/svenvanheugten/mutannot/blob/main/Example.CSharp.Tests/CalculatorTests.cs), and a simple F# example is available [here](https://github.com/svenvanheugten/mutannot/blob/main/Example.FSharp.Tests/ValidatorTests.fs).

Mutannot is also heavily [dogfooded](https://en.wikipedia.org/wiki/Eating_your_own_dog_food), however, so you can find a lot more examples in the program's [own integration tests](https://github.com/svenvanheugten/mutannot/tree/main/Mutannot.IntegrationTests).
