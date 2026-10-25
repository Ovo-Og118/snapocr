SnapOCR

框选屏幕上任意区域，用 Windows 自带的识别引擎把文字变成可复制的文本。

 快捷键`Ctrl+Alt+O` 


说明

- 引擎是系统的 `Windows.Media.Ocr`，离线跑，识别质量取决于语言包和字体清晰度。
- 没装识别语言包会提示：设置 → 时间和语言 → 语言和区域 → 语言选项 → 勾选“光学字符识别”。
- 快捷键被别的软件占用了会自动换一个，托盘气泡和日志里都会提示。
- 配置和日志在 `%APPDATA%\SnapOCR\`。

命令

```
SnapOCR.exe                        启动托盘
SnapOCR.exe --file a.png           识别一张图，结果打到 stdout
SnapOCR.exe --file a.png --json    输出 { text, language, languageName }
SnapOCR.exe --list-langs           看系统装了哪些识别语言
```

编译

需要 .NET 8 SDK。

```powershell
dotnet run --project .\src\SnapOCR   
.\build.ps1 
```

MIT
