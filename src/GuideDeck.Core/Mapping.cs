// Shared with the UWP widget. No Steam API or global SDL environment changes.
#nullable disable
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GuideDeck.Core
{
    public sealed class MappingEdit
    {
        public string Identity { get; internal set; }
        public string Name { get; internal set; }
        public string Original { get; internal set; }
        public string Modified { get; internal set; }
        public string RemovedBinding { get; internal set; }
        public bool ChangesGuide { get { return Original != Modified; } }
    }

    public static class MappingEditor
    {
        // Preserve order, unknown metadata, CRC, paddles and every non-Guide field.
        public static MappingEdit RemoveGuide(string line)
        {
            if (line == null || line.Length > 16384 || line.IndexOfAny(new[] {'\r', '\n', '"', '\\', '\0'}) >= 0)
                throw new FormatException("需要一行原始 SDL mapping；不接受 VDF 引號、跳脫字元或多行資料。");
            var fields = line.Split(',');
            if (fields.Length < 6 || !Regex.IsMatch(fields[0], "\\A[0-9a-fA-F]{32}\\z") || string.IsNullOrWhiteSpace(fields[1]))
                throw new FormatException("無法辨識控制器 GUID／SDL mapping。請從您自己的 Steam 控制器設定匯出。");
            var bindings = fields.Skip(2).Where(f => f.Length > 0).ToArray();
            if (bindings.Any(f => f.IndexOf(':') <= 0 || f.Any(char.IsWhiteSpace)))
                throw new FormatException("mapping 欄位格式不明，停止修改。");
            foreach (var required in new[] {"a:", "b:", "leftx:", "lefty:"})
                if (!bindings.Any(f => f.StartsWith(required, StringComparison.Ordinal)))
                    throw new FormatException("mapping 缺少基本按鍵／軸，停止修改。");
            if (bindings.Any(f => f.StartsWith("platform:", StringComparison.Ordinal) && f != "platform:Windows"))
                throw new FormatException("此原型只處理 Windows mapping。");
            var guides = bindings.Where(f => f.StartsWith("guide:", StringComparison.Ordinal)).ToArray();
            if (guides.Length > 1 || (guides.Length == 1 && !Regex.IsMatch(guides[0], "\\Aguide:b[0-9]+\\z")))
                throw new FormatException("Guide 綁定重複或不是已支援的按鈕格式，停止修改。");
            var crc = bindings.Where(f => f.StartsWith("crc:", StringComparison.Ordinal)).ToArray();
            if (crc.Length > 1) throw new FormatException("CRC 重複，無法區分裝置。");
            return new MappingEdit {
                Identity = fields[0].ToLowerInvariant() + "|" + (crc.Length == 1 ? crc[0] : "no-crc"),
                Name = fields[1], Original = line,
                Modified = string.Join(",", fields.Where((f, i) => i < 2 || !f.StartsWith("guide:", StringComparison.Ordinal))),
                RemovedBinding = guides.Length == 1 ? guides[0] : ""
            };
        }
    }
}
