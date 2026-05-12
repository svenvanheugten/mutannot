open System
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open Fli
open Argu

type MutationCase = { TestName: string; Patch: string }

let ensureCleanWorkingDirectory () =
    let gitState =
        cli {
            Exec "git"
            Arguments [ "status"; "--porcelain" ]
        }
        |> Command.execute
        |> Output.throwIfErrored

    if gitState.Text <> None then
        eprintfn "Uncommitted changes. Refusing to run."
        exit 2

let applyPatch patch =
    cli {
        Exec "git"
        Arguments [ "apply"; "-" ]
        Input patch
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let restore () =
    cli {
        Exec "git"
        Arguments [ "restore"; "--staged"; "--worktree"; "." ]
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let ensureBuilt projectPath =
    cli {
        Exec "dotnet"
        Arguments [ "build"; projectPath ]
        Output(new StreamWriter(Console.OpenStandardOutput()))
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

let runTest projectPath testName =
    cli {
        Exec "dotnet"
        Arguments [ "test"; projectPath; "--filter"; $"FullyQualifiedName={testName}" ]
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

let unindentPatch (s: string) =
    let lines = s.Split([| "\r\n"; "\n" |], StringSplitOptions.None)

    let indexOfFirstNonEmptyLine =
        lines |> Array.findIndex (not << String.IsNullOrWhiteSpace)

    let inndentantionOfFirstNonEmptyLine =
        lines[indexOfFirstNonEmptyLine] |> Seq.takeWhile Char.IsWhiteSpace |> Seq.length

    lines[indexOfFirstNonEmptyLine..]
    |> Seq.map (fun line -> line.Substring(min inndentantionOfFirstNonEmptyLine line.Length))
    |> String.concat Environment.NewLine

let getMutationCases projectPath =
    ensureBuilt projectPath

    let assemblyPath = getAssemblyPath projectPath

    use metadataLoadContext = getMetadataLoadContext assemblyPath

    let assemblyTypes =
        assemblyPath |> metadataLoadContext.LoadFromAssemblyPath |> _.GetTypes()

    let assemblyMethods =
        assemblyTypes
        |> Seq.collect _.GetMethods(BindingFlags.Public ||| BindingFlags.Instance)

    assemblyMethods
    |> Seq.collect (fun m ->
        m.GetCustomAttributesData()
        |> Seq.choose (fun attr ->
            match attr.AttributeType.FullName with
            | "Mutannot.MutationCaseAttribute" ->
                Some
                    { TestName = $"{m.DeclaringType.FullName}.{m.Name}"
                      Patch = attr.ConstructorArguments[0].Value :?> string |> unindentPatch }
            | _ -> None))
    |> Seq.toList

type Arguments =
    | [<MainCommand; ExactlyOnce>] ProjectPath of ProjectPath: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | ProjectPath _ -> "path/to/project.csproj|fsproj"

[<EntryPoint>]
let main argv =
    let parsedArguments =
        ArgumentParser.Create<Arguments>(programName = "mutannot")
        |> _.ParseCommandLine(argv)

    let projectPath = parsedArguments.GetResult ProjectPath

    ensureCleanWorkingDirectory ()

    AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> restore ())

    for index, mutationCase in getMutationCases projectPath |> Seq.indexed do
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

        Console.ForegroundColor <- ConsoleColor.Magenta
        printf "Output:\n"
        Console.ResetColor()
        applyPatch mutationCase.Patch

        match runTest projectPath mutationCase.TestName with
        | 0 ->
            Console.ForegroundColor <- ConsoleColor.Red
            eprintf "ERROR: Expected tested to fail, but it succeeded\n"
            Console.ResetColor()
            exit 3
        | _ ->
            Console.ForegroundColor <- ConsoleColor.Green
            printf "✓ Mutant killed\n\n"

        restore ()

    Console.ForegroundColor <- ConsoleColor.Green
    printf "Success: All mutants killed\n"
    Console.ResetColor()

    0
