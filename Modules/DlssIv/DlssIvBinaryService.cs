using System.IO.Compression;
using System.Security.Cryptography;
using GTAIVSetupUtility.Services;
using GTAIVSetupUtility.Services.Network;
using NLog;
using SevenZipExtractor;

namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// Optional binary maintenance for the already-installed DLSS-IV:
/// - official NVIDIA DLSS runtime (DLSSIV\nvngx_dlss.dll)
/// - official AMD FSR SDK runtime (DLSSIV\amd_fidelityfx_upscaler_dx12.dll + loader)
/// - the community fsr4xyz INT8 upscaler offered on RDNA2
/// Every overwritten file is backed up first. OptiScaler is never installed; only leftover
/// state from older builds is cleaned up.
/// </summary>
public static class DlssIvBinaryService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<string> SevenZipLibraryPath = new(ExtractSevenZipLibrary);

    private const string DlssLatestApi = "https://api.github.com/repos/NVIDIA/DLSS/releases/latest";
    private const string FsrLatestApi = "https://api.github.com/repos/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/releases/latest";
    private const string Fsr4LatestApi = "https://api.github.com/repos/the3rdparty1917/fsr4xyz/releases/latest";
    private const long OriginalFsrLoaderSize = 26_376;

    public static async Task<string> UpdateDlssAsync(
        DlssIvInstallation installation,
        DlssIvState state,
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        string dataDirectory = EnsureDataDirectory(installation);
        GitHubRelease release = await ReadReleaseAsync(DlssLatestApi, cancellationToken).ConfigureAwait(false);
        string tag = release.TagName ?? "latest";

        using var workspace = TempDirectory.Create();
        string tempDll = workspace.CreateFile(".dll");

        // The DLL lives in the SDK repository; downloading it directly avoids the ~84 MB demo zip.
        string directUrl =
            $"https://raw.githubusercontent.com/NVIDIA/DLSS/{tag}/lib/Windows_x86_64/rel/nvngx_dlss.dll";
        bool direct = await HttpService
            .TryDownloadFileAsync(directUrl, tempDll, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!direct)
        {
            Logger.Debug(" Direct DLSS DLL download unavailable; falling back to the NVIDIA SDK zip.");
            await DownloadDlssFromSdkZipAsync(release, tempDll, progress, cancellationToken).ConfigureAwait(false);
        }

        VerifyPeFile(tempDll);
        BackupFile(installation, "nvngx_dlss.dll");
        File.Copy(tempDll, Path.Combine(dataDirectory, "nvngx_dlss.dll"), true);
        Logger.Info($" Updated DLSS runtime to {tag}.");
        return tag;
    }

    public static async Task<string> UpdateFsrAsync(
        DlssIvInstallation installation,
        DlssIvState state,
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        string dataDirectory = EnsureDataDirectory(installation);
        GitHubRelease release = await ReadReleaseAsync(FsrLatestApi, cancellationToken).ConfigureAwait(false);
        string tag = release.TagName ?? "latest";

        using var workspace = TempDirectory.Create();
        string tempUpscaler = workspace.CreateFile(".dll");
        string tempLoader = workspace.CreateFile(".dll");

        // Download only the two signed DLLs from the SDK repository instead of the ~242 MB
        // source archive. The raw file host serves them individually and supports ranges.
        string baseUrl =
            $"https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/{tag}/Kits/FidelityFX/signedbin";
        bool direct = await HttpService.TryDownloadFilesAsync(
            [
                ($"{baseUrl}/amd_fidelityfx_upscaler_dx12.dll", tempUpscaler),
                ($"{baseUrl}/amd_fidelityfx_loader_dx12.dll", tempLoader)
            ],
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!direct)
        {
            Logger.Debug(" Direct FSR DLL download unavailable; falling back to the FSR SDK archive.");
            await DownloadFsrFromSdkZipAsync(release, tempUpscaler, tempLoader, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        VerifyPeFile(tempUpscaler);
        VerifyPeFile(tempLoader);

        BackupFile(installation, "amd_fidelityfx_upscaler_dx12.dll");
        BackupFile(installation, "amd_fidelityfx_loader_dx12.dll");
        File.Copy(tempUpscaler, Path.Combine(dataDirectory, "amd_fidelityfx_upscaler_dx12.dll"), true);
        File.Copy(tempLoader, Path.Combine(dataDirectory, "amd_fidelityfx_loader_dx12.dll"), true);

        bool lite = state.RequestedFsrProfile == "fsr4-lite" || state.FsrProfile == "fsr4-lite";
        state.FsrProfile = lite ? "fsr4-lite" : "fsr4";
        state.FsrSource = lite ? $"AMD FSR SDK {tag} (Lite-capable)" : $"AMD FSR SDK {tag}";
        state.FsrDllSha256 = ComputeSha256(Path.Combine(dataDirectory, "amd_fidelityfx_upscaler_dx12.dll"));
        Logger.Info($" Updated the FSR upscaler to {tag}.");
        return tag;
    }

    /// <summary>
    /// Downloads the community FSR 4 INT8 build that the DLSS-IV author tested with the mod
    /// (fsr4xyz), and installs only the upscaler DLL. The original 1.0.9 FSR loader is kept:
    /// replacing the loader with AMD's 2.4 SDK loader makes DLSS-IV fall back to FSR 3.1.
    /// </summary>
    public static async Task<string> InstallFsr4CommunityAsync(
        DlssIvInstallation installation,
        DlssIvState state,
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        string dataDirectory = EnsureDataDirectory(installation);
        GitHubRelease release = await ReadReleaseAsync(Fsr4LatestApi, cancellationToken).ConfigureAwait(false);
        string tag = release.TagName ?? "latest";

        string downloadUrl = release.Assets
            ?.FirstOrDefault(asset => asset.Name?.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) == true)
            ?.BrowserDownloadUrl
            ?? throw new InvalidOperationException("The latest fsr4xyz release has no 7z asset.");

        using var workspace = TempDirectory.Create();
        string archivePath = workspace.CreateFile(".7z");
        string tempDll = workspace.CreateFile(".dll");

        await HttpService.DownloadFileAsync(downloadUrl, archivePath, progress, cancellationToken)
            .ConfigureAwait(false);

        using (var archive = new ArchiveFile(archivePath, SevenZipLibraryPath.Value))
        {
            var entry = archive.Entries.FirstOrDefault(candidate =>
                Path.GetFileName(candidate.FileName.Replace('\\', '/'))
                    .Equals("amd_fidelityfx_upscaler_dx12.dll", StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new InvalidOperationException(
                    "amd_fidelityfx_upscaler_dx12.dll was not found in the fsr4xyz archive.");

            entry.Extract(tempDll);
        }

        VerifyPeFile(tempDll);
        EnsureOriginalFsrLoader(installation);
        BackupFile(installation, "amd_fidelityfx_upscaler_dx12.dll");
        File.Copy(tempDll, Path.Combine(dataDirectory, "amd_fidelityfx_upscaler_dx12.dll"), true);

        File.WriteAllText(Path.Combine(dataDirectory, "fsr4-community"), tag);
        state.FsrProfile = "fsr4";
        state.FsrSource = $"fsr4xyz {tag}";
        state.FsrDllSha256 = ComputeSha256(Path.Combine(dataDirectory, "amd_fidelityfx_upscaler_dx12.dll"));
        state.RequestedFsrProfile = "fsr4";
        Logger.Info($" Installed community FSR 4 upscaler from fsr4xyz {tag}.");
        return tag;
    }

    public static async Task<string?> GetLatestFsr4TagAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            GitHubRelease release = await ReadReleaseAsync(Fsr4LatestApi, cancellationToken).ConfigureAwait(false);
            return release.TagName;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " Could not query the latest fsr4xyz release.");
            return null;
        }
    }

    public static bool IsFsr4CommunityCurrent(
        DlssIvInstallation installation,
        DlssIvState state,
        string? latestTag = null)
    {
        if (!string.Equals(state.FsrProfile, "fsr4", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!File.Exists(Path.Combine(installation.DataDirectory, "fsr4-community")))
            return false;

        string dll = Path.Combine(installation.DataDirectory, "amd_fidelityfx_upscaler_dx12.dll");
        if (!File.Exists(dll))
            return false;

        if (latestTag != null &&
            !(state.FsrSource?.Contains(latestTag, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        return string.IsNullOrEmpty(state.FsrDllSha256) ||
               ComputeSha256(dll).Equals(state.FsrDllSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Restores files replaced by an old OptiScaler installation and removes the OptiScaler
    /// files. OptiScaler is never installed by the current module; this is cleanup only.
    /// </summary>
    public static void RemoveOptiScaler(DlssIvInstallation installation, DlssIvState state)
    {
        string dataDirectory = installation.DataDirectory;

        foreach (DlssIvBackedUpFile file in state.OptiScalerFiles.ToList())
        {
            string target = Path.GetFullPath(Path.Combine(dataDirectory, file.RelativePath));
            if (!target.StartsWith(Path.GetFullPath(dataDirectory), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (file.Existed && !string.IsNullOrEmpty(file.BackupRelativePath))
                {
                    string backup = Path.Combine(dataDirectory, file.BackupRelativePath);
                    if (File.Exists(backup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(backup, target, true);
                        continue;
                    }
                }

                File.Delete(target);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $" Could not restore/remove {file.RelativePath}.");
            }
        }

        state.OptiScalerFiles.Clear();
        state.OptiScalerEnabled = false;
        state.OptiScalerVersion = null;
        state.FsrProfile = state.PreOptiScalerFsrProfile;
        state.FsrSource = state.PreOptiScalerFsrSource;
        state.FsrDllSha256 = state.PreOptiScalerFsrDllSha256;
        state.PreOptiScalerFsrProfile = null;
        state.PreOptiScalerFsrSource = null;
        state.PreOptiScalerFsrDllSha256 = null;

        TryDeleteDirectory(Path.Combine(dataDirectory, "D3D12_Optiscaler"));
        TryDeleteDirectory(Path.Combine(dataDirectory, ".gtaiv-backup", "optiscaler"));
        Logger.Info(" Legacy OptiScaler installation removed from DLSSIV.");
    }

    public static string EnsureDataDirectory(DlssIvInstallation installation)
    {
        Directory.CreateDirectory(installation.DataDirectory);
        return installation.DataDirectory;
    }

    private static async Task<GitHubRelease> ReadReleaseAsync(string url, CancellationToken cancellationToken) =>
        await HttpService.GetJsonAsync(url, GitHubJsonContext.Default.GitHubRelease, cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException($"The release endpoint {url} returned no data.");

    private static async Task DownloadDlssFromSdkZipAsync(
        GitHubRelease release,
        string destination,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        string? downloadUrl = release.Assets
            ?.Where(asset => asset.Name is { } name &&
                             name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                             name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset.BrowserDownloadUrl)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(downloadUrl))
            throw new InvalidOperationException("The latest NVIDIA DLSS release has no Windows zip asset.");

        using var workspace = TempDirectory.Create();
        string tempZip = workspace.CreateFile(".zip");
        await HttpService.DownloadFileAsync(downloadUrl, tempZip, progress, cancellationToken)
            .ConfigureAwait(false);

        using var archive = ZipFile.OpenRead(tempZip);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            Path.GetFileName(candidate.FullName).Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase));

        if (entry == null)
            throw new InvalidOperationException("nvngx_dlss.dll was not found in the NVIDIA DLSS archive.");

        entry.ExtractToFile(destination, true);
    }

    private static async Task DownloadFsrFromSdkZipAsync(
        GitHubRelease release,
        string upscalerDestination,
        string loaderDestination,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        string downloadUrl = release.ZipballUrl
            ?? throw new InvalidOperationException("The latest FSR SDK release has no download archive.");

        using var workspace = TempDirectory.Create();
        string tempZip = workspace.CreateFile(".zip");
        await HttpService.DownloadFileAsync(downloadUrl, tempZip, progress, cancellationToken)
            .ConfigureAwait(false);

        using var archive = ZipFile.OpenRead(tempZip);
        ExtractSignedBin(archive, "amd_fidelityfx_upscaler_dx12.dll", upscalerDestination);
        ExtractSignedBin(archive, "amd_fidelityfx_loader_dx12.dll", loaderDestination);
    }

    private static void EnsureOriginalFsrLoader(DlssIvInstallation installation)
    {
        string loader = Path.Combine(installation.DataDirectory, "amd_fidelityfx_loader_dx12.dll");
        if (File.Exists(loader) && new FileInfo(loader).Length == OriginalFsrLoaderSize)
            return;

        string backup = loader + ".gtaiv-utility.bak";
        if (File.Exists(backup) && new FileInfo(backup).Length == OriginalFsrLoaderSize)
        {
            File.Copy(backup, loader, true);
            Logger.Info(" Restored DLSS-IV's original FSR loader.");
            return;
        }

        throw new InvalidOperationException(
            "The DLSS-IV FSR loader is not the original 1.0.9 loader. Reinstall the DLSS-IV archive's DLSSIV folder first.");
    }

    private static void ExtractSignedBin(ZipArchive archive, string fileName, string destination)
    {
        var entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
            candidate.FullName.Contains("signedbin", StringComparison.OrdinalIgnoreCase));

        if (entry == null)
            throw new InvalidOperationException($"{fileName} was not found in the FSR SDK archive.");

        entry.ExtractToFile(destination, true);
    }

    private static void BackupFile(DlssIvInstallation installation, string relativePath)
    {
        string source = Path.Combine(installation.DataDirectory, relativePath);
        if (!File.Exists(source))
            return;

        string backup = source + ".gtaiv-utility.bak";
        if (File.Exists(backup))
            return;

        try
        {
            File.Copy(source, backup);
            Logger.Debug($" Backed up {relativePath}.");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $" Could not back up {relativePath}.");
        }
    }

    private static void VerifyPeFile(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid Windows DLL.");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// SevenZipExtractor needs a real 7z.dll on disk. In a trimmed single-file publish the
    /// package's native asset is not next to the exe, so carry the x64 library as an embedded
    /// resource and extract it once.
    /// </summary>
    private static string ExtractSevenZipLibrary()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GTAIVSetupUtility");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "7z.dll");

        if (File.Exists(path))
            return path;

        using var resource = typeof(DlssIvBinaryService).Assembly.GetManifestResourceStream(
            "GTAIVSetupUtility.Assets.7z.dll");
        if (resource == null)
            throw new InvalidOperationException("The embedded 7z library is missing.");

        string temporary = path + "." + Guid.CreateVersion7().ToString("N");
        try
        {
            using (var file = File.Create(temporary))
            {
                resource.CopyTo(file);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            TryDelete(temporary);
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best-effort temp cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception)
        {
            // Best-effort cleanup; the mod folder may still hold a loaded DLL.
        }
    }
}
