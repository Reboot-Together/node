using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace AsterismApp;

public sealed class VerticalResizeBorder : ContentControl
{
    public VerticalResizeBorder()
    {
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    }
}

public sealed class HorizontalResizeBorder : ContentControl
{
    public HorizontalResizeBorder()
    {
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
