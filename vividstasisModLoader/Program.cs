using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UndertaleModLib;
using vividstasisModLoader;
using static vividstasisModLoader.ConsoleOutput;

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

// 构建脚本使用此模式自动安装模组，完成后直接退出，不等待控制台输入。
bool IsAutoInstallMode(string[] inputArgs)
{
    return HasArg(inputArgs, "--auto-install") || HasArg(inputArgs, "auto-install");
}

bool IsHelpRequested(string[] inputArgs)
{
    return inputArgs.Length == 0 || HasArg(inputArgs, "--help") || HasArg(inputArgs, "-h") || HasArg(inputArgs, "help");
}

bool IsOverwriteMode(string[] inputArgs)
{
    return HasArg(inputArgs, "--overwrite") || HasArg(inputArgs, "--ow") || HasArg(inputArgs, "-ow");
}

string? GetOptionValue(string[] inputArgs, string optionName)
{
    for (var index = 0; index < inputArgs.Length - 1; index++)
    {
        if (string.Equals(inputArgs[index], optionName, StringComparison.OrdinalIgnoreCase))
        {
            var value = inputArgs[index + 1];
            return value.StartsWith("-", StringComparison.Ordinal) ? null : value;
        }
    }

    return null;
}

string ResolveDirectoryOption(string[] inputArgs, string optionName, string defaultPath)
{
    var value = GetOptionValue(inputArgs, optionName);
    return CrossPlatformPath.GetFullPath(string.IsNullOrWhiteSpace(value) ? defaultPath : value);
}

void PrintHelp()
{
    var buildDate = GetBuildDate();
    Console.WriteLine($"VSML-Cross {AppInfo.VERSION} [Build:{buildDate}]");
    Console.WriteLine("Cross-platform VividStasis data patcher");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  VSML-Cross --data <path>");
    Console.WriteLine("  VSML-Cross --data <path> --overwrite");
    Console.WriteLine();
    Console.WriteLine("Input path:");
    Console.WriteLine("  data.win, game.droid, or game.ios");
    Console.WriteLine();
    Console.WriteLine("Output:");
    Console.WriteLine("  Default: <input-directory>/data/game-YYYYMMDDHHMMSS.win");
    Console.WriteLine("  --overwrite / --ow: modify the input file in place");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --help, -h       Show this help");
    Console.WriteLine("  --mods <path>    Read mods from this directory");
    Console.WriteLine("  --app-logs <path> Write logs to this directory");
    Console.WriteLine("  --silent         Write logs to app-logs without console output");
}

string GetBuildDate()
{
    var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
    var processPath = Environment.ProcessPath;
    var buildPath = !string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath)
        ? assemblyPath
        : processPath;

    return !string.IsNullOrWhiteSpace(buildPath) && File.Exists(buildPath)
        ? File.GetLastWriteTime(buildPath).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
        : DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}

string ResolveOutputPath(string inputPath, bool overwrite)
{
    if (overwrite)
    {
        return inputPath;
    }

    var inputDirectory = Path.GetDirectoryName(inputPath)
        ?? throw new InvalidOperationException("The input path has no parent directory.");
    var outputDirectory = Path.Combine(inputDirectory, "data");
    Directory.CreateDirectory(outputDirectory);

    var outputPath = Path.Combine(outputDirectory, $"game-{DateTime.Now:yyyyMMddHHmmss}.win");
    if (File.Exists(outputPath))
    {
        throw new IOException($"Output file already exists: {outputPath}. Wait one second and retry.");
    }

    return outputPath;
}

void ValidateInputPath(string inputPath)
{
    var fileName = Path.GetFileName(inputPath);
    if (!fileName.Equals("data.win", StringComparison.OrdinalIgnoreCase)
        && !fileName.Equals("game.droid", StringComparison.OrdinalIgnoreCase)
        && !fileName.Equals("game.ios", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("--data must point to data.win, game.droid, or game.ios.");
    }

    if (!File.Exists(inputPath))
    {
        throw new FileNotFoundException($"Input data file was not found: {inputPath}", inputPath);
    }
}

string? GetAutoInstallGamePath(string[] inputArgs)
{
    for (var index = 0; index < inputArgs.Length - 1; index++)
    {
        if (!string.Equals(inputArgs[index], "--auto-install", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(inputArgs[index], "auto-install", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var candidate = inputArgs[index + 1];
        return candidate.StartsWith("-", StringComparison.Ordinal) ? null : candidate;
    }

    return null;
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

    using var document = JsonDocument.Parse(File.ReadAllText("./config.json"));
    var root = document.RootElement;
    return new ModLoaderConfig
    {
        GamePath = ReadJsonString(root, "game_path", ReadJsonString(root, "GamePath", string.Empty))
    };
}

string ReadJsonString(JsonElement root, string name, string fallback)
{
    return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? fallback
        : fallback;
}

string QuoteJsonString(string value)
{
    return $"\"{JsonEncodedText.Encode(value).ToString()}\"";
}

string SerializeGamePathConfig(GamePathConfig config)
{
    return "{\n"
        + $"  \"game_path\": {QuoteJsonString(config.GamePath)},\n"
        + $"  \"force_custom_path\": {config.ForceUseCustomPath.ToString().ToLowerInvariant()}\n"
        + "}\n";
}

string SerializeModLoaderConfig(ModLoaderConfig config)
{
    return "{\n"
        + $"  \"game_path\": {QuoteJsonString(config.GamePath)}\n"
        + "}\n";
}

// 判断候选目录是否为可读取的 vivid/stasis 游戏目录。
bool IsUsableGamePath(string? gamePath)
{
    return !string.IsNullOrWhiteSpace(gamePath)
        && Directory.Exists(CrossPlatformPath.NormalizeSeparators(gamePath))
        && File.Exists(Path.Combine(CrossPlatformPath.NormalizeSeparators(gamePath), "data.win"));
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
            var json = SerializeGamePathConfig(defaultConfig);
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
        using var document = JsonDocument.Parse(File.ReadAllText(pathConfigFile));
        var root = document.RootElement;
        return new GamePathConfig
        {
            GamePath = ReadJsonString(root, "game_path", defaultConfig.GamePath),
            ForceUseCustomPath = root.TryGetProperty("force_custom_path", out var forceValue)
                && forceValue.ValueKind == JsonValueKind.True
        };
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
string ResolveGamePath(GamePathConfig pathConfig, bool allowInteractiveInput = true)
{
    var detectedGamePath = string.Empty;

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
        return CrossPlatformPath.NormalizeSeparators(pathConfig.GamePath);
    }

    if (!allowInteractiveInput)
    {
        throw new DirectoryNotFoundException(
            "自动安装模式未能自动找到 vivid/stasis 游戏目录。请传入 --auto-install <gamePath>。"
        );
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
    return CrossPlatformPath.NormalizeSeparators(pathConfig.GamePath);
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

    if (File.Exists(backupDataPath))
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
string[] GetModDirectories(string modsPath, bool dryRun)
{
    if (dryRun)
    {
        PrintInfo($"[dry-run] 将确保模组目录存在：{modsPath}", $"[dry-run] Would ensure mods directory exists: {modsPath}");
    }
    else
    {
        Directory.CreateDirectory(modsPath);
    }

    return Directory.GetDirectories(modsPath);
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

// 仅允许 UndertaleModLib 跳过由纯零字节组成的向前对齐区。
// 这类间隙不包含资源数据；其他重要警告仍应中止，避免自动保存不安全的数据。
bool TryAcceptZeroPaddingAlignmentWarning(
    Stream validationStream,
    string warning,
    out long skippedBytes)
{
    skippedBytes = 0;

    var match = Regex.Match(
        warning,
        @"^Reading misaligned at (?<actual>[0-9A-Fa-f]+), realigning back to (?<expected>[0-9A-Fa-f]+)",
        RegexOptions.CultureInvariant
    );

    if (!match.Success
        || !long.TryParse(match.Groups["actual"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var actual)
        || !long.TryParse(match.Groups["expected"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expected)
        || actual < 0
        || expected <= actual
        || expected > validationStream.Length)
    {
        return false;
    }

    validationStream.Position = actual;
    var remaining = expected - actual;
    var buffer = new byte[81920];

    while (remaining > 0)
    {
        var bytesToRead = (int)Math.Min(buffer.Length, remaining);
        var bytesRead = validationStream.Read(buffer, 0, bytesToRead);
        if (bytesRead != bytesToRead)
        {
            return false;
        }

        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        remaining -= bytesRead;
    }

    skippedBytes = expected - actual;
    return true;
}

// 读取 data.win 并解析为 UndertaleData。
UndertaleData ReadDataFile(FileInfo dataFile)
{
    try
    {
        using var fs = dataFile.OpenRead();
        using var validationStream = dataFile.OpenRead();
        var acceptedAlignmentGapCount = 0;
        long acceptedAlignmentGapBytes = 0;
        var nonImportantWarnings = new List<string>();

        var data = UndertaleIO.Read(fs, (warning, isImportant) =>
        {
            if (!isImportant)
            {
                nonImportantWarnings.Add(warning);
                return;
            }

            if (TryAcceptZeroPaddingAlignmentWarning(validationStream, warning, out var skippedBytes))
            {
                acceptedAlignmentGapCount++;
                acceptedAlignmentGapBytes += skippedBytes;
                return;
            }

            throw new InvalidDataException(
                $"UndertaleModLib 报告了无法安全忽略的文件读取警告：{Environment.NewLine}{warning}"
            );
        });

        if (acceptedAlignmentGapCount > 0)
        {
            PrintWarning(
                $"检测到并安全跳过 {acceptedAlignmentGapCount:N0} 个纯零对齐区，共 {acceptedAlignmentGapBytes:N0} 字节。",
                $"Detected and safely skipped {acceptedAlignmentGapCount:N0} zero-filled alignment gaps ({acceptedAlignmentGapBytes:N0} bytes total)."
            );
        }

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
        using var fs = new FileStream(dataFile.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
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

    File.WriteAllText("./config.json", SerializeModLoaderConfig(config));
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

// 统一执行跨平台数据文件修补流程。此版本只处理 --data 指定的文件，
// 不访问注册表、不创建备份、不覆盖 raw 文件，也不包含任何 IPC 联动。
void Run(string[] inputArgs)
{
    if (IsHelpRequested(inputArgs))
    {
        PrintHelp();
        return;
    }

    var inputValue = GetOptionValue(inputArgs, "--data");
    if (string.IsNullOrWhiteSpace(inputValue))
    {
        throw new ArgumentException("Missing required option: --data <path>. Use --help for usage.");
    }

    var inputPath = CrossPlatformPath.GetFullPath(inputValue);
    ValidateInputPath(inputPath);

    var overwrite = IsOverwriteMode(inputArgs);
    var dryRun = IsDryRunMode(inputArgs);
    var outputPath = ResolveOutputPath(inputPath, overwrite);
    var executableDirectory = AppContext.BaseDirectory;
    var modsPath = ResolveDirectoryOption(inputArgs, "--mods", Path.Combine(executableDirectory, "mods"));
    Environment.CurrentDirectory = executableDirectory;

    PrintAppBanner("vividstasis 模组加载器 (Cross-platform)", "vividstasis Mod Loader (Cross-platform)", AppInfo.VERSION, AppInfo.CHANGE_LOG);
    PrintInfo($"输入文件：{inputPath}", $"Input file: {inputPath}");
    PrintInfo($"输出文件：{outputPath}", $"Output file: {outputPath}");
    PrintInfo($"模组目录：{modsPath}", $"Mods directory: {modsPath}");

    if (dryRun)
    {
        PrintWarning("已启用 dry-run 模式：不会写入文件。", "Dry-run mode enabled: no files will be written.");
    }

    if (!overwrite && !dryRun)
    {
        File.Copy(inputPath, outputPath, false);
    }

    var dataFileInfo = new FileInfo(overwrite ? inputPath : outputPath);
    PrintSection("读取并修补数据", "Read and patch data");
    var data = ReadDataFile(new FileInfo(inputPath));
    var modDirs = GetModDirectories(modsPath, dryRun);

    if (modDirs.Length == 0)
    {
        PrintWarning("未发现可用模组目录。", "No mod directories found.");
    }

    var totalCodeCount = modDirs.Sum(modDir => new CodePatcher(data, modDir).GetCodeFileCount());
    if (totalCodeCount >= 40)
    {
        ConsoleOutput.AllocateExternalConsole(totalCodeCount);
    }

    var dataDirectory = Path.GetDirectoryName(dataFileInfo.FullName)
        ?? throw new InvalidOperationException("The output file has no parent directory.");
    PatchMods(data, dataDirectory, modDirs, dryRun);
    PrintStep("正在保存数据文件...", "Saving data file...");
    SaveDataFile(dataFileInfo, data, dryRun);
    PrintSuccess("修补流程完成。", "Patch flow completed.");
}

// 在文件底部统一触发执行，并防止未处理异常触发 Windows 应用程序错误弹窗。
try
{
    ConsoleOutput.Configure(args);
    Run(args);
}
catch (Exception e)
{
    PrintError($"修补已停止：{e.Message}", $"Patching stopped: {e.Message}");
    if (ConsoleOutput.IsSilentMode)
    {
        PrintError(e.ToString(), e.ToString());
    }
    else
    {
        Console.Error.WriteLine(e);
    }
    Environment.ExitCode = 1;
}

// 配置对象，保存基础运行参数。
class ModLoaderConfig
{
    public string GamePath { get; set; } = string.Empty;
}

class GamePathConfig
{
    [JsonPropertyName("game_path")]
    public string GamePath { get; set; } = string.Empty;

    [JsonPropertyName("force_use_custom_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ForceUseCustomPath { get; set; }

    [JsonPropertyName("force_custom_path")]
    public bool? ForceCustomPath { get; set; }

    // 新字段存在时以新字段为准；旧配置缺少新字段时兼容原字段。
    [JsonIgnore]
    public bool ShouldForceCustomPath => ForceCustomPath ?? ForceUseCustomPath;
}




