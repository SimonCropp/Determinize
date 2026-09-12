public class FormatDetectorTests
{
    [Test]
    [Arguments("report.pdf")]
    [Arguments("REPORT.PDF")]
    public async Task PdfByExtension(string name) =>
        await Assert.That(FormatDetector.Detect(name, [])).IsEqualTo(Format.Pdf);

    [Test]
    [Arguments("tool.nupkg")]
    [Arguments("tool.snupkg")]
    [Arguments("extension.vsix")]
    [Arguments("report.docx")]
    [Arguments("report.docm")]
    [Arguments("report.dotx")]
    [Arguments("sheet.xlsx")]
    [Arguments("sheet.xlsm")]
    [Arguments("sheet.xltx")]
    [Arguments("deck.pptx")]
    [Arguments("deck.pptm")]
    [Arguments("deck.potx")]
    [Arguments("TOOL.NUPKG")]
    public async Task PackageByExtension(string name) =>
        await Assert.That(FormatDetector.Detect(name, [])).IsEqualTo(Format.Package);

    // The extension list only covers what the default patterns enumerate. A file named directly on
    // the command line can be called anything, so an unrecognised extension reads the signature.
    [Test]
    public async Task UnknownExtensionFallsBackToThePdfSignature() =>
        await Assert.That(FormatDetector.Detect("odd.bin", "%PDF-1.7\n"u8.ToArray()))
            .IsEqualTo(Format.Pdf);

    [Test]
    public async Task UnknownExtensionFallsBackToTheZipSignature() =>
        await Assert.That(FormatDetector.Detect("odd.bin", [0x50, 0x4B, 0x03, 0x04]))
            .IsEqualTo(Format.Package);

    [Test]
    public async Task UnknownExtensionAndUnknownContentIsRejected() =>
        await Assert.That(() => FormatDetector.Detect("notes.txt", "hello"u8.ToArray()))
            .Throws<CommandException>();

    [Test]
    public async Task EmptyContentIsRejected() =>
        await Assert.That(() => FormatDetector.Detect("empty.bin", []))
            .Throws<CommandException>();

    // An extension added to the detector but not reachable from the default patterns would only
    // ever be found by a hand written -p, which is the bug this pins.
    [Test]
    public async Task EveryPackageExtensionHasADefaultPattern()
    {
        var patterns = new DeterminizeCommand { Input = "." }.Patterns;

        await Assert.That(patterns).Contains("*.pdf");

        foreach (var extension in FormatDetector.PackageExtensions)
        {
            await Assert.That(patterns).Contains($"*{extension}");
        }

        await Assert.That(patterns.Length).IsEqualTo(FormatDetector.PackageExtensions.Length + 1);
    }
}
