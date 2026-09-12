public class DeterminizeCommandTests
{
    [Test]
    public async Task OneRunCoversBothBackends()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);
        temp.Add(Samples.Nupkg);
        temp.Add(Samples.Docx);

        var console = new FakeInMemoryConsole();
        await Run(temp, console);

        var output = console.ReadOutputString();
        await Assert.That(output).Contains("normalized: ");
        await Assert.That(output).Contains("sample.pdf");
        await Assert.That(output).Contains("sample.nupkg");
        await Assert.That(output).Contains("sample.docx");
        await Assert.That(output).Contains("3 files processed, 3 normalized.");
    }

    // The property --check depends on. If normalizing were not idempotent, --check would report
    // every file as not deterministic forever and the option would be useless.
    [Test]
    public async Task ASecondRunChangesNothing()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);
        temp.Add(Samples.Nupkg);
        temp.Add(Samples.Docx);

        await Run(temp, new());

        var console = new FakeInMemoryConsole();
        await Run(temp, console);

        var output = console.ReadOutputString();
        await Assert.That(output).DoesNotContain("normalized: ");
        await Assert.That(output).Contains("3 files processed, 0 normalized.");
    }

    [Test]
    public async Task AnUnchangedFileKeepsItsTimestamp()
    {
        using var temp = new TempDirectory();
        var file = temp.Add(Samples.Pdf);

        await Run(temp, new());

        var written = File.GetLastWriteTimeUtc(file);
        await Run(temp, new());

        await Assert.That(File.GetLastWriteTimeUtc(file)).IsEqualTo(written);
    }

    [Test]
    public async Task CheckFailsOnInputThatIsNotYetDeterministic()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);
        var before = File.ReadAllBytes(temp.Combine("sample.pdf"));

        var console = new FakeInMemoryConsole();
        var exception = await Assert.That(() => Run(temp, console, command => command.Check = true))
            .Throws<CommandException>();

        await Assert.That(exception!.ExitCode).IsEqualTo(1);
        await Assert.That(console.ReadOutputString()).Contains("not deterministic: ");

        // --check writes nothing.
        await Assert.That(File.ReadAllBytes(temp.Combine("sample.pdf"))).IsEquivalentTo(before);
    }

    [Test]
    public async Task CheckPassesOnceNormalized()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);
        temp.Add(Samples.Nupkg);

        await Run(temp, new());

        var console = new FakeInMemoryConsole();
        await Run(temp, console, command => command.Check = true);

        await Assert.That(console.ReadOutputString()).Contains("2 files checked, all deterministic.");
    }

    [Test]
    public async Task CheckCannotBeCombinedWithTarget()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        await Assert.That(() => Run(
                    temp,
                    new(),
                    command =>
                    {
                        command.Check = true;
                        command.Target = temp.Combine("output");
                    }))
            .Throws<CommandException>();
    }

    [Test]
    public async Task ATargetRunLeavesTheInputAlone()
    {
        using var temp = new TempDirectory();
        var source = temp.Add(Samples.Pdf, Path.Combine("input", "sample.pdf"));
        var before = File.ReadAllBytes(source);
        var target = temp.Combine("output");

        var console = new FakeInMemoryConsole();
        var command = new DeterminizeCommand
        {
            Input = temp.Combine("input"),
            Target = target
        };
        await command.ExecuteAsync(console);

        await Assert.That(File.ReadAllBytes(source)).IsEquivalentTo(before);
        await Assert.That(File.Exists(Path.Combine(target, "sample.pdf"))).IsTrue();
        await Assert.That(console.ReadOutputString()).Contains(" -> ");
    }

    [Test]
    public async Task AnUnsupportedFileIsReported()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine("notes.txt");
        await File.WriteAllTextAsync(file, "hello");

        var exception = await Assert.That(
                async () => await new DeterminizeCommand { Input = file }.ExecuteAsync(new FakeInMemoryConsole()))
            .Throws<CommandException>();

        await Assert.That(exception!.Message).Contains("Not a PDF or a System.IO.Packaging file.");
    }

    [Test]
    public async Task AMissingPathIsReported()
    {
        using var temp = new TempDirectory();

        await Assert.That(
                async () => await new DeterminizeCommand { Input = temp.Combine("nope.pdf") }.ExecuteAsync(new FakeInMemoryConsole()))
            .Throws<CommandException>();
    }

    [Test]
    public async Task ADirectoryWithNothingMatchingIsReported()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.Combine("notes.txt"), "hello");

        await Assert.That(() => Run(temp, new()))
            .Throws<CommandException>();
    }

    [Test]
    public async Task QuietSuppressesOutput()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var console = new FakeInMemoryConsole();
        await Run(temp, console, command => command.Quiet = true);

        await Assert.That(console.ReadOutputString()).IsEmpty();
    }

    // A single file is the one path that reaches the signature fallback, since a directory run only
    // ever enumerates files that already matched a pattern.
    [Test]
    public async Task AnUnrecognisedExtensionIsDetectedByItsSignature()
    {
        using var temp = new TempDirectory();
        var file = temp.Add(Samples.Pdf, "document.bin");

        var console = new FakeInMemoryConsole();
        await new DeterminizeCommand { Input = file }.ExecuteAsync(console);

        await Assert.That(console.ReadOutputString()).Contains("normalized: ");
    }

    [Test]
    public async Task VerboseNamesTheFieldsChangedInAPdf()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var console = new FakeInMemoryConsole();
        await Run(temp, console, command => command.Verbose = true);

        var output = console.ReadOutputString();
        await Assert.That(output).Contains("/CreationDate");
        await Assert.That(output).Contains("xmp:CreateDate");
    }

    [Test]
    public async Task VerboseNamesTheEntriesChangedInAPackage()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Nupkg);

        var console = new FakeInMemoryConsole();
        await Run(temp, console, command => command.Verbose = true);

        var output = console.ReadOutputString();
        await Assert.That(output).Contains("removed ");
        await Assert.That(output).Contains("psmdcp");
        await Assert.That(output).Contains("patched _rels/.rels");
    }

    [Test]
    public async Task DetailIsIndentedUnderItsFile()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var console = new FakeInMemoryConsole();
        await Run(temp, console, command => command.Verbose = true);

        var lines = console.ReadOutputString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(_ => _.TrimEnd('\r'))
            .ToList();

        await Assert.That(lines[0]).StartsWith("normalized: ");
        await Assert.That(lines[1]).StartsWith("  ");
    }

    [Test]
    public async Task WithoutVerboseThereIsNoDetail()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var console = new FakeInMemoryConsole();
        await Run(temp, console);

        await Assert.That(console.ReadOutputString()).DoesNotContain("/CreationDate");
    }

    // Nothing changed, so there is nothing to explain. Printing a header with an empty body under it
    // would be worse than printing neither.
    [Test]
    public async Task AnUnchangedFileHasNoDetail()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);
        await Run(temp, new FakeInMemoryConsole());

        var console = new FakeInMemoryConsole();
        await Run(temp, console, command => command.Verbose = true);

        var output = console.ReadOutputString();
        await Assert.That(output).Contains("unchanged: ");
        await Assert.That(output).DoesNotContain("/CreationDate");
    }

    [Test]
    public async Task CheckExplainsWhyAFileIsNotDeterministic()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var console = new FakeInMemoryConsole();
        await Assert.That(
                () => Run(
                    temp,
                    console,
                    command =>
                    {
                        command.Check = true;
                        command.Verbose = true;
                    }))
            .Throws<CommandException>();

        var output = console.ReadOutputString();
        await Assert.That(output).Contains("not deterministic: ");
        await Assert.That(output).Contains("/CreationDate");
    }

    [Test]
    public async Task QuietBeatsVerbose()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var console = new FakeInMemoryConsole();
        await Run(
            temp,
            console,
            command =>
            {
                command.Verbose = true;
                command.Quiet = true;
            });

        await Assert.That(console.ReadOutputString()).IsEmpty();
    }

    static async Task Run(TempDirectory temp, FakeInMemoryConsole console, Action<DeterminizeCommand>? configure = null)
    {
        var command = new DeterminizeCommand
        {
            Input = temp.FullName
        };

        configure?.Invoke(command);

        await command.ExecuteAsync(console);
    }
}
