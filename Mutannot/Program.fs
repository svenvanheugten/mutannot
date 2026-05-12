open System
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open Fli

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

let unindented (s: string) =
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
                      Patch = attr.ConstructorArguments[0].Value :?> string |> unindented }
            | _ -> None))
    |> Seq.toList

[<EntryPoint>]
let main argv =
    if argv.Length <> 1 then
        eprintfn "Usage: mutannot <path/to/project.csproj|fsproj>"
        exit 1

    ensureCleanWorkingDirectory ()

    AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> restore ())

    let projectPath = argv[0]

    for mutationCase in getMutationCases projectPath do
        printfn "MUTATION\n\n%s" <| mutationCase.Patch
        applyPatch mutationCase.Patch

        match runTest projectPath mutationCase.TestName with
        | 0 ->
            eprintfn "Expected tested to fail, but it succeeded"
            exit 3
        | _ -> printfn "Mutant killed\n"

        restore ()

    0
