[Command(
    Description = "Rewrites a PDF or System.IO.Packaging file so the same source always produces byte-identical output.")]
public partial class DeterminizeCommand : ICommand
{
    // PDF plus every packaging extension, so one run over a directory of build artifacts covers the
    // lot. Built from the list the dispatch reads, so a format cannot be added to one and missed by
    // the other.
    static string[] defaultPatterns =
    [
        "*.pdf",
        ..FormatDetector.PackageExtensions.Select(_ => $"*{_}")
    ];

    [CommandParameter(
        0,
        Name = "path",
        Description = "File, or directory containing files, to determinize. Rewritten in place unless --target is used.")]
    public required string Input { get; set; }

    [CommandOption(
        "target",
        't',
        Description = "Write results here instead of modifying the input in place. An output file path when the input is a file, otherwise a directory mirroring the input tree.")]
    public string? Target { get; set; }

    [CommandOption(
        "pattern",
        'p',
        Description = "Search patterns applied when the input is a directory. Defaults to every known PDF and package extension.")]
    public string[] Patterns { get; set; } = defaultPatterns;

    [CommandOption(
        "recursive",
        'r',
        Description = "Recurse into subdirectories when the input is a directory.")]
    public bool Recursive { get; set; }

    [CommandOption(
        "check",
        Description = "Report which files are not already deterministic without writing anything. Exits with code 1 if any are found.")]
    public bool Check { get; set; }

    [CommandOption(
        "continue-on-error",
        Description = "Keep processing the remaining files after a failure, then exit with code 1.")]
    public bool ContinueOnError { get; set; }

    [CommandOption(
        "quiet",
        'q',
        Description = "Suppress per file and summary output. Errors are still written.")]
    public bool Quiet { get; set; }

    [CommandOption(
        "verbose",
        'v',
        Description = "List what changed in each file, indented under it.")]
    public bool Verbose { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (Check &&
            Target != null)
        {
            throw new CommandException("--check does not write anything, so it cannot be combined with --target.");
        }

        if (Patterns.Length == 0)
        {
            throw new CommandException("--pattern requires at least one value.");
        }

        var jobs = FileResolver.Resolve(Input, Target, Patterns, Recursive);
        if (jobs.Count == 0)
        {
            throw new CommandException($"No files matching {string.Join(", ", Patterns)} found in: {Input}");
        }

        var cancel = console.RegisterCancellationHandler();
        var changed = 0;
        var failed = 0;

        foreach (var job in jobs)
        {
            try
            {
                if (await Handle(console, job, cancel))
                {
                    changed++;
                }
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                if (!ContinueOnError)
                {
                    throw new CommandException($"{Relative(job.Source)}: {exception.Message}", innerException: exception);
                }

                failed++;
                await console.Error.WriteLineAsync($"failed: {Relative(job.Source)}: {exception.Message}");
            }
        }

        await WriteSummary(console, jobs.Count, changed, failed);
    }

    // Returns whether determinizing altered the file.
    async Task<bool> Handle(IConsole console, FileJob job, Cancel cancel)
    {
        var source = await File.ReadAllBytesAsync(job.Source, cancel);
        var result = await Determinize(source, job.Source, cancel);
        var isChanged = !result.Data.AsSpan().SequenceEqual(source);

        if (Check)
        {
            if (isChanged)
            {
                await Write(console, $"not deterministic: {Relative(job.Source)}");
                await WriteDetail(console, result, isChanged);
            }

            return isChanged;
        }

        // An unchanged file is left alone on an in place run rather than rewritten with the same
        // bytes, so its timestamp is not disturbed. A separate target always has to be written.
        if (isChanged ||
            !job.IsInPlace)
        {
            var directory = Path.GetDirectoryName(job.Target);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(job.Target, result.Data, cancel);
        }

        var status = isChanged ? "normalized" : "unchanged";
        if (job.IsInPlace)
        {
            await Write(console, $"{status}: {Relative(job.Source)}");
        }
        else
        {
            await Write(console, $"{status}: {Relative(job.Source)} -> {Relative(job.Target)}");
        }

        await WriteDetail(console, result, isChanged);

        return isChanged;
    }

    static async Task<DeterminizeResult> Determinize(byte[] source, string path, Cancel cancel)
    {
        var format = FormatDetector.Detect(path, source);

        if (format == Format.Pdf)
        {
            // No async overload is worth taking here: the bytes are already read, and what is left
            // is synchronous work over the buffer.
            var normalized = PdfNormalizer.Normalize(source, out var changes);
            return new(format, normalized, changes.Select(Describe).ToList());
        }

        // Read from a copy rather than the file: an in place run overwrites the file the conversion
        // read from.
        using var sourceStream = new MemoryStream(source, writable: false);
        using var converted = await DeterministicPackage.ConvertWithChangesAsync(sourceStream, cancel);
        return new(format, converted.Stream.ToArray(), converted.Changes.Select(Describe).ToList());
    }

    static string Describe(NormalizeChange change)
    {
        if (change.Count == 1)
        {
            return change.Name;
        }

        return $"{change.Name} x{change.Count}";
    }

    static string Describe(ConvertChange change)
    {
        var kind = change.Kind.ToString().ToLowerInvariant();
        if (change.Entry == null)
        {
            return kind;
        }

        return $"{kind} {change.Entry}";
    }

    async Task WriteDetail(IConsole console, DeterminizeResult result, bool isChanged)
    {
        if (!Verbose ||
            Quiet ||
            !isChanged)
        {
            return;
        }

        foreach (var line in Wrap(Detail(result)))
        {
            await console.Output.WriteLineAsync($"  {line}");
        }
    }

    // Both libraries report only what actually differed, and deliberately say nothing about the
    // normalizations they apply to every input alike. So a file can change with nothing to report -
    // a package whose entries were merely restamped and recompressed - and saying so beats printing
    // a header with nothing under it.
    static IReadOnlyList<string> Detail(DeterminizeResult result)
    {
        if (result.Changes.Count > 0)
        {
            return result.Changes;
        }

        if (result.Format == Format.Package)
        {
            return ["entry timestamps, compression and formatting"];
        }

        return ["bytes outside any reported field"];
    }

    // Comma separated and wrapped, so a PDF's handful of short field names share a line while a
    // package's long entry paths each get one of their own.
    static IEnumerable<string> Wrap(IReadOnlyList<string> values)
    {
        const int width = 76;
        var line = new StringBuilder();
        foreach (var value in values)
        {
            if (line.Length > 0 &&
                line.Length + 2 + value.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(", ");
            }

            line.Append(value);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    async Task WriteSummary(IConsole console, int total, int changed, int failed)
    {
        if (Check)
        {
            if (changed > 0 ||
                failed > 0)
            {
                throw new CommandException($"{Count(total)} checked, {changed} not deterministic{(failed > 0 ? $", {failed} failed" : null)}.");
            }

            await Write(console, $"{Count(total)} checked, all deterministic.");
            return;
        }

        await Write(console, $"{Count(total)} processed, {changed} normalized.");

        if (failed > 0)
        {
            throw new CommandException($"{Count(failed)} failed.");
        }
    }

    Task Write(IConsole console, string message)
    {
        if (Quiet)
        {
            return Task.CompletedTask;
        }

        return console.Output.WriteLineAsync(message);
    }

    static string Count(int value) => value == 1 ? "1 file" : $"{value} files";

    static string Relative(string path) => Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
}
