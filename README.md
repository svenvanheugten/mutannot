# mutannot

This lets you annotate .NET test cases with [mutations](https://en.wikipedia.org/wiki/Mutation_testing) that should cause the test to fail. Check out [the example](https://codeberg.org/svenvanheugten/mutannot/src/branch/main/Example.Tests/ValidatorTests.fs).

To use it, add the [`ShouldCatchAttribute`](https://codeberg.org/svenvanheugten/mutannot/src/branch/main/Example.Tests/ShouldCatchAttribute.fs) to your codebase, start annotating tests with git patches, and then run `mutannot [path/to/project.csproj|fsproj]`.

It will refuse to run if you have any uncommitted changes, since it actively mutates your code. As a work-around, you can use [git-temp-commit](https://codeberg.org/svenvanheugten/git-temp-commit) to create a temporary commit which is undone when the tool finishes running.

Usage:

```
USAGE: mutannot [--help] [--filter <SearchString>] [--validate-only] <ProjectPath>

PROJECTPATH:

    <ProjectPath>         path/to/project.csproj|fsproj

OPTIONS:

    --filter <SearchString>
                          filter down to mutations that contain the given search string.
    --validate-only       check if the patches apply, but don't run the mutations.
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
