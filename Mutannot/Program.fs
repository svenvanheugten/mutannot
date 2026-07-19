open System.IO
open Mutannot
open Argu

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
    Runner.run projectPath validateOnly maybeFilter

let runAnnotateType (parsedArguments: ParseResults<AnnotateTypeArguments>) =
    let testFilePath, typeName, diffFilePath = parsedArguments.GetResult Inputs
    TypeAnnotator.annotateType testFilePath typeName diffFilePath
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
