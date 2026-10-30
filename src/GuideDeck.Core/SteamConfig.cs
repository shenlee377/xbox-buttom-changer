using System.Text;

namespace GuideDeck.Core;

public sealed record MappingCandidate(int Index, string Identity, string Name, string Binding, string Before, string After, int Offset, int Length)
{
    public string DisplayName => $"{Name} · {Identity} · {(Binding.Length == 0 ? "無 Guide 欄位" : Binding)}";
}
public sealed record ConfigInspection(IReadOnlyList<MappingCandidate> Mappings, IReadOnlyList<string> Problems);

public static class SteamConfig
{
    public const int MaxBytes = 8 * 1024 * 1024;
    // Latin1 creates a lossless byte-indexed view; UTF-8 decoding is for display only.
    private sealed record Token(char Kind, string Value, int Offset, int Length);
    private sealed record Leaf(string[] Path, Token Value);

    public static ConfigInspection Inspect(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new FormatException("設定檔超過 8 MiB，停止處理。");
        var start = bytes.AsSpan().StartsWith(new byte[] {239, 187, 191}) ? 3 : 0;
        _ = new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
        var source = Encoding.Latin1.GetString(bytes);
        var tokens = Tokenize(source, start);
        var leaves = new List<Leaf>();
        var objects = new List<string[]>();
        int cursor = 0;
        Parse(tokens, ref cursor, [], leaves, objects, false);
        // Deliberately support only the observed direct InstallConfigStore property.
        var targets = leaves.Where(l => l.Path.SequenceEqual(new[] {"InstallConfigStore", "SDL_GamepadBind"})).ToArray();
        if (targets.Length != 1 || objects.Count(p => p.SequenceEqual(new[] {"InstallConfigStore"})) != 1 ||
            objects.Any(p => p.SequenceEqual(new[] {"InstallConfigStore", "SDL_GamepadBind"})))
            return new([], ["找不到唯一的 InstallConfigStore/SDL_GamepadBind 字串。此格式不支援；不新增或猜測設定。"]);
        var token = targets[0].Value;
        var mappings = new List<MappingCandidate>();
        var problems = new List<string>();
        if (token.Value.Contains('\\')) return new([], ["SDL_GamepadBind 使用跳脫格式，尚未支援；請使用剪貼簿 mapping 工作台。"]);
        int offset = token.Offset;
        foreach (var line in token.Value.Split('\n'))
        {
            string mapping = line.EndsWith('\r') ? line[..^1] : line;
            if (!string.IsNullOrWhiteSpace(mapping))
            {
                try
                {
                    var edit = MappingEditor.RemoveGuide(mapping);
                    mappings.Add(new(mappings.Count, edit.Identity, Decode(edit.Name), edit.RemovedBinding,
                        Decode(edit.Original), Decode(edit.Modified), offset, mapping.Length));
                }
                catch (FormatException) { problems.Add($"第 {mappings.Count + problems.Count + 1} 筆 mapping 格式不支援；整份設定不開放寫入。"); }
            }
            offset += line.Length + 1;
        }
        if (mappings.GroupBy(m => m.Identity).Any(g => g.Count() > 1))
            problems.Add("相同 GUID／CRC 有重複 mapping，無法安全選取。");
        if (mappings.Count == 0) problems.Add("沒有明確 mapping；缺少 mapping 不代表 Steam 不會接收 Guide。");
        return new(mappings, problems);
    }

    public static byte[] Patch(byte[] original, int index)
    {
        var inspection = Inspect(original);
        if (inspection.Problems.Count != 0) throw new InvalidOperationException(string.Join("\n", inspection.Problems));
        var candidate = inspection.Mappings.SingleOrDefault(m => m.Index == index) ?? throw new ArgumentException("請先選擇 mapping。");
        if (candidate.Binding.Length == 0) throw new InvalidOperationException("這筆 mapping 沒有 Guide 欄位；不代表第三方響應已被阻止。");
        var result = ReplaceMapping(original, candidate, candidate.After);
        var check = Inspect(result);
        if (check.Problems.Count != 0 || check.Mappings[index].Binding.Length != 0) throw new InvalidOperationException("修改後驗證失敗。");
        return result;
    }

    internal static byte[] ReplaceMapping(byte[] original, MappingCandidate candidate, string mapping)
    {
        var edit = MappingEditor.RemoveGuide(mapping);
        if (edit.Identity != candidate.Identity) throw new InvalidOperationException("裝置 identity 不一致。");
        var replacement = Encoding.UTF8.GetBytes(mapping);
        using var output = new MemoryStream();
        output.Write(original.AsSpan(0, candidate.Offset));
        output.Write(replacement);
        output.Write(original.AsSpan(candidate.Offset + candidate.Length));
        return output.ToArray();
    }

    private static string Decode(string latin) => Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(latin));
    private static List<Token> Tokenize(string text, int position)
    {
        var tokens = new List<Token>();
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position])) { position++; continue; }
            if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            { while (position < text.Length && text[position] != '\n') position++; continue; }
            var c = text[position++];
            if (c is '{' or '}') { tokens.Add(new(c, "", position - 1, 1)); continue; }
            if (c != '"') throw new FormatException("只支援帶引號的文字 VDF；未知格式不修改。");
            int start = position;
            bool closed = false;
            while (position < text.Length)
            {
                if (text[position] == '\\') { position += 2; continue; }
                if (text[position] == '"') { closed = true; break; }
                position++;
            }
            if (!closed) throw new FormatException("VDF 字串未結束。");
            tokens.Add(new('s', text[start..position], start, position - start));
            if (tokens.Count > 300000) throw new FormatException("VDF 過於複雜。");
            position++;
        }
        return tokens;
    }

    private static void Parse(List<Token> tokens, ref int i, string[] path, List<Leaf> leaves, List<string[]> objects, bool nested)
    {
        if (path.Length > 64) throw new FormatException("VDF 巢狀層數過多。");
        while (i < tokens.Count)
        {
            if (tokens[i].Kind == '}')
            {
                if (!nested) throw new FormatException("多餘的 VDF 結束括號。");
                i++; return;
            }
            if (tokens[i].Kind != 's') throw new FormatException("VDF 缺少鍵名。");
            var key = tokens[i++].Value;
            if (i == tokens.Count) throw new FormatException("VDF 缺少值。");
            var value = tokens[i++];
            var next = path.Append(key).ToArray();
            if (value.Kind == '{') { objects.Add(next); Parse(tokens, ref i, next, leaves, objects, true); }
            else if (value.Kind == 's') leaves.Add(new(next, value));
            else throw new FormatException("VDF 值格式錯誤。");
        }
        if (nested) throw new FormatException("VDF 物件未結束。");
    }
}
