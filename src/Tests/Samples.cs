// The sample files, and a scratch directory per test to run against. Every test that writes works
// on a copy: the samples are checked in and a tool that rewrites in place would otherwise
// determinize the fixtures on the first run and pass vacuously on every run after.
static class Samples
{
    public static string Directory { get; } = FullPath(ProjectFiles.samples);

    public static string Pdf { get; } = FullPath(ProjectFiles.samples.sample_pdf);

    public static string Nupkg { get; } = FullPath(ProjectFiles.samples.sample_nupkg);

    public static string Docx { get; } = FullPath(ProjectFiles.samples.sample_docx);

    static string FullPath(string relative) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));
}
