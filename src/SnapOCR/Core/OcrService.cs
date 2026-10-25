using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnapOcr.Core;

internal sealed record OcrLanguage(string Tag, string Name);

internal sealed record OcrOutput(string Text, string LanguageTag, string LanguageName);

/// <summary>
/// 包一层 Windows.Media.Ocr。完全离线:识别模型随系统语言包安装,
/// 不需要联网、不打包任何模型文件,进程体积只有几百 KB。
/// </summary>
internal static class OcrService
{
    public static IReadOnlyList<OcrLanguage> GetAvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => new OcrLanguage(l.LanguageTag, l.DisplayName))
                .OrderBy(l => l.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<OcrLanguage>();
        }
    }

    public static async Task<OcrOutput> RecognizeAsync(Bitmap bitmap, string? preferredLanguage)
    {
        var (engine, language) = CreateEngine(preferredLanguage);
        var png = Encode(bitmap);

        var primary = await RecognizeLinesAsync(engine, png).ConfigureAwait(false);

        // 中文引擎认汉字很准,但会把 Hello 认成 He110、2026 认成 2926。
        // 所以再用英文引擎跑一遍,把“一个汉字都没有的行”换成英文引擎的版本;
        // 有汉字的行一律保留中文引擎的结果。
        if (IsCjk(language.Tag))
        {
            var english = GetAvailableLanguages()
                .FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));

            if (english is not null && !string.Equals(english.Tag, language.Tag, StringComparison.OrdinalIgnoreCase))
            {
                var englishEngine = OcrEngine.TryCreateFromLanguage(new Language(english.Tag));
                if (englishEngine is not null)
                {
                    var secondary = await RecognizeLinesAsync(englishEngine, png).ConfigureAwait(false);
                    var merged = MergeLines(primary, secondary);

                    return new OcrOutput(
                        string.Join("\n", OrderLines(merged).Select(l => l.Text)).TrimEnd(),
                        $"{language.Tag}+{english.Tag}",
                        $"{language.Name} + {english.Name}");
                }
            }
        }

        return new OcrOutput(
            string.Join("\n", OrderLines(primary).Select(l => l.Text)).TrimEnd(),
            language.Tag,
            language.Name);
    }

    /// <summary>
    /// 系统引擎给的行序在稀疏版面(大段空白、标题+正文)上会乱跳,读出来像是串行了。
    /// 这里按“先从上到下、同一行内从左到右”重排一遍。
    /// </summary>
    private static List<LineBox> OrderLines(List<LineBox> lines)
    {
        if (lines.Count < 2)
        {
            return lines;
        }

        var heights = lines.Select(l => l.Rect.Height).Where(h => h > 0).OrderBy(h => h).ToList();
        var medianHeight = heights.Count > 0 ? heights[heights.Count / 2] : 0f;
        var tolerance = Math.Max(4f, medianHeight * 0.6f);

        var rows = new List<List<LineBox>>();

        foreach (var line in lines.OrderBy(l => l.Rect.Top))
        {
            var row = rows.Count > 0 ? rows[^1] : null;

            if (row is not null && Math.Abs(RowCenter(row) - (line.Rect.Top + line.Rect.Height / 2)) <= tolerance)
            {
                row.Add(line);
            }
            else
            {
                rows.Add(new List<LineBox> { line });
            }
        }

        return rows.SelectMany(r => r.OrderBy(l => l.Rect.Left)).ToList();
    }

    private static float RowCenter(List<LineBox> row) =>
        row.Average(l => l.Rect.Top + l.Rect.Height / 2);

    /// <summary>把位图编码成 PNG 字节,超长边先等比缩小到引擎上限。</summary>
    private static byte[] Encode(Bitmap bitmap)
    {
        var maxDimension = (int)OcrEngine.MaxImageDimension;
        Bitmap? scaled = null;

        try
        {
            var source = bitmap;
            if (bitmap.Width > maxDimension || bitmap.Height > maxDimension)
            {
                var factor = Math.Min(
                    (double)maxDimension / bitmap.Width,
                    (double)maxDimension / bitmap.Height);
                var size = new Size(
                    Math.Max(1, (int)(bitmap.Width * factor)),
                    Math.Max(1, (int)(bitmap.Height * factor)));
                scaled = new Bitmap(bitmap, size);
                source = scaled;
            }

            using var buffer = new MemoryStream();
            source.Save(buffer, ImageFormat.Png);
            return buffer.ToArray();
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    private static async Task<List<LineBox>> RecognizeLinesAsync(OcrEngine engine, byte[] png)
    {
        // WinRT 的解码器只吃 IRandomAccessStream,所以先把 PNG 写进去。
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);

        using var softwareBitmap = await decoder
            .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
            .AsTask()
            .ConfigureAwait(false);

        var result = await engine.RecognizeAsync(softwareBitmap).AsTask().ConfigureAwait(false);
        return ToLineBoxes(result);
    }

    /// <summary>
    /// 两遍识别的行数经常不一样(比如英文被拆成两行),所以按“竖直位置重叠”配对,
    /// 而不是按下标硬对。只替换文字,不改变行结构,避免丢内容。
    /// </summary>
    private static List<LineBox> MergeLines(List<LineBox> primary, List<LineBox> secondary)
    {
        var merged = new List<LineBox>(primary.Count);
        var used = new bool[secondary.Count];
        var previousMatch = -1;

        foreach (var line in primary)
        {
            var match = -1;

            if (IsLatinLine(line.Text))
            {
                match = BestMatch(secondary, used, line);
            }

            if (match < 0)
            {
                // 中文引擎把一行英文拆成两行时,第二段没得配;它和上一段其实同源,丢掉。
                if (previousMatch >= 0 && IsLatinLine(line.Text))
                {
                    var previous = secondary[previousMatch];
                    var shorter = Math.Min(previous.Rect.Height, line.Rect.Height);
                    if (shorter > 0 && VerticalOverlap(previous.Rect, line.Rect) > shorter * 0.5f)
                    {
                        continue;
                    }
                }

                merged.Add(line);
                previousMatch = -1;
                continue;
            }

            used[match] = true;

            // 中文引擎把一整行拆成两行时,两行会命中同一条英文行 —— 只输出一次。
            if (match == previousMatch)
            {
                continue;
            }

            merged.Add(line with { Text = secondary[match].Text });
            previousMatch = match;
        }

        return merged;
    }

    /// <summary>判断这一行是不是“以拉丁文字为主”,用来决定要不要采用英文引擎的版本。</summary>
    private static bool IsLatinLine(string text) => AsciiRatio(text) >= 0.75f;

    private static int BestMatch(List<LineBox> secondary, bool[] used, LineBox line)
    {
        var best = -1;
        var bestScore = 0f;

        for (var i = 0; i < secondary.Count; i++)
        {
            if (used[i] || secondary[i].Text.Length == 0 || !IsLatinLine(secondary[i].Text))
            {
                continue;
            }

            var overlap = VerticalOverlap(secondary[i].Rect, line.Rect);
            var shorter = Math.Min(secondary[i].Rect.Height, line.Rect.Height);
            if (shorter <= 0 || overlap <= shorter * 0.5f)
            {
                continue;
            }

            var score = AsciiRatio(secondary[i].Text);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    private static float VerticalOverlap(RectangleF a, RectangleF b)
    {
        var top = Math.Max(a.Top, b.Top);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return Math.Max(0, bottom - top);
    }

    private static float AsciiRatio(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var ascii = text.Count(c => c is >= ' ' and <= '~');
        return (float)ascii / text.Length;
    }

    private static bool HasCjk(string text) => text.Any(c => c is >= '\u2E80' and <= '\u9FFF'
        or >= '\uF900' and <= '\uFAFF'
        or >= '\u3040' and <= '\u30FF'
        or >= '\uAC00' and <= '\uD7AF');

    private static bool IsCjk(string languageTag) =>
        languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        || languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
        || languageTag.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    /// <summary>单个识别行:文字 + 由词的边界框拼出来的整行位置。</summary>
    private sealed record LineBox(string Text, RectangleF Rect);

    /// <summary>创建一个识别引擎,优先使用用户指定的语言。</summary>
    public static (OcrEngine Engine, OcrLanguage Language) CreateEngine(string? preferredTag)
    {
        var available = GetAvailableLanguages();
        if (available.Count == 0)
        {
            throw new InvalidOperationException(
                "系统里没有安装任何 OCR 识别语言包。请打开 " +
                "设置 → 时间和语言 → 语言和区域 → 对应语言 → 语言选项,勾选“光学字符识别”。");
        }

        OcrLanguage? target = null;

        if (!string.IsNullOrWhiteSpace(preferredTag))
        {
            target = available.FirstOrDefault(
                         l => string.Equals(l.Tag, preferredTag, StringComparison.OrdinalIgnoreCase))
                     ?? available.FirstOrDefault(
                         l => l.Tag.StartsWith(preferredTag, StringComparison.OrdinalIgnoreCase));
        }

        if (target is null)
        {
            // 跟随系统首选语言。
            var fromProfile = OcrEngine.TryCreateFromUserProfileLanguages();
            if (fromProfile is not null)
            {
                var lang = fromProfile.RecognizerLanguage;
                target = available.FirstOrDefault(
                    l => string.Equals(l.Tag, lang.LanguageTag, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    var fallback = new OcrLanguage(lang.LanguageTag, lang.DisplayName);
                    return (fromProfile, fallback);
                }
            }
        }

        target ??= available.FirstOrDefault(
                       l => l.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                   ?? available[0];

        var engine = OcrEngine.TryCreateFromLanguage(new Language(target.Tag))
                     ?? throw new InvalidOperationException($"无法创建 {target.Name} 的识别引擎。");

        return (engine, target);
    }

    /// <summary>
    /// Windows 返回的每个词都带边界框,直接 Join(" ") 会把中文拆成“你 好 世 界”,
    /// 所以按词间距判断要不要插空格,遇到 CJK 字符一律不插。
    /// </summary>
    private static List<LineBox> ToLineBoxes(OcrResult result)
    {
        var lines = new List<LineBox>();

        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            var text = ComposeLine(line);
            if (text.Length == 0)
            {
                continue;
            }

            lines.Add(new LineBox(text, BoundsOf(line)));
        }

        return lines;
    }

    private static string ComposeLine(OcrLine line)
    {
        var builder = new StringBuilder();

        var previousRight = double.NaN;
        var previousHeight = 0.0;
        var previousText = string.Empty;
        var firstInLine = true;

        foreach (var word in line.Words)
        {
            var text = word.Text ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            if (!firstInLine && !double.IsNaN(previousRight))
            {
                var rect = word.BoundingRect;
                var gap = rect.X - previousRight;
                var referenceHeight = Math.Max(previousHeight, rect.Height);

                var needSpace = gap > referenceHeight * 0.22
                                && !IsCjk(previousText[^1])
                                && !IsCjk(text[0])
                                && !StartsWithClosingPunctuation(text);

                if (needSpace)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(text);

            previousRight = word.BoundingRect.X + word.BoundingRect.Width;
            previousHeight = word.BoundingRect.Height;
            previousText = text;
            firstInLine = false;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>由行内所有词的框拼出整行位置,用于两遍识别之间配对。</summary>
    private static RectangleF BoundsOf(OcrLine line)
    {
        float left = float.MaxValue, top = float.MaxValue;
        float right = float.MinValue, bottom = float.MinValue;

        foreach (var word in line.Words)
        {
            var rect = word.BoundingRect;
            left = Math.Min(left, (float)rect.X);
            top = Math.Min(top, (float)rect.Y);
            right = Math.Max(right, (float)(rect.X + rect.Width));
            bottom = Math.Max(bottom, (float)(rect.Y + rect.Height));
        }

        return left > right ? RectangleF.Empty : RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static bool StartsWithClosingPunctuation(string text) =>
        text.Length > 0 && text[0] is '.' or ',' or ';' or ':' or ')' or ']' or '}' or '!' or '?'
            or '。' or '，' or '、' or '；' or '：' or '）' or '】' or '！' or '？';

    private static bool IsCjk(char c) =>
        c is >= '\u2E80' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFF00' and <= '\uFFEF'
            or >= '\u3000' and <= '\u303F';
}
