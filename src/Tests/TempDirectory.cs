// A scratch directory that cleans itself up. Any test that runs the tool needs one: the samples are
// checked in, and a tool whose whole job is rewriting files in place would determinize them on the
// first run and then pass vacuously forever after.
sealed class TempDirectory : IDisposable
{
    public string FullName { get; }

    public TempDirectory()
    {
        FullName = Path.Combine(Path.GetTempPath(), "determinize-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(FullName);
    }

    // Copies a sample in, optionally under a different relative name, and returns the full path.
    public string Add(string source, string? relativeName = null)
    {
        var target = Path.Combine(FullName, relativeName ?? Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    public string Combine(string relativeName) => Path.Combine(FullName, relativeName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullName, recursive: true);
        }
        catch
        {
            // A scratch directory that will not delete is not a test failure.
        }
    }
}
