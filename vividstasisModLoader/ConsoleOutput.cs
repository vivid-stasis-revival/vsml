using Spectre.Console;
using System.Text;

namespace vividstasisModLoader;

/// <summary>
/// 统一管理控制台双语输出与样式渲染。
/// </summary>
internal static class ConsoleOutput
{
    private static bool _silentMode;

    internal static bool IsSilentMode => _silentMode;

    internal static void Configure(string[] args)
    {
        _silentMode = args.Any(arg =>
            string.Equals(arg, "-slient", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--slient", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-silent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));

        var configuredLogDirectory = GetOptionValue(args, "--app-logs")
            ?? GetOptionValue(args, "--log-dir")
            ?? Path.Combine(AppContext.BaseDirectory, "app-logs");
        _logDirectoryPath = Path.GetFullPath(configuredLogDirectory);
        InitializeLogging();

        if (_silentMode)
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }
    }

    internal static void AllocateExternalConsole(int codeCount)
    {
        PrintInfo(
            $"代码补丁数量为 {codeCount}，跨平台版本将继续记录到当前日志文件。",
            $"Code patch count is {codeCount}; the cross-platform build will continue logging to the current log file."
        );
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = args[index + 1];
            return value.StartsWith("-", StringComparison.Ordinal) ? null : value;
        }

        return null;
    }

    private static readonly object LogLock = new();
    private static string _logDirectoryPath = string.Empty;
    private static string _logFilePath = string.Empty;
    private static StreamWriter? _logWriter;
    private static bool _loggingInitialized;

    private static void InitializeLogging()
    {
        if (_loggingInitialized)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_logDirectoryPath);

            _logFilePath = Path.Combine(_logDirectoryPath, $"vsml-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logWriter = new StreamWriter(_logFilePath, false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                lock (LogLock)
                {
                    _logWriter?.Dispose();
                }
            };

            _loggingInitialized = true;
            WriteLogLine("SYSTEM", "日志已初始化。", $"Log initialized at: {_logFilePath}");
        }
        catch
        {
            // 日志初始化失败时不中断主流程。
        }
    }

    private static string TranslateLevel(string level)
    {
        return level switch
        {
            "INFO" => "VML信息",
            "STEP" => "VML步骤",
            "SUCCESS" => "VML成功",
            "WARN" => "VML警告",
            "ERROR" => "VML错误",
            "SECTION" => "VML阶段",
            "SYSTEM" => "VML系统",
            "INPUT" => "VML输入",
            _ => $"VML{level}"
        };
    }

    /// <summary>
    /// 将日志写入 app-logs 目录下的当前运行日志文件。
    /// </summary>
    private static void WriteLogLine(string level, string zh, string en)
    {
        string mappedLevel = TranslateLevel(level);

        if (_logWriter is null)
        {
            return;
        }

        lock (LogLock)
        {
            _logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{mappedLevel}] {zh} ({en})");
        }
    }

    /// <summary>
    /// 对文本进行转义，避免 Spectre Markup 特殊字符导致渲染异常。
    /// </summary>
    private static string EscapeMarkup(string text)
    {
        return Markup.Escape(text);
    }

    /// <summary>
    /// 输出双语的信息提示。
    /// </summary>
    internal static void PrintInfo(string zh, string en)
    {
        if (!_silentMode)
            AnsiConsole.MarkupLine($"[cyan]信息[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("INFO", zh, en);
    }

    /// <summary>
    /// 输出双语的步骤提示。
    /// </summary>
    internal static void PrintStep(string zh, string en)
    {
        if (!_silentMode)
            AnsiConsole.MarkupLine($"[deepskyblue2]步骤[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("STEP", zh, en);
    }

    /// <summary>
    /// 输出双语的成功提示。
    /// </summary>
    internal static void PrintSuccess(string zh, string en)
    {
        if (!_silentMode)
            AnsiConsole.MarkupLine($"[green]成功[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("SUCCESS", zh, en);
    }

    /// <summary>
    /// 输出双语的警告提示。
    /// </summary>
    internal static void PrintWarning(string zh, string en)
    {
        if (!_silentMode)
            AnsiConsole.MarkupLine($"[yellow]警告[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("WARN", zh, en);
    }

    /// <summary>
    /// 输出双语的错误提示。
    /// </summary>
    internal static void PrintError(string zh, string en)
    {
        if (!_silentMode)
            AnsiConsole.MarkupLine($"[red]错误[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("ERROR", zh, en);
    }

    /// <summary>
    /// 输出分节标题，提升流程可读性。
    /// </summary>
    internal static void PrintSection(string zh, string en)
    {
        if (!_silentMode)
        {
            var title = $"[bold orange1]{EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]";
            AnsiConsole.Write(new Rule(title));
        }
        WriteLogLine("SECTION", zh, en);
    }

    /// <summary>
    /// 输出程序启动 Banner（矩形方框），展示标题、版本与变更日志。
    /// </summary>
    internal static void PrintAppBanner(string titleZh, string titleEn, string version, string changeLog)
    {
        if (_silentMode)
        {
            WriteLogLine("SECTION", $"{titleZh} | 版本: {version}", $"{titleEn} | Version: {version}");
            WriteLogLine("INFO", $"变更日志: {changeLog}", $"Change log: {changeLog}");
            return;
        }

        var panelContent = new Rows(
            new Align(new Markup($"[bold orange1]{EscapeMarkup(titleZh)}[/]"), HorizontalAlignment.Center),
            new Align(new Markup($"[grey]{EscapeMarkup(titleEn)}[/]"), HorizontalAlignment.Center),
            new Markup(string.Empty),
            new Markup($"[aqua]VERSION[/]: [white]{EscapeMarkup(version)}[/]"),
            new Markup($"[aqua]CHANGE_LOG[/]: [white]{EscapeMarkup(changeLog)}[/]")
        );

        var panel = new Panel(panelContent)
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.Write(panel);
        WriteLogLine("SECTION", $"{titleZh} | 版本: {version}", $"{titleEn} | Version: {version}");
        WriteLogLine("INFO", $"变更日志: {changeLog}", $"Change log: {changeLog}");
    }

    /// <summary>
    /// 输出运行路径不安全提示，并在确认后暂停退出。
    /// </summary>
    internal static void PrintUnsafeRunPathPause(string path, string reasonZh, string reasonEn)
    {
        if (_silentMode)
        {
            WriteLogLine("WARN", $"运行路径不安全：{path}", $"Unsafe run path: {path}");
            WriteLogLine("WARN", reasonZh, reasonEn);
            return;
        }

        var panelContent = new Rows(
            new Align(new Markup("[bold red]运行路径不安全[/]"), HorizontalAlignment.Center),
            new Align(new Markup("[grey]Unsafe run path[/]"), HorizontalAlignment.Center),
            new Markup(string.Empty),
            new Markup($"[white]{EscapeMarkup(path)}[/]"),
            new Markup(string.Empty),
            new Markup($"[yellow]{EscapeMarkup(reasonZh)}[/]"),
            new Markup($"[grey]({EscapeMarkup(reasonEn)})[/]")
        );

        var panel = new Panel(panelContent)
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.Write(panel);
        WriteLogLine("WARN", $"运行路径不安全：{path}", $"Unsafe run path: {path}");
        WriteLogLine("WARN", reasonZh, reasonEn);
        Console.ReadLine();
    }

    /// <summary>
    /// 读取双语提示下的游戏路径输入。
    /// </summary>
    internal static string AskGamePath()
    {
        if (_silentMode)
        {
            WriteLogLine("ERROR", "静默模式无法请求交互式游戏路径。", "Silent mode cannot prompt for an interactive game path.");
            return string.Empty;
        }

        WriteLogLine("INPUT", "请求输入游戏路径。", "Prompting for game path.");
        var gamePath = AnsiConsole.Ask<string>("[yellow]请输入游戏路径 / Please input the game path:[/]");
        WriteLogLine("INPUT", $"输入游戏路径：{gamePath}", $"Input game path: {gamePath}");
        return gamePath;
    }

    /// <summary>
    /// 输出还原模式完成提示。
    /// </summary>
    internal static void PrintRestoreModeCompleted()
    {
        if (!_silentMode)
            AnsiConsole.MarkupLine("[green]还原模式已完成。[/] [grey](Restore mode completed.)[/]");
        WriteLogLine("SUCCESS", "还原模式已完成。", "Restore mode completed.");
    }

    /// <summary>
    /// 输出结束提示并等待用户按下回车。
    /// </summary>
    internal static void PrintPauseHint()
    {
        if (_silentMode)
        {
            WriteLogLine("INFO", "修补完成，静默模式自动退出。", "Patching completed, silent mode auto exit.");
            return;
        }

        AnsiConsole.MarkupLine("[green]修补完成，按 Enter 退出。[/] [grey](Patching completed, press Enter to exit.)[/]");
        WriteLogLine("INFO", "修补完成，等待用户按 Enter 退出。", "Patching completed, waiting for Enter to exit.");
        Console.ReadLine();
    }
}
