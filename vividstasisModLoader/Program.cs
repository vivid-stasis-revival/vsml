using Microsoft.Win32;
using System.Text.Json;
using UndertaleModLib;
using vividstasisModLoader;
using static vividstasisModLoader.ConsoleOutput;

const string VERSION = "v0.1.1";
const string CHANGE_LOG = "修正部分路径处理逻辑，增强兼容性。";

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

    var config = JsonSerializer.Deserialize<ModLoaderConfig>(File.ReadAllText("./config.json"));
    return config ?? new ModLoaderConfig();
}

// 自动检测游戏目录，检测失败时提示用户手动输入。
string ResolveGamePath()
{
    var gamePath = Registry.GetValue(
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2093940",
        "InstallLocation",
        null
    ) as string ?? string.Empty;

    if (gamePath == string.Empty || !Directory.Exists(gamePath))
    {
        PrintWarning("未能自动检测到游戏路径，请确认 vivid/stasis 已安装。", "Couldn't find game path automatically. Please make sure vivid/stasis is installed.");
        gamePath = AskGamePath();
    }

    return gamePath;
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

// 读取 data.win 并解析为 UndertaleData。
UndertaleData ReadDataFile(FileInfo dataFile)
{
    try
    {
        using var fs = dataFile.OpenRead();
        return UndertaleIO.Read(fs);
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

    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
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
    PrintAppBanner("vividstasis 模组加载器", "vividstasis Mod Loader", VERSION, CHANGE_LOG);

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

    if (dryRun)
    {
        PrintWarning("已启用 dry-run 模式：仅输出流程，不执行写入/删除/覆盖。", "Dry-run mode enabled: output only, no write/delete/overwrite operations.");
    }

    CreateRestoreScript(dryRun);

    var config = LoadConfig();
    var gamePath = ResolveGamePath();
    config.GamePath = gamePath;
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
}

// 在文件底部统一触发执行。
Run(args);

// 配置对象，保存基础运行参数。
class ModLoaderConfig
{
    public string GamePath { get; set; } = string.Empty;
}

