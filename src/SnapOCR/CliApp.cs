using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SnapOcr.Core;

namespace SnapOcr;

/// <summary>
/// 命令行模式。注意本程序是 WinExe(没有控制台子系统的黑框),
/// 所以先 AttachConsole 到调用者控制台,再重新打开 stdout。
/// </summary>
internal static class CliApp
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string UsageText = """
        SnapOCR — 离线截图 OCR(使用 Windows 自带的识别引擎)

        用法:
          SnapOCR.exe                      启动托盘程序(全局快捷键截图识别)
          SnapOCR.exe --file <图片>         识别一张图片,结果输出到 stdout
          SnapOCR.exe --list-langs         列出系统已安装的识别语言
          SnapOCR.exe --help               显示本帮助

        选项:
          -f, --file <path>   输入图片(png / jpg / bmp / gif / tiff)
          -l, --lang <tag>    指定识别语言,如 zh-Hans-CN、en-US、ja-JP
              --json          以 JSON 输出 { text, language, languageName }
          -c, --clipboard     识别后同时写入剪贴板
          -h, --help          显示帮助

        退出码:0 成功 · 1 运行失败 · 2 参数错误

        注意:本程序是 GUI 子系统程序。在 PowerShell 里捕获输出请用管道,
              例如:$t = .\SnapOCR.exe --file a.png | Out-String
        """;

    public static int Run(string[] args)
    {
        var output = OpenOutput();

        try
        {
            return Execute(args, output);
        }
        finally
        {
            output.Flush();
        }
    }

    /// <summary>
    /// GUI 子系统进程默认没有控制台。这里按“已经被重定向 → 直接写句柄”的优先级取输出流,
    /// 只有在确实没有句柄时才去 AttachConsole,避免把调用方给的管道弄丢。
    /// </summary>
    private static TextWriter OpenOutput()
    {
        var handle = NativeInterop.GetStdHandle(NativeInterop.StdOutputHandle);

        if (!NativeInterop.IsValidHandle(handle))
        {
            if (NativeInterop.AttachConsole(NativeInterop.AttachParentProcess))
            {
                const uint openExisting = 3;
                const uint shareReadWrite = 3;
                handle = NativeInterop.CreateFileW(
                    "CONOUT$",
                    NativeInterop.GenericWrite,
                    shareReadWrite,
                    IntPtr.Zero,
                    openExisting,
                    0,
                    IntPtr.Zero);

                // 控制台代码页设成 UTF-8,否则中文会变乱码。
                NativeInterop.SetConsoleOutputCP(65001);
            }
        }

        if (!NativeInterop.IsValidHandle(handle))
        {
            return TextWriter.Null;
        }

        try
        {
            var stream = new FileStream(
                new SafeFileHandle(handle, ownsHandle: false),
                FileAccess.Write,
                bufferSize: 4096);
            return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
        catch
        {
            return TextWriter.Null;
        }
    }

    private static int Execute(string[] args, TextWriter output)
    {
        string? file = null;
        string? language = null;
        var asJson = false;
        var toClipboard = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                case "/?":
                    output.Write(UsageText);
                    return 0;

                case "--list-langs":
                case "--list-languages":
                    return ListLanguages(output);

                case "-f":
                case "--file":
                    if (++i >= args.Length)
                    {
                        return Fail(output, "缺少 --file 的参数值。");
                    }

                    file = args[i];
                    continue;

                case "-l":
                case "--lang":
                    if (++i >= args.Length)
                    {
                        return Fail(output, "缺少 --lang 的参数值。");
                    }

                    language = args[i];
                    continue;

                case "--json":
                    asJson = true;
                    continue;

                case "-c":
                case "--clipboard":
                    toClipboard = true;
                    continue;

                default:
                    return Fail(output, $"无法识别的参数:{arg}");
            }
        }

        if (file is null)
        {
            return Fail(output, "缺少 --file 参数。");
        }

        var path = Path.GetFullPath(file);
        if (!File.Exists(path))
        {
            output.WriteLine($"找不到文件:{path}");
            return 1;
        }

        try
        {
            OcrOutput result;
            using (var bitmap = new Bitmap(path))
            {
                result = OcrService.RecognizeAsync(bitmap, language).GetAwaiter().GetResult();
            }

            if (toClipboard)
            {
                ClipboardUtil.TrySetText(result.Text);
            }

            if (asJson)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        text = result.Text,
                        language = result.LanguageTag,
                        languageName = result.LanguageName,
                    },
                    JsonOptions));
            }
            else
            {
                output.WriteLine(result.Text);
            }

            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine("识别失败:" + ex.Message);
            return 1;
        }
    }

    private static int ListLanguages(TextWriter output)
    {
        var languages = OcrService.GetAvailableLanguages();
        if (languages.Count == 0)
        {
            output.WriteLine("没有安装任何 OCR 识别语言包。");
            return 1;
        }

        foreach (var language in languages)
        {
            output.WriteLine($"{language.Tag}\t{language.Name}");
        }

        return 0;
    }

    private static int Fail(TextWriter output, string message)
    {
        output.WriteLine("错误:" + message);
        output.WriteLine();
        output.Write(UsageText);
        return 2;
    }
}
