namespace GTAIVSetupUtility.Services;

/// <summary>
/// A throw-away working directory under the OS temp folder. Network assets are streamed there
/// and removed even when the operation fails or is cancelled.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    public string Path { get; }

    public static TempDirectory Create() =>
        new(Directory.CreateTempSubdirectory("GTAIVSetupUtility-").FullName);

    public string CreateFile(string extension) =>
        System.IO.Path.Combine(Path, $"{Guid.CreateVersion7():N}{extension}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A DLL can still be mapped by the game; the OS cleans %TEMP% eventually.
        }
    }
}
