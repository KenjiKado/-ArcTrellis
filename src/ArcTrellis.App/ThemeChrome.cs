using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ArcTrellis.App;

internal static class ThemeChrome
{
    public static void Apply(Window window, bool dark)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
