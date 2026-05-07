// src/ScreenshotVault.Core/Capture/ContextExtractor.cs
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.Core.Capture;

/// <summary>
/// Extracts window metadata from the currently active foreground window.
/// For browser windows, attempts to extract the current URL via UIAutomation.
///
/// THREADING: UIAutomation requires STA apartment state.
/// The public API wraps execution in a dedicated STA thread automatically.
/// </summary>
public sealed class ContextExtractor
{
    private static readonly TimeSpan UiaTimeout = TimeSpan.FromSeconds(3);

    // Executables known to expose URL via UIAutomation
    private static readonly HashSet<string> BrowserProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore" };

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Asynchronously captures context from the current foreground window.
    /// Safe to call from any thread; internally marshals to STA.
    /// </summary>
    public Task<WindowContext> ExtractActiveWindowContextAsync()
        => Task.Run(RunOnStaThread);

    private WindowContext RunOnStaThread()
    {
        WindowContext? result = null;
        Exception?     caught = null;

        var thread = new Thread(() =>
        {
            try   { result = ExtractCore(); }
            catch (Exception ex) { caught = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        bool finished = thread.Join(UiaTimeout);

        if (!finished)
        {
            // UIAutomation hung (common with some Electron/Java apps)
            thread.Interrupt();
            System.Diagnostics.Debug.WriteLine("[ContextExtractor] UIA timed out.");
            return WindowContext.Unknown;
        }

        if (caught != null)
            System.Diagnostics.Debug.WriteLine($"[ContextExtractor] Error: {caught.Message}");

        return result ?? WindowContext.Unknown;
    }

    private WindowContext ExtractCore()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return WindowContext.Unknown;

        GetWindowThreadProcessId(hwnd, out uint pid);

        System.Diagnostics.Process? process = null;
        try { process = System.Diagnostics.Process.GetProcessById((int)pid); }
        catch { return WindowContext.Unknown; }

        using (process)
        {
            string exeName     = process.ProcessName;
            string windowTitle = string.Empty;
            try   { windowTitle = process.MainWindowTitle; } catch { }

            string? url = null;
            if (BrowserProcessNames.Contains(exeName))
                url = TryExtractBrowserUrl(hwnd, exeName);

            return new WindowContext
            {
                ProcessName = exeName,
                WindowTitle = windowTitle,
                Url         = url,
                CapturedAt  = DateTime.UtcNow
            };
        }
    }

    private static string? TryExtractBrowserUrl(IntPtr hwnd, string exeName)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root == null) return null;

            var addressBar = FindAddressBar(root, exeName);
            if (addressBar == null) return null;

            if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                var value = ((ValuePattern)pattern).Current.Value;
                // Validate it looks like a URL before returning
                return (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("file://",  StringComparison.OrdinalIgnoreCase))
                    ? value : null;
            }
        }
        catch (ElementNotAvailableException) { /* Window closed during capture */ }
        catch (COMException)                 { /* Protected/elevated window     */ }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UIA] URL extraction failed: {ex.Message}");
        }
        return null;
    }

    private static AutomationElement? FindAddressBar(AutomationElement root, string exeName)
    {
        Condition condition;

        if (exeName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
        {
            // Firefox exposes URL via a combo box named "Search or enter address"
            condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                new PropertyCondition(AutomationElement.NameProperty,
                    "Search or enter address", PropertyConditionFlags.IgnoreCase));
        }
        else if (exeName.Equals("iexplore", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy Internet Explorer
            condition = new PropertyCondition(
                AutomationElement.NameProperty, "Address and search bar",
                PropertyConditionFlags.IgnoreCase);
        }
        else
        {
            // Chromium-based: Chrome, Edge, Brave, Opera, Vivaldi
            // The address bar AutomationId is consistent across Chromium versions
            condition = new OrCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty,
                    "address_and_search_bar"),
                new PropertyCondition(AutomationElement.NameProperty,
                    "Address and search bar", PropertyConditionFlags.IgnoreCase));
        }

        // TreeScope.Descendants with a depth guard via CacheRequest
        // Avoid full tree walk — stop at 5 levels deep for performance
        var walker = new TreeWalker(condition);
        return root.FindFirst(TreeScope.Descendants, condition);
    }
}
