namespace lively.Tests;

/// <summary>
/// Creates a unique directory under the system temp path and deletes it on dispose.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lively-tests-" + Guid.NewGuid().ToString("N"));

    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string CreateDirectory(params string[] parts)
    {
        var dir = Combine(parts);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string WriteFile(string content, params string[] parts)
    {
        var file = Combine(parts);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
    }
}
