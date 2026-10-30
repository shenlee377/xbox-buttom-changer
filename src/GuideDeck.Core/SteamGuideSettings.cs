using System.Text.Json;

namespace GuideDeck.Core;

public sealed record GuideSetting(string Key, string? Location, string? Value, int Offset, int Length)
{
    public string Label => Key == SteamGuideSettings.FocusKey ? "導覽鍵將使 Steam 視窗顯示在前" : "為控制器啟用導覽鍵組合鍵";
    public string State => Value == "0" ? "已儲存為關閉（效果待實測）" : Value == "1" ? "已儲存為開啟" : "此檔未明確儲存，狀態未知";
}
public sealed record GuideSettingChange(string Key, string Location, string Before, string After);
public sealed record GuideSettingsPlan(string ConfigPath, string SourceHash, GuideSettingChange[] Changes);
public sealed record GuideSettingsBackup(int SchemaVersion, string Kind, string ConfigPath, GuideSettingChange[] Changes,
    string ChangesHash, string ConfigBeforeHash, string ConfigAfterHash, DateTimeOffset CreatedAt);
public sealed record GuideSettingsRestorePlan(string ReceiptPath, string ReceiptHash, string ConfigPath, string SourceHash,
    GuideSettingChange[] Changes, bool AlreadyRestored);

public static class SteamGuideSettings
{
    public const string FocusKey = "Controller_CheckGuideButton";
    public const string ChordKey = "SteamController_Enable_Chord";
    internal static readonly string[] Keys = [FocusKey, ChordKey];
    public static IReadOnlyList<GuideSetting> Inspect(byte[] bytes) => SteamConfig.InspectGuideSettings(bytes);

    internal static byte[] Patch(byte[] bytes, IEnumerable<GuideSettingChange> changes)
    {
        var settings = Inspect(bytes); var result = (byte[])bytes.Clone();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            if (!Keys.Contains(change.Key) || !unique.Add(change.Key) || change.Before is not ("0" or "1") || change.After is not ("0" or "1"))
                throw new FormatException("Guide 修改內容不合法。");
            var current = settings.Single(s => s.Key == change.Key);
            if (current.Location != change.Location || current.Value != change.Before || current.Length != 1)
                throw new InvalidOperationException("Guide 設定與預覽不一致，請重新檢查。");
            result[current.Offset] = (byte)change.After[0];
        }
        _ = Inspect(result); return result;
    }
}

public static class GuideSettingsTransaction
{
    public static GuideSettingsPlan Preview(string path)
    {
        path = ValidatePath(path); var bytes = ConfigTransaction.ReadBounded(path);
        var changes = SteamGuideSettings.Inspect(bytes).Where(s => s.Value == "1")
            .Select(s => new GuideSettingChange(s.Key, s.Location!, "1", "0")).ToArray();
        return new(path, ConfigTransaction.Hash(bytes), changes);
    }
    public static string Apply(GuideSettingsPlan plan, string backupDirectory, Action? guard = null)
    {
        guard ??= ConfigTransaction.EnsureSteamExited;
        var path = ValidatePath(plan.ConfigPath);
        using var operation = ConfigTransaction.Lock(path); guard();
        var actual = Preview(path);
        if (actual.ConfigPath != plan.ConfigPath || actual.SourceHash != plan.SourceHash || !actual.Changes.SequenceEqual(plan.Changes) || plan.Changes.Length == 0)
            throw new InvalidOperationException("Guide 設定預覽已過期或沒有可修改的開關，請重新檢查。");
        var before = ConfigTransaction.ReadBounded(path);
        if (ConfigTransaction.Hash(before) != plan.SourceHash) throw new InvalidOperationException("寫入前設定已變更。");
        var after = SteamGuideSettings.Patch(before, plan.Changes);
        Directory.CreateDirectory(backupDirectory);
        var receipt = new GuideSettingsBackup(1, "SteamGuideSettings", path, plan.Changes, ChangesHash(plan.Changes),
            ConfigTransaction.Hash(before), ConfigTransaction.Hash(after), DateTimeOffset.UtcNow);
        var receiptPath = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.guide-settings.json");
        using (var file = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, receipt, new JsonSerializerOptions {WriteIndented = true}); file.Flush(true); }
        ConfigTransaction.ReplaceIfUnchanged(path, before, after, guard);
        return receiptPath;
    }
    public static GuideSettingsRestorePlan PreviewRestore(string receiptPath)
    {
        receiptPath = Path.GetFullPath(receiptPath);
        var raw = ConfigTransaction.ReadBounded(receiptPath, 65536);
        var backup = JsonSerializer.Deserialize<GuideSettingsBackup>(raw) ?? throw new FormatException("Guide 備份格式錯誤。");
        if (backup.SchemaVersion != 1 || backup.Kind != "SteamGuideSettings" || backup.Changes == null ||
            backup.Changes.Length is < 1 or > 2 || backup.ChangesHash != ChangesHash(backup.Changes) ||
            backup.Changes.Select(c => c.Key).Distinct().Count() != backup.Changes.Length ||
            backup.Changes.Any(c => !SteamGuideSettings.Keys.Contains(c.Key) || c.Before != "1" || c.After != "0"))
            throw new FormatException("Guide 備份驗證失敗。");
        var path = ValidatePath(backup.ConfigPath); var current = ConfigTransaction.ReadBounded(path);
        var settings = SteamGuideSettings.Inspect(current); var restore = new List<GuideSettingChange>();
        foreach (var saved in backup.Changes)
        {
            var setting = settings.Single(s => s.Key == saved.Key);
            if (setting.Location != saved.Location || setting.Value == null) throw new InvalidOperationException("Guide 設定路徑已改變，停止還原。");
            if (setting.Value == saved.Before) continue;
            if (setting.Value != saved.After) throw new InvalidOperationException("Guide 設定已有其他變更，停止還原。");
            restore.Add(new(saved.Key, saved.Location, saved.After, saved.Before));
        }
        return new(receiptPath, ConfigTransaction.Hash(raw), path, ConfigTransaction.Hash(current), restore.ToArray(), restore.Count == 0);
    }
    public static void Restore(GuideSettingsRestorePlan plan, Action? guard = null)
    {
        guard ??= ConfigTransaction.EnsureSteamExited; var path = ValidatePath(plan.ConfigPath);
        using var operation = ConfigTransaction.Lock(path); guard();
        var actual = PreviewRestore(plan.ReceiptPath);
        if (actual.ReceiptHash != plan.ReceiptHash || actual.ConfigPath != plan.ConfigPath || actual.SourceHash != plan.SourceHash ||
            actual.AlreadyRestored != plan.AlreadyRestored || !actual.Changes.SequenceEqual(plan.Changes))
            throw new InvalidOperationException("還原預覽已過期，請重新檢查。");
        if (actual.AlreadyRestored) return;
        var current = ConfigTransaction.ReadBounded(path);
        if (ConfigTransaction.Hash(current) != plan.SourceHash) throw new InvalidOperationException("還原前設定已改變。");
        ConfigTransaction.ReplaceIfUnchanged(path, current, SteamGuideSettings.Patch(current, plan.Changes), guard);
    }
    private static string ChangesHash(GuideSettingChange[] changes) => ConfigTransaction.Hash(JsonSerializer.SerializeToUtf8Bytes(changes));
    private static string ValidatePath(string path)
    {
        path = Path.GetFullPath(path);
        if (!new[] {"config.vdf", "localconfig.vdf"}.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("請選擇 Steam 的 config.vdf 或帳號 localconfig.vdf。");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("不修改連結形式的設定檔。");
        return path;
    }
}
