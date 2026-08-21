using System.Diagnostics;
using System.Runtime.InteropServices;

namespace vividstasisModLoader;

public static class VsmlApi
{
    public const int AbiVersion = 1;

    private static readonly object InstallLock = new();

    public static VsmlVersionInfo GetVersion() => new()
    {
        AbiVersion = AbiVersion,
        Version = AppInfo.VERSION,
        Runtime = RuntimeInformation.FrameworkDescription
    };

    public static VsmlReviewResult Review(VsmlInstallRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reviews = new VsmlEngine().Review(request).ToList();
            return new VsmlReviewResult
            {
                Ok = true,
                Mods = reviews,
                Warnings = reviews.SelectMany(mod => mod.Warnings).Distinct().ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception exception)
        {
            return new VsmlReviewResult
            {
                Ok = false,
                Error = MapError(exception, "VSML_REVIEW_FAILED", "检查模组失败。"),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    public static VsmlInstallResult Install(VsmlInstallRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = new CollectingVsmlLogger();
        try
        {
            VsmlEngineInstallOutcome outcome;
            lock (InstallLock)
            {
                outcome = new VsmlEngine(logger).Install(request);
            }

            return new VsmlInstallResult
            {
                Ok = true,
                Warnings = GetWarnings(logger),
                ChangedFiles = outcome.ChangedFiles,
                Mods = outcome.Mods,
                Logs = logger.Entries.ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception exception)
        {
            return new VsmlInstallResult
            {
                Ok = false,
                Error = MapError(exception, "VSML_INSTALL_FAILED", "安装模组失败。"),
                Warnings = GetWarnings(logger),
                Logs = logger.Entries.ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    public static VsmlRestoreResult Restore(VsmlRestoreRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = new CollectingVsmlLogger();
        try
        {
            VsmlEngineRestoreOutcome outcome;
            lock (InstallLock)
            {
                outcome = new VsmlEngine(logger).Restore(request);
            }

            return new VsmlRestoreResult
            {
                Ok = true,
                Warnings = GetWarnings(logger),
                ChangedFiles = outcome.ChangedFiles,
                Logs = logger.Entries.ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception exception)
        {
            return new VsmlRestoreResult
            {
                Ok = false,
                Error = MapError(exception, "VSML_RESTORE_FAILED", "还原备份失败。"),
                Warnings = GetWarnings(logger),
                Logs = logger.Entries.ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    public static VsmlValidationResult ValidateGame(string gameDirectory)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = new CollectingVsmlLogger();
        try
        {
            var outcome = new VsmlEngine(logger).ValidateGameContents(gameDirectory);
            return new VsmlValidationResult
            {
                Ok = true,
                GameDirectory = outcome.GameDirectory,
                DataFile = outcome.DataFile,
                VersionFile = outcome.VersionFile,
                GameVersion = outcome.GameVersion,
                DataFileSize = outcome.DataFileSize,
                DataSha256 = outcome.DataSha256,
                ParsedSuccessfully = true,
                Warnings = GetWarnings(logger),
                Logs = logger.Entries.ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception exception)
        {
            return new VsmlValidationResult
            {
                Ok = false,
                Error = MapError(exception, "VSML_GAME_VALIDATION_FAILED", "游戏目录检查失败。"),
                GameDirectory = gameDirectory,
                Warnings = GetWarnings(logger),
                Logs = logger.Entries.ToList(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static List<string> GetWarnings(CollectingVsmlLogger logger)
        => logger.Entries
            .Where(entry => entry.Level == "warning")
            .Select(entry => entry.Message)
            .Distinct()
            .ToList();

    private static VsmlError MapError(Exception exception, string fallbackCode, string fallbackMessage)
    {
        if (exception is VsmlException vsmlException)
        {
            return vsmlException.ToError();
        }

        return new VsmlError
        {
            Code = fallbackCode,
            Message = fallbackMessage,
            Detail = exception.ToString()
        };
    }
}
