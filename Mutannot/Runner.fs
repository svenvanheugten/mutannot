namespace Mutannot

open System
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open Mutannot.Annotations
open Fli

module Runner =
    // How a project's tests are discovered and run. VSTest is the classic
    // Microsoft.NET.Test.Sdk pipeline driven through `dotnet test --filter`; MTP is
    // Microsoft.Testing.Platform, where the build produces a self-hosting test
    // executable that takes its own command-line filter. mutannot only supports
    // xunit v3 on MTP (other frameworks error out in getRunnerKind). An MTP xunit v3
    // executable has two possible entry points with *different* filter syntaxes: the
    // in-process console runner (-class/-method) and the MTP runner
    // (--filter-class/--filter-method). mutannot builds with the MTP runner (see
    // forceMtpRunnerArgs) and filters it with --filter-class/--filter-method.
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

    // Building an MTP xunit v3 project with UseMicrosoftTestingPlatformRunner=true
    // gives its executable the MTP runner entry point, which mutannot filters with
    // --filter-class/--filter-method. This is a build-time property -- it selects the
    // executable's argument parser -- and every test run uses --no-build, so it goes
    // on the build, not the run. Added only for the MtpXunitV3 runner (VSTest goes
    // through `dotnet test` and never touches this entry point).
    let private forceMtpRunnerArgs = [ "-p:UseMicrosoftTestingPlatformRunner=true" ]

    let ensureBuilt buildArgs projectPath =
        cli {
            Exec "dotnet"
            Arguments([ "build"; projectPath ] @ buildArgs)
            Output(new StreamWriter(Console.OpenStandardOutput()))
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

    let private getAssemblyPath projectPath =
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

    // xunit v3 takes a fully qualified method (a single test) or class (all its
    // tests) filter, but spells the switches differently depending on which entry
    // point the executable uses (see RunnerKind).
    let private mtpFilterArgs scope =
        match scope with
        | TestMethod fqn -> [ "--filter-method"; fqn ]
        | TestClass fqn -> [ "--filter-class"; fqn ]

    let private runTest runnerKind projectPath scope =
        let buildArgs =
            match runnerKind with
            | VSTest -> mutatedBuildArgs
            | MtpXunitV3 -> forceMtpRunnerArgs @ mutatedBuildArgs

        ensureBuilt buildArgs projectPath

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
    // front; getMutations already built the project, so the runs use --no-build
    // (the MtpXunitV3 branch first pins the MTP runner entry point, see below).
    let private runControl runnerKind projectPath scope =
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
            // getMutations built plainly, so rebuild the original with the MTP runner
            // before running it against --filter-*. The property only changes the
            // entry point, so this is a near no-op incremental build, and only the
            // first baseline scope actually rebuilds.
            ensureBuilt forceMtpRunnerArgs projectPath

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
    // v3 there (referencesXunitV3 comes from the test assembly, see getMutations)
    // and errors out otherwise.
    let getRunnerKind projectPath referencesXunitV3 =
        let getProperty name =
            (cli {
                Exec "dotnet"
                Arguments [ "msbuild"; projectPath; $"--getProperty:{name}" ]
             }
             |> Command.execute
             |> Output.toText)
                .Trim()

        match getProperty "IsTestingPlatformApplication" with
        | "true" ->
            if referencesXunitV3 then
                MtpXunitV3
            else
                eprintfn
                    $"Project '{projectPath}' uses Microsoft.Testing.Platform but its tests are not xunit v3. mutannot only supports xunit v3 on Microsoft.Testing.Platform."

                exit 2
        | _ -> VSTest

    let private getMetadataLoadContext (assemblyPath: string) =
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

    let private tryGetShouldCatchPatch (attr: CustomAttributeData) =
        if attr.AttributeType.FullName = (typeof<ShouldCatchAttribute>).FullName then
            Some(attr.ConstructorArguments[0].Value :?> string |> unindentPatch)
        else
            None

    let private getMethodMutations (m: MethodInfo) =
        let testName = $"{m.DeclaringType.FullName}.{m.Name}"

        m.GetCustomAttributesData()
        |> Seq.choose tryGetShouldCatchPatch
        |> Seq.map (fun patch ->
            { TestName = testName
              TestScope = TestMethod testName
              Patch = patch })

    let private getTypeMutations (t: Type) =
        t.GetCustomAttributesData()
        |> Seq.choose tryGetShouldCatchPatch
        |> Seq.map (fun patch ->
            { TestName = t.FullName
              TestScope = TestClass t.FullName
              Patch = patch })

    // Returns the mutations found in the test assembly, along with whether that
    // assembly references xunit v3. The latter is read from what the assembly
    // actually binds to rather than from a declared PackageReference: xunit.v3 may
    // arrive transitively (e.g. via a shared testing package or a referenced
    // project), yet test code using [<Fact>] still references xunit.v3.core either
    // way. The assembly is already loaded here to discover mutations, so this reuses
    // it rather than making a separate msbuild query.
    let getMutations projectPath =
        ensureBuilt [] projectPath

        let assemblyPath = getAssemblyPath projectPath

        use metadataLoadContext = getMetadataLoadContext assemblyPath

        let assembly = metadataLoadContext.LoadFromAssemblyPath assemblyPath

        let referencesXunitV3 =
            assembly.GetReferencedAssemblies()
            |> Seq.exists (fun a -> not (isNull a.Name) && a.Name.StartsWith("xunit.v3", StringComparison.OrdinalIgnoreCase))

        let mutations =
            assembly.GetTypes()
            |> Seq.collect (fun t ->
                seq {
                    yield! getTypeMutations t

                    yield!
                        t.GetMethods(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
                        |> Seq.collect getMethodMutations
                })
            |> Seq.toList

        mutations, referencesXunitV3

    // Runs the mutations found in the test project. Returns the process exit code.
    let internal run projectPath validateOnly (maybeFilter: string option) =
        let mutations, referencesXunitV3 = getMutations projectPath

        // Detecting the runner needs the testing platform's build targets, which are
        // only imported once the project has been restored (done by getMutations
        // above). It is also irrelevant when only validating, so defer it.
        let runnerKind = lazy getRunnerKind projectPath referencesXunitV3

        let filteredMutations =
            mutations
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
