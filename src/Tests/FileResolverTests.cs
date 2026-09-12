public class FileResolverTests
{
    static string[] pdfPattern = ["*.pdf"];

    [Test]
    public async Task AFileWithNoTargetIsInPlace()
    {
        using var temp = new TempDirectory();
        var file = temp.Add(Samples.Pdf);

        var jobs = FileResolver.Resolve(file, null, pdfPattern, false);

        await Assert.That(jobs.Count).IsEqualTo(1);
        await Assert.That(jobs[0].Source).IsEqualTo(file);
        await Assert.That(jobs[0].Target).IsEqualTo(file);
        await Assert.That(jobs[0].IsInPlace).IsTrue();
    }

    [Test]
    public async Task ATargetThatIsNotADirectoryIsTheOutputFile()
    {
        using var temp = new TempDirectory();
        var file = temp.Add(Samples.Pdf);
        var target = temp.Combine("renamed.pdf");

        var jobs = FileResolver.Resolve(file, target, pdfPattern, false);

        await Assert.That(jobs[0].Target).IsEqualTo(target);
        await Assert.That(jobs[0].IsInPlace).IsFalse();
    }

    [Test]
    public async Task AnExistingDirectoryTargetKeepsTheFileName()
    {
        using var temp = new TempDirectory();
        var file = temp.Add(Samples.Pdf);
        var target = temp.Combine("out");
        Directory.CreateDirectory(target);

        var jobs = FileResolver.Resolve(file, target, pdfPattern, false);

        await Assert.That(jobs[0].Target).IsEqualTo(Path.Combine(target, "sample.pdf"));
    }

    // The directory does not exist yet, so only the trailing separator says it is one.
    [Test]
    public async Task ATrailingSeparatorTargetKeepsTheFileName()
    {
        using var temp = new TempDirectory();
        var file = temp.Add(Samples.Pdf);
        var target = temp.Combine("out") + Path.DirectorySeparatorChar;

        var jobs = FileResolver.Resolve(file, target, pdfPattern, false);

        await Assert.That(jobs[0].Target).IsEqualTo(Path.Combine(temp.Combine("out"), "sample.pdf"));
    }

    [Test]
    public async Task AMissingPathIsRejected()
    {
        using var temp = new TempDirectory();

        await Assert.That(() => FileResolver.Resolve(temp.Combine("nope.pdf"), null, pdfPattern, false))
            .Throws<CommandException>();
    }

    [Test]
    public async Task ADirectoryDoesNotRecurseByDefault()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf, "top.pdf");
        temp.Add(Samples.Pdf, Path.Combine("nested", "deep.pdf"));

        var jobs = FileResolver.Resolve(temp.FullName, null, pdfPattern, false);

        await Assert.That(jobs.Select(_ => Path.GetFileName(_.Source))).IsEquivalentTo(["top.pdf"]);
    }

    [Test]
    public async Task ARecursiveDirectoryFindsNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf, "top.pdf");
        temp.Add(Samples.Pdf, Path.Combine("nested", "deep.pdf"));

        var jobs = FileResolver.Resolve(temp.FullName, null, pdfPattern, true);

        await Assert.That(jobs.Select(_ => Path.GetFileName(_.Source)).Order())
            .IsEquivalentTo(["deep.pdf", "top.pdf"]);
    }

    [Test]
    public async Task ADirectoryTargetMirrorsTheInputTree()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("input");
        Directory.CreateDirectory(input);
        File.Copy(Samples.Pdf, Path.Combine(input, "top.pdf"));
        Directory.CreateDirectory(Path.Combine(input, "nested"));
        File.Copy(Samples.Pdf, Path.Combine(input, "nested", "deep.pdf"));

        var target = temp.Combine("output");
        var jobs = FileResolver.Resolve(input, target, pdfPattern, true);

        await Assert.That(jobs.Select(_ => _.Target).Order())
            .IsEquivalentTo(
            [
                Path.Combine(target, "nested", "deep.pdf"),
                Path.Combine(target, "top.pdf")
            ]);
    }

    // Without the exclusion a recursive run would pick up what it had just written and determinize
    // its own output.
    [Test]
    public async Task ATargetNestedUnderTheInputIsNotItselfProcessed()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf, "top.pdf");
        var target = temp.Combine("output");
        Directory.CreateDirectory(target);
        File.Copy(Samples.Pdf, Path.Combine(target, "already.pdf"));

        var jobs = FileResolver.Resolve(temp.FullName, target, pdfPattern, true);

        await Assert.That(jobs.Select(_ => Path.GetFileName(_.Source))).IsEquivalentTo(["top.pdf"]);
    }

    // Windows keeps legacy 8.3 name matching, so the search pattern alone would also return
    // "book.pdfa". On Linux this passes because the pattern never matched it in the first place.
    [Test]
    public async Task ALongerExtensionIsNotMatchedByAShorterPattern()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf, "book.pdfa");
        temp.Add(Samples.Pdf, "real.pdf");

        var jobs = FileResolver.Resolve(temp.FullName, null, pdfPattern, false);

        await Assert.That(jobs.Select(_ => Path.GetFileName(_.Source))).IsEquivalentTo(["real.pdf"]);
    }

    [Test]
    public async Task OverlappingPatternsYieldEachFileOnce()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Pdf);

        var jobs = FileResolver.Resolve(temp.FullName, null, ["*.pdf", "*.pdf", "*"], false);

        await Assert.That(jobs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ADirectoryWithNoMatchesYieldsNoJobs()
    {
        using var temp = new TempDirectory();
        temp.Add(Samples.Nupkg);

        var jobs = FileResolver.Resolve(temp.FullName, null, pdfPattern, false);

        await Assert.That(jobs).IsEmpty();
    }
}
