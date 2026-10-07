using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenTranslator.UI;

/// <summary>一个顶层窗口（句柄 + 标题）。</summary>
public sealed class WindowInfo
{
    public IntPtr Handle { get; }
    public string Title { get; }
    /// <summary>窗口所属进程名（可执行文件名，不含扩展名）；获取失败为 null。</summary>
    public string? ProcessName { get; }

    public WindowInfo(IntPtr handle, string title, string? processName)
    {
        Handle = handle;
        Title = title;
        ProcessName = processName;
    }

    public override string ToString() => Title;
}

/// <summary>枚举系统顶层窗口，并提供前台窗口检测。</summary>
public static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>当前前台窗口句柄。</summary>
    public static IntPtr ForegroundWindow => GetForegroundWindow();

    /// <summary>枚举可见、有标题、非工具窗口的顶层窗口（排除本程序窗口）。</summary>
    public static List<WindowInfo> GetTopLevelWindows()
    {
        var result = new List<WindowInfo>();
        var selfPid = (uint)Environment.ProcessId;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
                return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == selfPid)
                return true;

            var len = GetWindowTextLength(hWnd);
            if (len == 0)
                return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            result.Add(new WindowInfo(hWnd, title, GetProcessName(pid)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static string? GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
