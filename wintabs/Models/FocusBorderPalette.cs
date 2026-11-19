using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace wintabs.Models
{
    public sealed record FocusBorderSwatch(string Name, uint? Latte, uint? Mocha)
    {
        // Share of the swatch color mixed into the theme base; the rest is the base itself.
        private const double LightStrength = 0.30;
        private const double DarkStrength = 0.38;
        private static readonly Color LightBase = Colors.White;
        private static readonly Color DarkBase = Color.FromRgb(0x2B, 0x2B, 0x2B);

        public Brush LatteBrush => CreateBrush(false);
        public Brush MochaBrush => CreateBrush(true);

        /// <summary>
        /// A soft pastel tint of the swatch that keeps text on top of it readable.
        /// </summary>
        public Brush CreateBrush(bool dark)
        {
            var source = (dark ? Mocha : Latte) is uint rgb
                ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
                : SystemColors.AccentColor;

            var brush = new SolidColorBrush(dark
                ? Mix(source, DarkBase, DarkStrength)
                : Mix(source, LightBase, LightStrength));
            brush.Freeze();
            return brush;
        }

        private static Color Mix(Color color, Color baseColor, double amount)
        {
            static byte Channel(byte c, byte b, double t) => (byte)Math.Round(b + (c - b) * t);
            return Color.FromRgb(
                Channel(color.R, baseColor.R, amount),
                Channel(color.G, baseColor.G, amount),
                Channel(color.B, baseColor.B, amount));
        }
    }

    internal static class FocusBorderPalette
    {
        public static IReadOnlyList<FocusBorderSwatch> Swatches { get; } =
        [
            new("Windows accent", null, null),
            new("Rosewater", 0xDC8A78, 0xF5E0DC),
            new("Flamingo", 0xDD7878, 0xF2CDCD),
            new("Pink", 0xEA76CB, 0xF5C2E7),
            new("Mauve", 0x8839EF, 0xCBA6F7),
            new("Red", 0xD20F39, 0xF38BA8),
            new("Maroon", 0xE64553, 0xEBA0AC),
            new("Peach", 0xFE640B, 0xFAB387),
            new("Yellow", 0xDF8E1D, 0xF9E2AF),
            new("Green", 0x40A02B, 0xA6E3A1),
            new("Teal", 0x179299, 0x94E2D5),
            new("Sky", 0x04A5E5, 0x89DCEB),
            new("Sapphire", 0x209FB5, 0x74C7EC),
            new("Blue", 0x1E66F5, 0x89B4FA),
            new("Lavender", 0x7287FD, 0xB4BEFE),
        ];

        public static FocusBorderSwatch Find(string? name)
        {
            foreach (var swatch in Swatches)
            {
                if (string.Equals(swatch.Name, name, StringComparison.OrdinalIgnoreCase))
                    return swatch;
            }
            return Swatches[0];
        }
    }
}
