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
    type private RunnerKind =
        | VSTest
        | MtpXunitV3

    // The outcome of running a mutant's target test, classified from the runner's
    // exit code (see classifyOutcome). Kept separate from the raw code because only
    // one specific code per runner means "a test ran and failed" -- every other
    // non-zero code is an error that must not be miscounted as a kill.
    type private RunOutcome =
        // A test ran and passed: the mutation went undetected (the mutant survived).
        | Survived
        // A test ran and failed: the mutation was caught (the mutant was killed).
        | Killed
        // The run neither cleanly passed nor cleanly failed -- a crash, an invalid
        // filter matching zero tests, an infrastructure failure, and so on. Carries
        // the exit code for the diagnostic. This is *not* a kill: it means we could
        // not establish that a test caught the mutation.
        | Errored of exitCode: int

    // Classifies a test run's exit code into a RunOutcome. The runners disagree on
    // what a failing test looks like, and treating every non-zero code as a kill is
    // wrong: a mutant that crashes the host, breaks test discovery so zero tests
    // run, or trips an infrastructure error would all be scored as killed when no
    // test actually caught them.
    //
    // VSTest (`dotnet test`) collapses everything into 0 (all passed) or 1 (a test
    // failed); it has no other codes, so 1 is its sole failure signal. Microsoft's
    // Testing Platform is granular: 0 success, 2 "at least one test failed", and a
    // documented range of other codes (1 catch-all, 3 aborted, 5 invalid args, 7
    // crashed, 8 zero tests ran, 10 adapter failure, ...) for everything else -- so
    // a kill there is exactly code 2.
    // https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-exit-codes
    let private classifyOutcome runnerKind exitCode =
        match runnerKind, exitCode with
        | _, 0 -> Survived
        | VSTest, 1 -> Killed
        | MtpXunitV3, 2 -> Killed
        | _, code -> Errored code

    // What a mutation's test should be narrowed to when run. The concrete filter
    // argument differs per RunnerKind (see filter builders below), so the scope is
    // kept abstract until run time.
    type private TestScope =
        | TestMethod of fullyQualifiedName: string
        | TestClass of fullyQualifiedTypeName: string

    type private Mutation =
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
    //
    // The tree is keyed by segment too, so concurrent `run --jobs` workers (each
    // owning a segment) build into separate bin/obj and never race one another.
    let private mutatedBuildArgs gitRoot segment =
        [ "--artifacts-path"
          Path.Combine(gitRoot, ".mutannot", string segment, "artifacts") ]

    // Building an MTP xunit v3 project with UseMicrosoftTestingPlatformRunner=true
    // gives its executable the MTP runner entry point, which mutannot filters with
    // --filter-class/--filter-method. This is a build-time property -- it selects the
    // executable's argument parser -- and every test run uses --no-build, so it goes
    // on the build, not the run. Added only for the MtpXunitV3 runner (VSTest goes
    // through `dotnet test` and never touches this entry point).
    let private forceMtpRunnerArgs = [ "-p:UseMicrosoftTestingPlatformRunner=true" ]

    let private ensureBuilt buildArgs projectPath =
        cli {
            Exec "dotnet"
            Arguments([ "build"; projectPath ] @ buildArgs)
            Output(new StreamWriter(Console.OpenStandardOutput()))
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

    // The captured stdout+stderr of a completed command. Fli returns each stream as
    // an option, so a stream with no output reads back as null through toText/toError.
    let private captureOutput output =
        [ Output.toText output; Output.toError output ]
        |> List.filter (isNull >> not)
        |> String.concat ""

    // Like ensureBuilt but captures the build output instead of streaming it live.
    // Concurrent `run --jobs` workers can't stream to the shared console without
    // interleaving, so a mutant's build and test output are captured and printed as
    // one atomic block by the caller.
    let private ensureBuiltCaptured buildArgs projectPath =
        let output =
            cli {
                Exec "dotnet"
                Arguments([ "build"; projectPath ] @ buildArgs)
            }
            |> Command.execute

        output |> Output.throwIfErrored |> ignore
        captureOutput output

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

    // Human-readable description of what a control run targets, for its header.
    let private describeScope scope =
        match scope with
        | TestMethod fqn -> fqn
        | TestClass fqn -> fqn

    // Builds and runs a single mutant's target test, capturing the combined build and
    // test output. Returns the classified run outcome paired with that output so the
    // caller can print the whole mutation as one block -- required once `run --jobs`
    // runs several mutations concurrently and their output would otherwise interleave.
    let private runTest runnerKind gitRoot segment projectPath scope =
        let artifactsArgs = mutatedBuildArgs gitRoot segment

        let buildArgs =
            match runnerKind with
            | VSTest -> artifactsArgs
            | MtpXunitV3 -> forceMtpRunnerArgs @ artifactsArgs

        let buildOutput = ensureBuiltCaptured buildArgs projectPath

        let testOutput =
            match runnerKind with
            | VSTest ->
                cli {
                    Exec "dotnet"

                    Arguments(
                        [ "test"; projectPath; "--no-build"; "--filter"; vsTestFilter scope ]
                        @ artifactsArgs
                    )
                }
                |> Command.execute
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
                        @ artifactsArgs
                        @ [ "--" ]
                        @ mtpFilterArgs scope
                    )
                }
                |> Command.execute

        classifyOutcome runnerKind (Output.toExitCode testOutput), buildOutput + captureOutput testOutput

    // Runs one target test against the original, unmutated build and returns its
    // exit code. Mutation testing is only meaningful from a green baseline: because
    // mutannot recognizes a killed mutant by its failing run, a target that doesn't
    // already pass -- a broken build, a misdetected runner, an environment problem
    // -- would make its mutant look spuriously killed. The caller runs these up
    // front; run already built the project, so the runs use --no-build (for
    // MtpXunitV3 the caller has also already pinned the MTP runner entry point, see
    // ensureMtpRunnerBuilt).
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
            cli {
                Exec "dotnet"
                Arguments([ "run"; "--project"; projectPath; "--no-build"; "--" ] @ mtpFilterArgs scope)
                Output(new StreamWriter(Console.OpenStandardOutput()))
            }
            |> Command.execute
            |> Output.toExitCode

    // run built the project plainly, but the MtpXunitV3 control runs launch
    // it with `dotnet run --no-build` and filter it through the MTP runner entry
    // point, which only exists when the project is built with UseMicrosoftTestingPlatformRunner.
    // That entry point is a whole-run property, independent of which scope is being
    // tested, so rebuild once here rather than once per control run. The property
    // only changes the entry point, so this is a near no-op incremental build.
    // --no-restore because run already restored this same project into the
    // same obj/ and the property doesn't touch the package graph; without it dotnet
    // would re-evaluate restore even though nothing needs recompiling.
    let private ensureMtpRunnerBuilt projectPath =
        ensureBuilt ("--no-restore" :: forceMtpRunnerArgs) projectPath

    // A project runs on Microsoft.Testing.Platform when the SDK reports
    // IsTestingPlatformApplication; that property is contributed by the testing
    // platform's build targets, so it is picked up wherever the configuration lives
    // (the project file, Directory.Build.props, ...). mutannot only supports xunit
    // v3 there (referencesXunitV3 comes from the test assembly, see getMutations)
    // and errors out otherwise.
    let private getRunnerKind projectPath referencesXunitV3 =
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
    let private getMutations projectPath =
        let assemblyPath = getAssemblyPath projectPath

        use metadataLoadContext = getMetadataLoadContext assemblyPath

        let assembly = metadataLoadContext.LoadFromAssemblyPath assemblyPath

        let referencesXunitV3 =
            assembly.GetReferencedAssemblies()
            |> Seq.exists (fun a ->
                not (isNull a.Name)
                && a.Name.StartsWith("xunit.v3", StringComparison.OrdinalIgnoreCase))

        let mutations =
            assembly.GetTypes()
            |> Seq.collect (fun t ->
                seq {
                    yield! getTypeMutations t

                    yield!
                        // Static as well as instance methods: an F# test authored as a
                        // module-level `let` (rather than a member of a type) compiles to a
                        // static method, and its ShouldCatch would otherwise go unseen.
                        t.GetMethods(
                            BindingFlags.Public
                            ||| BindingFlags.Instance
                            ||| BindingFlags.Static
                            ||| BindingFlags.DeclaredOnly
                        )
                        |> Seq.collect getMethodMutations
                })
            |> Seq.toList

        mutations, referencesXunitV3

    // Runs the mutations found in the test project. Returns the process exit code.
    // `jobs` is the number of mutations to run concurrently; each concurrent worker
    // owns a .mutannot segment so their mutated sources and builds never collide.
    let internal run projectPath validateOnly (maybeFilter: string option) (jobs: int) =
        ensureBuilt [] projectPath

        let mutations, referencesXunitV3 = getMutations projectPath

        // Where the mutated build redirects its output (see mutatedBuildArgs); resolved
        // once from the target project's own repo.
        let gitRoot = Git.root (Path.GetDirectoryName projectPath)

        // Detecting the runner needs the testing platform's build targets, which are
        // only imported once the project has been restored (done by ensureBuilt
        // above). It is also irrelevant when only validating, so defer it.
        let runnerKind = lazy getRunnerKind projectPath referencesXunitV3

        let filteredMutations =
            mutations |> List.filter _.Patch.Contains(maybeFilter |> Option.defaultValue "")

        // The MtpXunitV3 control runs use `dotnet run --no-build`, so the original
        // must first be built with the MTP runner entry point. Do it once, up front,
        // rather than inside each control run (see ensureMtpRunnerBuilt). Skipped when
        // only validating, since no control runs happen then.
        if not validateOnly && runnerKind.Value = MtpXunitV3 then
            ensureMtpRunnerBuilt projectPath

        // Establish a green baseline before mutating anything (see runControl): run
        // every target test unmutated, up front, and refuse to proceed if any fails
        // -- otherwise its mutant's failing run couldn't be trusted as a kill.
        // Skipped when only validating, since no tests are run then.
        let baselineFailed =
            not validateOnly
            && filteredMutations
               |> List.map _.TestScope
               |> List.distinct
               |> List.indexed
               |> List.exists (fun (index, scope) ->
                   Console.ForegroundColor <- ConsoleColor.Green
                   printf $"CONTROL {index + 1}\n"

                   Console.ForegroundColor <- ConsoleColor.Magenta
                   printf "Test:\n"
                   Console.ResetColor()
                   printf "%s\n\n" (describeScope scope)

                   Console.ForegroundColor <- ConsoleColor.Magenta
                   printf "Output:\n"
                   Console.ResetColor()

                   runControl runnerKind.Value projectPath scope <> 0)

        if baselineFailed then
            Console.ForegroundColor <- ConsoleColor.Red
            eprintf "ERROR: Tests must pass on the unmutated project before mutations can be run\n"
            Console.ResetColor()
            4
        else
            // Serializes the formatted per-mutation block: with several jobs the
            // apply/build/test work overlaps, but each mutation's output (captured up
            // front by runTest) is printed as one atomic, uninterleaved unit.
            let printLock = obj ()

            // Applies and runs one mutation, then prints its block. Returns whether the
            // mutant was killed (or, when validating, whether its patch applied at all).
            let runOne segment (index, mutationCase) =
                let mutatedTestProjectPath =
                    Mutator.applyMutation projectPath segment mutationCase.Patch

                let result =
                    if validateOnly then
                        None
                    else
                        Some(runTest runnerKind.Value gitRoot segment mutatedTestProjectPath mutationCase.TestScope)

                lock printLock (fun () ->
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

                    match result with
                    | None -> true
                    | Some(outcome, output) ->
                        Console.ForegroundColor <- ConsoleColor.Magenta
                        printf "Output:\n"
                        Console.ResetColor()
                        printf "%s\n" output

                        match outcome with
                        | Killed ->
                            Console.ForegroundColor <- ConsoleColor.Green
                            printf "✓ Mutant killed\n\n"
                            true
                        | Survived ->
                            Console.ForegroundColor <- ConsoleColor.Red
                            eprintf "ERROR: Expected tests to fail, but they succeeded\n"
                            Console.ResetColor()
                            false
                        | Errored exitCode ->
                            Console.ForegroundColor <- ConsoleColor.Red

                            eprintf
                                "ERROR: Test run errored with exit code %d rather than reporting a test failure; cannot confirm the mutant was killed\n"
                                exitCode

                            Console.ResetColor()
                            false)

            let indexedMutations = List.indexed filteredMutations

            // Spread the mutations round-robin over `jobs` workers, each pinned to its
            // own segment (1-based), and run the workers concurrently. Distinct segments
            // keep their .mutannot subtrees apart; within a worker the mutations run
            // sequentially, reusing that segment's build output.
            let allKilled =
                [ for slot in 0 .. jobs - 1 ->
                      async {
                          return
                              indexedMutations
                              |> List.filter (fun (index, _) -> index % jobs = slot)
                              |> List.map (runOne (slot + 1))
                              |> List.forall id
                      } ]
                |> Async.Parallel
                |> Async.RunSynchronously
                |> Array.forall id

            if not allKilled then
                3
            else
                Console.ForegroundColor <- ConsoleColor.Green

                if validateOnly then
                    printf "Success: All mutations valid\n"
                else
                    printf "Success: All mutants killed\n"

                Console.ResetColor()

                0
