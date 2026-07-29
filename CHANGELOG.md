# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Fix a mutant being reported as killed when its test run exits non-zero for a reason other than a test failure (a crash, a filter matching zero tests, an infrastructure error); a kill is now recognized only by the runner's specific test-failure exit code (https://github.com/svenvanheugten/mutannot/pull/27)
- Fix `validate` reporting "No ShouldCatch attributes found" instead of surfacing the underlying error when run on a directory outside a git repository (https://github.com/svenvanheugten/mutannot/pull/19)
- Fix every control run triggering an unnecessary rebuild when using the Microsoft Testing Platform runner (https://github.com/svenvanheugten/mutannot/pull/10)

### Added

- Add a `--jobs` option to `run` to run mutations in parallel (https://github.com/svenvanheugten/mutannot/pull/9)

## [0.7.1] - 2026-06-26

### Fixed

- Fix `ShouldCatch` blocks in C# files not working on Windows (https://github.com/svenvanheugten/mutannot/pull/1)
