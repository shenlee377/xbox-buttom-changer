using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuideDeck.Core;

public sealed record ValidationReport(int SchemaVersion, Guid Id, DateTimeOffset ObservedAt, string Game, string Environment,
    TestScope Scope, string OsVersion, string? ConfigHash, string? MappingIdentity, string? MappingHash,
    IReadOnlyDictionary<string, Observation> Results, string Notes, string EvidenceSource = "user-observation");

public static class ValidationRules
{
    public static readonly IReadOnlyList<string> Keys = ValidationContract.Keys;
    public static readonly IReadOnlyList<string> Labels = ValidationContract.Labels;

    public static void Validate(ValidationReport report)
    {
        if (report.SchemaVersion != 2 || report.Id == Guid.Empty || report.EvidenceSource != "user-observation" ||
            string.IsNullOrWhiteSpace(report.Game) || string.IsNullOrWhiteSpace(report.Environment) || !Enum.IsDefined(report.Scope))
            throw new ArgumentException("請填寫遊戲／桌面與測試環境。");
        if (report.Game.Length > 256 || report.Environment.Length > 2048 || report.Notes.Length > 4096)
            throw new ArgumentException("測試描述過長。");
        ValidationContract.ValidateResults(report.Scope, report.Results, report.Notes);
    }

    public static string Evaluate(ValidationReport report, string? currentConfigHash)
    {
        Validate(report);
        if (report.ConfigHash == null || currentConfigHash == null || report.MappingIdentity == null || report.MappingHash == null)
            return "未綁定設定，僅供參考";
        if (report.ConfigHash != currentConfigHash) return "設定已變更，需重測";
        if (report.Results.Values.Contains(Observation.Failed)) return "此情境有失敗項目";
        if (report.Results.Values.Contains(Observation.Untested)) return "此情境尚未測完";
        return "此情境人工驗收通過";
    }
}

public static class AppFiles
{
    public static readonly JsonSerializerOptions Json = new() {WriteIndented = true, Converters = {new JsonStringEnumConverter()}};

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, value, Json); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static T? Load<T>(string path, int maxBytes = 1024 * 1024) =>
        JsonSerializer.Deserialize<T>(ConfigTransaction.ReadBounded(path, maxBytes), Json);

    public static string SaveReport(string directory, ValidationReport report)
    {
        ValidationRules.Validate(report);
        var path = Path.Combine(directory, report.Id + ".json");
        if (File.Exists(path)) throw new InvalidOperationException("這份觀察已儲存，請建立新紀錄。");
        Save(path, report);
        return path;
    }
}
