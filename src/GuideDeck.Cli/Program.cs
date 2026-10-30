using System.Text.Json;
using GuideDeck.Core;

try
{
    if (args.Length == 0) throw new ArgumentException("用法：inspect <config.vdf> | preview <config.vdf> <index> <plan.json> | apply <plan.json> --experimental | restore <backup.json>");
    switch (args[0])
    {
        case "inspect" when args.Length == 2:
            Console.WriteLine(JsonSerializer.Serialize(SteamConfig.Inspect(ConfigTransaction.ReadBounded(args[1])), new JsonSerializerOptions {WriteIndented = true})); break;
        case "preview" when args.Length == 4:
            var preview = ConfigTransaction.Preview(args[1], int.Parse(args[2]));
            File.WriteAllText(args[3], JsonSerializer.Serialize(preview, new JsonSerializerOptions {WriteIndented = true}));
            Console.WriteLine($"只會移除 Guide；Steam 實際效果仍待測試。\n- {preview.Before}\n+ {preview.After}\n預覽已儲存：{args[3]}"); break;
        case "apply" when args.Length == 3 && args[2] == "--experimental":
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("實際套用只在 Windows 開放；Linux 可做 inspect／preview 與測試。");
            var plan = JsonSerializer.Deserialize<PatchPreview>(File.ReadAllText(args[1])) ?? throw new FormatException("預覽格式錯誤。");
            Console.WriteLine("備份：" + ConfigTransaction.Apply(plan, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuideDeck", "Backups")));
            Console.WriteLine("設定已修改，效果尚未驗證。請重啟 Steam，測試 Game Bar、Guide、Shift+Tab 及普通手把操作。"); break;
        case "restore" when args.Length == 2:
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("還原只在 Windows 開放。");
            ConfigTransaction.Restore(args[1]); Console.WriteLine("已還原。"); break;
        default: throw new ArgumentException("參數不正確。執行不帶參數的指令可查看用法。");
    }
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
