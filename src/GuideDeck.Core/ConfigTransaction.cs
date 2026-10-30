using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace GuideDeck.Core;

public sealed record PatchPreview(string ConfigPath, string SourceHash, int MappingIndex, string Identity, string Before, string After);
public sealed record BackupReceipt(int SchemaVersion, string ConfigPath, string OriginalHash, string AppliedHash, int MappingIndex, string Identity, DateTimeOffset CreatedAt, string OriginalBase64);
public sealed record MappingBackup(int SchemaVersion, string ConfigPath, string Identity, string Before, string After,
    string BeforeMappingHash, string AfterMappingHash, string ConfigBeforeHash, string ConfigAfterHash, DateTimeOffset CreatedAt);
public sealed record RestorePreview(string ReceiptPath, string ReceiptHash, string ConfigPath, string SourceHash,
    string Identity, string CurrentMapping, string RestoredMapping, bool AlreadyRestored);

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
        // v2 never persists the rest of Steam's config (which may contain private data).
        var receipt = new MappingBackup(2, path, preview.Identity, preview.Before, preview.After,
            Hash(Encoding.UTF8.GetBytes(preview.Before)), Hash(Encoding.UTF8.GetBytes(preview.After)),
            Hash(original), Hash(modified), DateTimeOffset.UtcNow);
        var backup = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.backup.json");
        using (var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, receipt, new JsonSerializerOptions {WriteIndented = true});
            file.Flush(true);
        }
        ReplaceIfUnchanged(path, original, modified, guard);
        return backup;
    }

    public static RestorePreview PreviewRestore(string receiptPath)
    {
        receiptPath = Path.GetFullPath(receiptPath);
        var raw = ReadBounded(receiptPath, SteamConfig.MaxBytes * 2);
        using var doc = JsonDocument.Parse(raw);
        var version = doc.RootElement.GetProperty("SchemaVersion").GetInt32();
        string configPath, before, after, identity;
        if (version == 2)
        {
            var receipt = JsonSerializer.Deserialize<MappingBackup>(raw) ?? throw new FormatException("備份格式錯誤。");
            configPath = receipt.ConfigPath; before = receipt.Before; after = receipt.After; identity = receipt.Identity;
            if (Hash(Encoding.UTF8.GetBytes(before)) != receipt.BeforeMappingHash || Hash(Encoding.UTF8.GetBytes(after)) != receipt.AfterMappingHash)
                throw new InvalidOperationException("備份 mapping 雜湊驗證失敗。");
        }
        else if (version == 1)
        {
            var legacy = JsonSerializer.Deserialize<BackupReceipt>(raw) ?? throw new FormatException("舊版備份格式錯誤。");
            var original = Convert.FromBase64String(legacy.OriginalBase64);
            if (Hash(original) != legacy.OriginalHash || Hash(SteamConfig.Patch(original, legacy.MappingIndex)) != legacy.AppliedHash)
                throw new InvalidOperationException("舊版備份驗證失敗。");
            var mapping = SteamConfig.Inspect(original).Mappings.Single(m => m.Index == legacy.MappingIndex);
            configPath = legacy.ConfigPath; before = mapping.Before; after = mapping.After; identity = mapping.Identity;
            if (!string.Equals(identity, legacy.Identity, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("舊版備份 identity 不一致。");
        }
        else throw new FormatException("不支援此備份版本。");
        var edit = MappingEditor.RemoveGuide(before);
        if (!edit.ChangesGuide || edit.Modified != after || edit.Identity != identity)
            throw new InvalidOperationException("備份不是只移除 Guide 的有效紀錄。");
        var path = ValidatePath(configPath);
        var current = ReadBounded(path);
        var inspection = SteamConfig.Inspect(current);
        if (inspection.Problems.Count != 0) throw new InvalidOperationException(string.Join("\n", inspection.Problems));
        var candidate = inspection.Mappings.SingleOrDefault(m => m.Identity == identity) ?? throw new InvalidOperationException("找不到原來的控制器 mapping，停止還原。");
        bool restored = candidate.Before == before;
        if (!restored && candidate.Before != after)
            throw new InvalidOperationException("此控制器的 mapping 已有後續變更，停止還原以保留您的設定。");
        return new(receiptPath, Hash(raw), path, Hash(current), identity, candidate.Before, before, restored);
    }

    public static void Restore(string receiptPath, Action? guard = null) => Restore(PreviewRestore(receiptPath), guard);

    public static void Restore(RestorePreview preview, Action? guard = null)
    {
        guard ??= EnsureSteamExited;
        var path = ValidatePath(preview.ConfigPath);
        using var operation = Lock(path);
        guard();
        if (PreviewRestore(preview.ReceiptPath) != preview) throw new InvalidOperationException("還原預覽後設定或備份已變更，請重新預覽。");
        if (preview.AlreadyRestored) return;
        var current = ReadBounded(path);
        if (Hash(current) != preview.SourceHash) throw new InvalidOperationException("還原前設定已變更，請重新預覽。");
        var candidate = SteamConfig.Inspect(current).Mappings.Single(m => m.Identity == preview.Identity);
        var restored = SteamConfig.ReplaceMapping(current, candidate, preview.RestoredMapping);
        ReplaceIfUnchanged(path, current, restored, guard);
    }

    internal static FileStream Lock(string path) => new(path + ".guidedeck.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static string ValidatePath(string path)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(path), "config.vdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("請選擇 Steam 的 config.vdf。");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("不修改連結形式的設定檔。");
        return path;
    }
    public static byte[] ReadBounded(string path, int maxBytes = SteamConfig.MaxBytes)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maxBytes) throw new FormatException("檔案過大。");
        var data = new byte[(int)file.Length];
        file.ReadExactly(data);
        return data;
    }
    internal static void ReplaceIfUnchanged(string path, byte[] expected, byte[] replacement, Action guard)
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
