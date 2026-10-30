using System.Text.Json.Serialization;

namespace GTAIVSetupUtility.Services.Network;

/// <summary>
/// The subset of the GitHub release payload the updater reads.
/// </summary>
internal sealed class GitHubRelease
{
    public string? TagName { get; init; }
    public string? ZipballUrl { get; init; }
    public List<GitHubAsset>? Assets { get; init; }
}

internal sealed class GitHubAsset
{
    public string? Name { get; init; }
    public string? BrowserDownloadUrl { get; init; }
    public string? Url { get; init; }
}

internal sealed class GitLabRelease
{
    public GitLabAssets? Assets { get; init; }
}

internal sealed class GitLabAssets
{
    public List<GitLabAssetLink>? Links { get; init; }
}

internal sealed class GitLabAssetLink
{
    public string? Name { get; init; }
    public string? Url { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
[JsonSerializable(typeof(GitLabRelease))]
[JsonSerializable(typeof(List<GitLabRelease>))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext
{
}
