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

public sealed record SurfacePalette(
    string Key,
    string DisplayName,
    bool IsLight,
    Windows.UI.Color AppBackground,
    Windows.UI.Color SidebarBackground,
    Windows.UI.Color DocumentBackground,
    Windows.UI.Color CardBackground,
    Windows.UI.Color Border,
    Windows.UI.Color PrimaryText,
    Windows.UI.Color SecondaryText,
    Windows.UI.Color PlaceholderText,
    Windows.UI.Color HoverBackground,
    Windows.UI.Color PressedBackground,
    Windows.UI.Color SelectedBackground,
    Windows.UI.Color SelectedHoverBackground,
    Windows.UI.Color ScrollThumb,
    Windows.UI.Color ScrollThumbHover,
    Windows.UI.Color ScrollThumbPressed)
{
    public string Css(Windows.UI.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
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

public static class SurfaceThemes
{
    public static IReadOnlyList<SurfacePalette> All { get; } =
    [
        Palette("dark", "Asterism 다크", false,
            0x181818, 0x202020, 0x1E1E1E, 0x252526, 0x303030,
            0xD4D4D4, 0x969696, 0x858585, 0x282828, 0x333333,
            0x2A2A2A, 0x303030, 0x555555, 0x707070, 0x808080),
        Palette("light", "소프트 라이트", true,
            0xF3F3F3, 0xF0F0F0, 0xFFFFFF, 0xF5F5F5, 0xD8D8D8,
            0x252525, 0x686868, 0x7A7A7A, 0xEAEAEA, 0xE0E0E0,
            0xE6E6E6, 0xDDDDDD, 0xA8A8A8, 0x8E8E8E, 0x777777),
        Palette("midnight", "딥 나이트", false,
            0x0F1115, 0x15181D, 0x12151A, 0x1B1F26, 0x2A3039,
            0xD9DDE5, 0x929AA7, 0x7F8793, 0x20252D, 0x292F39,
            0x232A34, 0x2A323D, 0x4B5563, 0x667180, 0x788493)
    ];

    public static SurfacePalette Get(string? key) =>
        All.FirstOrDefault(theme => theme.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    private static SurfacePalette Palette(
        string key,
        string displayName,
        bool isLight,
        uint appBackground,
        uint sidebarBackground,
        uint documentBackground,
        uint cardBackground,
        uint border,
        uint primaryText,
        uint secondaryText,
        uint placeholderText,
        uint hoverBackground,
        uint pressedBackground,
        uint selectedBackground,
        uint selectedHoverBackground,
        uint scrollThumb,
        uint scrollThumbHover,
        uint scrollThumbPressed) => new(
            key,
            displayName,
            isLight,
            Color(appBackground),
            Color(sidebarBackground),
            Color(documentBackground),
            Color(cardBackground),
            Color(border),
            Color(primaryText),
            Color(secondaryText),
            Color(placeholderText),
            Color(hoverBackground),
            Color(pressedBackground),
            Color(selectedBackground),
            Color(selectedHoverBackground),
            Color(scrollThumb),
            Color(scrollThumbHover),
            Color(scrollThumbPressed));

    private static Windows.UI.Color Color(uint rgb) => ColorHelper.FromArgb(
        255,
        (byte)(rgb >> 16),
        (byte)(rgb >> 8),
        (byte)rgb);
}
