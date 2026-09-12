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
        var isChanged = !result.AsSpan().SequenceEqual(source);

        if (Check)
        {
            if (isChanged)
            {
                await Write(console, $"not deterministic: {Relative(job.Source)}");
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

            await File.WriteAllBytesAsync(job.Target, result, cancel);
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

        return isChanged;
    }

    static async Task<byte[]> Determinize(byte[] source, string path, Cancel cancel)
    {
        // No async overload is worth taking here: the bytes are already read, and what is left is
        // synchronous work over the buffer.
        if (FormatDetector.Detect(path, source) == Format.Pdf)
        {
            return PdfNormalizer.Normalize(source);
        }

        // Read from a copy rather than the file: an in place run overwrites the file the conversion
        // read from.
        using var sourceStream = new MemoryStream(source, writable: false);
        using var targetStream = await DeterministicPackage.ConvertAsync(sourceStream, cancel);
        return targetStream.ToArray();
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
