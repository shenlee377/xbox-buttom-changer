// Shared with the UWP widget; keep compatible with C# 8 and .NET Standard 2.0.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuideDeck.Core
{
    public enum TestScope { DesktopUnfocused, DesktopFocused, SteamGame, OtherGame }
    public enum Observation { Untested, Passed, Failed, NotApplicable }
    public static class ValidationContract
    {
        public static readonly IReadOnlyList<string> Keys = Array.AsReadOnly(new[] {"gamebar", "no-steam-focus", "no-guide-overlay", "overlay-shortcut", "normal-input", "haptics"});
        public static readonly IReadOnlyList<string> Labels = Array.AsReadOnly(new[] {"Guide 只開啟 Game Bar", "Guide 沒有聚焦 Steam／進 Big Picture", "Guide 沒有開啟 Steam 遊戲 Overlay", "Shift+Tab 仍能開啟 Steam Overlay", "普通按鍵、搖桿與扳機正常", "震動與需要的額外按鍵正常"});
        public static void ValidateResults(TestScope scope, IReadOnlyDictionary<string, Observation> results, string notes)
        {
            if (!Enum.IsDefined(typeof(TestScope), scope) || results == null || notes == null || results.Count != Keys.Count ||
                Keys.Any(k => !results.TryGetValue(k, out var r) || !Enum.IsDefined(typeof(Observation), r)))
                throw new ArgumentException("驗收項目不完整或格式錯誤。");
            if (results.Values.All(r => r == Observation.Untested || r == Observation.NotApplicable))
                throw new ArgumentException("至少需要一項實際觀察。");
            if (new[] {"gamebar", "no-steam-focus", "normal-input"}.Any(k => results[k] == Observation.NotApplicable) ||
                (scope == TestScope.SteamGame && new[] {"no-guide-overlay", "overlay-shortcut"}.Any(k => results[k] == Observation.NotApplicable)))
                throw new ArgumentException("核心驗收項目不能標成不適用。");
            if (results.Values.Contains(Observation.NotApplicable) && string.IsNullOrWhiteSpace(notes))
                throw new ArgumentException("請說明不適用項目的原因。");
        }
    }
}
