using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace GuideDeck.Desktop;

internal static class DesktopStorage
{
    public static string Root { get; } = ResolveRoot();
    private static string ResolveRoot()
    {
        uint length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) == 122 && length is > 1 and < 1024)
        {
            var name = new StringBuilder((int)length);
            if (GetCurrentPackageFamilyName(ref length, name) == 0)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", name.ToString(), "LocalState", "GuideDeck");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuideDeck");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);
}
