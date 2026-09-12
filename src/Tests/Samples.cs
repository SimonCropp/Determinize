// The sample files, and a scratch directory per test to run against. Every test that writes works
// on a copy: the samples are checked in and a tool that rewrites in place would otherwise
// determinize the fixtures on the first run and pass vacuously on every run after.
static class Samples
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "samples");

    public static string Pdf { get; } = Path.Combine(Directory, "sample.pdf");

    public static string Nupkg { get; } = Path.Combine(Directory, "sample.nupkg");

    public static string Docx { get; } = Path.Combine(Directory, "sample.docx");
}
