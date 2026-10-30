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
Test("restore preserves unrelated subsequent Steam/user changes", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {}); File.AppendAllText(path, "// later setting");
    ConfigTransaction.Restore(receipt, () => {}); Check(File.ReadAllBytes(path).SequenceEqual(Config().Concat(Encoding.UTF8.GetBytes("// later setting"))));
}));
Test("tampered backup is rejected", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    var record = JsonSerializer.Deserialize<MappingBackup>(File.ReadAllText(receipt))!;
    File.WriteAllText(receipt, JsonSerializer.Serialize(record with {BeforeMappingHash = "bad"})); Throws(() => ConfigTransaction.Restore(receipt, () => {}));
}));
Test("cooperating operations cannot overlap", () => WithConfig((path, dir) => {
    using var locked = new FileStream(path + ".guidedeck.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Throws(() => ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {}));
}));
Test("duplicate root container rejected", () => Check(SteamConfig.Inspect(Config().Concat(Encoding.UTF8.GetBytes("\"InstallConfigStore\" {} ")).ToArray()).Problems.Count > 0));
Test("object/string target collision rejected", () => {
    var text = Encoding.UTF8.GetString(Config()).Replace("\"SDL_GamepadBind\"", "\"SDL_GamepadBind\" {} \"SDL_GamepadBind\"");
    Check(SteamConfig.Inspect(Encoding.UTF8.GetBytes(text)).Problems.Count > 0);
});
Test("duplicate ordinary binding rejected", () => Throws(() => MappingEditor.RemoveGuide(line + "a:b9,")));
Test("CRC case normalized", () => Check(MappingEditor.RemoveGuide(line.Replace("085d", "085D")).Identity == MappingEditor.RemoveGuide(line).Identity));
Test("invalid CRC rejected", () => Throws(() => MappingEditor.RemoveGuide(line.Replace("crc:085d", "crc:../x"))));
Test("new receipt contains selected mapping only", () => WithConfig((path, dir) => {
    File.AppendAllText(path, "\n// private config fixture: not-a-real-secret");
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    var raw = File.ReadAllText(receipt); Check(!raw.Contains("not-a-real-secret")); Check(!raw.Contains("OriginalBase64"));
    Check(JsonSerializer.Deserialize<MappingBackup>(raw)!.SchemaVersion == 2);
}));
Test("legacy v1 receipt remains restorable without overwriting unrelated settings", () => WithConfig((path, dir) => {
    var original = Config(); var modified = SteamConfig.Patch(original, 0);
    var receipt = new BackupReceipt(1, path, ConfigTransaction.Hash(original), ConfigTransaction.Hash(modified), 0,
        MappingEditor.RemoveGuide(line).Identity, DateTimeOffset.UtcNow, Convert.ToBase64String(original));
    var file = Path.Combine(dir, "old.backup.json"); File.WriteAllText(file, JsonSerializer.Serialize(receipt));
    File.WriteAllBytes(path, modified.Concat(Encoding.UTF8.GetBytes("// newer preference")).ToArray());
    ConfigTransaction.Restore(file, () => {});
    Check(File.ReadAllBytes(path).SequenceEqual(original.Concat(Encoding.UTF8.GetBytes("// newer preference"))));
}));
Test("restore follows identity when controller lines reorder", () => WithConfig((path, dir) => {
    var second = line.Replace("crc:085d", "crc:1111"); File.WriteAllBytes(path, Config(line + "\n" + second));
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    File.WriteAllBytes(path, Config(second + "\n" + line.Replace("guide:b5,", "")));
    ConfigTransaction.Restore(receipt, () => {}); Check(File.ReadAllBytes(path).SequenceEqual(Config(second + "\n" + line)));
}));
Test("restore refuses modified normal buttons on same controller", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    File.WriteAllText(path, File.ReadAllText(path).Replace("a:b0", "a:b9")); var current = File.ReadAllBytes(path);
    Throws(() => ConfigTransaction.Restore(receipt, () => {})); Check(File.ReadAllBytes(path).SequenceEqual(current));
}));
Test("restore does not recreate a missing controller", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    File.WriteAllBytes(path, Config(line.Replace("crc:085d", "crc:1111"))); Throws(() => ConfigTransaction.Restore(receipt, () => {}));
}));
Test("restore is idempotent", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    ConfigTransaction.Restore(receipt, () => {}); Check(ConfigTransaction.PreviewRestore(receipt).AlreadyRestored);
    ConfigTransaction.Restore(receipt, () => {}); Check(File.ReadAllBytes(path).SequenceEqual(Config()));
}));
Test("stale restore preview rejected", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    var plan = ConfigTransaction.PreviewRestore(receipt); File.AppendAllText(path, "// changed after preview");
    Throws(() => ConfigTransaction.Restore(plan, () => {}));
}));
Test("restore preview detects receipt modification", () => WithConfig((path, dir) => {
    var receipt = ConfigTransaction.Apply(ConfigTransaction.Preview(path, 0), dir, () => {});
    var plan = ConfigTransaction.PreviewRestore(receipt); File.AppendAllText(receipt, " ");
    Throws(() => ConfigTransaction.Restore(plan, () => {}));
}));
ValidationReport Report() => new(2, Guid.NewGuid(), DateTimeOffset.UtcNow, "Game", "Xbox USB; Steam test-build", TestScope.SteamGame,
    "test OS", "config-hash", "identity", "mapping-hash", ValidationRules.Keys.ToDictionary(k => k, _ => Observation.Passed), "");
Test("manual report pass limited to exact config", () => Check(ValidationRules.Evaluate(Report(), "config-hash") == "此情境人工驗收通過"));
Test("changed config marks reports stale", () => Check(ValidationRules.Evaluate(Report(), "new-hash") == "設定已變更，需重測"));
Test("unbound mapping cannot produce pass", () => Check(ValidationRules.Evaluate(Report() with {MappingHash = null}, "config-hash") == "未綁定設定，僅供參考"));
Test("unknown observation cannot produce pass", () => { var r = Report(); var d = new Dictionary<string, Observation>(r.Results) {["haptics"] = Observation.Untested}; Check(ValidationRules.Evaluate(r with {Results = d}, "config-hash") == "此情境尚未測完"); });
Test("failure takes precedence over incomplete", () => { var r = Report(); var d = new Dictionary<string, Observation>(r.Results) {["gamebar"] = Observation.Failed, ["haptics"] = Observation.Untested}; Check(ValidationRules.Evaluate(r with {Results = d}, "config-hash") == "此情境有失敗項目"); });
Test("Steam overlay checks cannot be skipped in Steam game", () => { var r = Report(); var d = new Dictionary<string, Observation>(r.Results) {["overlay-shortcut"] = Observation.NotApplicable}; Throws(() => ValidationRules.Validate(r with {Results = d, Notes = "skip"})); });
Test("desktop overlay check may be inapplicable with reason", () => { var r = Report(); var d = new Dictionary<string, Observation>(r.Results) {["overlay-shortcut"] = Observation.NotApplicable}; ValidationRules.Validate(r with {Scope = TestScope.DesktopUnfocused, Results = d, Notes = "Desktop has no game overlay"}); });
Test("not applicable requires explanation", () => { var r = Report(); var d = new Dictionary<string, Observation>(r.Results) {["haptics"] = Observation.NotApplicable}; Throws(() => ValidationRules.Validate(r with {Results = d})); });
Test("empty observation report rejected", () => Throws(() => ValidationRules.Validate(Report() with {Results = ValidationRules.Keys.ToDictionary(k => k, _ => Observation.Untested)})));
Test("report file roundtrip and duplicate prevention", () => WithConfig((_, dir) => { var r = Report(); var file = AppFiles.SaveReport(dir, r); Check(AppFiles.Load<ValidationReport>(file)!.Id == r.Id); Throws(() => AppFiles.SaveReport(dir, r)); }));
Test("system appearance follows light apps preference", () => Check(ThemePolicy.Resolve(ThemeMode.System, true, false) == ThemeVariant.Light));
Test("system appearance follows dark apps preference", () => Check(ThemePolicy.Resolve(ThemeMode.System, false, false) == ThemeVariant.Dark));
Test("manual light overrides dark system", () => Check(ThemePolicy.Resolve(ThemeMode.Light, false, false) == ThemeVariant.Light));
Test("manual dark overrides light system", () => Check(ThemePolicy.Resolve(ThemeMode.Dark, true, false) == ThemeVariant.Dark));
Test("high contrast overrides every appearance preference", () => { foreach (var mode in Enum.GetValues<ThemeMode>()) Check(ThemePolicy.Resolve(mode, true, true) == ThemeVariant.HighContrast); });
Test("unknown stored theme normalizes to system", () => Check(ThemePolicy.Normalize((ThemeMode)99) == ThemeMode.System && ThemePolicy.Resolve((ThemeMode)99, false, false) == ThemeVariant.Dark));
byte[] GuideConfig(string focus = "1", string chord = "1", string newline = "\n") => Encoding.UTF8.GetBytes(
    "\"UserLocalConfigStore\"" + newline + "{" + newline + "\"Software\" { \"Valve\" { \"Steam\" {" + newline +
    "\"Controller_CheckGuideButton\" \"" + focus + "\"" + newline + "\"SteamController_Enable_Chord\" \"" + chord + "\"" + newline +
    "\"OverlayEnabled\" \"1\" \"SteamController_XBoxSupport\" \"1\" \"Note\" \"玩家\\\\手把\" } } }" + newline + "}" + newline);
void WithGuideConfig(Action<string, string> test)
{
    var dir = Path.Combine(Path.GetTempPath(), "guidedeck-policy-tests-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try { var path = Path.Combine(dir, "localconfig.vdf"); File.WriteAllBytes(path, GuideConfig()); test(path, dir); }
    finally { Directory.Delete(dir, true); }
}
Test("Guide switches read without SDL mapping", () => Check(SteamGuideSettings.Inspect(GuideConfig()).All(s => s.Value == "1")));
Test("Guide missing fields remain unknown, not disabled", () => {
    var missing = SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes("\"UserLocalConfigStore\" {}"));
    Check(missing.Count == 2 && missing.All(s => s.Value == null && s.Location == null));
});
Test("Guide preview never adds missing focus key", () => WithGuideConfig((path, dir) => {
    var text = Encoding.UTF8.GetString(GuideConfig()).Replace("\"Controller_CheckGuideButton\" \"1\"", "// focus missing");
    File.WriteAllText(path, text); var plan = GuideSettingsTransaction.Preview(path); Check(plan.Changes.Length == 1);
    _ = GuideSettingsTransaction.Apply(plan, dir, () => {});
    Check(File.ReadAllText(path) == text.Replace("\"SteamController_Enable_Chord\" \"1\"", "\"SteamController_Enable_Chord\" \"0\""));
}));
Test("Guide patch preserves BOM CRLF Unicode escapes overlay and Steam Input", () => WithGuideConfig((path, dir) => {
    var original = new byte[] {239,187,191}.Concat(GuideConfig(newline: "\r\n")).ToArray(); File.WriteAllBytes(path, original);
    var plan = GuideSettingsTransaction.Preview(path); Check(plan.Changes.Length == 2);
    var receipt = GuideSettingsTransaction.Apply(plan, dir, () => {});
    var expected = Encoding.UTF8.GetString(original).Replace("\"Controller_CheckGuideButton\" \"1\"", "\"Controller_CheckGuideButton\" \"0\"")
        .Replace("\"SteamController_Enable_Chord\" \"1\"", "\"SteamController_Enable_Chord\" \"0\"");
    Check(File.ReadAllBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(expected)));
    Check(!File.ReadAllText(receipt).Contains("Note") && !File.ReadAllText(receipt).Contains("OverlayEnabled"));
    GuideSettingsTransaction.Restore(GuideSettingsTransaction.PreviewRestore(receipt), () => {});
    Check(File.ReadAllBytes(path).SequenceEqual(original));
}));
Test("Guide only enabled switches enter the plan", () => WithGuideConfig((path, _) => {
    File.WriteAllBytes(path, GuideConfig(focus: "0")); var plan = GuideSettingsTransaction.Preview(path);
    Check(plan.Changes.Length == 1 && plan.Changes[0].Key == SteamGuideSettings.ChordKey);
}));
Test("Guide disabled values cannot create a no-op backup", () => WithGuideConfig((path, dir) => {
    File.WriteAllBytes(path, GuideConfig("0", "0")); var plan = GuideSettingsTransaction.Preview(path);
    Check(plan.Changes.Length == 0); Throws(() => GuideSettingsTransaction.Apply(plan, dir, () => {}));
    Check(Directory.GetFiles(dir).Count(p => p.EndsWith(".guide-settings.json")) == 0);
}));
Test("Guide duplicate Boolean rejected case insensitively", () => Throws(() => SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes(
    Encoding.UTF8.GetString(GuideConfig()).Replace("\"Note\"", "\"controller_checkguidebutton\" \"0\" \"Note\"")))));
Test("Guide duplicate ancestor rejected even when second object empty", () => Throws(() => SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes(
    Encoding.UTF8.GetString(GuideConfig()).Replace("\"Software\"", "\"Software\" {} \"Software\"")))));
Test("Guide unknown root rejected", () => Throws(() => SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(GuideConfig()).Replace("UserLocalConfigStore", "Other")))));
Test("Guide unknown value rejected", () => Throws(() => SteamGuideSettings.Inspect(GuideConfig(chord: "true"))));
Test("Guide unknown nested path rejected", () => Throws(() => SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(GuideConfig()).Replace("\"Valve\"", "\"SomeApp\"")))));
Test("Guide object/string collision rejected", () => Throws(() => SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes(
    Encoding.UTF8.GetString(GuideConfig()).Replace("\"Note\"", "\"SteamController_Enable_Chord\" {} \"Note\"")))));
Test("Guide install store root switches supported", () => Check(SteamGuideSettings.Inspect(Encoding.UTF8.GetBytes(
    "\"InstallConfigStore\" { \"Controller_CheckGuideButton\" \"1\" }")).Single(s => s.Key == SteamGuideSettings.FocusKey).Value == "1"));
Test("Guide differently cased keys retain their original spelling", () => WithGuideConfig((path, dir) => {
    var original = Encoding.UTF8.GetString(GuideConfig()).Replace("Controller_CheckGuideButton", "controller_checkguidebutton"); File.WriteAllText(path, original);
    _ = GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {});
    Check(File.ReadAllText(path).Contains("\"controller_checkguidebutton\" \"0\""));
}));
Test("Guide stale plan rejects external changes", () => WithGuideConfig((path, dir) => {
    var plan = GuideSettingsTransaction.Preview(path); File.AppendAllText(path, "// Steam wrote settings"); var current = File.ReadAllBytes(path);
    Throws(() => GuideSettingsTransaction.Apply(plan, dir, () => {})); Check(current.SequenceEqual(File.ReadAllBytes(path)));
}));
Test("Guide tampered plan cannot change unrelated settings", () => WithGuideConfig((path, dir) => {
    var plan = GuideSettingsTransaction.Preview(path); plan.Changes[0] = plan.Changes[0] with {Key = "OverlayEnabled"};
    Throws(() => GuideSettingsTransaction.Apply(plan, dir, () => {})); Check(File.ReadAllBytes(path).SequenceEqual(GuideConfig()));
}));
Test("Guide running Steam guard blocks mutation and backup", () => WithGuideConfig((path, dir) => {
    Throws(() => GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => throw new Exception("Steam running")));
    Check(File.ReadAllBytes(path).SequenceEqual(GuideConfig())); Check(!Directory.EnumerateFiles(dir, "*.guide-settings.json").Any());
}));
Test("Guide late Steam launch aborts atomic replacement", () => WithGuideConfig((path, dir) => {
    int guarded = 0;
    Throws(() => GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => { if (++guarded == 2) throw new Exception("Steam started"); }));
    Check(File.ReadAllBytes(path).SequenceEqual(GuideConfig())); Check(Directory.EnumerateFiles(dir, "*.guide-settings.json").Count() == 1);
    Check(!Directory.EnumerateFiles(dir, "*.tmp").Any());
}));
Test("Guide restore preserves unrelated later settings", () => WithGuideConfig((path, dir) => {
    var receipt = GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {}); File.AppendAllText(path, "// unrelated later setting");
    GuideSettingsTransaction.Restore(GuideSettingsTransaction.PreviewRestore(receipt), () => {});
    Check(File.ReadAllBytes(path).SequenceEqual(GuideConfig().Concat(Encoding.UTF8.GetBytes("// unrelated later setting"))));
    var again = GuideSettingsTransaction.PreviewRestore(receipt); Check(again.AlreadyRestored); GuideSettingsTransaction.Restore(again, () => {});
}));
Test("Guide restore handles one switch already restored", () => WithGuideConfig((path, dir) => {
    var receipt = GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {});
    File.WriteAllBytes(path, GuideConfig("1", "0")); var plan = GuideSettingsTransaction.PreviewRestore(receipt); Check(plan.Changes.Length == 1);
    GuideSettingsTransaction.Restore(plan, () => {}); Check(File.ReadAllBytes(path).SequenceEqual(GuideConfig()));
}));
Test("Guide restore rejects disappeared switch", () => WithGuideConfig((path, dir) => {
    var receipt = GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {});
    File.WriteAllText(path, "\"UserLocalConfigStore\" {}"); Throws(() => GuideSettingsTransaction.PreviewRestore(receipt));
}));
Test("Guide stale restore and edited backup rejected", () => WithGuideConfig((path, dir) => {
    var receipt = GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {});
    var plan = GuideSettingsTransaction.PreviewRestore(receipt); File.AppendAllText(path, "// later"); Throws(() => GuideSettingsTransaction.Restore(plan, () => {}));
    var backup = JsonSerializer.Deserialize<GuideSettingsBackup>(File.ReadAllText(receipt))!;
    File.WriteAllText(receipt, JsonSerializer.Serialize(backup with {ChangesHash = "bad"})); Throws(() => GuideSettingsTransaction.PreviewRestore(receipt));
}));
Test("Guide repair touches only selected account file", () => WithGuideConfig((path, dir) => {
    var other = Path.Combine(dir, "other-account"); Directory.CreateDirectory(other); var otherFile = Path.Combine(other, "localconfig.vdf"); File.WriteAllBytes(otherFile, GuideConfig());
    _ = GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {});
    Check(File.ReadAllBytes(otherFile).SequenceEqual(GuideConfig()));
}));
Test("Guide operations use the same per-file mutation lock", () => WithGuideConfig((path, dir) => {
    using var operation = new FileStream(path + ".guidedeck.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Throws(() => GuideSettingsTransaction.Apply(GuideSettingsTransaction.Preview(path), dir, () => {}));
}));

Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
