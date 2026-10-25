using System.Drawing;
using System.Drawing.Imaging;

namespace SnapOcr.Core;

internal static class ScreenCapture
{
    /// <summary>截取虚拟屏幕(包含所有显示器)上的指定区域,按物理像素抓取。</summary>
    public static Bitmap Capture(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
