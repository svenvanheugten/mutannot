# mutannot

[![mutannot on NuGet](https://img.shields.io/nuget/v/mutannot?label=mutannot)](https://www.nuget.org/packages/mutannot)
[![Mutannot.Annotations on NuGet](https://img.shields.io/nuget/v/Mutannot.Annotations?label=Mutannot.Annotations)](https://www.nuget.org/packages/Mutannot.Annotations)

This lets you write the [mutations](https://en.wikipedia.org/wiki/Mutation_testing) that should cause a test to fail [directly into your test code](https://sven.memcmp.org/2026-05-13-encoding-mutations-directly-into-the-test-code/), to make it easier to understand the thinking behind a test.

Check out [the example](https://github.com/svenvanheugten/mutannot/blob/main/Example.FSharp.Tests/ValidatorTests.fs).

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

# Usage

Add the [Mutannot.Annotations](https://www.nuget.org/packages/Mutannot.Annotations) NuGet package to your test project (or [add a copy of the attribute manually](https://github.com/svenvanheugten/mutannot/blob/main/Mutannot.Annotations/ShouldCatchAttribute.fs) if you prefer no dependency), annotate test methods or test types with git patches, and then run `dotnet tool run mutannot -- run [path/to/testproject.csproj|fsproj]`.

```text
USAGE: mutannot [--help] [<subcommand> [<options>]]

SUBCOMMANDS:

    run <options>         run mutations for path/to/testproject.csproj|fsproj.
    annotate-type <options>
                          annotate an F# type with a ShouldCatch attribute from a git diff.

    Use 'mutannot <subcommand> --help' for additional information.

OPTIONS:

    --help                display this list of options.
```
