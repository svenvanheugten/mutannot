# mutannot

This lets you write the [mutations](https://en.wikipedia.org/wiki/Mutation_testing) that should cause a test to fail [directly into your test code](https://sven.memcmp.org/2026-05-13-encoding-mutations-directly-into-the-test-code/), to make it easier to understand the thinking behind a test.

To use it, install the [Mutannot.Annotations](https://www.nuget.org/packages/Mutannot.Annotations) NuGet package (or [add the attribute manually](https://codeberg.org/svenvanheugten/mutannot/src/branch/main/Mutannot.Annotations/ShouldCatchAttribute.fs) if you prefer no dependency), annotate test methods or test types with git patches, and then run `mutannot run [path/to/testproject.csproj|fsproj]`.

Check out [the example](https://codeberg.org/svenvanheugten/mutannot/src/branch/main/Example.FSharp.Tests/ValidatorTests.fs).

Usage:

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
