using System.Text;
using System.Text.Json;
using GuideDeck.Core;

const string guid = "03005d085e040000000b000011056800";
const string line = guid + ",Xbox Elite,a:b0,b:b1,leftx:a0,lefty:a1,guide:b5,paddle1:b15,crc:085d,platform:Windows,";
int passed = 0, failed = 0;
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (Exception) { return; } throw new Exception("Expected rejection"); }
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
byte[] Config(string mapping = line, string newline = "\n") => Encoding.UTF8.GetBytes("// guide:b99, untouched comment" + newline +
    "\"InstallConfigStore\"" + newline + "{" + newline + "\t\"SDL_GamepadBind\"\t\t\"" + mapping + "\"" + newline +
    "\t\"Software\" { \"Valve\" { \"Steam\" { \"OverlayEnabled\" \"1\" \"SteamInput\" \"unchanged\" } } }" + newline + "}" + newline);
void WithConfig(Action<string, string> test)
{
    var dir = Path.Combine(Path.GetTempPath(), "guidedeck-tests-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try { var path = Path.Combine(dir, "config.vdf"); File.WriteAllBytes(path, Config()); test(path, dir); }
    finally { Directory.Delete(dir, true); }
}
Test("only Guide changes, including paddles and metadata", () => {
    var edit = MappingEditor.RemoveGuide(line);
    Check(edit.Modified == line.Replace("guide:b5,", "")); Check(edit.RemovedBinding == "guide:b5");
});
Test("idempotent mapping editor", () => { var first = MappingEditor.RemoveGuide(line); Check(!MappingEditor.RemoveGuide(first.Modified).ChangesGuide); });
Test("final Guide without trailing comma", () => Check(MappingEditor.RemoveGuide(guid + ",Pad,a:b0,b:b1,leftx:a0,lefty:a1,guide:b5").Modified.EndsWith("lefty:a1")));
Test("Guide-like name and metadata are preserved", () => { var s = line.Replace("Xbox Elite", "guide:b77").Replace("crc:085d", "note:guide:b99"); Check(MappingEditor.RemoveGuide(s).Modified == s.Replace("guide:b5,", "")); });
Test("different device CRCs remain distinct", () => Check(MappingEditor.RemoveGuide(line).Identity != MappingEditor.RemoveGuide(line.Replace("crc:085d", "crc:1234")).Identity));
Test("malformed GUID rejected", () => Throws(() => MappingEditor.RemoveGuide(line[1..])));
Test("duplicate Guide rejected", () => Throws(() => MappingEditor.RemoveGuide(line + "guide:b6,")));
Test("unknown Guide binding rejected", () => Throws(() => MappingEditor.RemoveGuide(line.Replace("guide:b5", "guide:h0.1"))));
Test("non-Windows platform rejected", () => Throws(() => MappingEditor.RemoveGuide(line.Replace("Windows", "Linux"))));
Test("missing ordinary bindings rejected", () => Throws(() => MappingEditor.RemoveGuide(line.Replace("leftx:a0,", ""))));
Test("multiline clipboard input rejected", () => Throws(() => MappingEditor.RemoveGuide(line + "\n" + line)));
Test("VDF patch preserves every byte outside selected mapping", () => {
    var bytes = Config(); Check(SteamConfig.Inspect(bytes).Problems.Count == 0);
    var patched = SteamConfig.Patch(bytes, 0);
    Check(patched.SequenceEqual(Config(line.Replace("guide:b5,", ""))));
});
Test("UTF8 BOM CRLF Unicode and escaped unrelated values preserved", () => {
    var bytes = new byte[] {239,187,191}.Concat(Config(line.Replace("Xbox Elite", "玩家手把"), "\r\n")).ToArray();
    var text = Encoding.UTF8.GetString(bytes).Replace("\"unchanged\"", "\"path\\\\with\\\"quote\""); bytes = Encoding.UTF8.GetBytes(text);
    Check(SteamConfig.Inspect(bytes).Mappings[0].Name == "玩家手把");
    Check(SteamConfig.Patch(bytes, 0).SequenceEqual(Encoding.UTF8.GetBytes(text.Replace("guide:b5,", ""))));
});
Test("selected controller only", () => {
    var second = line.Replace("crc:085d", "crc:1111"); var bytes = Config(line + "\n" + second);
    Check(SteamConfig.Patch(bytes, 1).SequenceEqual(Config(line + "\n" + second.Replace("guide:b5,", ""))));
});
Test("duplicate identity blocks all writes", () => { var bytes = Config(line + "\n" + line); Check(SteamConfig.Inspect(bytes).Problems.Count > 0); Throws(() => SteamConfig.Patch(bytes, 0)); });
Test("wrong VDF path ignored", () => { var bytes = Encoding.UTF8.GetBytes("\"Other\" { \"SDL_GamepadBind\" \"" + line + "\" }"); Check(SteamConfig.Inspect(bytes).Mappings.Count == 0); });
Test("missing mapping does not imply blocked Guide", () => Check(SteamConfig.Inspect(Encoding.UTF8.GetBytes("\"InstallConfigStore\" {} ")).Problems.Count > 0));
Test("duplicate target properties rejected", () => {
    var bytes = Encoding.UTF8.GetBytes("\"InstallConfigStore\" { \"SDL_GamepadBind\" \"" + line + "\" \"SDL_GamepadBind\" \"" + line + "\" }");
    Check(SteamConfig.Inspect(bytes).Problems.Count > 0);
});
Test("malformed VDF rejected", () => Throws(() => SteamConfig.Inspect(Config()[..^3])));
Test("unclosed string rejected", () => Throws(() => SteamConfig.Inspect(Encoding.UTF8.GetBytes("\"oops"))));
Test("invalid UTF8 rejected", () => Throws(() => SteamConfig.Inspect(new byte[] {0xFF,0xFE,0x00})));
Test("oversized config rejected", () => Throws(() => SteamConfig.Inspect(new byte[SteamConfig.MaxBytes + 1])));
Test("preview is read-only", () => WithConfig((path, dir) => { var bytes = File.ReadAllBytes(path); _ = ConfigTransaction.Preview(path, 0); Check(File.ReadAllBytes(path).SequenceEqual(bytes)); Check(Directory.GetFiles(dir).Length == 1); }));
Test("apply backup and restore exact bytes", () => WithConfig((path, dir) => {
    var bytes = File.ReadAllBytes(path); var preview = ConfigTransaction.Preview(path, 0);
    var receipt = ConfigTransaction.Apply(preview, Path.Combine(dir, "backup"), () => {});
    Check(File.Exists(receipt)); Check(File.ReadAllBytes(path).SequenceEqual(SteamConfig.Patch(bytes, 0)));
    ConfigTransaction.Restore(receipt, () => {}); Check(File.ReadAllBytes(path).SequenceEqual(bytes));
}));
Test("Steam running guard blocks apply without backup or change", () => WithConfig((path, dir) => {
    var preview = ConfigTransaction.Preview(path, 0); Throws(() => ConfigTransaction.Apply(preview, Path.Combine(dir, "backup"), () => throw new Exception("Steam running")));
    Check(ConfigTransaction.Hash(File.ReadAllBytes(path)) == preview.SourceHash); Check(!Directory.Exists(Path.Combine(dir, "backup")));
}));
Test("stale preview cannot overwrite user changes", () => WithConfig((path, dir) => {
    var preview = ConfigTransaction.Preview(path, 0); File.AppendAllText(path, "\n// user change"); var current = File.ReadAllBytes(path);
    Throws(() => ConfigTransaction.Apply(preview, dir, () => {})); Check(File.ReadAllBytes(path).SequenceEqual(current));
}));
Test("tampered preview cannot broaden mutation", () => WithConfig((path, dir) => {
    var preview = ConfigTransaction.Preview(path, 0); Throws(() => ConfigTransaction.Apply(preview with {After = "evil"}, dir, () => {})); Check(File.ReadAllBytes(path).SequenceEqual(Config()));
}));
Test("late Steam launch aborts write and keeps recovery receipt", () => WithConfig((path, dir) => {
    int guards = 0; var preview = ConfigTransaction.Preview(path, 0);
    Throws(() => ConfigTransaction.Apply(preview, Path.Combine(dir, "backup"), () => { if (++guards == 2) throw new Exception("Steam started"); }));
    Check(File.ReadAllBytes(path).SequenceEqual(Config())); Check(Directory.GetFiles(Path.Combine(dir, "backup")).Length == 1); Check(Directory.GetFiles(dir, "*.tmp").Length == 0);
}));
Test("restore refuses subsequent Steam/user changes", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {}); File.AppendAllText(path, "// later setting");
    var current = File.ReadAllBytes(path); Throws(() => ConfigTransaction.Restore(receipt, () => {})); Check(File.ReadAllBytes(path).SequenceEqual(current));
}));
Test("tampered backup is rejected", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    var record = JsonSerializer.Deserialize<BackupReceipt>(File.ReadAllText(receipt))!;
    File.WriteAllText(receipt, JsonSerializer.Serialize(record with {OriginalHash = "bad"})); Throws(() => ConfigTransaction.Restore(receipt, () => {}));
}));
Test("cooperating operations cannot overlap", () => WithConfig((path, dir) => {
    using var locked = new FileStream(path + ".guidedeck.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Throws(() => ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {}));
}));
Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
