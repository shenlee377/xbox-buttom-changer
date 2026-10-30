using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GuideDeck.Core;

public sealed record PatchPreview(string ConfigPath, string SourceHash, int MappingIndex, string Identity, string Before, string After);
public sealed record BackupReceipt(int SchemaVersion, string ConfigPath, string OriginalHash, string AppliedHash, int MappingIndex, string Identity, DateTimeOffset CreatedAt, string OriginalBase64);

public static class ConfigTransaction
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static void EnsureSteamExited()
    {
        foreach (var name in new[] {"steam", "steamwebhelper"})
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) throw new InvalidOperationException("請從 Steam 選單完整離開 Steam 後再操作。程式不會強制關閉 Steam／遊戲。"); }
            finally { foreach (var p in processes) p.Dispose(); }
        }
    }

    public static PatchPreview Preview(string path, int index)
    {
        path = ValidatePath(path);
        var original = ReadBounded(path);
        _ = SteamConfig.Patch(original, index);
        var candidate = SteamConfig.Inspect(original).Mappings.Single(m => m.Index == index);
        return new(path, Hash(original), index, candidate.Identity, candidate.Before, candidate.After);
    }

    public static string Apply(PatchPreview preview, string backupDirectory, Action? guard = null)
    {
        guard ??= EnsureSteamExited;
        var path = ValidatePath(preview.ConfigPath);
        using var operation = Lock(path);
        guard();
        var original = ReadBounded(path);
        if (Hash(original) != preview.SourceHash) throw new InvalidOperationException("設定在預覽後已變更，請重新預覽。");
        var actual = Preview(path, preview.MappingIndex);
        if (actual != preview) throw new InvalidOperationException("預覽內容不一致，停止修改。");
        var modified = SteamConfig.Patch(original, preview.MappingIndex);
        Directory.CreateDirectory(backupDirectory);
        var receipt = new BackupReceipt(1, path, Hash(original), Hash(modified), preview.MappingIndex, preview.Identity,
            DateTimeOffset.UtcNow, Convert.ToBase64String(original));
        var backup = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.backup.json");
        using (var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, receipt, new JsonSerializerOptions {WriteIndented = true});
            file.Flush(true);
        }
        ReplaceIfUnchanged(path, original, modified, guard);
        return backup;
    }

    public static void Restore(string receiptPath, Action? guard = null)
    {
        guard ??= EnsureSteamExited;
        var info = new FileInfo(receiptPath);
        if (info.Length > SteamConfig.MaxBytes * 2) throw new InvalidOperationException("備份大小異常。");
        var receipt = JsonSerializer.Deserialize<BackupReceipt>(File.ReadAllBytes(receiptPath)) ?? throw new FormatException("備份格式錯誤。");
        if (receipt.SchemaVersion != 1) throw new FormatException("不支援此備份版本。");
        var path = ValidatePath(receipt.ConfigPath);
        using var operation = Lock(path);
        guard();
        var original = Convert.FromBase64String(receipt.OriginalBase64);
        if (Hash(original) != receipt.OriginalHash || original.Length > SteamConfig.MaxBytes) throw new InvalidOperationException("備份內容驗證失敗。");
        // A receipt may only describe the exact, narrowly-scoped patch we support.
        if (Hash(SteamConfig.Patch(original, receipt.MappingIndex)) != receipt.AppliedHash)
            throw new InvalidOperationException("備份不是有效的 Guide 修改紀錄。");
        var current = ReadBounded(path);
        if (Hash(current) != receipt.AppliedHash)
            throw new InvalidOperationException("Steam 設定在套用後已改變。為保留後續設定，不自動覆蓋；原始備份仍保留。");
        ReplaceIfUnchanged(path, current, original, guard);
    }

    private static FileStream Lock(string path) => new(path + ".guidedeck.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static string ValidatePath(string path)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(path), "config.vdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("請選擇 Steam 的 config.vdf。");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("不修改連結形式的設定檔。");
        return path;
    }
    public static byte[] ReadBounded(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > SteamConfig.MaxBytes) throw new FormatException("設定檔過大。");
        var data = new byte[(int)file.Length];
        file.ReadExactly(data);
        return data;
    }
    private static void ReplaceIfUnchanged(string path, byte[] expected, byte[] replacement, Action guard)
    {
        var temp = path + ".guidedeck-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(replacement); file.Flush(true); }
            guard();
            if (!ReadBounded(path).AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("寫入前設定已變更，停止操作。");
            // Replace the existing file, preserving destination metadata/ACLs on Windows.
            // Unsupported filesystems fail instead of falling back to a delete+write.
            File.Replace(temp, path, null);
            if (!ReadBounded(path).AsSpan().SequenceEqual(replacement)) throw new IOException("寫入後驗證失敗；請保留備份並停止後續修改。");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
