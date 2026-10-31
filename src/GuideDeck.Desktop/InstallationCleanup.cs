using System.IO;

namespace GuideDeck.Desktop;

internal static class InstallationCleanup
{
    // This hook runs only during an explicit uninstall, never on upgrade.
    // Do not search other profiles, Steam folders, arbitrary exports, or shared Velopack logs.
    internal static void RemoveUserData()
    {
        if (DesktopStorage.IsPackaged) return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuideDeck");
        if (!string.Equals(Path.GetFullPath(root), Path.GetFullPath(DesktopStorage.Root), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unexpected data directory; cleanup refused.");
        RemoveTree(new DirectoryInfo(root));
    }
    private static void RemoveTree(DirectoryInfo directory)
    {
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { directory.Delete(); return; }
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if (item is DirectoryInfo child) RemoveTree(child);
            else { item.Attributes &= ~FileAttributes.ReadOnly; item.Delete(); }
        }
        directory.Delete();
    }
}
