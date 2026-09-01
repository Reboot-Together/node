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
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 205, 213, 226);
        titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 116, 129, 151);
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 28, 40, 59);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 43, 58, 82);
        titleBar.ButtonPressedForegroundColor = Colors.White;
        AppTitleBar.SizeChanged += (_, _) => UpdateCaptionButtonSpacing();
        UpdateCaptionButtonSpacing();
    }

    private void UpdateCaptionButtonSpacing()
    {
        var inset = AppWindow.TitleBar.RightInset;
        CaptionButtonSpacer.Width = new GridLength(Math.Max(0, inset));
    }
}
