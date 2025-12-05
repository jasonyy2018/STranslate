using Microsoft.Extensions.Logging;
using STranslate.Plugin;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack;

namespace STranslate.Core;

public class UpdaterService(
    ILogger<UpdaterService> logger,
    IHttpService httpService,
    INotification notification
    )
{
    private SemaphoreSlim UpdateLock { get; } = new SemaphoreSlim(1);

    public async Task UpdateAppAsync(bool silentUpdate = true)
    {
        await UpdateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!silentUpdate)
                notification.Show("Update Check", "Checking for updates...");

            var updateManager = await GitHubUpdateManagerAsync(httpService, "https://github.com/zggsong7/STranslate"/*Constant.GitHub*/).ConfigureAwait(false);

            if (!updateManager.IsInstalled)
            {
                if (!silentUpdate)
                    notification.Show("Update Check", "Application is not installed via Velopack. Update aborted.");
                logger.LogInformation("Application is not installed via Velopack. Update aborted.");
                return;
            }

            var newUpdateInfo = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (newUpdateInfo == null)
            {
                if (!silentUpdate)
                    notification.Show("Update Check", "No update info found.");
                logger.LogInformation("No update info found.");
                return;
            }

            var newReleaseVersion = SemanticVersioning.Version.Parse(newUpdateInfo.TargetFullRelease.Version.ToString());
            var currentVersion = SemanticVersioning.Version.Parse(Constant.Version);

            logger.LogInformation($"Future Release <{JsonSerializer.Serialize(newUpdateInfo.TargetFullRelease, JsonOptions)}>");

            if (newReleaseVersion <= currentVersion)
            {
                if (!silentUpdate)
                    notification.Show("Update Check", "You are already on the latest version.");
                return;
            }

            if (!silentUpdate)
                notification.Show("Update Available", $"New version {newReleaseVersion} found. Updating...");

            await updateManager.DownloadUpdatesAsync(newUpdateInfo).ConfigureAwait(false);

            //if (DataLocation.PortableDataLocationInUse())
            //{
            //    var targetDestination = updateManager.RootAppDirectory +
            //                            $"\\app-{newReleaseVersion}\\{DataLocation.PortableFolderName}";
            //    FilesFolders.CopyAll(DataLocation.PortableDataPath, targetDestination, (s) => _api.ShowMsgBox(s));
            //    if (!FilesFolders.VerifyBothFolderFilesEqual(DataLocation.PortableDataPath, targetDestination,
            //            (s) => _api.ShowMsgBox(s)))
            //        _api.ShowMsgBox(string.Format(
            //            _api.GetTranslation("update_flowlauncher_fail_moving_portable_user_profile_data"),
            //            DataLocation.PortableDataPath,
            //            targetDestination));
            //}
            //else
            //{
            //    await updateManager.CreateUninstallerRegistryEntry().ConfigureAwait(false);
            //}

            var newVersionTips = NewVersionTips(newReleaseVersion.ToString());

            if (!silentUpdate)
                notification.Show("Update Ready", newVersionTips);
            logger.LogInformation($"Update success:{newVersionTips}");

            if (await iNKORE.UI.WPF.Modern.Controls.MessageBox.ShowAsync(newVersionTips, "STranslate",
                    System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes)
                updateManager.ApplyUpdatesAndRestart(newUpdateInfo);
        }
        catch (Exception e)
        {
            if (e is HttpRequestException or WebException or SocketException ||
                e.InnerException is TimeoutException)
            {
                logger.LogError(e, $"Check your connection and proxy settings to github-cloud.s3.amazonaws.com.");
            }
            else
            {
                logger.LogError(e, $"Error Occurred");
            }

            if (!silentUpdate)
                notification.Show("Update Failed", "Failed to update the application. Please check your connection.");
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    private class GithubRelease
    {
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }

        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
    }

    // https://github.com/Squirrel/Squirrel.Windows/blob/master/src/Squirrel/UpdateManager.Factory.cs
    private static async Task<UpdateManager> GitHubUpdateManagerAsync(IHttpService httpService,string repository)
    {
        var uri = new Uri(repository);
        var api = $"https://api.github.com/repos{uri.AbsolutePath}/releases";

        var jsonStream = await httpService.GetAsStreamAsync(api, CancellationToken.None).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<GithubRelease>>(jsonStream).ConfigureAwait(false);
        if (releases == null || releases.Count == 0)
            throw new InvalidOperationException("No releases found in the repository.");

        var latest = releases
            .Where(r => !r.Prerelease)
            .OrderByDescending(r => r.PublishedAt)
            .FirstOrDefault() ?? throw new InvalidOperationException("No stable release found in the repository.");
        var latestUrl = latest.HtmlUrl.Replace("/tag/", "/download/");
        
        var manager = new UpdateManager(latestUrl);

        return manager;
    }

    private string NewVersionTips(string version)
    {
        var tips = string.Format("New version {0} is available, would you like to restart STranslate to use the update?", version);

        return tips;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}