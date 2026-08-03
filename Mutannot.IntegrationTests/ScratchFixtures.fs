// Scratch-directory fixtures: locating mutannot's own tree and standing up unique,
// self-cleaning working directories under a chosen VCS (git, a non-co-located jj, or
// none) for a test to run against. These are the filesystem/VCS lifecycle half of the
// harness; the project-file and graph builders that fill a scratch live in TestSupport.
module Mutannot.IntegrationTests.ScratchFixtures

open System
open System.IO
open Fli

// Mutannot's own working tree. Scratch fixtures are created under it so they inherit
// the same NuGet configuration the real projects restore with, but each is its own
// git repository (see withScratch) rather than part of this one.
//
// Located by walking up from the test binary until `mutannot.slnx` is found, rather
// than asking a VCS for the root. That keeps the harness working whether mutannot
// itself is checked out under git, jj (co-located or not), or a plain tarball — the
// VCS backends are exercised deliberately by withScratch/withJjScratch, not here.
let repoRoot =
    let marker = "mutannot.slnx"

    let rec walkUp (dir: DirectoryInfo) =
        if isNull dir then
            failwithf "Could not locate mutannot repo root (%s not found above %s)" marker AppContext.BaseDirectory
        elif File.Exists(Path.Combine(dir.FullName, marker)) then
            dir.FullName
        else
            walkUp dir.Parent

    walkUp (DirectoryInfo AppContext.BaseDirectory)

// Recursively deletes a directory, clearing the read-only attribute on every entry
// first. `Directory.Delete(_, true)` refuses to remove read-only files, and on
// Windows git marks its loose objects under .git/objects read-only -- so any scratch
// that has been committed into would otherwise fail cleanup with
// UnauthorizedAccessException. Clearing the bit up front makes the delete succeed on
// every platform (it's a no-op where nothing is read-only).
let rec private forceDelete (dir: string) =
    if Directory.Exists dir then
        for file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) do
            File.SetAttributes(file, FileAttributes.Normal)

        Directory.Delete(dir, true)

// Runs `body scratchAbs` against a unique, self-cleaning scratch directory that is
// its own git repository. Each scratch is `git init`ed so the mutator resolves its
// git root. That keeps every test's output out of mutannot's own tree and isolated
// from the other tests, tests can run in parallel.
let withScratch (body: string -> unit) =
    let scratch = Path.Combine(repoRoot, ".inttest-" + Guid.NewGuid().ToString("N"))

    try
        Directory.CreateDirectory scratch |> ignore

        // Make the scratch behave like a real consumer's repo: ignore build output
        // and mutannot's generated files so `validate`'s `git ls-files` scan doesn't
        // pick up generated sources.
        File.WriteAllText(
            Path.Combine(scratch, ".gitignore"),
            "[Bb]in/\n[Oo]bj/\n.mutannot/\n*.mutated.csproj\n*.mutated.fsproj\n"
        )

        cli {
            Exec "git"
            Arguments [ "init" ]
            WorkingDirectory scratch
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

        body scratch
    finally
        forceDelete scratch

// Runs `body scratchAbs` against a unique, self-cleaning scratch directory that is a
// jj repository *not* co-located with git. It sits under the system temp path rather
// than mutannot's own tree so that no ancestor git repository is in scope: `git
// rev-parse` genuinely fails there, which is what makes the jj backend the one that
// gets exercised. `--config git.colocate=false` keeps jj from writing a .git that
// `git rev-parse` would otherwise find. The .gitignore is honoured by jj too, so
// build output and mutannot's generated files stay out of the source scan.
let withJjScratch (body: string -> unit) =
    let scratch =
        Path.Combine(Path.GetTempPath(), "mutannot-jj-" + Guid.NewGuid().ToString("N"))

    try
        Directory.CreateDirectory scratch |> ignore

        File.WriteAllText(
            Path.Combine(scratch, ".gitignore"),
            "[Bb]in/\n[Oo]bj/\n.mutannot/\n*.mutated.csproj\n*.mutated.fsproj\n"
        )

        cli {
            Exec "jj"
            Arguments [ "--config"; "git.colocate=false"; "git"; "init" ]
            WorkingDirectory scratch
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

        body scratch
    finally
        forceDelete scratch

// Like withScratch but the scratch sits under the system temp path with no VCS
// initialized, so `git rev-parse` genuinely fails there. Used to exercise the
// outside-any-repository error paths, where the whole point is that no repository is
// in scope.
let withTempScratch (body: string -> unit) =
    let scratch =
        Path.Combine(Path.GetTempPath(), "mutannot-notvcs-" + Guid.NewGuid().ToString("N"))

    try
        Directory.CreateDirectory scratch |> ignore
        body scratch
    finally
        forceDelete scratch

// Dispatches to withScratch or withJjScratch by backend, so a single [<Theory>] can
// exercise the same behaviour under git and under a non-co-located jj repo. `jj =
// false` selects the git fixture, `jj = true` the jj one; pass it straight through
// from the theory's InlineData.
let withScratchFor (jj: bool) (body: string -> unit) =
    if jj then withJjScratch body else withScratch body
