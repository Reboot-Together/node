using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace AsterismApp;

public sealed partial class MainWindow
{
    private void ConfigureTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarAppearance(CurrentSurface);
        AppTitleBar.SizeChanged += (_, _) => UpdateCaptionButtonSpacing();
        UpdateCaptionButtonSpacing();
    }

    private void ApplyTitleBarAppearance(SurfacePalette surface)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = surface.PrimaryText;
        titleBar.ButtonInactiveForegroundColor = surface.SecondaryText;
        titleBar.ButtonHoverBackgroundColor = surface.HoverBackground;
        titleBar.ButtonHoverForegroundColor = surface.PrimaryText;
        titleBar.ButtonPressedBackgroundColor = surface.PressedBackground;
        titleBar.ButtonPressedForegroundColor = surface.PrimaryText;
        titleBar.BackgroundColor = surface.AppBackground;
        titleBar.InactiveBackgroundColor = surface.AppBackground;
    }

    private void UpdateCaptionButtonSpacing()
    {
        var inset = AppWindow.TitleBar.RightInset;
        CaptionButtonSpacer.Width = new GridLength(Math.Max(0, inset));
    }
}
