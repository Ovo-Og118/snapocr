using System;
using System.Drawing;
using System.Windows.Forms;
using SnapOcr.Core;

namespace SnapOcr.UI;

/// <summary>识别结果窗口:默认已经复制到剪贴板,用户既可以按 Esc 走人,也可以直接改字再复制。</summary>
internal sealed class ResultForm : Form
{
    private static readonly Color BorderColor = Color.FromArgb(212, 215, 220);
    private static readonly Color Muted = Color.FromArgb(92, 96, 102);
    private static readonly Color Ok = Color.FromArgb(28, 140, 80);
    private static readonly Color Bad = Color.FromArgb(198, 62, 62);

    private readonly TextBox _text;
    private readonly Label _status;
    private readonly Font _uiFont = new("Segoe UI", 9f);
    private readonly Font _textFont = new("Segoe UI", 10.5f);

    public ResultForm(string text, bool copied, string languageName)
    {
        Text = "SnapOCR";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);

        // 先建句柄拿到真实 DPI,再按比例算尺寸:否则在 125% / 150% 的屏幕上窗口会偏小。
        _ = Handle;
        var scale = DeviceDpi / 96.0;
        int Px(int value) => (int)Math.Round(value * scale);

        Padding = new Padding(Px(1));
        MinimumSize = new Size(Px(400), Px(220));
        Size = new Size(Px(580), Px(340));

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Px(40),
            BackColor = Color.FromArgb(247, 248, 250),
        };

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"识别结果 · {languageName}",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Px(14), 0, 0, 0),
            ForeColor = Muted,
            Font = _uiFont,
        };

        var closeButton = new Button
        {
            Dock = DockStyle.Right,
            Width = Px(42),
            Text = "✕",
            FlatStyle = FlatStyle.Flat,
            ForeColor = Muted,
            Font = _uiFont,
            TabStop = false,
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 90, 90);
        closeButton.Click += (_, _) => Close();

        header.Controls.Add(title);
        header.Controls.Add(closeButton);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(Px(14), Px(12), Px(14), Px(8)),
            BackColor = Color.White,
        };

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
            Font = _textFont,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(28, 30, 34),
            Text = text,
        };

        body.Controls.Add(_text);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Px(54),
            BackColor = Color.FromArgb(250, 250, 251),
            Padding = new Padding(Px(14), Px(11), Px(14), Px(11)),
        };

        _status = new Label
        {
            Dock = DockStyle.Fill,
            Text = copied ? "✓ 已复制到剪贴板" : "未自动复制,按 Ctrl+C 复制",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = copied ? Ok : Muted,
            Font = _uiFont,
        };

        var copyButton = new Button
        {
            Dock = DockStyle.Right,
            Width = Px(92),
            Text = "复制",
            FlatStyle = FlatStyle.System,
            TabStop = false,
        };
        copyButton.Click += (_, _) => DoCopy();

        var spacer = new Panel { Dock = DockStyle.Right, Width = Px(8) };

        var closeBottom = new Button
        {
            Dock = DockStyle.Right,
            Width = Px(92),
            Text = "关闭",
            FlatStyle = FlatStyle.System,
            TabStop = false,
        };
        closeBottom.Click += (_, _) => Close();

        footer.Controls.Add(_status);
        footer.Controls.Add(copyButton);
        footer.Controls.Add(spacer);
        footer.Controls.Add(closeBottom);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);

        PositionNearCursor();
    }

    private void PositionNearCursor()
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;

        var x = Math.Min(cursor.X + 18, area.Right - Width - 12);
        var y = Math.Min(cursor.Y + 18, area.Bottom - Height - 12);

        Location = new Point(Math.Max(area.Left + 12, x), Math.Max(area.Top + 12, y));
    }

    private void DoCopy()
    {
        if (ClipboardUtil.TrySetText(_text.Text))
        {
            _status.Text = "✓ 已复制到剪贴板";
            _status.ForeColor = Ok;
        }
        else
        {
            _status.Text = "复制失败,请选中后按 Ctrl+C";
            _status.ForeColor = Bad;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _text.Focus();
        _text.SelectAll();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiFont.Dispose();
            _textFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
