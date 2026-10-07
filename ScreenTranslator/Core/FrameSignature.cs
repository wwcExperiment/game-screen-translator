using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenTranslator.Core;

/// <summary>把一张位图降采样成灰度签名，供变化检测用。</summary>
public static class FrameSignature
{
    /// <summary>
    /// 用 box-average 把位图降采样到 <paramref name="width"/>×<paramref name="height"/>，
    /// 每个输出像素等于对应源区域的 Rec.601 亮度均值。返回亮度字节数组。
    /// </summary>
    public static byte[] FromBitmap(Bitmap bitmap, int width, int height)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
            var stride = data.Stride;
            var buffer = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            var gray = new byte[width * height];
            for (var oy = 0; oy < height; oy++)
            {
                var sy0 = oy * bitmap.Height / height;
                var sy1 = (oy + 1) * bitmap.Height / height;
                if (sy1 <= sy0) sy1 = sy0 + 1;

                for (var ox = 0; ox < width; ox++)
                {
                    var sx0 = ox * bitmap.Width / width;
                    var sx1 = (ox + 1) * bitmap.Width / width;
                    if (sx1 <= sx0) sx1 = sx0 + 1;

                    long sum = 0;
                    var count = 0;
                    for (var sy = sy0; sy < sy1; sy++)
                    {
                        for (var sx = sx0; sx < sx1; sx++)
                        {
                            var idx = sy * stride + sx * bytesPerPixel;
                            var b = buffer[idx];
                            var g = buffer[idx + 1];
                            var r = buffer[idx + 2];
                            sum += (r * 299 + g * 587 + b * 114) / 1000;
                            count++;
                        }
                    }
                    gray[oy * width + ox] = (byte)(sum / count);
                }
            }
            return gray;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
