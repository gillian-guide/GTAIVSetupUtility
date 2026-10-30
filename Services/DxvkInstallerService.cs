using System.Formats.Tar;
using System.IO.Compression;
using GTAIVSetupUtility.Models;
using GTAIVSetupUtility.Services.Network;
using NLog;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// The release shape used by one DXVK flavour. GitHub exposes a release object with an asset
/// list, GitLab exposes a link list, and a few old builds are only reachable as a single asset.
/// </summary>
internal enum DxvkReleaseApi
{
    GitHubLatest,
    GitHubAsset,
    GitLabLatestList,
    GitLabRelease
}

internal sealed record DxvkInstallPlan(
    int Tier,
    bool Async,
    string Name,
    string ApiUrl,
    DxvkReleaseApi Api,
    int AssetIndex = 0,
    string? LatestToken = null,
    bool GplAsyncCache = false);

internal static class DxvkInstallerService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string GitHubDxvkLatest = "https://api.github.com/repos/doitsujin/dxvk/releases/latest";
    private const string GitHubSarekLatest = "https://api.github.com/repos/pythonlover02/dxvk-Sarek/releases/latest";
    private const string GitLabGplAsyncLatest = "https://gitlab.com/api/v4/projects/43488626/releases/";
    private const string GitLabGplAsync262 = "https://gitlab.com/api/v4/projects/43488626/releases/v2.6.2-1";

    private static readonly DxvkInstallPlan[] Plans =
    [
        new(1, false, "DXVK-Sarek", GitHubSarekLatest, DxvkReleaseApi.GitHubLatest, AssetIndex: 1,
            LatestToken: "DXVK-Sarek"),
        new(1, true, "DXVK-Sarek-async", GitHubSarekLatest, DxvkReleaseApi.GitHubLatest, LatestToken: "DXVK-Sarek-async"),

        new(2, false, "DXVK 2.6.2", "https://api.github.com/repos/doitsujin/dxvk/releases/assets/222230856",
            DxvkReleaseApi.GitHubAsset),
        new(2, true, "DXVK-gplasync 2.6.2", GitLabGplAsync262, DxvkReleaseApi.GitLabRelease),

        new(3, false, "DXVK", GitHubDxvkLatest, DxvkReleaseApi.GitHubLatest, LatestToken: "DXVK"),
        new(3, true, "DXVK-gplasync", GitLabGplAsyncLatest, DxvkReleaseApi.GitLabLatestList,
            LatestToken: "DXVK-gplasync", GplAsyncCache: true),

        new(-1, false, "DXVK 1.10.1", "https://api.github.com/repos/doitsujin/dxvk/releases/assets/60669426",
            DxvkReleaseApi.GitHubAsset),
        new(-1, true, "DXVK-async 1.10.1", "https://api.github.com/repos/Sporif/dxvk-async/releases/assets/60677007",
            DxvkReleaseApi.GitHubAsset)
    ];

    public static DxvkInstallPlan? FindPlan(int tier, bool async) =>
        Array.Find(Plans, plan => plan.Tier == tier && plan.Async == async);

    public static async Task InstallAsync(
        DxvkInstallPlan plan,
        string gameDirectory,
        bool fusionFixLatest,
        IReadOnlyList<string> dxvkConfig,
        IProgress<int> downloadProgress,
        IProgress<DxvkState> stateProgress,
        bool writeDxvkConfig = true,
        CancellationToken cancellationToken = default)
    {
        using var workspace = TempDirectory.Create();
        string archive = workspace.CreateFile(".tar.gz");

        try
        {
            stateProgress.Report(DxvkState.Downloading);
            string downloadUrl = await ResolveDownloadUrlAsync(plan, cancellationToken).ConfigureAwait(false);

            Logger.Debug($" Downloading {plan.Name} from {downloadUrl}...");
            await HttpService.DownloadFileAsync(downloadUrl, archive, downloadProgress, cancellationToken)
                .ConfigureAwait(false);

            stateProgress.Report(DxvkState.Installing);
            await ExtractAsync(archive, gameDirectory, fusionFixLatest, dxvkConfig, writeDxvkConfig, cancellationToken)
                .ConfigureAwait(false);

            stateProgress.Report(DxvkState.Installed);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $" Could not install {plan.Name}.");
            throw;
        }
    }

    public static async Task UninstallAsync(bool fusionFixLatest, string gameDirectory)
    {
        Logger.Info(" Removing all DXVK files present.");

        List<string> files =
        [
            "d3d10.dll", "d3d10_1.dll", "d3d10core.dll", "d3d11.dll", "dxgi.dll",
            "dxvk.conf", "GTAIV.dxvk-cache", "PlayGTAIV.dxvk-cache", "LaunchGTAIV.dxvk-cache",
            "GTAIV_d3d9.log", "PlayGTAIV_d3d9.log", "LaunchGTAIV_d3d9.log"
        ];

        if (!fusionFixLatest)
            files.Add("d3d9.dll");

        await HelperService.DeleteFilesAsync(gameDirectory, files).ConfigureAwait(false);
    }

    private static async Task<string> ResolveDownloadUrlAsync(
        DxvkInstallPlan plan,
        CancellationToken cancellationToken)
    {
        switch (plan.Api)
        {
            case DxvkReleaseApi.GitHubLatest:
                {
                    GitHubRelease? release = await HttpService
                        .GetJsonAsync(plan.ApiUrl, GitHubJsonContext.Default.GitHubRelease, cancellationToken)
                        .ConfigureAwait(false);

                    if (release?.Assets is not { } assets || assets.Count <= plan.AssetIndex)
                        throw new InvalidOperationException($"The {plan.Name} release has no matching download asset.");

                    return assets[plan.AssetIndex].BrowserDownloadUrl
                           ?? throw new InvalidOperationException($"The {plan.Name} asset has no download URL.");
                }

            case DxvkReleaseApi.GitHubAsset:
                {
                    GitHubAsset? asset = await HttpService
                        .GetJsonAsync(plan.ApiUrl, GitHubJsonContext.Default.GitHubAsset, cancellationToken)
                        .ConfigureAwait(false);

                    return asset?.BrowserDownloadUrl
                           ?? throw new InvalidOperationException($"The {plan.Name} asset has no download URL.");
                }

            case DxvkReleaseApi.GitLabLatestList:
                {
                    List<GitLabRelease>? releases = await HttpService
                        .GetJsonAsync(plan.ApiUrl, GitHubJsonContext.Default.ListGitLabRelease, cancellationToken)
                        .ConfigureAwait(false);

                    return FindGitLabArchive(releases?.FirstOrDefault(), plan.Name);
                }

            case DxvkReleaseApi.GitLabRelease:
                {
                    GitLabRelease? release = await HttpService
                        .GetJsonAsync(plan.ApiUrl, GitHubJsonContext.Default.GitLabRelease, cancellationToken)
                        .ConfigureAwait(false);

                    return FindGitLabArchive(release, plan.Name);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(plan), plan.Api, "Unknown DXVK release API.");
        }
    }

    private static string FindGitLabArchive(GitLabRelease? release, string name) =>
        release?.Assets?.Links
            ?.FirstOrDefault(link => link.Name?.Contains("tar.gz", StringComparison.OrdinalIgnoreCase) == true)
            ?.Url
        ?? throw new InvalidOperationException($"The {name} release has no .tar.gz asset.");

    private static async Task ExtractAsync(
        string archivePath,
        string installDirectory,
        bool fusionFixLatest,
        IReadOnlyList<string> dxvkConfig,
        bool writeDxvkConfig,
        CancellationToken cancellationToken)
    {
        string destinationFile = fusionFixLatest ? "vulkan.dll" : "d3d9.dll";
        bool extracted = false;

        await using (var file = File.OpenRead(archivePath))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        await using (var tar = new TarReader(gzip))
        {
            while (await tar.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (entry.DataStream is null || !entry.Name.EndsWith("x32/d3d9.dll", StringComparison.Ordinal))
                    continue;

                await using var output = File.Create(Path.Combine(installDirectory, destinationFile));
                await entry.DataStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                extracted = true;
                Logger.Debug($" Extracted as {destinationFile}.");
                break;
            }
        }

        if (!extracted)
            throw new InvalidOperationException("d3d9.dll was not found in the DXVK archive.");

        if (!writeDxvkConfig)
        {
            Logger.Debug(" Keeping the existing dxvk.conf (DLL-only update).");
            return;
        }

        Logger.Debug(" Writing dxvk.conf...");
        await File.WriteAllLinesAsync(
            Path.Combine(installDirectory, "dxvk.conf"), dxvkConfig, cancellationToken).ConfigureAwait(false);
        Logger.Debug(" dxvk.conf successfully written to the game folder.");
    }
}
