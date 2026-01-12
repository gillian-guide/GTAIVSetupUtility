using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GTAIVSetupUtility.ViewModels;
using NLog;

namespace GTAIVSetupUtility.Services;

public abstract class DxvkInstallerService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    public static async Task DownloadDxvk(
    IProgress<int> downloadProgress,
    IProgress<DxvkState> stateProgress,
    string gameDirectory,
    bool ffixLatest,
    string link,
    List<string> dxvkConf,
    bool gitlab,
    bool alt,
    int release = 0)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Other");
        
        var firstResponse = await httpClient.GetAsync(link);
        firstResponse.EnsureSuccessStatusCode();
        
        string firstResponseBody = await firstResponse.Content.ReadAsStringAsync();
        var parsed = JsonDocument.Parse(firstResponseBody).RootElement;
        
        string? downloadUrl = (gitlab, alt) switch
        {
            (false, false) => parsed.GetProperty("assets")[release].GetProperty("browser_download_url").GetString(),
            (true, false) => parsed[release]
                .GetProperty("assets")
                .GetProperty("links")
                .EnumerateArray()
                .First(jsonElement => jsonElement.GetProperty("name").GetString()!.Contains("tar.gz"))
                .GetProperty("url")
                .GetString(),
            (false, true) => parsed.GetProperty("browser_download_url").GetString(),
            (true, true) => parsed.GetProperty("assets")
                .GetProperty("links")
                .EnumerateArray()
                .First(jsonElement => jsonElement.GetProperty("name").GetString()!.Contains("tar.gz"))
                .GetProperty("url")
                .GetString()
        };
        await InstallDxvk(downloadUrl!, downloadProgress, stateProgress);
        
        stateProgress.Report(DxvkState.Installing);
        
        await ExtractDxvk(gameDirectory, dxvkConf, ffixLatest);
        
        stateProgress.Report(DxvkState.Installed);
    }

    private static async Task InstallDxvk(
        string downloadUrl,
        IProgress<int> progress,
        IProgress<DxvkState> stateProgress)
    {
        try
        {
            Logger.Debug(" Downloading the .tar.gz...");
            
            stateProgress.Report(DxvkState.Downloading);

            using var client = new HttpClient();
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream("./dxvk.tar.gz", FileMode.Create, FileAccess.Write,
                FileShare.None, 8192, true);

            byte[] buffer = new byte[8192];
            long totalBytesRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalBytesRead += bytesRead;

                if (totalBytes <= 0) continue;

                var percentage = (double)totalBytesRead / totalBytes * 100;
                int percentageInt = Convert.ToInt16(percentage);
                
                progress.Report(percentageInt);
            }
            
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Error downloading DXVK");
            throw;
        }
    }

    private static async Task ExtractDxvk(string installationDir, List<string> dxvkConf, bool ffixLatest)
    {
        try
        {
            Logger.Debug(" Extracting the d3d9.dll from the archive...");
            await using (var fsIn = new FileStream("./dxvk.tar.gz", FileMode.Open, FileAccess.Read))
            await using (var gzipStream = new GZipStream(fsIn, CompressionMode.Decompress))
            await using (var tarReader = new TarReader(gzipStream))
            {
                while (await tarReader.GetNextEntryAsync() is { } entry)
                {
                    if (entry.DataStream == null || !entry.Name.EndsWith("x32/d3d9.dll"))
                        continue;

                    string destinationFileName = ffixLatest ? "vulkan.dll" : "d3d9.dll";
                    string destinationPath = Path.Combine(installationDir, destinationFileName);

                    await using (var fsOut = File.Create(destinationPath))
                    {
                        await entry.DataStream.CopyToAsync(fsOut);
                    }

                    Logger.Debug($" Extracted as {destinationFileName}");
                    break;
                }
            }

            Logger.Debug(" Deleting the .tar.gz...");
            File.Delete("./dxvk.tar.gz");

            Logger.Debug(" Writing the dxvk.conf...");
            string confPath = Path.Combine(installationDir, "dxvk.conf");

            await File.WriteAllLinesAsync(confPath, dxvkConf);

            Logger.Debug(" dxvk.conf successfully written to game folder.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Error extracting DXVK");
            throw;
        }
    }

    public static async Task UninstallDxvk(bool ffixLatest, string gameDirectory)
    {
        Logger.Info(" Removing all DXVK files present.");
        List<string> tobedeleted = ["d3d10.dll", "d3d10_1.dll", "d3d10core.dll", "d3d11.dll", "dxgi.dll", "dxvk.conf", "GTAIV.dxvk-cache", "PlayGTAIV.dxvk-cache", "LaunchGTAIV.dxvk-cache", "GTAIV_d3d9.log", "PlayGTAIV_d3d9.log", "LaunchGTAIV_d3d9.log"];
        if (!ffixLatest) tobedeleted.Add("d3d9.dll");
        await HelperService.DeleteFiles(gameDirectory, tobedeleted);
    }
}