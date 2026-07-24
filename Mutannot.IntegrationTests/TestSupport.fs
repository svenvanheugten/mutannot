module Mutannot.IntegrationTests.TestSupport

open System
open System.IO
open Fli

// Tests that build the shared Example.* fixtures in place tag themselves with this
// collection so xUnit runs them serially rather than letting their builds collide.
[<Literal>]
let ExampleProjectsCollection = "Example projects"

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

// The mutator resolves patched paths against the git root (via `git rev-parse`),
// so the throwaway fixtures below have to live under it too.
let gitRoot =
    (cli {
        Exec "git"
        Arguments [ "rev-parse"; "--show-toplevel" ]
     }
     |> Command.execute
     |> Output.toText)
        .Trim()

// Runs `body relName scratchAbs` against a unique, self-cleaning scratch
// directory under the git root. `.mutannot/<relName>` (where the mutator mirrors
// the sources it patches) is cleaned up alongside it.
let withScratch (body: string -> string -> unit) =
    let name = ".inttest-" + Guid.NewGuid().ToString("N")
    let scratch = Path.Combine(gitRoot, name)

    try
        Directory.CreateDirectory scratch |> ignore
        body name scratch
    finally
        for dir in [ scratch; Path.Combine(gitRoot, ".mutannot", name) ] do
            if Directory.Exists dir then
                Directory.Delete(dir, true)

// --- Project-file builders ------------------------------------------------
//
// The scratch fixtures below all need throwaway .csproj/.fsproj files that are
// mostly identical boilerplate. These helpers assemble that XML so each test
// spells out only what its scenario actually varies (a pinned assembly name, an
// InternalsVisibleTo, a Compile include) instead of a full hand-written project.

// Every scratch project lives two levels under the git root (<scratch>/<Proj>/),
// so this reaches the real annotations library each test references so it can
// carry [ShouldCatch].
let annotationsReference = "../../Mutannot.Annotations/Mutannot.Annotations.fsproj"

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
let xunitTestProject (extraProps: string list) (compiles: string list) (projectRefs: string list) =
    sdkProject
        extraProps
        [ itemGroup (compiles |> List.map compileInclude)
          itemGroup ((projectRefs @ [ annotationsReference ]) |> List.map projectReference)
          xunitV2Packages ]

let build (projectPath: string) =
    cli {
        Exec "dotnet"
        Arguments [ "build"; projectPath; "-c"; "Debug" ]
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let sha256 (bytes: byte[]) =
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes)
