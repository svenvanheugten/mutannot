module Mutannot.IntegrationTests.TestSupport

open System
open System.IO
open Fli
open Mutannot.IntegrationTests.ScratchFixtures

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
// scratch xunit v2 test project carries, with the runner marked build-only. Versions
// come from the Directory.Packages.props each scratch carries (see ScratchFixtures),
// so the references are declared without a Version, exactly as the real projects are.
let xunitV2Packages =
    "  <ItemGroup>\n"
    + "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" />\n"
    + "    <PackageReference Include=\"xunit\" />\n"
    + "    <PackageReference Include=\"xunit.runner.visualstudio\">\n"
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

// The Microsoft.Testing.Platform + xunit v3 package set every scratch MTP project
// carries: the runner marked build-only, exactly as the real MTP test projects use.
// Versions resolve from the scratch's Directory.Packages.props (see ScratchFixtures).
let mtpPackages =
    "  <ItemGroup>\n"
    + "    <PackageReference Include=\"xunit.v3\" />\n"
    + "    <PackageReference Include=\"xunit.runner.visualstudio\">\n"
    + "      <PrivateAssets>all</PrivateAssets>\n"
    + "      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n"
    + "    </PackageReference>\n"
    + "  </ItemGroup>"

// A scratch Microsoft.Testing.Platform xunit v3 test project: an executable with the
// platform's dotnet-test support and xunit.v3, so the SDK reports
// IsTestingPlatformApplication and mutannot detects it as MtpXunitV3. Mirrors
// xunitV2TestProject's shape (extra props / F# compiles / project refs).
let mtpXunitV3Project (extraProps: string list) (compiles: string list) (projectRefs: string list) =
    sdkProject
        ([ "<IsPackable>false</IsPackable>"
           "<Nullable>enable</Nullable>"
           "<ImplicitUsings>enable</ImplicitUsings>"
           "<OutputType>Exe</OutputType>"
           "<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>" ]
         @ extraProps)
        [ itemGroup (compiles |> List.map compileInclude)
          itemGroup ((projectRefs |> List.map projectReference) @ [ annotationsReference () ])
          mtpPackages ]

// --- Scratch-project graph ------------------------------------------------
//
// Most integration tests only need "some project to mutate": a green library plus a
// test whose ShouldCatch kills exactly one mutant, run end to end with an assertion
// on the exit code. The pieces below model that as data so a test spells out only the
// axis it actually exercises. `graphWithKillableMutant` is the canonical such project; tests that
// vary a single dimension start from it and override one field.

type Language =
    | Csharp
    | Fsharp

type Runner =
    | XunitV2
    | MtpXunitV3

// One source file inside a project directory. `Name` may include subdirectories.
type SourceFile = { Name: string; Content: string }

// A project written into <scratch>/<Dir>, its project file named <Dir>.<ext>. `Runner
// = None` is a library; `Some` a test project. `Compiles` overrides the F# <Compile>
// includes (defaulting to the source names in order) for the rare project that
// authors them differently, e.g. with backslashes. `Items` are extra <ItemGroup>
// entries (e.g. an <InternalsVisibleTo>), honoured on library projects.
type Project =
    { Dir: string
      Language: Language
      Runner: Runner option
      Props: string list
      ProjectRefs: string list
      Compiles: string list option
      Items: string list
      Sources: SourceFile list }

// The projects to write and the test project's file that `run` targets.
type Graph =
    { Projects: Project list
      RunTarget: string }

let private ext =
    function
    | Csharp -> ".csproj"
    | Fsharp -> ".fsproj"

// F# projects list their sources with <Compile>; C# picks them up implicitly.
let private projectCompiles (p: Project) =
    match p.Language, p.Compiles with
    | Fsharp, Some compiles -> compiles
    | Fsharp, None -> p.Sources |> List.map (fun s -> s.Name)
    | Csharp, _ -> []

let private renderProject (p: Project) =
    let compiles = projectCompiles p

    match p.Runner with
    | None -> sdkProject p.Props [ itemGroup (compiles |> List.map compileInclude); itemGroup p.Items ]
    | Some XunitV2 -> xunitV2TestProject p.Props compiles p.ProjectRefs
    | Some MtpXunitV3 -> mtpXunitV3Project p.Props compiles p.ProjectRefs

// Writes every project (and its sources) into `scratch` and returns the absolute path
// of the run target.
let writeGraph (scratch: string) (graph: Graph) =
    for p in graph.Projects do
        let dir = Path.Combine(scratch, p.Dir)
        Directory.CreateDirectory dir |> ignore

        for s in p.Sources do
            let path = Path.Combine(dir, s.Name)
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllText(path, s.Content)

        File.WriteAllText(Path.Combine(dir, p.Dir + ext p.Language), renderProject p)

    Path.Combine(scratch, graph.RunTarget)

// Writes the graph into `scratch` and runs `mutannot run <target> extraArgs`, returning
// the exit code. `graph |> runInWith scratch [ "--jobs"; "2" ]`.
let runInWith (scratch: string) (extraArgs: string list) (graph: Graph) =
    let target = writeGraph scratch graph
    Program.main (Array.ofList ("run" :: target :: extraArgs))

// `graph |> runIn scratch`: the common no-extra-args case.
let runIn (scratch: string) (graph: Graph) = runInWith scratch [] graph

// --- Building graphs ------------------------------------------------------

let file name content = { Name = name; Content = content }

// A library project under <scratch>/<dir>.
let library (lang: Language) (dir: string) (sources: SourceFile list) =
    { Dir = dir
      Language = lang
      Runner = None
      Props = []
      ProjectRefs = []
      Compiles = None
      Items = []
      Sources = sources }

// A test project under <scratch>/<dir> using `runner`, referencing `refs` (relative
// include paths) and carrying `sources`.
let testProject (lang: Language) (runner: Runner) (dir: string) (refs: string list) (sources: SourceFile list) =
    { Dir = dir
      Language = lang
      Runner = Some runner
      Props = []
      ProjectRefs = refs
      Compiles = None
      Items = []
      Sources = sources }

let private mapProjects f (g: Graph) =
    { g with
        Projects = List.map f g.Projects }

// Appends properties to the library project (the one under test).
let withLibProps (props: string list) (g: Graph) =
    g
    |> mapProjects (fun p ->
        if Option.isNone p.Runner then
            { p with Props = p.Props @ props }
        else
            p)

// Pins an explicit <AssemblyName> on the library so a mutated build keeps the name.
let pinAssemblyName (name: string) =
    withLibProps [ $"<AssemblyName>{name}</AssemblyName>" ]

// Switches the test project's runner (e.g. `graphWithKillableMutant Csharp |> withRunner MtpXunitV3`).
let withRunner (runner: Runner) (g: Graph) =
    g
    |> mapProjects (fun p ->
        if Option.isSome p.Runner then
            { p with Runner = Some runner }
        else
            p)

// --- Source helpers -------------------------------------------------------

// A unified diff for a single file. `hunk` is the `@@ ... @@` header; `lines` the
// body (context lines plus `-`/`+`), each already carrying its leading marker.
let diff (path: string) (hunk: string) (lines: string list) =
    String.concat "\n" ([ $"--- a/{path}"; $"+++ b/{path}"; hunk ] @ lines)

// Wraps a patch in a triple-quoted ShouldCatch attribute. The patch lines sit at
// column 0 inside the literal so their diff markers land verbatim.
let csharpShouldCatch (patch: string) =
    "[ShouldCatch(\"\"\"\n" + patch + "\n\"\"\")]"

let fsharpShouldCatch (patch: string) =
    "[<ShouldCatch(\"\"\"\n" + patch + "\n\"\"\")>]"

// --- The canonical graph with a killable mutant ---------------------------
//
// A green library (a single `add`) plus a test that pins it and carries a ShouldCatch
// flipping `+` to `-`. A real `run` establishes the green baseline, applies the patch,
// and the now-failing test proves the mutant was killed -- so `graphWithKillableMutant
// lang |> runIn scratch` returns 0. The library lives under "Calc", the test under
// "Calc.Tests".

let private csharpKillable =
    let patch =
        diff
            "Calc/Calc.cs"
            "@@ -1,5 +1,5 @@"
            [ " namespace Calc;"
              " public static class Calc"
              " {"
              "-    public static int Add(int x, int y) => x + y;"
              "+    public static int Add(int x, int y) => x - y;"
              " }" ]

    let testSource =
        "using Mutannot.Annotations;\n"
        + "using Xunit;\n"
        + "namespace CalcTests;\n"
        + "public class CalcTests\n"
        + "{\n"
        + "    "
        + csharpShouldCatch patch
        + "\n"
        + "    [Fact]\n"
        + "    public void Add_Works() => Assert.Equal(5, Calc.Calc.Add(2, 3));\n"
        + "}\n"

    { Projects =
        [ { library
                Csharp
                "Calc"
                [ file
                      "Calc.cs"
                      "namespace Calc;\npublic static class Calc\n{\n    public static int Add(int x, int y) => x + y;\n}\n" ] with
              Props = [ "<Nullable>enable</Nullable>" ] }
          testProject Csharp XunitV2 "Calc.Tests" [ "../Calc/Calc.csproj" ] [ file "Tests.cs" testSource ]
          |> fun p ->
              { p with
                  Props =
                      [ "<IsPackable>false</IsPackable>"
                        "<Nullable>enable</Nullable>"
                        "<ImplicitUsings>enable</ImplicitUsings>" ] } ]
      RunTarget = "Calc.Tests/Calc.Tests.csproj" }

let private fsharpKillable =
    let patch =
        diff
            "Calc/Calc.fs"
            "@@ -3,2 +3,2 @@ namespace Calc"
            [ " module Calc ="; "-    let add x y = x + y"; "+    let add x y = x - y" ]

    let testSource =
        String.concat
            "\n"
            [ "module Calc.Tests.Tests"
              ""
              "open Xunit"
              "open Mutannot.Annotations"
              "open Calc"
              ""
              "[<Fact>]"
              fsharpShouldCatch patch
              "let ``add sums`` () ="
              "    Assert.Equal(5, Calc.add 2 3)"
              "" ]

    { Projects =
        [ library
              Fsharp
              "Calc"
              [ file
                    "Calc.fs"
                    (String.concat "\n" [ "namespace Calc"; ""; "module Calc ="; "    let add x y = x + y"; "" ]) ]
          testProject Fsharp XunitV2 "Calc.Tests" [ "../Calc/Calc.fsproj" ] [ file "Tests.fs" testSource ] ]
      RunTarget = "Calc.Tests/Calc.Tests.fsproj" }

// The canonical project to mutate.
let graphWithKillableMutant =
    function
    | Csharp -> csharpKillable
    | Fsharp -> fsharpKillable

let sha256 (bytes: byte[]) =
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes)
