using System.Drawing;
using System.Drawing.Imaging;
using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class FrameSignatureTests
{
    private static Bitmap Solid(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    [Fact]
    public void FromBitmap_returns_signature_of_expected_size()
    {
        using var bmp = Solid(100, 50, Color.Black);
        var sig = FrameSignature.FromBitmap(bmp, 8, 8);
        Assert.Equal(64, sig.Length);
    }

    [Fact]
    public void FromBitmap_solid_red_gives_luminance_76()
    {
        using var bmp = Solid(100, 50, Color.Red);
        var sig = FrameSignature.FromBitmap(bmp, 8, 8);
        Assert.All(sig, b => Assert.Equal((byte)76, b));
    }

    [Fact]
    public void FromBitmap_solid_white_gives_luminance_255()
    {
        using var bmp = Solid(100, 50, Color.White);
        var sig = FrameSignature.FromBitmap(bmp, 8, 8);
        Assert.All(sig, b => Assert.Equal((byte)255, b));
    }

    [Fact]
    public void FromBitmap_solid_black_gives_luminance_0()
    {
        using var bmp = Solid(100, 50, Color.Black);
        var sig = FrameSignature.FromBitmap(bmp, 8, 8);
        Assert.All(sig, b => Assert.Equal((byte)0, b));
    }
}
