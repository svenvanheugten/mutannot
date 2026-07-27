# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Fix `validate` reporting "No ShouldCatch attributes found" instead of surfacing the underlying error when run on a directory outside a git repository (https://github.com/svenvanheugten/mutannot/issues/11)

### Added

- Add a `--jobs` option to `run` to run mutations in parallel.

## [0.7.1] - 2026-06-26

### Fixed

- Fix `ShouldCatch` blocks in C# files not working on Windows (https://github.com/svenvanheugten/mutannot/pull/1)
