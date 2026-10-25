using System;
using System.IO;
using System.Text;

namespace SnapOcr.Core;

/// <summary>
/// 极简日志。只在 %APPDATA%\SnapOCR\snapocr.log 追加,超过 512 KB 自动截断。
/// 用户反馈“按了没反应”时,这份日志基本能直接定位问题。
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    private static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapOCR");

    internal static string FilePath => Path.Combine(Directory, "snapocr.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);

                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    file.Delete();
                }

                File.AppendAllText(
                    FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败绝不影响主流程。
        }
    }

    public static void Error(string context, Exception ex) =>
        Write($"[ERROR] {context}: {ex.GetType().Name}: {ex.Message}");
}
