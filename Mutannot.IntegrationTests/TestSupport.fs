module Mutannot.IntegrationTests.TestSupport

open System
open System.IO
open Fli

// Mutannot's own working tree. Scratch fixtures are created under it so they inherit
// the same NuGet configuration the real projects restore with, but each is its own
// git repository (see withScratch) rather than part of this one.
//
// Located by walking up from the test binary until `mutannot.slnx` is found, rather
// than asking a VCS for the root. That keeps the harness working whether mutannot
// itself is checked out under git, jj (co-located or not), or a plain tarball — the
// VCS backends are exercised deliberately by withScratch/withJjScratch, not here.
let repoRoot =
    let marker = "mutannot.slnx"

    let rec walkUp (dir: DirectoryInfo) =
        if isNull dir then
            failwithf "Could not locate mutannot repo root (%s not found above %s)" marker AppContext.BaseDirectory
        elif File.Exists(Path.Combine(dir.FullName, marker)) then
            dir.FullName
        else
            walkUp dir.Parent

    walkUp (DirectoryInfo AppContext.BaseDirectory)

// Runs `body scratchAbs` against a unique, self-cleaning scratch directory that is
// its own git repository. Each scratch is `git init`ed so the mutator resolves its
// git root. That keeps every test's output out of mutannot's own tree and isolated
// from the other tests, tests can run in parallel.
let withScratch (body: string -> unit) =
    let scratch = Path.Combine(repoRoot, ".inttest-" + Guid.NewGuid().ToString("N"))

    try
        Directory.CreateDirectory scratch |> ignore

        // Make the scratch behave like a real consumer's repo: ignore build output
        // and mutannot's generated files so `validate`'s `git ls-files` scan doesn't
        // pick up generated sources.
        File.WriteAllText(
            Path.Combine(scratch, ".gitignore"),
            "[Bb]in/\n[Oo]bj/\n.mutannot/\n*.mutated.csproj\n*.mutated.fsproj\n"
        )

        cli {
            Exec "git"
            Arguments [ "init" ]
            WorkingDirectory scratch
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

        body scratch
    finally
        if Directory.Exists scratch then
            Directory.Delete(scratch, true)

// Runs `body scratchAbs` against a unique, self-cleaning scratch directory that is a
// jj repository *not* co-located with git. It sits under the system temp path rather
// than mutannot's own tree so that no ancestor git repository is in scope: `git
// rev-parse` genuinely fails there, which is what makes the jj backend the one that
// gets exercised. `--config git.colocate=false` keeps jj from writing a .git that
// `git rev-parse` would otherwise find. The .gitignore is honoured by jj too, so
// build output and mutannot's generated files stay out of the source scan.
let withJjScratch (body: string -> unit) =
    let scratch =
        Path.Combine(Path.GetTempPath(), "mutannot-jj-" + Guid.NewGuid().ToString("N"))

    try
        Directory.CreateDirectory scratch |> ignore

        File.WriteAllText(
            Path.Combine(scratch, ".gitignore"),
            "[Bb]in/\n[Oo]bj/\n.mutannot/\n*.mutated.csproj\n*.mutated.fsproj\n"
        )

        cli {
            Exec "jj"
            Arguments [ "--config"; "git.colocate=false"; "git"; "init" ]
            WorkingDirectory scratch
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

        body scratch
    finally
        if Directory.Exists scratch then
            Directory.Delete(scratch, true)

let build (projectPath: string) =
    cli {
        Exec "dotnet"
        Arguments [ "build"; projectPath; "-c"; "Debug" ]
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

// Absolute path to the prebuilt Mutannot.Annotations DLL, built exactly once.
//
// Scratch projects reference this DLL rather than the .csproj. A ProjectReference
// pulls the shared Mutannot.Annotations project into every scratch build graph, so
// running the tests in parallel would trigger concurrent builds of that one project,
// racing on its obj/bin. `lazy` (thread-safe by default) builds it on first use and
// hands every caller the resulting DLL. netstandard2.0 is the project's only TFM.
let annotationsDll =
    lazy
        (let proj =
            Path.Combine(repoRoot, "Mutannot.Annotations", "Mutannot.Annotations.csproj")

         build proj
         Path.Combine(repoRoot, "Mutannot.Annotations", "bin", "Debug", "netstandard2.0", "Mutannot.Annotations.dll"))

// --- Project-file builders ------------------------------------------------
//
// The scratch fixtures below all need throwaway .csproj/.fsproj files that are
// mostly identical boilerplate. These helpers assemble that XML so each test
// spells out only what its scenario actually varies (a pinned assembly name, an
// InternalsVisibleTo, a Compile include) instead of a full hand-written project.

// A `<Reference>` to the prebuilt annotations DLL each test carries so its scratch
// sources can use [ShouldCatch]. The HintPath is absolute so it resolves from both
// the scratch project and the same-directory `.mutated` copy the mutator emits.
let annotationsReference () =
    $"<Reference Include=\"Mutannot.Annotations\"><HintPath>{annotationsDll.Value}</HintPath></Reference>"

let compileInclude (path: string) = $"<Compile Include=\"{path}\" />"

let projectReference (path: string) =
    $"<ProjectReference Include=\"{path}\" />"

// Wraps items in an `<ItemGroup>` (indented to sit inside a <Project>), or emits
// nothing for an empty list so callers can pass groups unconditionally.
let itemGroup (items: string list) =
    match items with
    | [] -> ""
    | _ ->
        "  <ItemGroup>\n"
        + (items |> List.map (fun i -> "    " + i + "\n") |> String.concat "")
        + "  </ItemGroup>"

// A `<Project Sdk="Microsoft.NET.Sdk">` targeting net10.0, plus any extra property
// lines and (already-rendered, e.g. via `itemGroup`) item-group blocks. This is
// the frame every scratch fixture shares.
let sdkProject (extraProps: string list) (itemGroups: string list) =
    let props =
        "<TargetFramework>net10.0</TargetFramework>" :: extraProps
        |> List.map (fun p -> "    " + p + "\n")
        |> String.concat ""

    let items =
        itemGroups
        |> List.filter (fun g -> g <> "")
        |> List.map (fun g -> g + "\n")
        |> String.concat ""

    "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
    + "  <PropertyGroup>\n"
    + props
    + "  </PropertyGroup>\n"
    + items
    + "</Project>\n"

// The Microsoft.NET.Test.Sdk + xunit + visualstudio-runner `<ItemGroup>` every
// scratch xunit v2 test project carries, with the runner marked build-only.
let xunitV2Packages =
    "  <ItemGroup>\n"
    + "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />\n"
    + "    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n"
    + "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.4\">\n"
    + "      <PrivateAssets>all</PrivateAssets>\n"
    + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
    + "    </PackageReference>\n"
    + "  </ItemGroup>"

// A scratch xunit v2 test project: net10.0, the standard test-package set and a
// reference to Mutannot.Annotations, plus whatever `<Compile>` includes (F#
// projects need them), extra property lines and extra project references (e.g.
// the library under test) the scenario adds.
let xunitV2TestProject (extraProps: string list) (compiles: string list) (projectRefs: string list) =
    sdkProject
        extraProps
        [ itemGroup (compiles |> List.map compileInclude)
          itemGroup ((projectRefs |> List.map projectReference) @ [ annotationsReference () ])
          xunitV2Packages ]

let sha256 (bytes: byte[]) =
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes)
