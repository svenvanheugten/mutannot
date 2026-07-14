open System
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open Mutannot
open Mutannot.Annotations
open Fli
open Argu

// How a project's tests are discovered and run. VSTest is the classic
// Microsoft.NET.Test.Sdk pipeline driven through `dotnet test --filter`; MTP is
// Microsoft.Testing.Platform, where the build produces a self-hosting test
// executable that takes its own command-line filter. mutannot only supports
// xunit v3 on MTP (other frameworks error out in getRunnerKind).
type RunnerKind =
    | VSTest
    | MtpXunitV3

// What a mutation's test should be narrowed to when run. The concrete filter
// argument differs per RunnerKind (see filter builders below), so the scope is
// kept abstract until run time.
type TestScope =
    | TestMethod of fullyQualifiedName: string
    | TestClass of fullyQualifiedTypeName: string

type Mutation =
    { TestName: string
      TestScope: TestScope
      Patch: string }

// A mutated project keeps the original's assembly name so InternalsVisibleTo and
// anything else keyed on it keep working (see Mutator.createMutatedProject). Its
// build output therefore must not land in the shared bin/obj: it would clobber
// the original same-named assembly and, being newer than its sources, leave a
// later rebuild of the original treating the stale mutant as up to date.
// --artifacts-path redirects both bin/ and obj/ into a separate tree keyed by
// project file name, so X.mutated lands apart from X. It is passed to both the
// build and the (--no-build) test run so the runner looks where the build wrote.
let mutatedBuildArgs = [ "--artifacts-path"; ".mutannot/artifacts" ]

let ensureBuilt buildArgs projectPath =
    cli {
        Exec "dotnet"
        Arguments([ "build"; projectPath ] @ buildArgs)
        Output(new StreamWriter(Console.OpenStandardOutput()))
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let getAssemblyPath projectPath =
    cli {
        Exec "dotnet"
        Arguments [ "msbuild"; projectPath; "--getProperty:TargetPath" ]
    }
    |> Command.execute
    |> Output.toText

let private vsTestFilter scope =
    match scope with
    | TestMethod fqn -> $"FullyQualifiedName={fqn}"
    // A trailing '.' anchors the match to members of the type rather than any
    // type whose name merely starts with it.
    | TestClass fqn -> $"FullyQualifiedName~{fqn}."

// xunit v3's in-process (MTP) runner takes its own filter switches: -method for
// a single fully qualified test method, -class for every test in a type.
let private mtpFilterArgs scope =
    match scope with
    | TestMethod fqn -> [ "-method"; fqn ]
    | TestClass fqn -> [ "-class"; fqn ]

let runTest runnerKind projectPath scope =
    ensureBuilt mutatedBuildArgs projectPath

    match runnerKind with
    | VSTest ->
        cli {
            Exec "dotnet"

            Arguments([ "test"; projectPath; "--no-build"; "--filter"; vsTestFilter scope ] @ mutatedBuildArgs)

            Output(new StreamWriter(Console.OpenStandardOutput()))
        }
        |> Command.execute
        |> Output.toExitCode
    | MtpXunitV3 ->
        // An MTP project builds into a self-hosting test executable; `dotnet run`
        // launches it (via the runtime, not the native apphost) and forwards the
        // xunit filter switches after `--`. Running the already-built project this
        // way propagates the test exit code without having to locate the binary in
        // the redirected artifacts tree ourselves.
        cli {
            Exec "dotnet"

            Arguments(
                [ "run"; "--project"; projectPath; "--no-build" ]
                @ mutatedBuildArgs
                @ [ "--" ]
                @ mtpFilterArgs scope
            )

            Output(new StreamWriter(Console.OpenStandardOutput()))
        }
        |> Command.execute
        |> Output.toExitCode

// Runs one target test against the original, unmutated build and returns its
// exit code. Mutation testing is only meaningful from a green baseline: because
// mutannot recognizes a killed mutant by its failing run, a target that doesn't
// already pass -- a broken build, a misdetected runner, an environment problem
// -- would make its mutant look spuriously killed. The caller runs these up
// front; the project is already built (by getMutations), hence --no-build.
let runControl runnerKind projectPath scope =
    match runnerKind with
    | VSTest ->
        cli {
            Exec "dotnet"
            Arguments [ "test"; projectPath; "--no-build"; "--filter"; vsTestFilter scope ]
            Output(new StreamWriter(Console.OpenStandardOutput()))
        }
        |> Command.execute
        |> Output.toExitCode
    | MtpXunitV3 ->
        cli {
            Exec "dotnet"
            Arguments([ "run"; "--project"; projectPath; "--no-build"; "--" ] @ mtpFilterArgs scope)
            Output(new StreamWriter(Console.OpenStandardOutput()))
        }
        |> Command.execute
        |> Output.toExitCode

// A project runs on Microsoft.Testing.Platform when the SDK reports
// IsTestingPlatformApplication; that property is contributed by the testing
// platform's build targets, so it is picked up wherever the configuration lives
// (the project file, Directory.Build.props, ...). mutannot only supports xunit
// v3 there, detected by its package reference, and errors out otherwise.
let getRunnerKind projectPath =
    let getProperty name =
        (cli {
            Exec "dotnet"
            Arguments [ "msbuild"; projectPath; $"--getProperty:{name}" ]
         }
         |> Command.execute
         |> Output.toText)
            .Trim()

    let hasXunitV3PackageReference () =
        let json =
            cli {
                Exec "dotnet"
                Arguments [ "msbuild"; projectPath; "--getItem:PackageReference" ]
            }
            |> Command.execute
            |> Output.toText

        use doc = System.Text.Json.JsonDocument.Parse json

        match doc.RootElement.TryGetProperty "Items" with
        | true, items ->
            match items.TryGetProperty "PackageReference" with
            | true, refs ->
                refs.EnumerateArray()
                |> Seq.exists (fun r -> r.GetProperty("Identity").GetString() = "xunit.v3")
            | false, _ -> false
        | false, _ -> false

    match getProperty "IsTestingPlatformApplication" with
    | "true" ->
        if hasXunitV3PackageReference () then
            MtpXunitV3
        else
            eprintfn
                $"Project '{projectPath}' uses Microsoft.Testing.Platform but does not reference xunit.v3. mutannot only supports xunit v3 on Microsoft.Testing.Platform."

            exit 2
    | _ -> VSTest

let getMetadataLoadContext (assemblyPath: string) =
    // This allows us to inspect assemblies regardless of the platform that they were built for
    // https://learn.microsoft.com/en-us/dotnet/standard/assembly/inspect-contents-using-metadataloadcontext
    let assemblyDir = Path.GetDirectoryName assemblyPath

    let pathAssemblyResolver =
        [ yield assemblyPath
          yield! Directory.EnumerateFiles(assemblyDir, "*.dll")
          yield! Directory.EnumerateFiles(assemblyDir, "*.exe")
          yield! Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll") ]
        |> PathAssemblyResolver

    new MetadataLoadContext(pathAssemblyResolver, typeof<obj>.Assembly.GetName().Name)

let getDiffForFile filePath =
    cli {
        Exec "git"
        Arguments [ "diff"; "HEAD"; "--"; filePath ]
    }
    |> Command.execute
    |> Output.toText

let annotateType testFilePath typeName diffFilePath =
    let patch = getDiffForFile diffFilePath

    if patch = "" then
        eprintfn $"No diff found for '{diffFilePath}'."
        exit 2

    let testSource = File.ReadAllText testFilePath

    match TypeAnnotator.annotateTypeWithPatch typeName patch testSource with
    | Ok updatedSource ->
        File.WriteAllText(testFilePath, updatedSource)
        printfn $"Annotated type '{typeName}' in '{testFilePath}'."
    | Error message ->
        eprintfn "%s" message
        exit 2

let unindentPatch (s: string) =
    // Split on '\n' only (not "\r\n") so a CRLF file's patch keeps the '\r' as
    // part of each line. `git apply` matches a patch against the target file
    // byte-for-byte, so the patch must carry the file's own line endings. The
    // final join therefore reuses '\n', leaving any surviving '\r' in place;
    // joining with Environment.NewLine instead would force CRLF onto every line
    // on Windows and mangle patches for LF files.
    let lines = s.Split('\n')

    let indexOfFirstNonEmptyLine =
        lines |> Array.findIndex (not << String.IsNullOrWhiteSpace)

    let inndentantionOfFirstNonEmptyLine =
        lines[indexOfFirstNonEmptyLine] |> Seq.takeWhile Char.IsWhiteSpace |> Seq.length

    lines[indexOfFirstNonEmptyLine..]
    |> Seq.map (fun line -> line.Substring(min inndentantionOfFirstNonEmptyLine line.Length))
    |> String.concat "\n"

let tryGetShouldCatchPatch (attr: CustomAttributeData) =
    if attr.AttributeType.FullName = (typeof<ShouldCatchAttribute>).FullName then
        Some(attr.ConstructorArguments[0].Value :?> string |> unindentPatch)
    else
        None

let getMethodMutations (m: MethodInfo) =
    let testName = $"{m.DeclaringType.FullName}.{m.Name}"

    m.GetCustomAttributesData()
    |> Seq.choose tryGetShouldCatchPatch
    |> Seq.map (fun patch ->
        { TestName = testName
          TestScope = TestMethod testName
          Patch = patch })

let getTypeMutations (t: Type) =
    t.GetCustomAttributesData()
    |> Seq.choose tryGetShouldCatchPatch
    |> Seq.map (fun patch ->
        { TestName = t.FullName
          TestScope = TestClass t.FullName
          Patch = patch })

let getMutations projectPath =
    ensureBuilt [] projectPath

    let assemblyPath = getAssemblyPath projectPath

    use metadataLoadContext = getMetadataLoadContext assemblyPath

    let assemblyTypes =
        assemblyPath |> metadataLoadContext.LoadFromAssemblyPath |> _.GetTypes()

    assemblyTypes
    |> Seq.collect (fun t ->
        seq {
            yield! getTypeMutations t

            yield!
                t.GetMethods(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
                |> Seq.collect getMethodMutations
        })
    |> Seq.toList

type RunArguments =
    | [<MainCommand; ExactlyOnce>] ProjectPath of ProjectPath: string
    | Filter of SearchString: string
    | Validate_Only

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | ProjectPath _ -> "path/to/testproject.csproj|fsproj"
            | Filter _ -> "filter down to mutations that contain the given search string."
            | Validate_Only -> "check if the patches apply, but don't run the mutations."

type AnnotateTypeArguments =
    | [<MainCommand; ExactlyOnce>] Inputs of TestFilePath: string * TypeName: string * DiffFilePath: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Inputs _ -> "path to the test file, type name, and path to the changed file to diff."

type Arguments =
    | [<CliPrefix(CliPrefix.None)>] Run of ParseResults<RunArguments>
    | [<CliPrefix(CliPrefix.None)>] Annotate_Type of ParseResults<AnnotateTypeArguments>

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Run _ -> "run mutations for path/to/testproject.csproj|fsproj."
            | Annotate_Type _ -> "annotate an F# type with a ShouldCatch attribute from a git diff."

let runMutations (parsedArguments: ParseResults<RunArguments>) =
    let projectPath = Path.GetFullPath(parsedArguments.GetResult ProjectPath)
    let validateOnly = parsedArguments.Contains Validate_Only
    let maybeFilter = parsedArguments.TryGetResult Filter

    // Detecting the runner needs the testing platform's build targets, which are
    // only imported once the project has been restored (done by getMutations
    // below). It is also irrelevant when only validating, so defer it.
    let runnerKind = lazy getRunnerKind projectPath

    let filteredMutations =
        getMutations projectPath
        |> List.filter _.Patch.Contains(maybeFilter |> Option.defaultValue "")

    // Establish a green baseline before mutating anything (see runControl): run
    // every target test unmutated, up front, and refuse to proceed if any fails
    // -- otherwise its mutant's failing run couldn't be trusted as a kill.
    // Skipped when only validating, since no tests are run then.
    let baselineFailed =
        not validateOnly
        && filteredMutations
           |> List.map _.TestScope
           |> List.distinct
           |> List.exists (fun scope -> runControl runnerKind.Value projectPath scope <> 0)

    if baselineFailed then
        Console.ForegroundColor <- ConsoleColor.Red
        eprintf "ERROR: Tests must pass on the unmutated project before mutations can be run\n"
        Console.ResetColor()
        4
    else
        for index, mutationCase in List.indexed filteredMutations do
            Console.ForegroundColor <- ConsoleColor.Green
            printf $"MUTATION {index + 1}\n"

            Console.ForegroundColor <- ConsoleColor.Magenta
            printf "Test:\n"
            Console.ResetColor()
            printf "%s\n\n" mutationCase.TestName

            Console.ForegroundColor <- ConsoleColor.Magenta
            printf "Patch:\n"
            Console.ResetColor()
            printf "%s\n" mutationCase.Patch

            let mutatedTestProjectPath = Mutator.applyMutation projectPath mutationCase.Patch

            if not validateOnly then
                Console.ForegroundColor <- ConsoleColor.Magenta
                printf "Output:\n"
                Console.ResetColor()

                match runTest runnerKind.Value mutatedTestProjectPath mutationCase.TestScope with
                | 0 ->
                    Console.ForegroundColor <- ConsoleColor.Red
                    eprintf "ERROR: Expected tests to fail, but they succeeded\n"
                    Console.ResetColor()
                    exit 3
                | _ ->
                    Console.ForegroundColor <- ConsoleColor.Green
                    printf "✓ Mutant killed\n\n"

        Console.ForegroundColor <- ConsoleColor.Green

        if validateOnly then
            printf "Success: All mutations valid\n"
        else
            printf "Success: All mutants killed\n"

        Console.ResetColor()

        0

let runAnnotateType (parsedArguments: ParseResults<AnnotateTypeArguments>) =
    let testFilePath, typeName, diffFilePath = parsedArguments.GetResult Inputs
    annotateType testFilePath typeName diffFilePath
    0

[<EntryPoint>]
let main argv =
    let parser =
        ArgumentParser.Create<Arguments>(programName = "mutannot", errorHandler = ProcessExiter())

    let parsedArguments = parser.ParseCommandLine argv

    match parsedArguments.TryGetSubCommand() with
    | Some(Run runArguments) -> runMutations runArguments
    | Some(Annotate_Type annotateTypeArguments) -> runAnnotateType annotateTypeArguments
    | None ->
        eprintf "%s" (parser.PrintUsage())
        2
