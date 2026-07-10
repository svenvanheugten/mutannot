open System
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open Mutannot
open Mutannot.Annotations
open Fli
open Argu

type Mutation =
    { TestName: string
      TestFilter: string
      Patch: string }

let ensureBuilt projectPath =
    cli {
        Exec "dotnet"
        Arguments [ "build"; projectPath ]
        Output(new StreamWriter(Console.OpenStandardOutput()))
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let runTest projectPath testFilter =
    ensureBuilt projectPath

    cli {
        Exec "dotnet"

        Arguments [ "test"; projectPath; "--no-build"; "--filter"; testFilter ]

        Output(new StreamWriter(Console.OpenStandardOutput()))
    }
    |> Command.execute
    |> Output.toExitCode

let getAssemblyPath projectPath =
    cli {
        Exec "dotnet"
        Arguments [ "msbuild"; projectPath; "--getProperty:TargetPath" ]
    }
    |> Command.execute
    |> Output.toText

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
          TestFilter = $"FullyQualifiedName={testName}"
          Patch = patch })

let getTypeMutations (t: Type) =
    let testFilter = $"FullyQualifiedName~{t.FullName}."

    t.GetCustomAttributesData()
    |> Seq.choose tryGetShouldCatchPatch
    |> Seq.map (fun patch ->
        { TestName = t.FullName
          TestFilter = testFilter
          Patch = patch })

let getMutations projectPath =
    ensureBuilt projectPath

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

    let filteredMutations =
        getMutations projectPath
        |> Seq.filter _.Patch.Contains(maybeFilter |> Option.defaultValue "")
        |> Seq.indexed

    for index, mutationCase in filteredMutations do
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

            match runTest mutatedTestProjectPath mutationCase.TestFilter with
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
