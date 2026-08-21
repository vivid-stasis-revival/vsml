namespace vividstasisModLoader;

public sealed class VsmlLogEntry
{
    public string Level { get; init; } = "info";
    public string Stage { get; init; } = string.Empty;
    public string? Mod { get; init; }
    public string? Entry { get; init; }
    public string Message { get; init; } = string.Empty;
    public string MessageEn { get; init; } = string.Empty;
}

public interface IVsmlLogger
{
    void Log(VsmlLogEntry entry);
}

internal sealed class NullVsmlLogger : IVsmlLogger
{
    internal static NullVsmlLogger Instance { get; } = new();

    private NullVsmlLogger()
    {
    }

    public void Log(VsmlLogEntry entry)
    {
    }
}

internal sealed class ConsoleVsmlLogger : IVsmlLogger
{
    internal static ConsoleVsmlLogger Instance { get; } = new();

    private ConsoleVsmlLogger()
    {
    }

    public void Log(VsmlLogEntry entry)
    {
        switch (entry.Level)
        {
            case "step":
                ConsoleOutput.PrintStep(entry.Message, entry.MessageEn);
                break;
            case "success":
                ConsoleOutput.PrintSuccess(entry.Message, entry.MessageEn);
                break;
            case "warning":
                ConsoleOutput.PrintWarning(entry.Message, entry.MessageEn);
                break;
            case "error":
                ConsoleOutput.PrintError(entry.Message, entry.MessageEn);
                break;
            case "section":
                ConsoleOutput.PrintSection(entry.Message, entry.MessageEn);
                break;
            default:
                ConsoleOutput.PrintInfo(entry.Message, entry.MessageEn);
                break;
        }
    }
}

internal sealed class CollectingVsmlLogger(IVsmlLogger? downstream = null) : IVsmlLogger
{
    private readonly List<VsmlLogEntry> _entries = [];

    internal IReadOnlyList<VsmlLogEntry> Entries => _entries;

    public void Log(VsmlLogEntry entry)
    {
        _entries.Add(entry);
        downstream?.Log(entry);
    }
}

internal static class VsmlLoggerExtensions
{
    internal static void Info(this IVsmlLogger logger, string stage, string zh, string en, string? mod = null, string? entry = null)
        => logger.Write("info", stage, zh, en, mod, entry);

    internal static void Step(this IVsmlLogger logger, string stage, string zh, string en, string? mod = null, string? entry = null)
        => logger.Write("step", stage, zh, en, mod, entry);

    internal static void Success(this IVsmlLogger logger, string stage, string zh, string en, string? mod = null, string? entry = null)
        => logger.Write("success", stage, zh, en, mod, entry);

    internal static void Warning(this IVsmlLogger logger, string stage, string zh, string en, string? mod = null, string? entry = null)
        => logger.Write("warning", stage, zh, en, mod, entry);

    internal static void Error(this IVsmlLogger logger, string stage, string zh, string en, string? mod = null, string? entry = null)
        => logger.Write("error", stage, zh, en, mod, entry);

    internal static void Section(this IVsmlLogger logger, string stage, string zh, string en, string? mod = null, string? entry = null)
        => logger.Write("section", stage, zh, en, mod, entry);

    private static void Write(
        this IVsmlLogger logger,
        string level,
        string stage,
        string zh,
        string en,
        string? mod,
        string? entry)
    {
        logger.Log(new VsmlLogEntry
        {
            Level = level,
            Stage = stage,
            Mod = mod,
            Entry = entry,
            Message = zh,
            MessageEn = en
        });
    }
}
