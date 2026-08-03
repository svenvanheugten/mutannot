namespace Mutannot

open Fli

module Vcs =
    type private Backend =
        | Git
        | Jj

    // True when running `exec args` in `directory` exits 0. Any failure counts as
    // "not this kind of repository": a non-zero exit (the directory is not owned by
    // this VCS), or the executable not existing at all -- jj in particular is often
    // not installed, and shelling out to a missing binary throws rather than exiting.
    let private succeeds (exec: string) (args: string list) (directory: string) =
        try
            (cli {
                Exec exec
                Arguments args
                WorkingDirectory directory
             }
             |> Command.execute
             |> Output.toExitCode) = 0
        with _ ->
            false

    // Pick the backend that owns `directory`. Git is tried first: it covers both a
    // plain git repository and a jj repository co-located with git (which carries a
    // .git that `git rev-parse` finds), so jj is only consulted for the remaining
    // case -- a jj repository that is not co-located. When neither matches, fall back
    // to Git so its "not a git repository" error surfaces downstream unchanged,
    // rather than inventing a new failure mode here.
    let private backend (directory: string) =
        if succeeds "git" [ "rev-parse"; "--show-toplevel" ] directory then
            Git
        elif succeeds "jj" [ "root" ] directory then
            Jj
        else
            Git

    let root (directory: string) =
        match backend directory with
        | Git -> Git.root directory
        | Jj -> Jj.root directory

    let sourceFiles (directory: string) =
        match backend directory with
        | Git -> Git.sourceFiles directory
        | Jj -> Jj.sourceFiles directory

    let changedSourceFiles (root: string) (baseBranch: string) =
        match backend root with
        | Git -> Git.changedSourceFiles root baseBranch
        | Jj -> Jj.changedSourceFiles root baseBranch

    let showAtBase (root: string) (baseBranch: string) (relativePath: string) =
        match backend root with
        | Git -> Git.showAtBase root baseBranch relativePath
        | Jj -> Jj.showAtBase root baseBranch relativePath

    // Applying a patch is backend-independent -- `git apply` needs no repository -- so
    // this is a thin wrapper over Git.apply that keeps patch application on the same
    // Vcs surface as the lookups above.
    let apply (gitRoot: string) (extraArgs: string list) (patch: string) = Git.apply gitRoot extraArgs patch
