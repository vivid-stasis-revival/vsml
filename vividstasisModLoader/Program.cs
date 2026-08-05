using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using UndertaleModLib;
using vividstasisModLoader;
using vividstasisModLoader.TVOClientCommunicate;
using static vividstasisModLoader.ConsoleOutput;
string? gamePathForBackupCleanup = null;
var dryRunForBackupCleanup = false;


// 版本以及修改日志移至AppInfo.cs以便于在不同模块统一引用
// string VERSION = AppInfo.VERSION;
// string CHANGE_LOG = AppInfo.CHANGE_LOG;

// 判断路径中是否包含中文或少量真正危险的特殊字符。
bool HasUnsafePathCharacters(string path)
{
    foreach (var ch in path)
    {
        if (IsChineseCharacter(ch))
        {
            return true;
        }

        if (ch is '*' or '?' or '"' or '<' or '>' or '|')
        {
            return true;
        }
    }

    return false;
}

// 判断字符是否属于常见中文字符范围。
bool IsChineseCharacter(char ch)
{
    return (ch >= '\u4e00' && ch <= '\u9fff')
        || (ch >= '\u3400' && ch <= '\u4dbf')
        || (ch >= '\uF900' && ch <= '\uFAFF');
}

// 判断命令行参数中是否包含目标参数。
bool HasArg(string[] inputArgs, string expected)
{
    foreach (var arg in inputArgs)
    {
        if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

// 解析命令行参数，判断是否为还原模式。
bool IsRestoreMode(string[] inputArgs)
{
    return HasArg(inputArgs, "restore");
}

// 解析命令行参数，判断是否为 dry-run 模式。
bool IsDryRunMode(string[] inputArgs)
{
    return HasArg(inputArgs, "--dry-run") || HasArg(inputArgs, "dry-run");
}

// 生成一键还原脚本，便于用户快速回退。
void CreateRestoreScript(bool dryRun)
{
    if (dryRun)
    {
        PrintInfo("[dry-run] 将生成 restore.cmd 脚本。", "[dry-run] Would create restore.cmd script.");
        return;
    }

    File.WriteAllText("./restore.cmd", "vividstasisModLoader restore");
}

// 读取配置文件，若不存在或解析失败则返回默认配置。
ModLoaderConfig LoadConfig()
{
    if (!File.Exists("./config.json"))
    {
        return new ModLoaderConfig();
    }

    var config = SimpleJson.ParseModLoaderConfig(File.ReadAllText("./config.json"));
    return config ?? new ModLoaderConfig();
}

// 判断候选目录是否为可读取的 vivid/stasis 游戏目录。
bool IsUsableGamePath(string? gamePath)
{
    return !string.IsNullOrWhiteSpace(gamePath)
        && Directory.Exists(gamePath)
        && File.Exists(Path.Combine(gamePath, "data.win"));
}

// 从 VML 可执行文件目录创建或读取 path.json。
// 通过 BVO 启动时 AppContext.BaseDirectory 同样指向 BVO 的 vml 目录。
GamePathConfig LoadOrCreateGamePathConfig()
{
    var pathConfigFile = Path.Combine(AppContext.BaseDirectory, "path.json");
    var defaultConfig = new GamePathConfig
    {
        ForceCustomPath = false
    };

    if (!File.Exists(pathConfigFile))
    {
        try
        {
            var json = SimpleJson.ToJson(defaultConfig);
            File.WriteAllText(pathConfigFile, json);
            PrintInfo(
                $"已创建游戏路径配置：{pathConfigFile}",
                $"Created game path configuration: {pathConfigFile}"
            );
        }
        catch (Exception e)
        {
            PrintWarning(
                $"无法创建 path.json：{e.Message}",
                $"Could not create path.json: {e.Message}"
            );
        }

        return defaultConfig;
    }

    try
    {
        var config = SimpleJson.ParseGamePathConfig(File.ReadAllText(pathConfigFile));
        return config ?? defaultConfig;
    }
    catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
    {
        PrintWarning(
            $"无法读取 path.json，将忽略自定义路径：{e.Message}",
            $"Could not read path.json; the custom path will be ignored: {e.Message}"
        );
        return defaultConfig;
    }
}

// 自动检测游戏目录，检测失败时再使用 path.json 的 game_path。
string ResolveGamePath(GamePathConfig pathConfig)
{
    var detectedGamePath = Registry.GetValue(
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2093940",
        "InstallLocation",
        null
    ) as string ?? string.Empty;

    if (IsUsableGamePath(detectedGamePath))
    {
        return detectedGamePath;
    }

    if (IsUsableGamePath(pathConfig.GamePath))
    {
        PrintWarning(
            "自动检测游戏目录失败，正在使用 path.json 中的 game_path。",
            "Automatic game path detection failed; using game_path from path.json."
        );
        return pathConfig.GamePath;
    }

    if (PipeClient.IPCMode)
    {
        if (IsUsableGamePath(PipeClient.GamePath))
        {
            Console.Error.WriteLine($"[VML IPC] Using game path from BVO: {PipeClient.GamePath}");
            return PipeClient.GamePath;
        }
        Console.Error.WriteLine("[VML IPC] No valid game path (registry, path.json, BVO all failed). Exiting.");
        Environment.Exit(1);
    }

    PrintWarning(
        "自动检测和 path.json 均未提供有效游戏目录，请手动输入。",
        "Neither automatic detection nor path.json provided a valid game directory; please enter one manually."
    );
    return AskGamePath();
}

// force_custom_path 启用时只接受 path.json 的 game_path，不再读取 IPC 或注册表路径。
string ResolveForcedCustomGamePath(GamePathConfig pathConfig)
{
    if (!IsUsableGamePath(pathConfig.GamePath))
    {
        throw new DirectoryNotFoundException(
            "path.json 已启用 force_custom_path，但 game_path 不是包含 data.win 的有效游戏目录。"
        );
    }

    PrintInfo(
        "path.json 已启用 force_custom_path，已绕过全部游戏目录自动获取。",
        "force_custom_path is enabled in path.json; all automatic game path discovery has been bypassed."
    );
    return pathConfig.GamePath;
}

bool IsLengthMismatchValidationWarning(string warning)
{
    return warning.StartsWith("WARNING: File specified length ", StringComparison.Ordinal);
}

// 仅在 UndertaleModLib 明确报告块长度不一致时删除备份，避免坏备份覆盖游戏文件。
bool RemoveBackupDataFileIfLengthMismatch(string? gamePath, bool dryRun)
{
    if (string.IsNullOrWhiteSpace(gamePath))
    {
        return false;
    }

    var backupDataPath = Path.Combine(gamePath, "backup", "data.win");
    if (!File.Exists(backupDataPath))
    {
        return false;
    }

    const string lengthMismatchMarker = "Backup data.win length mismatch";

    try
    {
        using var fs = File.OpenRead(backupDataPath);
        UndertaleIO.Read(fs, (warning, _) =>
        {
            if (IsLengthMismatchValidationWarning(warning))
            {
                throw new InvalidDataException(lengthMismatchMarker);
            }
        });

        return false;
    }
    catch (InvalidDataException e) when (e.Message == lengthMismatchMarker)
    {
        if (dryRun)
        {
            PrintWarning(
                $"[dry-run] 备份 data.win 存在长度不一致，将删除：{backupDataPath}",
                $"[dry-run] Backup data.win has a length mismatch and would be deleted: {backupDataPath}"
            );
        }
        else
        {
            File.Delete(backupDataPath);
            PrintWarning(
                $"备份 data.win 存在长度不一致，已删除以防污染游戏：{backupDataPath}",
                $"Backup data.win has a length mismatch and was deleted to prevent game contamination: {backupDataPath}"
            );
        }

        return true;
    }
    catch (Exception e)
    {
        PrintWarning(
            $"无法验证备份 data.win，保留备份：{e.Message}",
            $"Could not validate backup data.win; it was retained: {e.Message}"
        );
        return false;
    }
}

// 在还原模式下恢复备份文件并删除备份目录。
bool TryRestoreFromBackup(bool restoreMode, bool dryRun, string backupFolderPath, string gamePath)
{
    if (!restoreMode)
    {
        return false;
    }

    if (!Directory.Exists(backupFolderPath))
    {
        PrintError("未找到备份目录。", "Couldn't find backup folder.");
        return true;
    }

    PrintSection("开始还原备份", "Start restoring backup");

    foreach (var file in Directory.GetFiles(backupFolderPath, "*.*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(backupFolderPath, file);
        var targetFile = Path.Combine(gamePath, relativePath);

        if (dryRun)
        {
            PrintInfo($"[dry-run] 将还原文件：{relativePath} -> {targetFile}", $"[dry-run] Would restore file: {relativePath} -> {targetFile}");
        }
        else
        {
            File.Copy(file, targetFile, true);
            PrintSuccess($"已还原文件：{relativePath}", $"Restored file: {relativePath}");
        }
    }

    if (dryRun)
    {
        PrintInfo($"[dry-run] 将删除备份目录：{backupFolderPath}", $"[dry-run] Would delete backup folder: {backupFolderPath}");
        PrintSuccess("[dry-run] 备份还原模拟完成。", "[dry-run] Backup restore simulation completed.");
    }
    else
    {
        Directory.Delete(backupFolderPath, true);
        PrintSuccess("备份还原完成。", "Backup restore completed.");
    }

    return true;
}

// 根据游戏版本准备备份，并在已有备份时先回滚 data.win 与音频组。
void PrepareBackup(string gamePath, string dataFilePath, string backupFolderPath, bool dryRun)
{
    var backupDataPath = Path.Combine(backupFolderPath, "data.win");
    var gameVerPath = Path.Combine(gamePath, "ver");
    var backupVerPath = Path.Combine(backupFolderPath, "ver");

    if (dryRun)
    {
        PrintInfo($"[dry-run] 将确保备份目录存在：{backupFolderPath}", $"[dry-run] Would ensure backup folder exists: {backupFolderPath}");
    }
    else
    {
        Directory.CreateDirectory(backupFolderPath);
    }

    var gameVer = File.ReadAllText(gameVerPath);
    if (File.Exists(backupVerPath))
    {
        var backupVer = File.ReadAllText(backupVerPath);
        if (gameVer != backupVer)
        {
            if (dryRun)
            {
                PrintWarning($"[dry-run] 版本不一致，将删除旧备份目录：{backupFolderPath}", $"[dry-run] Version mismatch, would delete old backup folder: {backupFolderPath}");
            }
            else
            {
                Directory.Delete(backupFolderPath, true);
            }
        }
    }

    if (dryRun)
    {
        PrintInfo($"[dry-run] 将重新创建备份目录：{backupFolderPath}", $"[dry-run] Would recreate backup folder: {backupFolderPath}");
    }
    else
    {
        Directory.CreateDirectory(backupFolderPath);
    }

    var invalidBackupDataFileRemoved = RemoveBackupDataFileIfLengthMismatch(gamePath, dryRun);

    if (File.Exists(backupDataPath) && !invalidBackupDataFileRemoved)
    {
        if (dryRun)
        {
            PrintInfo($"[dry-run] 将从备份恢复：{backupDataPath} -> {dataFilePath}", $"[dry-run] Would restore from backup: {backupDataPath} -> {dataFilePath}");
        }
        else
        {
            File.Copy(backupDataPath, dataFilePath, true);
        }

        foreach (var file in Directory.GetFiles(backupFolderPath, "*.dat"))
        {
            var targetFile = Path.Combine(gamePath, Path.GetFileName(file));
            if (dryRun)
            {
                PrintInfo($"[dry-run] 将恢复音频组：{file} -> {targetFile}", $"[dry-run] Would restore audiogroup: {file} -> {targetFile}");
            }
            else
            {
                File.Copy(file, targetFile, true);
            }
        }

        if (dryRun)
        {
            PrintInfo("[dry-run] 已模拟从备份恢复 data.win 与音频组。", "[dry-run] Simulated restoring data.win and audiogroups from backup.");
        }
        else
        {
            PrintInfo("已从备份恢复 data.win 和音频组文件。", "Restored data.win and audiogroups from backup.");
        }
    }
    else
    {
        if (dryRun)
        {
            PrintInfo($"[dry-run] 将创建 data.win 备份：{dataFilePath} -> {backupDataPath}", $"[dry-run] Would backup data.win: {dataFilePath} -> {backupDataPath}");
        }
        else
        {
            File.Copy(dataFilePath, backupDataPath);
        }

        foreach (var file in Directory.GetFiles(gamePath, "*.dat"))
        {
            var targetFile = Path.Combine(backupFolderPath, Path.GetFileName(file));
            if (dryRun)
            {
                PrintInfo($"[dry-run] 将备份音频组：{file} -> {targetFile}", $"[dry-run] Would backup audiogroup: {file} -> {targetFile}");
            }
            else
            {
                File.Copy(file, targetFile, true);
            }
        }

        if (dryRun)
        {
            PrintInfo($"[dry-run] 将备份版本文件：{gameVerPath} -> {backupVerPath}", $"[dry-run] Would backup version file: {gameVerPath} -> {backupVerPath}");
        }
        else
        {
            File.Copy(gameVerPath, backupVerPath);
        }

        if (dryRun)
        {
            PrintSuccess("[dry-run] 备份创建模拟完成。", "[dry-run] Backup creation simulation completed.");
        }
        else
        {
            PrintSuccess("已创建备份文件。", "Backup file created.");
        }
    }
}

// 获取 mods 目录下的所有模组目录。
string[] GetModDirectories(bool dryRun)
{
    if (dryRun)
    {
        PrintInfo("[dry-run] 将确保 mods 目录存在。", "[dry-run] Would ensure mods directory exists.");
    }
    else
    {
        Directory.CreateDirectory("./mods");
    }

    return Directory.GetDirectories("./mods");
}

// 对每个模组依次执行字体、文本、图片、音频、Shader、对象和代码修补。
void PatchMods(UndertaleData data, string gamePath, string[] modDirs, bool dryRun)
{
    // 统一处理单个修补模块：资源存在检查、dry-run 提示、实际执行和成功输出。
    void HandlePatch(
        bool exists,
        string stepZh,
        string stepEn,
        string missingZh,
        string missingEn,
        string dryRunDetectedZh,
        string dryRunDetectedEn,
        string successZh,
        string successEn,
        Action execute)
    {
        // 资源不存在：正常模式静默，dry-run 输出跳过提示。
        if (!exists)
        {
            if (dryRun)
            {
                PrintWarning(missingZh, missingEn);
            }
            return;
        }

        // 资源存在时输出当前步骤。
        PrintStep(stepZh, stepEn);

        // dry-run 只打印“将执行”，正常模式才实际执行修补。
        if (dryRun)
        {
            PrintInfo(dryRunDetectedZh, dryRunDetectedEn);
        }
        else
        {
            execute();
        }

        // 模块处理完成后统一输出成功信息。
        PrintSuccess(successZh, successEn);
    }

    foreach (var modDir in modDirs)
    {
        // 进入当前模组处理分节。
        PrintSection($"处理模组：{modDir}", $"Processing mod: {modDir}");

        // 先为当前模组实例化所有修补器，并统一检测是否至少存在一种可处理资源。
        var fontReplacer = new FontReplacer(data, modDir);
        var strReplacer = new StringReplacer(data, modDir);
        var spriteReplacer = new SpriteReplacer(data, modDir);
        var audioReplacer = new AudioReplacer(data, gamePath, modDir);
        var shaderReplacer = new ShaderReplacer(data, modDir);
        var objectPatcher = new ObjectPatcher(data, modDir);
        var codePatcher = new CodePatcher(data, modDir);

        var hasAnyPatchResource =
            fontReplacer.Exist()
            || strReplacer.Exist()
            || spriteReplacer.Exist()
            || audioReplacer.Exist()
            || shaderReplacer.Exist()
            || objectPatcher.Exist()
            || codePatcher.Exist();

        // 当该模组目录中没有任何可识别的修补资源时，给出统一警告并跳过。
        if (!hasAnyPatchResource)
        {
            PrintWarning(
                "该模组目录未检测到任何可用修补资源，请检查文件是否摆放正确。",
                "No patch resources were detected in this mod folder. Please check whether files are placed correctly."
            );
            continue;
        }

        // 字体资源修补。
        HandlePatch(
            fontReplacer.Exist(),
            "正在修补字体...",
            "Patching fonts...",
            "未检测到 fonts 资源，已跳过。",
            "No fonts resource found, skipped.",
            "[dry-run] 已检测到 fonts 资源，将执行字体修补。",
            "[dry-run] Fonts resources detected, would patch fonts.",
            "字体修补完成。",
            "Fonts patched.",
            fontReplacer.Execute
        );

        // 文本资源修补（excel）。
        HandlePatch(
            strReplacer.Exist(),
            "正在修补文本...",
            "Patching strings...",
            "未检测到 excel 文本资源，已跳过。",
            "No excel string resources found, skipped.",
            "[dry-run] 已检测到 excel 文本资源，将执行文本修补。",
            "[dry-run] Excel string resources detected, would patch strings.",
            "文本修补完成。",
            "Strings patched.",
            strReplacer.Execute
        );

        // 图片资源修补（sprites）。
        HandlePatch(
            spriteReplacer.Exist(),
            "正在修补图片...",
            "Patching sprites...",
            "未检测到 sprites 资源，已跳过。",
            "No sprites resources found, skipped.",
            "[dry-run] 已检测到 sprites 资源，将执行图片修补。",
            "[dry-run] Sprites resources detected, would patch sprites.",
            "图片修补完成。",
            "Sprites patched.",
            spriteReplacer.Execute
        );

        // 音频资源修补。
        HandlePatch(
            audioReplacer.Exist(),
            "正在修补音频...",
            "Patching audios...",
            "未检测到 audios 资源，已跳过。",
            "No audios resources found, skipped.",
            "[dry-run] 已检测到 audios 资源，将执行音频修补。",
            "[dry-run] Audios resources detected, would patch audios.",
            "音频修补完成。",
            "Audios patched.",
            audioReplacer.Execute
        );

        // Shader 资源修补。
        HandlePatch(
            shaderReplacer.Exist(),
            "正在修补 Shader...",
            "Patching shaders...",
            "未检测到 shaders 资源，已跳过。",
            "No shaders resources found, skipped.",
            "[dry-run] 已检测到 shaders 资源，将执行 Shader 修补。",
            "[dry-run] Shaders resources detected, would patch shaders.",
            "Shader 修补完成。",
            "Shaders patched.",
            shaderReplacer.Execute
        );

        // 对象定义修补。
        HandlePatch(
            objectPatcher.Exist(),
            "正在修补对象...",
            "Patching objects...",
            "未检测到 objects 资源，已跳过。",
            "No objects resources found, skipped.",
            "[dry-run] 已检测到 objects 资源，将执行对象修补。",
            "[dry-run] Objects resources detected, would patch objects.",
            "对象修补完成。",
            "Objects patched.",
            objectPatcher.Execute
        );

        // 代码补丁修补（代码替换与插入）。
        HandlePatch(
            codePatcher.Exist(),
            "正在修补代码...",
            "Patching codes...",
            "未检测到代码补丁资源，已跳过。",
            "No code patch resources found, skipped.",
            "[dry-run] 已检测到代码补丁资源，将执行代码修补。",
            "[dry-run] Code patch resources detected, would patch codes.",
            "代码修补完成。",
            "Codes patched.",
            codePatcher.Execute
        );
    }
}

// 处理 raw 文件覆盖，并在首次覆盖前写入备份文件。
void PatchRawFiles(string[] modDirs, string gamePath, string backupFolderPath, bool dryRun)
{
    foreach (var modDir in modDirs)
    {
        var rawPath = Path.Combine(modDir, "raw");
        if (!Directory.Exists(rawPath))
        {
            continue;
        }

        PrintStep($"正在处理 raw 文件：{modDir}", $"Patching raw files for: {modDir}");

        foreach (var rawFile in Directory.GetFiles(rawPath, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(rawPath, rawFile);
            var gamePathFile = new FileInfo(Path.Combine(gamePath, relativePath));
            var backupPathFile = new FileInfo(Path.Combine(backupFolderPath, relativePath));

            if (gamePathFile.Exists && !backupPathFile.Exists)
            {
                if (dryRun)
                {
                    PrintInfo($"[dry-run] 将创建备份目录：{backupPathFile.Directory!.FullName}", $"[dry-run] Would create backup directory: {backupPathFile.Directory!.FullName}");
                    PrintInfo($"[dry-run] 将备份原文件：{gamePathFile.FullName} -> {backupPathFile.FullName}", $"[dry-run] Would backup original file: {gamePathFile.FullName} -> {backupPathFile.FullName}");
                }
                else
                {
                    if (!backupPathFile.Directory!.Exists)
                    {
                        Directory.CreateDirectory(backupPathFile.Directory.FullName);
                    }

                    File.Copy(gamePathFile.FullName, backupPathFile.FullName);
                }
            }

            if (dryRun)
            {
                PrintInfo($"[dry-run] 将确保目标目录存在：{gamePathFile.Directory!.FullName}", $"[dry-run] Would ensure target directory exists: {gamePathFile.Directory!.FullName}");
                PrintInfo($"[dry-run] 将覆盖 raw 文件：{rawFile} -> {gamePathFile.FullName}", $"[dry-run] Would replace raw file: {rawFile} -> {gamePathFile.FullName}");
            }
            else
            {
                if (!gamePathFile.Directory!.Exists)
                {
                    Directory.CreateDirectory(gamePathFile.Directory.FullName);
                }

                File.Copy(rawFile, gamePathFile.FullName, true);
            }
        }

        PrintSuccess("raw 文件修补完成。", "Raw files patched.");
    }
}

// 与对象偏移或块长度相关的警告表示 data.win 不完整或不一致，必须停止修补。
bool IsDataIntegrityValidationWarning(string warning)
{
    return warning.StartsWith("Reading misaligned at ", StringComparison.Ordinal)
        || warning.StartsWith("WARNING: File specified length ", StringComparison.Ordinal);
}

// 读取 data.win 并解析为 UndertaleData。
UndertaleData ReadDataFile(FileInfo dataFile)
{
    try
    {
        using var fs = dataFile.OpenRead();
        var nonImportantWarnings = new List<string>();

        var data = UndertaleIO.Read(fs, (warning, isImportant) =>
        {
            if (isImportant || IsDataIntegrityValidationWarning(warning))
            {
                throw new InvalidDataException(
                    $"UndertaleModLib 报告了数据验证错误，已停止修补：{Environment.NewLine}{warning}"
                );
            }

            nonImportantWarnings.Add(warning);
        });

        foreach (var warning in nonImportantWarnings.Distinct())
        {
            PrintWarning($"UndertaleModLib：{warning}", $"UndertaleModLib: {warning}");
        }

        return data;
    }
    catch (FileNotFoundException e)
    {
        throw new FileNotFoundException($"Data file '{e.FileName}' does not exist");
    }
}

// 将修补后的 UndertaleData 写回 data.win。
void SaveDataFile(FileInfo dataFile, UndertaleData data, bool dryRun)
{
    if (dryRun)
    {
        PrintInfo($"[dry-run] 将写入 data.win：{dataFile.FullName}", $"[dry-run] Would write data.win: {dataFile.FullName}");
        return;
    }

    try
    {
        using var fs = dataFile.OpenWrite();
        UndertaleIO.Write(fs, data);
    }
    catch (Exception e)
    {
        PrintError($"保存 data.win 失败：{e.Message}", $"Failed to save data.win: {e.Message}");
    }
}

// 持久化当前配置到 config.json。
void SaveConfig(ModLoaderConfig config, bool dryRun)
{
    if (dryRun)
    {
        PrintInfo("[dry-run] 将写入 config.json。", "[dry-run] Would write config.json.");
        return;
    }

    var json = SimpleJson.ToJson(config);
    File.WriteAllText("./config.json", json);
}

// 修补完成后暂停，便于查看日志输出。
void PauseAfterPatch(bool dryRun)
{
    PrintSection("修补流程完成", "Patch flow completed");

    if (dryRun)
    {
        PrintInfo("[dry-run] 未实际写入任何文件。", "[dry-run] No files were actually modified.");
    }
    PrintPauseHint();
}

// 统一执行主流程。
void Run(string[] inputArgs)
{
    // TVOC联动用
    PipeClient.Init(inputArgs);
    if (inputArgs.Length > 0 && inputArgs[0] == "ipc" && !PipeClient.IPCMode)
    {
        // BVO requested shutdown via EXIT, or pipe was closed
        return;
    }

    // 防止TVOC联动启动时运行路径被认为是TVOC目录
    if (PipeClient.IPCMode)
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory;
    }
    else
    {
        PrintAppBanner("vividstasis 模组加载器", "vividstasis Mod Loader", AppInfo.VERSION, AppInfo.CHANGE_LOG);
    }

    Console.WriteLine($"当前运行目录:{Environment.CurrentDirectory}");

    // 启动后先检查当前运行路径，命中中文或特殊字符时直接暂停，避免后续文件操作损坏数据。
    var runPath = Environment.CurrentDirectory;
    if (HasUnsafePathCharacters(runPath))
    {
        PrintUnsafeRunPathPause(
            runPath,
            "当前运行路径包含中文或特殊字符，已停止修补以避免损坏。",
            "The current run path contains Chinese or special characters. Patching has been stopped to avoid damage."
        );
        return;
    }

    var restoreMode = IsRestoreMode(inputArgs);
    var dryRun = IsDryRunMode(inputArgs);
    dryRunForBackupCleanup = dryRun;

    if (dryRun)
    {
        PrintWarning("已启用 dry-run 模式：仅输出流程，不执行写入/删除/覆盖。", "Dry-run mode enabled: output only, no write/delete/overwrite operations.");
    }

    CreateRestoreScript(dryRun);

    var config = LoadConfig();
    var pathConfig = LoadOrCreateGamePathConfig();

    // force_custom_path 的优先级最高；否则 IPC 模式使用 BVO 传入路径，
    // 独立运行时执行注册表检测与 path.json 回退。
    string gamePath;

    if (pathConfig.ShouldForceCustomPath)
    {
        gamePath = ResolveForcedCustomGamePath(pathConfig);
    }
    else if (PipeClient.IPCMode)
    {
        gamePath = PipeClient.GamePath;
    }
    else
    {
        gamePath = ResolveGamePath(pathConfig);
    }

    config.GamePath = gamePath;
    gamePathForBackupCleanup = gamePath;

    PrintInfo($"游戏路径：{gamePath}", $"Game path: {gamePath}");

    var dataFilePath = Path.Combine(gamePath, "data.win");
    var backupFolderPath = Path.Combine(gamePath, "backup\\");

    if (TryRestoreFromBackup(restoreMode, dryRun, backupFolderPath, gamePath))
    {
        PrintRestoreModeCompleted();
        return;
    }

    PrintSection("准备备份", "Preparing backup");
    PrepareBackup(gamePath, dataFilePath, backupFolderPath, dryRun);

    var dataFileInfo = new FileInfo(dataFilePath);
    PrintSection("读取并修补数据", "Read and patch data");
    var data = ReadDataFile(dataFileInfo);
    var modDirs = GetModDirectories(dryRun);

    if (modDirs.Length == 0)
    {
        PrintWarning("未发现可用模组目录。", "No mod directories found.");
    }

    PatchMods(data, gamePath, modDirs, dryRun);
    PrintStep("正在保存 data.win...", "Saving data.win...");
    SaveDataFile(dataFileInfo, data, dryRun);
    PrintSection("处理 raw 文件", "Process raw files");
    PatchRawFiles(modDirs, gamePath, backupFolderPath, dryRun);
    SaveConfig(config, dryRun);
    PrintSuccess("配置已保存。", "Configuration saved.");
    PauseAfterPatch(dryRun);
    PipeClient.SendMessage("PATCH_COMPLETE");
    PipeClient.Shutdown();
}

// 在文件底部统一触发执行，并防止未处理异常触发 Windows 应用程序错误弹窗。
try
{
    Run(args);
}
catch (Exception e)
{
    PrintError($"修补已停止：{e.Message}", $"Patching stopped: {e.Message}");
    Console.Error.WriteLine(e);
    RemoveBackupDataFileIfLengthMismatch(gamePathForBackupCleanup, dryRunForBackupCleanup);
    PipeClient.SendException(e);
    PipeClient.SendMessage("PATCH_FAILED");
    PipeClient.Shutdown();
    Environment.ExitCode = 1;

    if (!PipeClient.IPCMode)
    {
        Console.WriteLine("修补失败，按 Enter 退出。 (Patching failed, press Enter to exit.)");
        Console.ReadLine();
    }
}

// 配置对象，保存基础运行参数。
class ModLoaderConfig
{
    public string GamePath { get; set; } = string.Empty;
}

class GamePathConfig
{
    [JsonPropertyName("game_path")]
    public string GamePath { get; set; } = @"C:\example\path\";

    [JsonPropertyName("force_use_custom_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ForceUseCustomPath { get; set; }

    [JsonPropertyName("force_custom_path")]
    public bool? ForceCustomPath { get; set; }

    // 新字段存在时以新字段为准；旧配置缺少新字段时兼容原字段。
    [JsonIgnore]
    public bool ShouldForceCustomPath => ForceCustomPath ?? ForceUseCustomPath;
}

