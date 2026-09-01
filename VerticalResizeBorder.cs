using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace AsterismApp;

public sealed class VerticalResizeBorder : ContentControl
{
    public VerticalResizeBorder()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    }
}

public sealed class HorizontalResizeBorder : ContentControl
{
    public HorizontalResizeBorder()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
