using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SnapOcr.Core;

internal static class ClipboardUtil
{
    /// <summary>剪贴板被别的程序占用时会抛异常,重试几次基本都能成功。</summary>
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
            catch (ThreadStateException)
            {
                return false;
            }
        }

        try
        {
            Clipboard.SetDataObject(text, copy: true, retryTimes: 10, retryDelay: 100);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
