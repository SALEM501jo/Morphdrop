using System.Windows;
using Morphdrop.Media;
using Velopack;
using Velopack.Sources;

namespace Morphdrop.Settings;

/// <summary>Updates come from the project's GitHub Releases, built and installed by Velopack.</summary>
internal static class Updates
{
    public const string Repository = "https://github.com/SALEM501jo/Morphdrop";

    public static async Task CheckAsync(Window owner)
    {
        var manager = new UpdateManager(new GithubSource(Repository, null, false, null));
        if (!manager.IsInstalled)
        {
            MessageBox.Show(owner, "Updates work in the installed version of Morphdrop (this copy runs from a build folder).", "Morphdrop");
            return;
        }

        try
        {
            UpdateInfo? update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                MessageBox.Show(owner, $"You have the latest version ({manager.CurrentVersion}).", "Morphdrop");
                return;
            }

            if (!ConfirmDialog.Ask($"Morphdrop {update.TargetFullRelease.Version} is available. Download it and restart Morphdrop?", "Update", "Later"))
                return;

            await manager.DownloadUpdatesAsync(update);
            Log.Info($"Updating to {update.TargetFullRelease.Version}");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            MessageBox.Show(owner, $"Couldn't check for updates: {ex.Message}", "Morphdrop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
