namespace Mutannot

module Vcs =
    // A thin indirection in front of the version-control operations mutannot needs --
    // the working-tree root (to anchor patch paths and locate build output), the
    // source-file scan, and patch application. Routing them through one module means a
    // second backend can later be slotted in behind a single seam; for now each just
    // delegates to Git.
    let root (directory: string) = Git.root directory

    let sourceFiles (directory: string) = Git.sourceFiles directory

    let apply (gitRoot: string) (extraArgs: string list) (patch: string) = Git.apply gitRoot extraArgs patch
