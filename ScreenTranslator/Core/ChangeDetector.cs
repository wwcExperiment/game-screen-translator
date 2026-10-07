namespace ScreenTranslator.Core;

public interface IChangeDetector
{
    /// <summary>判断当前帧相对上一帧是否发生变化；并输出是否达到「显著变化」（可用于取消在途请求）。</summary>
    bool IsChanged(byte[] current, out bool significant);
}

/// <summary>
/// 基于「切块」的变化检测：
/// 把降采样后的灰度签名切成 <see cref="_blockSize"/>×<see cref="_blockSize"/> 的小块，
/// 某一块内显著变化像素（灰度差超过 <see cref="_pixelEpsilon"/>）占比超过 <see cref="_blockChangeFraction"/>
/// 即判定该块「有变化」；当变化块数占总块数的比例达到 <see cref="_changeFraction"/> 时，判定为内容变化；
/// 达到更高的 <see cref="_cancelFraction"/> 时，判定为「显著变化」（应取消在途请求）。
/// </summary>
public sealed class ChangeDetector : IChangeDetector
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _pixelEpsilon;
    private readonly int _blockSize;
    private readonly double _blockChangeFraction;
    private readonly double _changeFraction;
    private readonly double _cancelFraction;

    private byte[]? _previous;
    private DateTime _lastLogUtc = DateTime.MinValue;

    /// <param name="signatureWidth">灰度签名的宽度（像素数）。</param>
    /// <param name="signatureHeight">灰度签名的高度（像素数）。</param>
    /// <param name="changeFraction">变化块数占总块数的比例达到该值即视为内容变化（0–1）。</param>
    /// <param name="cancelChangeFraction">变化块数占总块数的比例达到该值即视为「显著变化」、应取消在途请求（应大于 changeFraction）。</param>
    /// <param name="pixelEpsilon">单个像素灰度差超过该值才视为显著变化（0–255）。</param>
    /// <param name="blockSize">切块的边长（签名像素）。</param>
    /// <param name="blockChangeFraction">块内显著变化像素占比超过该值即视为该块有变化（0–1）。</param>
    public ChangeDetector(int signatureWidth, int signatureHeight, double changeFraction, double cancelChangeFraction,
        int pixelEpsilon = 10, int blockSize = 8, double blockChangeFraction = 0.05)
    {
        _width = signatureWidth;
        _height = signatureHeight;
        _changeFraction = changeFraction;
        _cancelFraction = cancelChangeFraction;
        _pixelEpsilon = pixelEpsilon;
        _blockSize = blockSize;
        _blockChangeFraction = blockChangeFraction;
    }

    public bool IsChanged(byte[] current, out bool significant)
    {
        significant = false;
        if (_previous is null || _previous.Length != current.Length)
        {
            _previous = current;
            Log.Write($"变化检测初始化：首帧或尺寸变化（签名 {current.Length} 字节）");
            return true;
        }

        var cols = (_width + _blockSize - 1) / _blockSize;
        var rows = (_height + _blockSize - 1) / _blockSize;
        var totalBlocks = cols * rows;

        var changedBlocks = 0;
        for (var by = 0; by < rows; by++)
        {
            var y0 = by * _blockSize;
            var y1 = Math.Min(y0 + _blockSize, _height);
            for (var bx = 0; bx < cols; bx++)
            {
                var x0 = bx * _blockSize;
                var x1 = Math.Min(x0 + _blockSize, _width);

                var area = (x1 - x0) * (y1 - y0);
                var changed = 0;
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        var i = y * _width + x;
                        if (Math.Abs(_previous[i] - current[i]) > _pixelEpsilon)
                            changed++;
                    }

                if ((double)changed / area > _blockChangeFraction)
                    changedBlocks++;
            }
        }

        _previous = current;

        var ratio = (double)changedBlocks / totalBlocks;
        var result = ratio >= _changeFraction;
        significant = ratio >= _cancelFraction;

        // 采样日志：判定有变化时立即记一条；否则每秒最多一条「心跳」，
        // 用来判断截图是否真的在变（排查 PrintWindow 返回固定画面导致的「不翻译」）。
        var now = DateTime.UtcNow;
        if (result || (now - _lastLogUtc).TotalMilliseconds >= 1000)
        {
            Log.Write($"变化检测: {changedBlocks}/{totalBlocks} 块（变化阈值≥{_changeFraction:0.###}，取消阈值≥{_cancelFraction:0.###}）=> {(significant ? "显著变化" : result ? "有变化" : "无变化")}");
            _lastLogUtc = now;
        }

        return result;
    }
}
