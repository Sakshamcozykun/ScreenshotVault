// src/ScreenshotVault.Core/Capture/ScreenCaptureService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenshotVault.Core.Capture;

/// <summary>
/// Captures the full virtual screen (all monitors) using GDI.
/// Uses physical pixel dimensions via GetDeviceCaps to handle DPI scaling correctly.
/// </summary>
public sealed class ScreenCaptureService
{
    // GetDeviceCaps indices
    private const int DESKTOPHORZRES = 118; // Physical width of entire desktop
    private const int DESKTOPVERTRES = 117; // Physical height of entire desktop

    [DllImport("user32.dll")]  private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")]  private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]  private static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]   private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]   private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")]   private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")]   private static extern bool   DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]   private static extern bool   DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")]   private static extern bool   BitBlt(IntPtr hdc, int x, int y, int cx, int cy,
                                   IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")]   private static extern int    GetDeviceCaps(IntPtr hdc, int nIndex);

    private const uint SRCCOPY = 0x00CC0020;

    /// <summary>
    /// Captures the full virtual desktop and returns PNG bytes.
    /// Handles multi-monitor and per-monitor DPI setups.
    /// </summary>
    public byte[] CaptureScreen()
    {
        IntPtr desktopDC   = IntPtr.Zero;
        IntPtr memDC       = IntPtr.Zero;
        IntPtr hBitmap     = IntPtr.Zero;
        IntPtr hOldObj     = IntPtr.Zero;
        IntPtr hDesktopWnd = GetDesktopWindow();

        try
        {
            desktopDC = GetDC(hDesktopWnd);

            // Use physical pixel dimensions — critical for DPI-scaled desktops
            int width  = GetDeviceCaps(desktopDC, DESKTOPHORZRES);
            int height = GetDeviceCaps(desktopDC, DESKTOPVERTRES);

            memDC   = CreateCompatibleDC(desktopDC);
            hBitmap = CreateCompatibleBitmap(desktopDC, width, height);
            hOldObj = SelectObject(memDC, hBitmap);

            BitBlt(memDC, 0, 0, width, height, desktopDC, 0, 0, SRCCOPY);
            SelectObject(memDC, hOldObj);

            using var bmp = Image.FromHbitmap(hBitmap);
            using var ms  = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        finally
        {
            if (hBitmap  != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDC    != IntPtr.Zero) DeleteDC(memDC);
            if (desktopDC != IntPtr.Zero) ReleaseDC(hDesktopWnd, desktopDC);
        }
    }
}
