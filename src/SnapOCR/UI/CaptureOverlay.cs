using System;
using System.Drawing;
using System.Windows.Forms;

namespace SnapOcr.UI;

/// <summary>
/// 全屏蒙版 + 拖动框选。背景直接用截图,所以选中的区域保持原样亮度,
/// 其余部分压暗,比“截图后再画”更接近系统截图工具的手感。
/// </summary>
internal sealed class CaptureOverlay : Form
{
    private static readonly Color Accent = Color.FromArgb(41, 121, 255);

    private readonly Bitmap _screen;
    private readonly Rectangle _virtualScreen;
    private readonly Font _hintFont = new("Segoe UI", 9.5f);
    private readonly Font _badgeFont = new("Segoe UI", 9f);

    private Point _anchor;
    private bool _dragging;
    private Rectangle _selection;

    public Rectangle? SelectedRegion { get; private set; }

    public CaptureOverlay(Bitmap screen)
    {
        _screen = screen;
        _virtualScreen = SystemInformation.VirtualScreen;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        BackColor = Color.Black;
        Bounds = _virtualScreen;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 跨显示器创建窗口时 WinForms 可能按 DPI 重新算一次尺寸,这里强制拉回物理像素。
        Bounds = _virtualScreen;
        Activate();
        Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(_screen, new Rectangle(0, 0, _screen.Width, _screen.Height));

        using var dim = new SolidBrush(Color.FromArgb(118, 0, 0, 0));

        if (_selection.Width > 1 && _selection.Height > 1)
        {
            using (var outside = new Region(new Rectangle(0, 0, _screen.Width, _screen.Height)))
            {
                outside.Exclude(_selection);
                g.FillRegion(dim, outside);
            }

            using var border = new Pen(Accent, 2f);
            g.DrawRectangle(border, _selection);
            DrawSizeBadge(g);
            DrawHint(g);
        }
        else
        {
            g.FillRectangle(dim, 0, 0, _screen.Width, _screen.Height);
            DrawHint(g);
        }
    }

    private void DrawSizeBadge(Graphics g)
    {
        var text = $"{_selection.Width} × {_selection.Height}";
        var size = TextRenderer.MeasureText(
            text, _badgeFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);

        var x = _selection.Left;
        var y = _selection.Top - size.Height - 12;
        if (y < 8)
        {
            y = _selection.Top + 8;
        }

        if (x + size.Width + 24 > _screen.Width)
        {
            x = Math.Max(8, _screen.Width - size.Width - 24);
        }

        var box = new Rectangle(x, y, size.Width + 14, size.Height + 8);

        using var background = new SolidBrush(Color.FromArgb(218, 22, 22, 22));
        g.FillRectangle(background, box);
        TextRenderer.DrawText(
            g, text, _badgeFont, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawHint(Graphics g)
    {
        const string text = "拖动选择识别区域   ·   Esc / 右键取消";
        var size = TextRenderer.MeasureText(text, _hintFont);
        var box = new Rectangle(
            (_screen.Width - size.Width) / 2 - 16,
            _screen.Height - size.Height - 56,
            size.Width + 32,
            size.Height + 18);

        using var background = new SolidBrush(Color.FromArgb(196, 10, 10, 10));
        g.FillRectangle(background, box);
        TextRenderer.DrawText(
            g, text, _hintFont, box, Color.FromArgb(235, 235, 235),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Cancel();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _anchor = e.Location;
            _selection = Rectangle.Empty;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _selection = Normalize(_anchor, e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;

        var region = Normalize(_anchor, e.Location);
        if (region.Width < 4 || region.Height < 4)
        {
            Cancel();
            return;
        }

        SelectedRegion = region;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Cancel();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Cancel()
    {
        SelectedRegion = null;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hintFont.Dispose();
            _badgeFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
