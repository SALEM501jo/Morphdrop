using System.Windows;
using ShiftDrop.Media;
using Velopack;
using Velopack.Sources;

namespace ShiftDrop.Settings;

/// <summary>Updates come from the project's GitHub Releases, built and installed by Velopack.</summary>
internal static class Updates
{
    public const string Repository = "https://github.com/SALEM501jo/ShiftDrop";

    public static async Task CheckAsync(Window owner)
    {
        var manager = new UpdateManager(new GithubSource(Repository, null, false, null));
        if (!manager.IsInstalled)
        {
            MessageBox.Show(owner, "Updates work in the installed version of ShiftDrop (this copy runs from a build folder).", "ShiftDrop");
            return;
        }

        try
        {
            UpdateInfo? update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                MessageBox.Show(owner, $"You have the latest version ({manager.CurrentVersion}).", "ShiftDrop");
                return;
            }

            if (!ConfirmDialog.Ask($"ShiftDrop {update.TargetFullRelease.Version} is available. Download it and restart ShiftDrop?", "Update", "Later"))
                return;

            await manager.DownloadUpdatesAsync(update);
            Log.Info($"Updating to {update.TargetFullRelease.Version}");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            MessageBox.Show(owner, $"Couldn't check for updates: {ex.Message}", "ShiftDrop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
