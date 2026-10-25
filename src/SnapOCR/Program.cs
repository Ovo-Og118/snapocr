using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SnapOcr;

internal static class Program
{
    private const string MutexName = @"Local\SnapOCR.SingleInstance";

    /// <summary>被第二个实例 Set 时,已运行的托盘程序立刻开始一次截图识别。</summary>
    internal const string ActivateEventName = @"Local\SnapOCR.Activate";

    [STAThread]
    private static int Main(string[] args)
    {
        // --tray 由开机自启项写入,表示“安静地启动托盘”,不算 CLI 模式。
        var argv = args
            .Where(a => !string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (argv.Length > 0)
        {
            return CliApp.Run(argv);
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // 已经在运行:通知它开始截图,然后退出。
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
                handle.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 旧实例还没建好句柄,忽略。
            }

            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayContext());

        GC.KeepAlive(mutex);
        return 0;
    }
}
