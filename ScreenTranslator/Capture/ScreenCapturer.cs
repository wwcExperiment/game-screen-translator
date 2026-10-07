using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenTranslator.Core;

namespace ScreenTranslator.Capture;

/// <summary>
/// 截取屏幕指定区域或目标窗口，返回降采样签名 + 完整 PNG。
/// 提供了目标窗口句柄时，优先用 PrintWindow 直接抓取窗口内容——
/// 被叠加层（或其它窗口）遮挡的部分也能抓到，且不会把叠加层本身截进去。
/// </summary>
public sealed class ScreenCapturer : IFrameSource
{
    private const uint PW_CLIENTONLY = 0x00000001;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    private readonly Rectangle _region;
    private readonly int _sigWidth;
    private readonly int _sigHeight;
    private readonly Func<IntPtr>? _targetWindow;
    private string? _lastMode;

    /// <param name="targetWindow">可选：返回目标窗口句柄的委托（每次截图时读取，支持运行时切换）。返回 <see cref="IntPtr.Zero"/> 时退回屏幕截图。</param>
    public ScreenCapturer(Rectangle region, int sigWidth, int sigHeight, Func<IntPtr>? targetWindow = null)
    {
        _region = region;
        _sigWidth = sigWidth;
        _sigHeight = sigHeight;
        _targetWindow = targetWindow;
    }

    public Frame Capture()
    {
        using var bitmap = new Bitmap(_region.Width, _region.Height, PixelFormat.Format24bppRgb);
        string mode;
        using (var g = Graphics.FromImage(bitmap))
        {
            var hwnd = _targetWindow?.Invoke() ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                mode = "CopyFromScreen(无目标窗口)";
                g.CopyFromScreen(_region.Left, _region.Top, 0, 0, _region.Size, CopyPixelOperation.SourceCopy);
            }
            else if (TryCaptureWindow(g, hwnd))
            {
                mode = "PrintWindow";
            }
            else
            {
                mode = "CopyFromScreen(PrintWindow 失败回退)";
                g.CopyFromScreen(_region.Left, _region.Top, 0, 0, _region.Size, CopyPixelOperation.SourceCopy);
            }
        }

        if (mode != _lastMode)
        {
            Log.Write($"截图方式: {mode}（区域 {_region.Width}×{_region.Height}）");
            _lastMode = mode;
        }

        var signature = FrameSignature.FromBitmap(bitmap, _sigWidth, _sigHeight);
        var png = EncodePng(bitmap);
        return new Frame(signature, png);
    }

    /// <summary>用 PrintWindow 抓取目标窗口内容并裁剪到 region；失败返回 false，由调用方退回屏幕截图。</summary>
    private bool TryCaptureWindow(Graphics target, IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var client) || client.Width <= 0 || client.Height <= 0)
            return false;

        var origin = new POINT();
        ClientToScreen(hwnd, ref origin);

        // region（屏幕坐标）在窗口客户区里的裁剪矩形（窗口坐标）
        var src = new Rectangle(_region.Left - origin.X, _region.Top - origin.Y, _region.Width, _region.Height);
        if (src.X < 0 || src.Y < 0 || src.Right > client.Width || src.Bottom > client.Height)
            return false; // region 不在窗口内，退回屏幕截图

        using var windowBitmap = new Bitmap(client.Width, client.Height, PixelFormat.Format24bppRgb);
        using (var wg = Graphics.FromImage(windowBitmap))
        {
            var hdc = wg.GetHdc();
            bool ok;
            try
            {
                // 只抓客户区：不带 PW_CLIENTONLY 时 PrintWindow 会把标题栏等非客户区一并画进位图，
                // 使客户区内容整体下移一个标题栏高度，导致截图相对框选区域向上偏移、叠加层相对向下偏离。
                ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
            }
            finally
            {
                wg.ReleaseHdc(hdc);
            }
            if (!ok)
                return false;
        }

        target.DrawImage(windowBitmap, new Rectangle(0, 0, _region.Width, _region.Height), src, GraphicsUnit.Pixel);
        return true;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
}
