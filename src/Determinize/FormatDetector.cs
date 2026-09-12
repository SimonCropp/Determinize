// Picks the library that handles a file. The extension decides, because that is what the pattern
// list is written in and a directory run has to sort a mixed tree file by file. A file named
// directly on the command line can carry any extension, so an unrecognised one falls back to the
// signature rather than failing outright.
static class FormatDetector
{
    // Every System.IO.Packaging format the packaging library is known to handle: NuGet packages, the
    // Office Open XML documents, and the VSIX container. Plain ".zip" is handled too, but is left
    // out so that neither the default pattern list nor an unqualified run rewrites every archive it
    // finds. Reach it with -p "*.zip".
    public static string[] PackageExtensions { get; } =
    [
        ".nupkg",
        ".snupkg",
        ".vsix",
        ".docx",
        ".docm",
        ".dotx",
        ".xlsx",
        ".xlsm",
        ".xltx",
        ".pptx",
        ".pptm",
        ".potx"
    ];

    public static Format Detect(string path, byte[] content)
    {
        var extension = Path.GetExtension(path);

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Format.Pdf;
        }

        if (PackageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Format.Package;
        }

        var span = content.AsSpan();

        if (span.StartsWith("%PDF-"u8))
        {
            return Format.Pdf;
        }

        // Every System.IO.Packaging container is a zip, and the packaging library detects the
        // specific format from the content rather than the name, so "PK" is as far as this has to
        // look. It covers both the local file header and an empty archive's end of central directory.
        if (span.StartsWith("PK"u8))
        {
            return Format.Package;
        }

        // No path in the message: every caller is already reporting one.
        throw new CommandException("Not a PDF or a System.IO.Packaging file.");
    }
}
