open System.IO
open Mutannot
open Argu

type RunArguments =
    | [<MainCommand; ExactlyOnce>] ProjectPath of ProjectPath: string
    | Filter of SearchString: string
    | Only_New_Or_Updated_Since of BaseBranch: string
    | Jobs of Count: int
    | Validate_Only

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | ProjectPath _ -> "path/to/testproject.csproj|fsproj"
            | Filter _ -> "filter down to mutations that contain the given search string."
            | Only_New_Or_Updated_Since _ ->
                "run only the mutations that are new or updated compared to the given base branch (e.g. main)."
            | Jobs _ -> "number of mutations to run in parallel (default: 1)."
            | Validate_Only -> "check if the patches apply, but don't run the mutations."

type ValidateArguments =
    | [<MainCommand; ExactlyOnce>] TargetPath of TargetPath: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | TargetPath _ -> "path to a C# or F# source file or a directory to scan."

type Arguments =
    | [<CliPrefix(CliPrefix.None)>] Run of ParseResults<RunArguments>
    | [<CliPrefix(CliPrefix.None)>] Validate of ParseResults<ValidateArguments>

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Run _ -> "run mutations for path/to/testproject.csproj|fsproj."
            | Validate _ -> "quickly check that a source file's ShouldCatch patches still apply, without building."

let runMutations (parsedArguments: ParseResults<RunArguments>) =
    let projectPath = Path.GetFullPath(parsedArguments.GetResult ProjectPath)
    let validateOnly = parsedArguments.Contains Validate_Only
    let maybeFilter = parsedArguments.TryGetResult Filter
    let jobs = parsedArguments.GetResult(Jobs, defaultValue = 1)

    // The base branch to diff against, resolved into the concrete set of new or
    // updated patches here (rather than in Runner) so the reusable extractPatches
    // logic in PatchValidator, which is compiled after Runner, stays available.
    let maybeAllowedPatches =
        parsedArguments.TryGetResult Only_New_Or_Updated_Since
        |> Option.map (fun baseBranch ->
            PatchValidator.newOrUpdatedPatches (Vcs.root (Path.GetDirectoryName projectPath)) baseBranch)

    if jobs < 1 then
        eprintfn "--jobs must be at least 1."
        2
    else
        Runner.run projectPath validateOnly maybeFilter maybeAllowedPatches jobs

let runValidate (parsedArguments: ParseResults<ValidateArguments>) =
    let targetPath = parsedArguments.GetResult TargetPath
    PatchValidator.validate targetPath

[<EntryPoint>]
let main argv =
    let parser =
        ArgumentParser.Create<Arguments>(programName = "mutannot", errorHandler = ProcessExiter())

    let parsedArguments = parser.ParseCommandLine argv

    match parsedArguments.TryGetSubCommand() with
    | Some(Run runArguments) -> runMutations runArguments
    | Some(Validate validateArguments) -> runValidate validateArguments
    | None ->
        eprintf "%s" (parser.PrintUsage())
        2
