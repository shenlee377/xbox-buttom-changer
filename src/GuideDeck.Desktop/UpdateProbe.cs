using System.IO;
using System.Text.Json;

namespace GuideDeck.Desktop;

// Exercises the exact installed updater, local fixture download, apply, restart and corruption
// rejection on disposable Windows CI runners. Never accepts production feed overrides.
internal static class UpdateProbe
{
    internal static bool TryRun(string[] args)
    {
        if (args.Length == 0 || !args[0].StartsWith("--ci-update-", StringComparison.Ordinal)) return false;
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") { Environment.ExitCode = 2; return true; }
        try { RunAsync(args).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            if (args.Length >= 3) File.WriteAllText(args[2], JsonSerializer.Serialize(new { Error = error.ToString() }));
            Environment.ExitCode = 1;
        }
        return true;
    }
    private static async Task RunAsync(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected command, local fixture, result path.");
        var service = new UpdateService(Path.GetFullPath(args[1]));
        if (!service.Installed) throw new InvalidOperationException("Probe requires a real installed build.");
        if (args[0] == "--ci-update-complete")
        {
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { service.Version, Pending = service.Pending?.Version.ToString(), Data = DesktopStorage.Root }));
            return;
        }
        var info = await service.CheckAsync() ?? throw new InvalidOperationException("Expected a newer fixture.");
        await service.DownloadAsync(info, _ => { }, CancellationToken.None);
        if (args[0] != "--ci-update-apply") throw new ArgumentException("Unknown probe command.");
        service.Restart(info.TargetFullRelease, "--ci-update-complete", args[1], args[2]);
    }
}
