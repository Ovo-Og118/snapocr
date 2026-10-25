using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SnapOcr.Core;
using SnapOcr.UI;

namespace SnapOcr;

internal sealed class TrayContext : ApplicationContext
{
    private const string AppName = "SnapOCR";

    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly HotkeyWindow _hotkey;
    private readonly EventWaitHandle _activateEvent;
    private readonly System.Windows.Forms.Timer _activateTimer;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _languageMenu;
    private readonly ToolStripMenuItem _startupItem;

    private HotkeySpec _spec;
    private bool _busy;
    private bool _disposed;

    public TrayContext()
    {
        _settings = Settings.Load();

        // 先注册热键:注册结果决定托盘提示和菜单里显示哪个组合键。
        _hotkey = new HotkeyWindow();
        var (spec, hotkeyWarning) = RegisterHotkey();
        _spec = spec;
        _hotkey.Pressed += StartCapture;

        _icon = LoadIcon();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = $"{AppName} · {_spec.Text} 截图识别",
        };
        _tray.DoubleClick += (_, _) => StartCapture();

        _captureItem = new ToolStripMenuItem($"截图识别   ({_spec.Text})");
        _captureItem.Click += (_, _) => StartCapture();

        _languageMenu = new ToolStripMenuItem("识别语言");
        _startupItem = new ToolStripMenuItem("开机启动");
        _startupItem.Click += (_, _) => ToggleStartup();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_captureItem);
        menu.Items.Add(new ToolStripMenuItem("识别图片文件…", null, (_, _) => CaptureFromFile()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_languageMenu);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("关于 " + AppName, null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApp()));
        menu.Opening += (_, _) => RefreshMenuState();
        _tray.ContextMenuStrip = menu;

        BuildLanguageMenu();

        Log.Write($"启动 v{typeof(TrayContext).Assembly.GetName().Version?.ToString(3)},快捷键={_spec.Text},语言={_settings.Language ?? "跟随系统"},可用语言={OcrService.GetAvailableLanguages().Count}");

        if (hotkeyWarning is not null)
        {
            Log.Write("[WARN] " + hotkeyWarning);
            ShowBalloon("全局快捷键", hotkeyWarning, ToolTipIcon.Warning);
        }

        // 让“再次双击 exe”可以触发一次截图,而不是无声无息地退出。
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ActivateEventName);
        _activateTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _activateTimer.Tick += (_, _) =>
        {
            if (_activateEvent.WaitOne(0))
            {
                StartCapture();
            }
        };
        _activateTimer.Start();
    }

    /// <summary>Ctrl+Alt+O 很容易被别的工具占用(截图/录屏/输入法),所以准备一条备用链。</summary>
    private static readonly string[] FallbackHotkeys =
    {
        "Ctrl+Alt+O",
        "Ctrl+Alt+Q",
        "Ctrl+Shift+O",
        "Ctrl+Alt+S",
    };

    private (HotkeySpec Spec, string? Warning) RegisterHotkey()
    {
        var configuredValid = HotkeySpec.TryParse(_settings.Hotkey, out var configured, out var error);

        var candidates = new List<HotkeySpec>();
        if (configuredValid)
        {
            candidates.Add(configured);
        }

        foreach (var text in FallbackHotkeys)
        {
            if (!HotkeySpec.TryParse(text, out var candidate, out _))
            {
                continue;
            }

            if (candidates.Any(c => c.Key == candidate.Key && c.Modifiers == candidate.Modifiers))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            return (HotkeySpec.Default, "快捷键配置无效:" + error);
        }

        foreach (var candidate in candidates)
        {
            if (!_hotkey.TryRegister(candidate))
            {
                continue;
            }

            if (configuredValid && candidate.Key == configured.Key && candidate.Modifiers == configured.Modifiers)
            {
                return (candidate, null);
            }

            var reason = configuredValid
                ? $"{configured.Text} 已被其他程序占用,本次改用 {candidate.Text}。"
                : $"配置里的快捷键无效({error}),本次改用 {candidate.Text}。";

            return (candidate, reason + " 想固定下来就改 " + Settings.ConfigPath);
        }

        return (configured, $"候选快捷键全被占用了,请改 {Settings.ConfigPath} 里的 Hotkey 字段;现在只能从托盘菜单截图。");
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("SnapOCR.app.ico");
        if (stream is not null)
        {
            return new Icon(stream);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    // ---------- 截图流程 ----------

    private async void StartCapture()
    {
        if (_busy || _disposed)
        {
            return;
        }

        _busy = true;
        try
        {
            Log.Write("热键/菜单触发截图");

            // 让快捷键的按键抬起、菜单收起,避免把菜单残影截进去。
            await Task.Delay(60);

            var virtualScreen = SystemInformation.VirtualScreen;
            using var screen = ScreenCapture.Capture(virtualScreen);

            Rectangle region;
            using (var overlay = new CaptureOverlay(screen))
            {
                var dialogResult = overlay.ShowDialog();
                Log.Write($"框选结束:{dialogResult},区域={overlay.SelectedRegion}");

                if (dialogResult != DialogResult.OK || overlay.SelectedRegion is not { } selected)
                {
                    return;
                }

                region = selected;
            }

            region.Intersect(new Rectangle(0, 0, screen.Width, screen.Height));
            if (region.Width < 4 || region.Height < 4)
            {
                return;
            }

            using var crop = screen.Clone(region, PixelFormat.Format32bppArgb);
            SetTrayText("识别中…");

            var started = Environment.TickCount64;
            var result = await OcrService.RecognizeAsync(crop, _settings.Language);
            Log.Write($"识别完成:{(Environment.TickCount64 - started)} ms,{result.Text.Length} 字符,语言={result.LanguageTag}");

            PresentResult(result);
        }
        catch (Exception ex)
        {
            Log.Error("截图识别失败", ex);
            ShowBalloon("识别失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            SetTrayText($"{AppName} · {_spec.Text} 截图识别");
        }
    }

    private async void CaptureFromFile()
    {
        if (_busy || _disposed)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择要识别的图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*",
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _busy = true;
        try
        {
            using var bitmap = new Bitmap(dialog.FileName);
            SetTrayText("识别中…");
            var result = await OcrService.RecognizeAsync(bitmap, _settings.Language);
            PresentResult(result);
        }
        catch (Exception ex)
        {
            ShowBalloon("识别失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            SetTrayText($"{AppName} · {_spec.Text} 截图识别");
        }
    }

    private void PresentResult(OcrOutput result)
    {
        var text = result.Text.Trim();
        if (text.Length == 0)
        {
            Log.Write("识别结果为空");
            ShowBalloon("没有识别到文字", "试试更小更清晰的区域,或确认系统已安装对应语言的识别包。", ToolTipIcon.Info);
            return;
        }

        var copied = _settings.CopyToClipboard && ClipboardUtil.TrySetText(text);

        if (_settings.ShowResultWindow)
        {
            new ResultForm(text, copied, result.LanguageName).Show();
        }
        else if (copied)
        {
            ShowBalloon("已复制到剪贴板", Shorten(text), ToolTipIcon.None);
        }
    }

    // ---------- 菜单 ----------

    private void BuildLanguageMenu()
    {
        _languageMenu.DropDownItems.Clear();

        var languages = OcrService.GetAvailableLanguages();
        if (languages.Count == 0)
        {
            _languageMenu.DropDownItems.Add(new ToolStripMenuItem("未检测到识别语言包") { Enabled = false });
            return;
        }

        var autoItem = new ToolStripMenuItem("跟随系统") { Tag = string.Empty };
        autoItem.Click += (_, _) => SelectLanguage(string.Empty);
        _languageMenu.DropDownItems.Add(autoItem);
        _languageMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var language in languages)
        {
            var item = new ToolStripMenuItem($"{language.Name}  ({language.Tag})") { Tag = language.Tag };
            item.Click += (_, _) => SelectLanguage(language.Tag);
            _languageMenu.DropDownItems.Add(item);
        }

        RefreshLanguageChecks();
    }

    private void SelectLanguage(string tag)
    {
        _settings.Language = string.IsNullOrEmpty(tag) ? null : tag;
        _settings.Save();
        RefreshLanguageChecks();
    }

    private void RefreshLanguageChecks()
    {
        var current = _settings.Language ?? string.Empty;
        foreach (ToolStripItem item in _languageMenu.DropDownItems)
        {
            if (item is ToolStripMenuItem { Tag: string tag } menuItem)
            {
                menuItem.Checked = string.Equals(tag, current, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void ToggleStartup()
    {
        var enable = !Startup.IsEnabled();
        Startup.Set(enable);
        RefreshMenuState();

        ShowBalloon(
            enable ? "已加入开机启动" : "已取消开机启动",
            enable ? "下次登录 Windows 时会自动在托盘运行。" : null,
            ToolTipIcon.None);
    }

    private void RefreshMenuState()
    {
        _startupItem.Checked = Startup.IsEnabled();
        _captureItem.Enabled = !_busy;
    }

    private void ShowAbout()
    {
        var version = typeof(TrayContext).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var languages = OcrService.GetAvailableLanguages();

        var body =
            $"{AppName} {version}\n\n" +
            "离线截图 OCR:框选屏幕上的任意区域,用 Windows 自带的识别引擎\n" +
            "把文字变成可复制的文本。全程不联网、不上传图片。\n\n" +
            $"全局快捷键:{_spec.Text}\n" +
            $"已安装识别语言:{languages.Count} 种\n\n" +
            "MIT License · github.com/your-name/snapocr";

        MessageBox.Show(
            body,
            "关于 " + AppName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ---------- 基础设施 ----------

    private void SetTrayText(string text)
    {
        // NotifyIcon.Text 上限 63 个字符。
        _tray.Text = text.Length <= 63 ? text : text[..63];
    }

    private void ShowBalloon(string title, string? text, ToolTipIcon icon)
    {
        if (_disposed)
        {
            return;
        }

        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = string.IsNullOrWhiteSpace(text) ? title : text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(4000);
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        ExitThread();
    }

    private static string Shorten(string text)
    {
        var single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= 90 ? single : single[..90] + "…";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _activateTimer.Stop();
            _activateTimer.Dispose();
            _hotkey.Dispose();
            _activateEvent.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }
}
