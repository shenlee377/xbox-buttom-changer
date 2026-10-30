#nullable disable
using System;

namespace GuideDeck.Core
{
    public enum ThemeMode { System, Light, Dark }
    public enum ThemeVariant { Light, Dark, HighContrast }
    public static class ThemePolicy
    {
        public static ThemeMode Normalize(ThemeMode mode) => Enum.IsDefined(typeof(ThemeMode), mode) ? mode : ThemeMode.System;
        public static ThemeVariant Resolve(ThemeMode mode, bool systemUsesLight, bool highContrast)
        {
            if (highContrast) return ThemeVariant.HighContrast;
            mode = Normalize(mode);
            return mode == ThemeMode.Light || (mode == ThemeMode.System && systemUsesLight) ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }
}
