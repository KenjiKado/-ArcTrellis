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

    public static void HideIcon(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 1); // WS_EX_DLGMODALFRAME
        SendMessage(handle, 0x80, IntPtr.Zero, IntPtr.Zero);
        SendMessage(handle, 0x80, new IntPtr(1), IntPtr.Zero);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x27);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
