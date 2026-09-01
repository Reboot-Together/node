using System.ComponentModel;
using Microsoft.UI;

namespace AsterismApp;

public sealed class TypographySettings : INotifyPropertyChanged
{
    private double _scale = 1;

    public double Small => 8.4 * _scale;
    public double Ui => 9.1 * _scale;
    public double Body => 9.8 * _scale;
    public double Title => 15.4 * _scale;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(double scale)
    {
        scale = Math.Clamp(scale, .8, 1.4);
        if (Math.Abs(scale - _scale) < .001) return;
        _scale = scale;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public sealed record AccentPalette(
    string Key,
    string DisplayName,
    Windows.UI.Color Accent,
    Windows.UI.Color Bright,
    Windows.UI.Color Surface)
{
    public string CssColor => $"#{Accent.R:X2}{Accent.G:X2}{Accent.B:X2}";
}

public static class AppearanceThemes
{
    public static IReadOnlyList<AccentPalette> All { get; } =
    [
        Palette("gold", "별빛 골드", 0xD1, 0xAF, 0x61, 0xEE, 0xD3, 0x8F, 0x4A, 0x40, 0x26),
        Palette("blue", "성운 블루", 0x6C, 0xB6, 0xFF, 0xA8, 0xD5, 0xFF, 0x25, 0x38, 0x4A),
        Palette("teal", "오로라 틸", 0x69, 0xC3, 0xB1, 0xA0, 0xE0, 0xD4, 0x23, 0x41, 0x3C),
        Palette("purple", "은하 퍼플", 0xB7, 0x9C, 0xF4, 0xD4, 0xC5, 0xFF, 0x3B, 0x31, 0x52)
    ];

    public static AccentPalette Get(string? key) =>
        All.FirstOrDefault(theme => theme.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    private static AccentPalette Palette(
        string key,
        string name,
        byte accentR,
        byte accentG,
        byte accentB,
        byte brightR,
        byte brightG,
        byte brightB,
        byte surfaceR,
        byte surfaceG,
        byte surfaceB) => new(
            key,
            name,
            ColorHelper.FromArgb(255, accentR, accentG, accentB),
            ColorHelper.FromArgb(255, brightR, brightG, brightB),
            ColorHelper.FromArgb(255, surfaceR, surfaceG, surfaceB));
}
