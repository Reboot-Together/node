using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace AsterismApp;

public sealed class DirectionalCursorCanvas : Canvas
{
    private InputCursor? _cursor;
    private GraphCursorDirection? _direction;

    public void SetCursor(GraphCursorDirection direction)
    {
        if (_direction == direction) return;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = InputDesktopResourceCursor.CreateFromModule(
            "Asterism.CursorResources.dll",
            (uint)(201 + (int)direction));
        _direction = direction;
        ProtectedCursor = _cursor;
    }

    public void ResetCursor()
    {
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
        _direction = null;
    }
}
