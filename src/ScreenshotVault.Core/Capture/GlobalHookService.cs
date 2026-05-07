// src/ScreenshotVault.Core/Capture/GlobalHookService.cs
using System.Runtime.InteropServices;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.Core.Capture;

public sealed class CaptureEventArgs : EventArgs
{
    public byte[] ImageBytes { get; }
    public WindowContext Context { get; }
    public CaptureEventArgs(byte[] imageBytes, WindowContext context)
    {
        ImageBytes = imageBytes;
        Context = context;
    }
}

public sealed class GlobalHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN    = 0x0100;
    private const int VK_SNAPSHOT   = 0x2C;  // PrtScn

    private IntPtr _hookHandle = IntPtr.Zero;
    // Field reference prevents GC collecting the delegate while hook is live
    private readonly LowLevelKeyboardProc _hookCallback;
    private readonly ContextExtractor _contextExtractor;
    private readonly ScreenCaptureService _captureService;
    private bool _disposed;

    public event EventHandler<CaptureEventArgs>? ScreenshotCaptured;

    public GlobalHookService(ContextExtractor ctx, ScreenCaptureService cap)
    {
        _contextExtractor = ctx;
        _captureService   = cap;
        _hookCallback     = HookProc;
    }

    /// <summary>
    /// Installs the global low-level keyboard hook.
    /// Must be called from a thread with a message pump (UI thread).
    /// </summary>
    public void Install()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module  = process.MainModule
            ?? throw new InvalidOperationException("Cannot get main module.");

        _hookHandle = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _hookCallback,
            GetModuleHandle(module.ModuleName!),
            0);  // threadId = 0 → system-wide hook

        if (_hookHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed. Win32 error code: {err}. " +
                $"Ensure the app is not running as a restricted process.");
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Per MSDN: nCode < 0 must be forwarded unchanged
        if (nCode >= 0 && wParam == WM_KEYDOWN)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (kb.vkCode == VK_SNAPSHOT)
            {
                // Do NOT await here — we must return quickly or Windows kills the hook
                _ = Task.Run(HandleCaptureAsync);
                // Return non-zero to suppress Windows' default clipboard copy
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private async Task HandleCaptureAsync()
    {
        try
        {
            // Capture screen pixels first (fast GDI call)
            byte[] imageBytes = _captureService.CaptureScreen();

            // UIAutomation is slower; run after image grab
            WindowContext context = await _contextExtractor
                .ExtractActiveWindowContextAsync();

            ScreenshotCaptured?.Invoke(this, new CaptureEventArgs(imageBytes, context));
        }
        catch (Exception ex)
        {
            // Log but never let capture exceptions bubble unhandled
            System.Diagnostics.Debug.WriteLine($"[Capture] Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
