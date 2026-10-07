using System.Drawing;
using ScreenTranslator.Capture;

namespace ScreenTranslator.Tests.Capture;

public sealed class ScreenCapturerIntegrationTests
{
    [Fact]
    public void Capture_returns_valid_signature_and_png()
    {
        var capturer = new ScreenCapturer(new Rectangle(0, 0, 64, 64), 16, 16);
        var frame = capturer.Capture();

        Assert.Equal(16 * 16, frame.Signature.Length);

        Assert.True(frame.PngBytes.Length > 0);
        // PNG 魔数
        Assert.Equal((byte)0x89, frame.PngBytes[0]);
        Assert.Equal((byte)'P', frame.PngBytes[1]);
        Assert.Equal((byte)'N', frame.PngBytes[2]);
        Assert.Equal((byte)'G', frame.PngBytes[3]);
    }

    [Fact]
    public void Capture_with_zero_target_window_uses_screen_capture()
    {
        var capturer = new ScreenCapturer(new Rectangle(0, 0, 64, 64), 16, 16, () => IntPtr.Zero);
        var frame = capturer.Capture();

        Assert.Equal(16 * 16, frame.Signature.Length);
        Assert.True(frame.PngBytes.Length > 0);
    }
}
