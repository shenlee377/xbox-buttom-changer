using Velopack;
using Velopack.Sources;

namespace GuideDeck.Desktop;

internal sealed class UpdateService
{
    internal const string Repository = "https://github.com/shenlee377/xbox-buttom-changer";
    internal const string Channel = "win-x64";
    private readonly UpdateManager manager;
    internal UpdateService()
    {
        manager = new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: true), Options);
    }
    // Only the CI probe can select a local fixture. No user-controlled URL, token or feed setting.
    internal UpdateService(string fixture) => manager = new UpdateManager(fixture, Options);
    private static UpdateOptions Options => new() { ExplicitChannel = Channel, AllowVersionDowngrade = false };
    internal bool Installed => !DesktopStorage.IsPackaged && manager.IsInstalled;
    internal string? Version => manager.CurrentVersion?.ToString();
    internal VelopackAsset? Pending => manager.UpdatePendingRestart;
    internal Task<UpdateInfo?> CheckAsync() => manager.CheckForUpdatesAsync();
    internal Task DownloadAsync(UpdateInfo info, Action<int> progress, CancellationToken token) => manager.DownloadUpdatesAsync(info, progress, token);
    internal void Restart(VelopackAsset asset, params string[] args) => manager.ApplyUpdatesAndRestart(asset, args);
}
