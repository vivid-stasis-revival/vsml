using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UndertaleModLib;

namespace vividstasisModLoader;

/// <summary>
/// Reusable data.win patch engine. This type deliberately has no console, IPC,
/// registry, current-directory, process-exit, or interactive-input dependency.
/// </summary>
internal sealed class VsmlEngine(IVsmlLogger? logger = null)
{
    private readonly IVsmlLogger _logger = logger ?? NullVsmlLogger.Instance;

    internal VsmlEngineInstallOutcome Install(
        VsmlInstallRequest request,
        Action<int>? codeFileCountObserved = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApiVersion(request.ApiVersion);

        var gamePath = ValidateGameDirectory(request.GameDirectory);
        var options = request.Options ?? new VsmlInstallOptions();
        var modDirs = NormalizeModDirectories(request.Mods);
        var dataFilePath = Path.Combine(gamePath, "data.win");
        var backupFolderPath = Path.Combine(gamePath, "backup");
        var changedFiles = new List<string>();
        var operationWorkspace = Path.Combine(Path.GetTempPath(), "vsml", Guid.NewGuid().ToString("N"));

        try
        {
            if (options.Backup)
            {
                _logger.Section("backup", "准备备份", "Preparing backup");
                PrepareBackup(
                    gamePath,
                    dataFilePath,
                    backupFolderPath,
                    options.DryRun,
                    options.RestoreBeforeApply);
            }

            var dataFileInfo = new FileInfo(dataFilePath);
            _logger.Section("data", "读取并修补数据", "Read and patch data");
            var data = ReadDataFile(dataFileInfo);

            if (modDirs.Length == 0)
            {
                _logger.Warning("mods", "未发现可用模组目录。", "No mod directories found.");
            }

            var totalCodeCount = modDirs.Sum(modDir => new CodePatcher(data, modDir).GetCodeFileCount());
            codeFileCountObserved?.Invoke(totalCodeCount);

            PatchMods(data, gamePath, modDirs, options.DryRun, operationWorkspace);
            _logger.Step("save", "正在保存 data.win...", "Saving data.win...");
            SaveDataFile(dataFileInfo, data, options.DryRun);
            if (!options.DryRun)
            {
                changedFiles.Add(dataFilePath);
            }

            _logger.Section("raw", "处理 raw 文件", "Process raw files");
            PatchRawFiles(
                modDirs,
                gamePath,
                backupFolderPath,
                options.DryRun,
                options.Backup,
                changedFiles);

            return new VsmlEngineInstallOutcome
            {
                ChangedFiles = changedFiles,
                Mods = modDirs.Select(path => new VsmlModResult { Path = path, Ok = true }).ToList()
            };
        }
        finally
        {
            TryDeleteOperationWorkspace(operationWorkspace);
        }
    }

    internal VsmlEngineRestoreOutcome Restore(VsmlRestoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApiVersion(request.ApiVersion);

        var gamePath = ValidateRestoreGameDirectory(request.GameDirectory);
        var backupFolderPath = Path.Combine(gamePath, "backup");
        var backupDataPath = Path.Combine(backupFolderPath, "data.win");
        if (!Directory.Exists(backupFolderPath) || !File.Exists(backupDataPath))
        {
            throw new VsmlException(
                "VSML_RESTORE_BACKUP_NOT_FOUND",
                "未找到包含 data.win 的有效 VSML 备份。",
                file: backupDataPath);
        }

        var changedFiles = new List<string>();
        _logger.Section("restore", "开始还原备份", "Start restoring backup");

        foreach (var file in Directory.GetFiles(backupFolderPath, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(backupFolderPath, file);
            var targetFile = Path.Combine(gamePath, relativePath);

            if (request.DryRun)
            {
                _logger.Info(
                    "restore",
                    $"[dry-run] 将还原文件：{relativePath} -> {targetFile}",
                    $"[dry-run] Would restore file: {relativePath} -> {targetFile}");
            }
            else
            {
                File.Copy(file, targetFile, true);
                changedFiles.Add(targetFile);
                _logger.Success(
                    "restore",
                    $"已还原文件：{relativePath}",
                    $"Restored file: {relativePath}");
            }
        }

        if (request.DryRun)
        {
            _logger.Info(
                "restore",
                $"[dry-run] 将删除备份目录：{backupFolderPath}",
                $"[dry-run] Would delete backup folder: {backupFolderPath}");
            _logger.Success(
                "restore",
                "[dry-run] 备份还原模拟完成。",
                "[dry-run] Backup restore simulation completed.");
        }
        else
        {
            Directory.Delete(backupFolderPath, true);
            _logger.Success("restore", "备份还原完成。", "Backup restore completed.");
        }

        return new VsmlEngineRestoreOutcome { ChangedFiles = changedFiles };
    }

    internal IReadOnlyList<VsmlModReview> Review(VsmlInstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApiVersion(request.ApiVersion);
        ValidateGameDirectory(request.GameDirectory);

        var reviews = new List<VsmlModReview>();
        foreach (var mod in request.Mods ?? [])
        {
            if (!mod.Enabled)
            {
                reviews.Add(new VsmlModReview
                {
                    Path = mod.Path,
                    Enabled = false,
                    Exists = Directory.Exists(mod.Path)
                });
                continue;
            }

            var fullPath = NormalizeModDirectory(mod.Path);
            if (IsDisabledModDirectory(fullPath))
            {
                continue;
            }
            if (!Directory.Exists(fullPath))
            {
                reviews.Add(new VsmlModReview
                {
                    Path = fullPath,
                    Enabled = true,
                    Exists = false,
                    Warnings = ["模组目录不存在。"]
                });
                continue;
            }

            foreach (var discoveredPath in DiscoverModDirectories(fullPath))
            {
                reviews.Add(ReviewModDirectory(discoveredPath));
            }
        }

        return reviews;
    }

    internal VsmlEngineValidationOutcome ValidateGameContents(string gameDirectory)
    {
        var gamePath = ValidateGameDirectory(gameDirectory);
        var dataFilePath = Path.Combine(gamePath, "data.win");
        var versionFilePath = Path.Combine(gamePath, "ver");
        var dataFile = new FileInfo(dataFilePath);

        _logger.Section(
            "validation",
            "完整解析 data.win（只读）",
            "Fully parse data.win (read-only)");
        _ = ReadDataFile(dataFile);
        _logger.Success(
            "validation",
            "UndertaleModLib 已完整解析 data.win。",
            "UndertaleModLib fully parsed data.win.");

        _logger.Step(
            "validation",
            "正在计算 data.win 的 SHA-256...",
            "Computing the SHA-256 of data.win...");
        using var stream = dataFile.OpenRead();
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var gameVersion = File.ReadAllText(versionFilePath).Trim();
        _logger.Success(
            "validation",
            "只读结构校验与 SHA-256 计算完成。",
            "Read-only structural validation and SHA-256 calculation completed.");

        return new VsmlEngineValidationOutcome
        {
            GameDirectory = gamePath,
            DataFile = dataFilePath,
            VersionFile = versionFilePath,
            GameVersion = gameVersion,
            DataFileSize = dataFile.Length,
            DataSha256 = sha256
        };
    }

    internal static string ValidateGameDirectory(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new VsmlException("VSML_INVALID_GAME_DIRECTORY", "游戏目录不能为空。");
        }

        if (!Path.IsPathFullyQualified(gameDirectory))
        {
            throw new VsmlException(
                "VSML_INVALID_GAME_DIRECTORY",
                "游戏目录必须是绝对路径。",
                file: gameDirectory);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(gameDirectory.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new VsmlException(
                "VSML_INVALID_GAME_DIRECTORY",
                "游戏目录格式无效。",
                exception.Message,
                file: gameDirectory,
                innerException: exception);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new VsmlException(
                "VSML_INVALID_GAME_DIRECTORY",
                "游戏目录不存在。",
                file: fullPath);
        }

        var dataFile = Path.Combine(fullPath, "data.win");
        if (!File.Exists(dataFile))
        {
            throw new VsmlException(
                "VSML_MISSING_DATA_FILE",
                "游戏目录中缺少 data.win。",
                file: dataFile);
        }

        var versionFile = Path.Combine(fullPath, "ver");
        if (!File.Exists(versionFile))
        {
            throw new VsmlException(
                "VSML_MISSING_VERSION_FILE",
                "游戏目录中缺少 ver 文件。",
                file: versionFile);
        }

        return fullPath;
    }

    private static void ValidateApiVersion(int apiVersion)
    {
        if (apiVersion != VsmlApi.AbiVersion)
        {
            throw new VsmlException(
                "VSML_API_VERSION_MISMATCH",
                $"不支持的 VSML API 版本：{apiVersion}；当前版本为 {VsmlApi.AbiVersion}。");
        }
    }

    private static string[] NormalizeModDirectories(List<VsmlModRequest>? mods)
    {
        var result = new List<string>();
        foreach (var mod in mods ?? [])
        {
            if (!mod.Enabled)
            {
                continue;
            }

            var fullPath = NormalizeModDirectory(mod.Path);
            if (!Directory.Exists(fullPath))
            {
                throw new VsmlException(
                    "VSML_MOD_DIRECTORY_NOT_FOUND",
                    "模组目录不存在。",
                    mod: fullPath,
                    file: fullPath);
            }

            result.AddRange(DiscoverModDirectories(fullPath));
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Preserve the upstream/direct mod layout, but allow callers to pass an
    // arbitrary extracted directory. A breadth-first search chooses the
    // shallowest compatible mod roots and never relies on a wrapper name.
    private static IReadOnlyList<string> DiscoverModDirectories(string root)
    {
        if (HasRecognizedModLayout(root))
        {
            return [root];
        }

        var discovered = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (IsDisabledModDirectory(child))
                {
                    continue;
                }
                if (HasRecognizedModLayout(child))
                {
                    discovered.Add(child);
                }
                else
                {
                    pending.Enqueue(child);
                }
            }
        }

        // Keeping the original directory when nothing is recognized preserves
        // the previous warning/no-op behavior for upstream callers.
        return discovered.Count > 0 ? discovered : [root];
    }

    private static bool HasRecognizedModLayout(string path)
    {
        if (File.Exists(Path.Combine(path, "codepatches.json")))
        {
            return true;
        }
        foreach (var directoryName in new[]
        {
            "fonts", "excel", "sprites", "audios", "shaders", "objects", "codes", "raw"
        })
        {
            if (Directory.Exists(Path.Combine(path, directoryName)))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool IsDisabledModDirectory(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.EndsWith("_disabled", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_disable", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeModDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new VsmlException("VSML_INVALID_MOD_DIRECTORY", "模组目录不能为空。");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new VsmlException(
                "VSML_INVALID_MOD_DIRECTORY",
                "模组目录必须是绝对路径。",
                mod: path,
                file: path);
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new VsmlException(
                "VSML_INVALID_MOD_DIRECTORY",
                "模组目录格式无效。",
                exception.Message,
                mod: path,
                file: path,
                innerException: exception);
        }
    }

    private void PrepareBackup(
        string gamePath,
        string dataFilePath,
        string backupFolderPath,
        bool dryRun,
        bool restoreBeforeApply)
    {
        var backupDataPath = Path.Combine(backupFolderPath, "data.win");
        var gameVerPath = Path.Combine(gamePath, "ver");
        var backupVerPath = Path.Combine(backupFolderPath, "ver");

        if (dryRun)
        {
            _logger.Info(
                "backup",
                $"[dry-run] 将确保备份目录存在：{backupFolderPath}",
                $"[dry-run] Would ensure backup folder exists: {backupFolderPath}");
        }
        else
        {
            Directory.CreateDirectory(backupFolderPath);
        }

        var existingBackupIsValid = false;
        if (File.Exists(backupDataPath))
        {
            var invalidReason = new FileInfo(backupDataPath).Length == 0
                ? "备份 data.win 为空。"
                : null;

            if (invalidReason is not null)
            {
                if (dryRun)
                {
                    _logger.Warning(
                        "backup",
                        $"[dry-run] {invalidReason} 将废弃旧备份目录：{backupFolderPath}",
                        $"[dry-run] Backup validation failed; would discard the old backup folder: {backupFolderPath}");
                }
                else
                {
                    _logger.Warning(
                        "backup",
                        $"{invalidReason} 正在废弃旧备份并重新创建。",
                        "Backup validation failed; discarding it and creating a new backup.");
                    Directory.Delete(backupFolderPath, true);
                }
            }
            else
            {
                existingBackupIsValid = true;
                _logger.Success(
                    "backup",
                    "备份校验通过：data.win 文件有效。",
                    "Backup validation passed: the data.win file is valid.");
            }
        }

        if (dryRun)
        {
            _logger.Info(
                "backup",
                $"[dry-run] 将重新创建备份目录：{backupFolderPath}",
                $"[dry-run] Would recreate backup folder: {backupFolderPath}");
        }
        else
        {
            Directory.CreateDirectory(backupFolderPath);
        }

        if (existingBackupIsValid && File.Exists(backupDataPath))
        {
            if (!restoreBeforeApply)
            {
                _logger.Info(
                    "backup",
                    "已保留现有备份，本次安装不会在应用前还原。",
                    "Existing backup retained; this install will not restore it before applying.");
                return;
            }

            if (dryRun)
            {
                _logger.Info(
                    "backup",
                    $"[dry-run] 将从备份恢复：{backupDataPath} -> {dataFilePath}",
                    $"[dry-run] Would restore from backup: {backupDataPath} -> {dataFilePath}");
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
                    _logger.Info(
                        "backup",
                        $"[dry-run] 将恢复音频组：{file} -> {targetFile}",
                        $"[dry-run] Would restore audiogroup: {file} -> {targetFile}");
                }
                else
                {
                    File.Copy(file, targetFile, true);
                }
            }

            _logger.Success(
                "backup",
                dryRun ? "[dry-run] 已模拟在安装前自动还原 data.win 与音频组。" : "安装前已自动还原备份中的 data.win 和音频组文件。",
                dryRun ? "[dry-run] Simulated restoring data.win and audiogroups from backup." : "Restored data.win and audiogroups from backup.");
            return;
        }

        if (dryRun)
        {
            _logger.Info(
                "backup",
                $"[dry-run] 将创建 data.win 备份：{dataFilePath} -> {backupDataPath}",
                $"[dry-run] Would backup data.win: {dataFilePath} -> {backupDataPath}");
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
                _logger.Info(
                    "backup",
                    $"[dry-run] 将备份音频组：{file} -> {targetFile}",
                    $"[dry-run] Would backup audiogroup: {file} -> {targetFile}");
            }
            else
            {
                File.Copy(file, targetFile);
            }
        }

        if (dryRun)
        {
            _logger.Info(
                "backup",
                $"[dry-run] 将备份版本文件：{gameVerPath} -> {backupVerPath}",
                $"[dry-run] Would backup version file: {gameVerPath} -> {backupVerPath}");
        }
        else
        {
            File.Copy(gameVerPath, backupVerPath);
        }

        _logger.Success(
            "backup",
            dryRun ? "[dry-run] 备份创建模拟完成。" : "已创建备份文件。",
            dryRun ? "[dry-run] Backup creation simulation completed." : "Backup file created.");
    }

    private void PatchMods(
        UndertaleData data,
        string gamePath,
        string[] modDirs,
        bool dryRun,
        string operationWorkspace)
    {
        void HandlePatch(
            bool exists,
            string stage,
            string modDir,
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
            if (!exists)
            {
                if (dryRun)
                {
                    _logger.Warning(stage, missingZh, missingEn, modDir);
                }
                return;
            }

            _logger.Step(stage, stepZh, stepEn, modDir);
            if (dryRun)
            {
                _logger.Info(stage, dryRunDetectedZh, dryRunDetectedEn, modDir);
            }
            else
            {
                execute();
            }
            _logger.Success(stage, successZh, successEn, modDir);
        }

        foreach (var modDir in modDirs)
        {
            _logger.Section("mod", $"处理模组：{modDir}", $"Processing mod: {modDir}", modDir);

            var packagerDirectory = Path.Combine(operationWorkspace, "packager");
            var fontReplacer = new FontReplacer(data, modDir, packagerDirectory);
            var strReplacer = new StringReplacer(data, modDir);
            var spriteReplacer = new SpriteReplacer(data, modDir, packagerDirectory, _logger);
            var audioReplacer = new AudioReplacer(data, gamePath, modDir);
            var shaderReplacer = new ShaderReplacer(data, modDir);
            var objectPatcher = new ObjectPatcher(data, modDir);
            var codePatcher = new CodePatcher(data, modDir, _logger);

            var hasAnyPatchResource =
                fontReplacer.Exist()
                || strReplacer.Exist()
                || spriteReplacer.Exist()
                || audioReplacer.Exist()
                || shaderReplacer.Exist()
                || objectPatcher.Exist()
                || codePatcher.Exist();

            if (!hasAnyPatchResource)
            {
                _logger.Warning(
                    "mod",
                    "该模组目录未检测到任何可用修补资源，请检查文件是否摆放正确。",
                    "No patch resources were detected in this mod folder. Please check whether files are placed correctly.",
                    modDir);
                continue;
            }

            HandlePatch(fontReplacer.Exist(), "font", modDir, "正在修补字体...", "Patching fonts...", "未检测到 fonts 资源，已跳过。", "No fonts resource found, skipped.", "[dry-run] 已检测到 fonts 资源，将执行字体修补。", "[dry-run] Fonts resources detected, would patch fonts.", "字体修补完成。", "Fonts patched.", fontReplacer.Execute);
            HandlePatch(strReplacer.Exist(), "string", modDir, "正在修补文本...", "Patching strings...", "未检测到 excel 文本资源，已跳过。", "No excel string resources found, skipped.", "[dry-run] 已检测到 excel 文本资源，将执行文本修补。", "[dry-run] Excel string resources detected, would patch strings.", "文本修补完成。", "Strings patched.", strReplacer.Execute);
            HandlePatch(spriteReplacer.Exist(), "sprite", modDir, "正在修补图片...", "Patching sprites...", "未检测到 sprites 资源，已跳过。", "No sprites resources found, skipped.", "[dry-run] 已检测到 sprites 资源，将执行图片修补。", "[dry-run] Sprites resources detected, would patch sprites.", "图片修补完成。", "Sprites patched.", spriteReplacer.Execute);
            HandlePatch(audioReplacer.Exist(), "audio", modDir, "正在修补音频...", "Patching audios...", "未检测到 audios 资源，已跳过。", "No audios resource found, skipped.", "[dry-run] 已检测到 audios 资源，将执行音频修补。", "[dry-run] Audios resources detected, would patch audios.", "音频修补完成。", "Audios patched.", audioReplacer.Execute);
            HandlePatch(shaderReplacer.Exist(), "shader", modDir, "正在修补 Shader...", "Patching shaders...", "未检测到 shaders 资源，已跳过。", "No shaders resource found, skipped.", "[dry-run] 已检测到 shaders 资源，将执行 Shader 修补。", "[dry-run] Shaders resources detected, would patch shaders.", "Shader 修补完成。", "Shaders patched.", shaderReplacer.Execute);
            HandlePatch(objectPatcher.Exist(), "object", modDir, "正在修补对象...", "Patching objects...", "未检测到 objects 资源，已跳过。", "No objects resource found, skipped.", "[dry-run] 已检测到 objects 资源，将执行对象修补。", "[dry-run] Objects resources detected, would patch objects.", "对象修补完成。", "Objects patched.", objectPatcher.Execute);
            HandlePatch(codePatcher.Exist(), "code", modDir, "正在修补代码...", "Patching codes...", "未检测到代码补丁资源，已跳过。", "No code patch resources found, skipped.", "[dry-run] 已检测到代码补丁资源，将执行代码修补。", "[dry-run] Code patch resources detected, would patch codes.", "代码修补完成。", "Codes patched.", codePatcher.Execute);
        }
    }

    private void PatchRawFiles(
        string[] modDirs,
        string gamePath,
        string backupFolderPath,
        bool dryRun,
        bool backup,
        List<string> changedFiles)
    {
        foreach (var modDir in modDirs)
        {
            var rawPath = Path.Combine(modDir, "raw");
            if (!Directory.Exists(rawPath))
            {
                continue;
            }

            _logger.Step("raw", $"正在处理 raw 文件：{modDir}", $"Patching raw files for: {modDir}", modDir);

            foreach (var rawFile in Directory.GetFiles(rawPath, "*.*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(rawPath, rawFile);
                var gamePathFile = new FileInfo(Path.Combine(gamePath, relativePath));
                var backupPathFile = new FileInfo(Path.Combine(backupFolderPath, relativePath));

                if (backup && gamePathFile.Exists && !backupPathFile.Exists)
                {
                    if (dryRun)
                    {
                        _logger.Info("raw", $"[dry-run] 将创建备份目录：{backupPathFile.Directory!.FullName}", $"[dry-run] Would create backup directory: {backupPathFile.Directory!.FullName}", modDir);
                        _logger.Info("raw", $"[dry-run] 将备份原文件：{gamePathFile.FullName} -> {backupPathFile.FullName}", $"[dry-run] Would backup original file: {gamePathFile.FullName} -> {backupPathFile.FullName}", modDir);
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
                    _logger.Info("raw", $"[dry-run] 将确保目标目录存在：{gamePathFile.Directory!.FullName}", $"[dry-run] Would ensure target directory exists: {gamePathFile.Directory!.FullName}", modDir);
                    _logger.Info("raw", $"[dry-run] 将覆盖 raw 文件：{rawFile} -> {gamePathFile.FullName}", $"[dry-run] Would replace raw file: {rawFile} -> {gamePathFile.FullName}", modDir);
                }
                else
                {
                    if (!gamePathFile.Directory!.Exists)
                    {
                        Directory.CreateDirectory(gamePathFile.Directory.FullName);
                    }
                    File.Copy(rawFile, gamePathFile.FullName, true);
                    changedFiles.Add(gamePathFile.FullName);
                }
            }

            _logger.Success("raw", "raw 文件修补完成。", "Raw files patched.", modDir);
        }
    }

    private UndertaleData ReadDataFile(FileInfo dataFile)
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
                    $"UndertaleModLib 报告了无法安全忽略的文件读取警告：\n{warning}");
            });

            if (acceptedAlignmentGapCount > 0)
            {
                _logger.Warning(
                    "data",
                    $"检测到并安全跳过 {acceptedAlignmentGapCount:N0} 个纯零对齐区，共 {acceptedAlignmentGapBytes:N0} 字节。",
                    $"Detected and safely skipped {acceptedAlignmentGapCount:N0} zero-filled alignment gaps ({acceptedAlignmentGapBytes:N0} bytes total).");
            }

            foreach (var warning in nonImportantWarnings.Distinct())
            {
                _logger.Warning("data", $"UndertaleModLib：{warning}", $"UndertaleModLib: {warning}");
            }

            return data;
        }
        catch (VsmlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new VsmlException(
                "VSML_DATA_READ_FAILED",
                "读取 data.win 失败。",
                exception.ToString(),
                file: dataFile.FullName,
                innerException: exception);
        }
    }

    private void SaveDataFile(FileInfo dataFile, UndertaleData data, bool dryRun)
    {
        if (dryRun)
        {
            _logger.Info("save", $"[dry-run] 将写入 data.win：{dataFile.FullName}", $"[dry-run] Would write data.win: {dataFile.FullName}");
            return;
        }

        try
        {
            using var fs = dataFile.OpenWrite();
            UndertaleIO.Write(fs, data);
        }
        catch (Exception exception)
        {
            throw new VsmlException(
                "VSML_DATA_WRITE_FAILED",
                "保存 data.win 失败。",
                exception.ToString(),
                file: dataFile.FullName,
                innerException: exception);
        }
    }

    private static bool TryAcceptZeroPaddingAlignmentWarning(Stream validationStream, string warning, out long skippedBytes)
    {
        skippedBytes = 0;
        var match = Regex.Match(
            warning,
            @"^Reading misaligned at (?<actual>[0-9A-Fa-f]+), realigning back to (?<expected>[0-9A-Fa-f]+)",
            RegexOptions.CultureInvariant);

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

    private static VsmlModReview ReviewModDirectory(string modPath)
    {
        var resources = new List<string>();
        var warnings = new List<string>();
        foreach (var directoryName in new[] { "fonts", "excel", "sprites", "audios", "shaders", "objects", "codes", "raw" })
        {
            if (Directory.Exists(Path.Combine(modPath, directoryName)))
            {
                resources.Add(directoryName);
            }
        }

        var codePatchFile = Path.Combine(modPath, "codepatches.json");
        var codePatchCount = 0;
        if (File.Exists(codePatchFile))
        {
            resources.Add("codepatches");
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(codePatchFile));
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    codePatchCount = document.RootElement.GetArrayLength();
                }
                else
                {
                    warnings.Add("codepatches.json 的根节点不是数组。");
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                warnings.Add($"无法读取 codepatches.json：{exception.Message}");
            }
        }

        if (resources.Count == 0)
        {
            warnings.Add("未检测到任何可识别的修补资源。");
        }

        var codeDirectory = Path.Combine(modPath, "codes");
        return new VsmlModReview
        {
            Path = modPath,
            Enabled = true,
            Exists = true,
            Resources = resources,
            CodeFileCount = Directory.Exists(codeDirectory) ? Directory.GetFiles(codeDirectory, "*.gml").Length : 0,
            CodePatchCount = codePatchCount,
            Warnings = warnings
        };
    }

    private static string ValidateRestoreGameDirectory(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Path.IsPathFullyQualified(gameDirectory))
        {
            throw new VsmlException(
                "VSML_INVALID_GAME_DIRECTORY",
                "游戏目录必须是非空绝对路径。",
                file: gameDirectory);
        }

        var fullPath = Path.GetFullPath(gameDirectory.Trim());
        var dataFile = Path.Combine(fullPath, "data.win");
        if (!Directory.Exists(fullPath) || !File.Exists(dataFile))
        {
            throw new VsmlException(
                "VSML_INVALID_GAME_DIRECTORY",
                "还原目标不是包含 data.win 的有效游戏目录。",
                file: fullPath);
        }

        return fullPath;
    }

    private void TryDeleteOperationWorkspace(string operationWorkspace)
    {
        try
        {
            if (Directory.Exists(operationWorkspace))
            {
                Directory.Delete(operationWorkspace, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                "cleanup",
                $"无法清理 VSML 临时目录：{operationWorkspace}",
                $"Could not clean the VSML temporary directory: {operationWorkspace}");
        }
    }
}

internal sealed class VsmlEngineInstallOutcome
{
    internal List<string> ChangedFiles { get; init; } = [];
    internal List<VsmlModResult> Mods { get; init; } = [];
}

internal sealed class VsmlEngineRestoreOutcome
{
    internal List<string> ChangedFiles { get; init; } = [];
}

internal sealed class VsmlEngineValidationOutcome
{
    internal string GameDirectory { get; init; } = string.Empty;
    internal string DataFile { get; init; } = string.Empty;
    internal string VersionFile { get; init; } = string.Empty;
    internal string GameVersion { get; init; } = string.Empty;
    internal long DataFileSize { get; init; }
    internal string DataSha256 { get; init; } = string.Empty;
}
