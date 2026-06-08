using Spectre.Console;
using System.Text;

namespace vividstasisModLoader;

/// <summary>
/// 统一管理控制台双语输出与样式渲染。
/// </summary>
internal static class ConsoleOutput
{
    private static readonly object LogLock = new();
    private static readonly string LogDirectoryPath = string.Empty;
    private static readonly string LogFilePath = string.Empty;
    private static readonly StreamWriter? LogWriter;

    static ConsoleOutput()
    {
        try
        {
            LogDirectoryPath = Path.Combine(AppContext.BaseDirectory, "app-logs");
            Directory.CreateDirectory(LogDirectoryPath);

            LogFilePath = Path.Combine(LogDirectoryPath, $"vsml-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            LogWriter = new StreamWriter(LogFilePath, false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                lock (LogLock)
                {
                    LogWriter?.Dispose();
                }
            };

            WriteLogLine("SYSTEM", "日志已初始化。", $"Log initialized at: {LogFilePath}");
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

        if (vividstasisModLoader.TVOClientCommunicate.PipeClient.IPCMode)
        {
            try
            {
                // 发送日志信息到BVOClient
                vividstasisModLoader.TVOClientCommunicate.PipeClient.SendMessage($"[{mappedLevel}] {zh}");
            }
            catch { }
        }

        if (LogWriter is null)
        {
            return;
        }

        lock (LogLock)
        {
            LogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{mappedLevel}] {zh} ({en})");
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
        AnsiConsole.MarkupLine($"[cyan]信息[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("INFO", zh, en);
    }

    /// <summary>
    /// 输出双语的步骤提示。
    /// </summary>
    internal static void PrintStep(string zh, string en)
    {
        AnsiConsole.MarkupLine($"[deepskyblue2]步骤[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("STEP", zh, en);
    }

    /// <summary>
    /// 输出双语的成功提示。
    /// </summary>
    internal static void PrintSuccess(string zh, string en)
    {
        AnsiConsole.MarkupLine($"[green]成功[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("SUCCESS", zh, en);
    }

    /// <summary>
    /// 输出双语的警告提示。
    /// </summary>
    internal static void PrintWarning(string zh, string en)
    {
        AnsiConsole.MarkupLine($"[yellow]警告[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("WARN", zh, en);
    }

    /// <summary>
    /// 输出双语的错误提示。
    /// </summary>
    internal static void PrintError(string zh, string en)
    {
        AnsiConsole.MarkupLine($"[red]错误[/][white] {EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]");
        WriteLogLine("ERROR", zh, en);
    }

    /// <summary>
    /// 输出分节标题，提升流程可读性。
    /// </summary>
    internal static void PrintSection(string zh, string en)
    {
        var title = $"[bold orange1]{EscapeMarkup(zh)}[/] [grey]({EscapeMarkup(en)})[/]";
        AnsiConsole.Write(new Rule(title));
        WriteLogLine("SECTION", zh, en);
    }

    /// <summary>
    /// 输出程序启动 Banner（矩形方框），展示标题、版本与变更日志。
    /// </summary>
    internal static void PrintAppBanner(string titleZh, string titleEn, string version, string changeLog)
    {
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
        AnsiConsole.MarkupLine("[green]还原模式已完成。[/] [grey](Restore mode completed.)[/]");
        WriteLogLine("SUCCESS", "还原模式已完成。", "Restore mode completed.");
    }

    /// <summary>
    /// 输出结束提示并等待用户按下回车。
    /// </summary>
    internal static void PrintPauseHint()
    {
        if (vividstasisModLoader.TVOClientCommunicate.PipeClient.IPCMode)
        {
            AnsiConsole.MarkupLine("[green]修补完成，IPC模式将自动退出。[/] [grey](Patching completed, auto exit.)[/]");
            WriteLogLine("INFO", "修补完成，IPC模式自动退出。", "Patching completed, auto exit.");
            return;
        }

        AnsiConsole.MarkupLine("[green]修补完成，按 Enter 退出。[/] [grey](Patching completed, press Enter to exit.)[/]");
        WriteLogLine("INFO", "修补完成，等待用户按 Enter 退出。", "Patching completed, waiting for Enter to exit.");
        Console.ReadLine();
    }
}
