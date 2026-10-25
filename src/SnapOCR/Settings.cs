using System;
using System.IO;
using System.Text.Json;

namespace SnapOcr;

internal sealed class Settings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+O";

    /// <summary>BCP-47 语言标记,如 zh-Hans-CN;null 表示跟随系统。</summary>
    public string? Language { get; set; }

    public bool CopyToClipboard { get; set; } = true;

    public bool ShowResultWindow { get; set; } = true;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapOCR");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>配置文件位置,用于在提示里告诉用户去哪儿改。</summary>
    public static string ConfigPath => FilePath;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回退到默认值,不打扰用户。
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 配置写入失败不影响主流程。
        }
    }
}
