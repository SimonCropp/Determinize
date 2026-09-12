# <img src="/src/icon.png" height="30px"> Determinize

[![Build status](https://github.com/SimonCropp/Determinize/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/SimonCropp/Determinize/actions/workflows/ci.yml)
[![NuGet Status](https://img.shields.io/nuget/v/Determinize.svg?label=Determinize)](https://www.nuget.org/packages/Determinize/)

A [dotnet tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) that makes files deterministic. Rewrites PDFs and System.IO.Packaging containers (nupkg, docx, xlsx, pptx) so the same source always produces byte-identical output. Helpful for testing, build reproducibility, security verification, and ensuring output integrity across different build environments.

It is a command line front end over two libraries, and does no normalizing of its own:

 * [DeterministicPdf](https://github.com/SimonCropp/DeterministicPdf) for PDFs.
 * [DeterministicIoPackaging](https://github.com/SimonCropp/DeterministicIoPackaging) for [System.IO.Packaging](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging) containers.

**See [Milestones](../../milestones?state=closed) for release notes.**


## NuGet

 * https://nuget.org/packages/Determinize


## Installation

```
dotnet tool install -g Determinize
```


## Usage

```
determinize <path> [options]
```

`path` is a file, or a directory containing files. It is rewritten in place unless `--target` is used.

 * `-t|--target` Write results here instead of modifying the input in place. An output file path when the input is a file, otherwise a directory mirroring the input tree.
 * `-p|--pattern` Search patterns applied when the input is a directory. Repeat the option for multiple patterns. Defaults to every extension listed below.
 * `-r|--recursive` Recurse into subdirectories when the input is a directory.
 * `--check` Report which files are not already deterministic without writing anything. Exits with code 1 if any are found.
 * `--continue-on-error` Keep processing the remaining files after a failure, then exit with code 1.
 * `-q|--quiet` Suppress per file and summary output. Errors are still written.
 * `-v|--verbose` List what changed in each file, indented under it.

A file that is already deterministic is left untouched, so an in place run does not disturb its timestamp.


### Seeing what changed

`--verbose` lists what was changed in each file, which for `--check` is the answer to why a file is not reproducible:

```
determinize ./artifacts -r -v

normalized: artifacts/report.pdf
  /CreationDate, /ModDate, /ID x2, xmp:CreateDate, xmp:ModifyDate
  xmp:MetadataDate, xmpMM:DocumentID, XMP packet whitespace
normalized: artifacts/tool.nupkg
  reordered, removed .signature.p7s, patched [Content_Types].xml
  patched _rels/.rels, patched icon.png
2 files processed, 2 normalized.
```

PDFs name the field that was neutralized; packages name the entry that was removed or patched. Both come from the underlying libraries rather than being inferred here.

Only real differences are listed. Both libraries apply normalizations to every input alike — entry timestamps, compression, XML formatting — and report none of them, because they are the same for every file and so never the reason one file differs from another. A file whose only difference is one of those is reported as changed with that noted in place of a detail list.


## Supported files

Which library handles a file is decided by its extension, so one run covers a mixed directory:

| Extension | Handled by |
| --- | --- |
| `.pdf` | DeterministicPdf |
| `.nupkg` `.snupkg` `.vsix` | DeterministicIoPackaging |
| `.docx` `.docm` `.dotx` | DeterministicIoPackaging |
| `.xlsx` `.xlsm` `.xltx` | DeterministicIoPackaging |
| `.pptx` `.pptm` `.potx` | DeterministicIoPackaging |

A file named directly on the command line can carry any extension. An unrecognised one is identified
by its signature instead, so `determinize document.bin` works if the content is a PDF or a zip.

Plain `.zip` files are handled, but are deliberately left out of the default patterns so that a
recursive run does not rewrite every archive it finds. Reach them with `-p "*.zip"`.


## Examples

Determinize one file in place:

```
determinize document.pdf
```

Determinize a tree into a separate output directory:

```
determinize ./input -r --target ./output
```

Fail a build when any artifact is not deterministic:

```
determinize ./artifacts -r --check
```

Only the packages in a directory, leaving everything else alone:

```
determinize ./artifacts -p "*.nupkg" -p "*.snupkg"
```


## Icon

https://thenounproject.com/icon/pattern-8435895/