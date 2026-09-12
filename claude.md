# Claude Code Reference

## What this is

`determinize`, a CliFx dotnet tool. It does no normalizing of its own — it is a command line front
end over two libraries, consumed as NuGet packages:

| Library | Entry point used | Covers |
| --- | --- | --- |
| [DeterministicPdf](https://github.com/SimonCropp/DeterministicPdf) | `PdfNormalizer.Normalize(byte[])` | `.pdf` |
| [DeterministicIoPackaging](https://github.com/SimonCropp/DeterministicIoPackaging) | `DeterministicPackage.ConvertAsync(Stream, Cancel)` | every System.IO.Packaging container |

Both return a normalized copy rather than writing in place, and both materialize the whole file in a
buffer — neither is a streaming operation, so neither offers a source/target overload. That is why
`Handle` reads the file to a `byte[]` first and compares the result against it to decide whether
anything changed.

## Build and test

```bash
dotnet build src --configuration Release
```

```bash
dotnet test src --configuration Release --no-build --no-restore
```

TUnit on Microsoft.Testing.Platform, which is what the `"test": { "runner": ... }` block in
`global.json` selects. Without it the .NET 10 SDK routes through VSTest and the run fails.

A Release build also packs, into the gitignored `nugets`, because `ProjectDefaults` sets
`GeneratePackageOnBuild` for a package project in Release.

## Backend selection

`FormatDetector.Detect` decides, and the order matters:

1. `.pdf` — the PDF library.
2. Anything in `FormatDetector.PackageExtensions` — the packaging library.
3. Otherwise the signature: `%PDF-` or `PK`.
4. Otherwise `CommandException`.

Extension first rather than content first, because the `--pattern` list is already written in
extensions and a directory run has to sort a mixed tree file by file without opening everything.
The signature fallback is only reachable for a file named directly on the command line or found by a
hand written `-p`, since `FileResolver.ResolveDirectory` only ever enumerates files that already
matched a pattern.

`DeterminizeCommand.defaultPatterns` is **built from** `FormatDetector.PackageExtensions` rather than
written out beside it, so an extension cannot be added to the dispatch and missed by the default
patterns. `FormatDetectorTests.EveryPackageExtensionHasADefaultPattern` pins that.

Plain `.zip` is handled by the packaging library but is deliberately absent from
`PackageExtensions`, so a recursive run does not rewrite every archive it finds. `-p "*.zip"` reaches
it.

## Idempotence is the load bearing property

`--check` is only usable in CI if normalizing twice produces what normalizing once produced.
Both libraries document it, but this tool is the first thing to depend on it across *both* at once,
so `DeterminizeCommandTests.ASecondRunChangesNothing` asserts it over a real PDF, nupkg and docx. If
that test ever fails, `--check` reports the affected format as non-deterministic forever and the
option is dead rather than merely wrong.

## Provenance

`FileResolver`, `FileJob`, `PathComparison` and `Program` are ported near-verbatim from two
abandoned branches that each built a single-library version of this tool:

- `D:\Code\DeterministicIoPackaging`, branch `cli-tool`, commit `91dcce2` ("Add detpackage dotnet tool")
- `D:\Code\DeterministicPdf`, commit `90d55de` ("Add detpdf dotnet tool") — the branch was deleted,
  so the commit is only reachable through that repo's reflog.

Two details in `FileResolver` look arbitrary and are not:

- `MatchesExtension` re-checks the extension because Windows keeps legacy 8.3 name matching, so a
  `*.pdf` search pattern also returns `book.pdfa`.
- `ResolveDirectory` skips files under the target, because a `--target` nested inside the input would
  otherwise feed its own output back in on a recursive run.

## Project structure

- `src/Determinize/` — the tool
  - `DeterminizeCommand.cs` — the single (default) CliFx command
  - `FormatDetector.cs` / `Format.cs` — which library handles a file
  - `FileResolver.cs` — expands the path parameter into source/target pairs
- `src/Tests/` — TUnit, plain assertions, no Verify
  - `samples/` — a real PDF, nupkg and docx. Tests copy them into a `TempDirectory` and never run
    the tool against the checked in copies, which an in place tool would rewrite once and then
    pass against vacuously forever.
