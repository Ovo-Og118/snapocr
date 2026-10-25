using System;
using System.Windows.Forms;

namespace SnapOcr.Core;

/// <summary>
/// 一个不可见的消息窗口,只用来接收 WM_HOTKEY。
/// 用 NativeWindow 而不是 Form,是为了避免创建真正的窗体(不闪任务栏、不占焦点)。
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA11;

    private bool _registered;
    private bool _disposed;

    public event Action? Pressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "SnapOCR.Hotkey" });
    }

    public bool TryRegister(HotkeySpec spec)
    {
        Unregister();
        _registered = NativeInterop.RegisterHotKey(
            Handle,
            HotkeyId,
            spec.Modifiers | NativeInterop.ModNoRepeat,
            (uint)spec.Key);
        return _registered;
    }

    private void Unregister()
    {
        if (!_registered || _disposed)
        {
            return;
        }

        NativeInterop.UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _disposed = true;

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
