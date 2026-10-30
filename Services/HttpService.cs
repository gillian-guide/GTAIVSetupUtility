using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// One shared <see cref="HttpClient"/> for every network call. Creating a client per request
/// exhausts sockets and loses connection pooling, so all downloads/API calls go through here.
/// </summary>
internal static class HttpService
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        var client = new HttpClient(handler)
        {
            // Release assets are large; the per-call cancellation token still aborts hung requests.
            Timeout = TimeSpan.FromMinutes(15)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "GTAIVSetupUtility/3.0 (+https://github.com/gillian-guide/GTAIVSetupUtility)");
        return client;
    }

    public static async Task<T?> GetJsonAsync<T>(
        string url,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        return await JsonSerializer
            .DeserializeAsync(stream, typeInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a single file, returning <see langword="false"/> for HTTP 404 so callers can
    /// fall back to an archive-based download path.
    /// </summary>
    public static async Task<bool> TryDownloadFileAsync(
        string url,
        string destination,
        IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        await SaveAsync(response, destination, progress, -1, 0, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await SaveAsync(response, destination, progress, -1, 0, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads several files while reporting one combined percentage. A failed HEAD request
    /// only loses the exact total; the files are still downloaded.
    /// </summary>
    public static async Task<bool> TryDownloadFilesAsync(
        IReadOnlyList<(string Url, string Destination)> files,
        IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        long total = 0;
        foreach ((string url, string _) in files)
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Client
                .SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            if (response.IsSuccessStatusCode)
                total += response.Content.Headers.ContentLength ?? 0;
        }

        long completed = 0;
        foreach ((string url, string destination) in files)
        {
            using var response = await Client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            response.EnsureSuccessStatusCode();
            completed += await SaveAsync(response, destination, progress, total, completed, cancellationToken)
                .ConfigureAwait(false);
        }

        progress?.Report(100);
        return true;
    }

    private static async Task<long> SaveAsync(
        HttpResponseMessage response,
        string destination,
        IProgress<int>? progress,
        long aggregateTotal,
        long completedBefore,
        CancellationToken cancellationToken)
    {
        long responseTotal = response.Content.Headers.ContentLength ?? -1;
        long total = aggregateTotal > 0 ? aggregateTotal : responseTotal;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var target = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;

            if (total > 0)
            {
                long overall = completedBefore + readTotal;
                progress?.Report((int)Math.Clamp(overall * 100 / total, 0, 100));
            }
        }

        if (aggregateTotal <= 0)
            progress?.Report(100);

        return readTotal;
    }
}
