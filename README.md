# mutannot

This lets you write the [mutations](https://en.wikipedia.org/wiki/Mutation_testing) that should cause a test to fail [directly into your test code](https://sven.memcmp.org/2026-05-13-encoding-mutations-directly-into-the-test-code/), to make it easier to understand the thinking behind a test.

Check out [the example](https://codeberg.org/svenvanheugten/mutannot/src/branch/main/Example.FSharp.Tests/ValidatorTests.fs).

Currently, only .NET is supported, but I might extend this to other languages and frameworks in the future.

To use it, install the [Mutannot.Annotations](https://www.nuget.org/packages/Mutannot.Annotations) NuGet package (or [add the attribute manually](https://codeberg.org/svenvanheugten/mutannot/src/branch/main/Mutannot.Annotations/ShouldCatchAttribute.fs) if you prefer no dependency), annotate test methods or test types with git patches, and then run `mutannot run [path/to/testproject.csproj|fsproj]`.

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

<details>
<summary>Instructions for adding the flake to a devshell</summary>

```diff
@@ -2,12 +2,18 @@
   inputs = {
     nixpkgs.url = "github:nixos/nixpkgs/nixpkgs-unstable";
     flake-utils.url = "github:numtide/flake-utils";
+    mutannot = {
+      url = "git+https://codeberg.org/svenvanheugten/mutannot.git?ref=main";
+      inputs.nixpkgs.follows = "nixpkgs";
+      inputs.flake-utils.follows = "flake-utils";
+    };
   };
   outputs =
     {
       self,
       nixpkgs,
       flake-utils,
+      mutannot,
     }:
     flake-utils.lib.eachDefaultSystem (
       system:
@@ -17,7 +23,7 @@
       {
         packages.default = pkgs.callPackage ./default.nix { };
         devShells.default = pkgs.mkShell {
-          packages = [ ];
+          packages = [ mutannot.packages.${system}.default ];
         };
       }
     )
```
</details>
