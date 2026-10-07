using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class ChangeDetectorTests
{
    private const int W = 64;
    private const int H = 64;

    private static ChangeDetector Make(double changeFraction = 0.05, double cancelChangeFraction = 0.15, int epsilon = 10) =>
        new(W, H, changeFraction, cancelChangeFraction, epsilon, blockSize: 8, blockChangeFraction: 0.05);

    private static byte[] Grid(byte fill = 0)
    {
        var g = new byte[W * H];
        if (fill != 0) Array.Fill(g, fill);
        return g;
    }

    private static void FillRect(byte[] g, int x0, int x1, int y0, int y1, byte v)
    {
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                g[y * W + x] = v;
    }

    [Fact]
    public void First_frame_counts_as_changed()
    {
        Assert.True(Make().IsChanged(Grid(), out _));
    }

    [Fact]
    public void Identical_frames_are_not_changed()
    {
        var d = Make();
        d.IsChanged(Grid(), out _);
        Assert.False(d.IsChanged(Grid(), out _));
    }

    [Fact]
    public void Tiny_fluctuation_below_epsilon_is_ignored()
    {
        // 全屏 +3（低于 epsilon=10），模拟光线起伏，不应触发。
        var d = Make();
        d.IsChanged(Grid(), out _);
        Assert.False(d.IsChanged(Grid(fill: 3), out _));
    }

    [Fact]
    public void Small_isolated_region_is_ignored()
    {
        // 模拟 30×30 的小图标：只改变左上角一小块，命中的块数太少，不触发全局变化。
        var d = Make();
        d.IsChanged(Grid(), out _);
        var icon = Grid();
        FillRect(icon, 0, 3, 0, 10, 200);
        Assert.False(d.IsChanged(icon, out _));
    }

    [Fact]
    public void Large_region_change_is_detected()
    {
        // 模拟一条文字行变化：覆盖多个块，触发全局变化。
        var d = Make();
        d.IsChanged(Grid(), out _);
        var text = Grid();
        FillRect(text, 0, 40, 0, 13, 200);
        Assert.True(d.IsChanged(text, out _));
    }

    [Fact]
    public void Change_fraction_controls_block_count_threshold()
    {
        var low = Make(changeFraction: 0.10); // 阈值 6.4 块 → 需 ≥7 块
        low.IsChanged(Grid(), out _);
        var fourBlocks = Grid();
        FillRect(fourBlocks, 0, 16, 0, 16, 200); // 2×2 = 4 块
        Assert.False(low.IsChanged(fourBlocks, out _));

        var high = Make(changeFraction: 0.10);
        high.IsChanged(Grid(), out _);
        var eightBlocks = Grid();
        FillRect(eightBlocks, 0, 32, 0, 16, 200); // 4×2 = 8 块
        Assert.True(high.IsChanged(eightBlocks, out _));
    }

    [Fact]
    public void Cancel_threshold_flags_significant_change()
    {
        // 变化块数占比达到 cancelChangeFraction（0.15）时 significant=true；
        // 低于它但达到 changeFraction（0.05）时 significant=false。
        var minor = Grid();
        FillRect(minor, 0, 32, 0, 16, 200); // 4×2 = 8 块（8/64=0.125：有变化，未达取消阈值）
        var minorDet = Make(changeFraction: 0.05, cancelChangeFraction: 0.15);
        minorDet.IsChanged(Grid(), out _);
        Assert.True(minorDet.IsChanged(minor, out var minorSig));
        Assert.False(minorSig);

        var major = Grid();
        FillRect(major, 0, 64, 0, 16, 200); // 8×2 = 16 块（16/64=0.25：达到取消阈值）
        var majorDet = Make(changeFraction: 0.05, cancelChangeFraction: 0.15);
        majorDet.IsChanged(Grid(), out _);
        Assert.True(majorDet.IsChanged(major, out var majorSig));
        Assert.True(majorSig);
    }
}
