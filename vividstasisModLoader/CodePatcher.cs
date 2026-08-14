using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace vividstasisModLoader;

public class CodePatcher(UndertaleData data, string modDir)
{
    private string _patchFilePath = $"{modDir}/codepatches.json";
    private string _codeReplacePath = $"{modDir}/codes/";
    private string _codePatchesPath = $"{modDir}/codepatches/";
    private List<CodePatch>? _patches = [];
    private GlobalDecompileContext _globalDecompileContext;
    private Dictionary<string, string> _cachedCodes = [];

    // 日志里用模组目录名指认补丁来源，装了十几个模组时才知道该去改谁。
    private readonly string _modName = Path.GetFileName(
        modDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    const string objectPrefix = "gml_Object_";

    // GetDecompiledText 反编译失败时返回的注释开头，用于识别“这不是真的代码”。
    private const string DecompileFailureMarker = "/*\nDECOMPILER FAILED!";

    public bool Exist()
    {
        return Directory.Exists(_codeReplacePath) || File.Exists(_patchFilePath);
    }

    public void Execute()
    {
        if (!Exist()) return;

        if (Directory.Exists(_codeReplacePath))
        {
            CodeImportGroup replaceGroup = new(data) { AutoCreateAssets = true };
            foreach (var file in Directory.GetFiles(_codeReplacePath, "*.gml"))
            {
                var code = File.ReadAllText(file);
                var codeName = Path.GetFileNameWithoutExtension(file);
                // replaceGroup.QueueReplace(codeName, code);
                var manualLink = false;
                if (codeName.StartsWith(objectPrefix, StringComparison.Ordinal))
                {
                    var lastUnderscore = codeName.LastIndexOf('_');
                    var secondLastUnderscore = codeName.LastIndexOf('_', lastUnderscore - 1);
                    // 调试时可在此观察下划线分割位置。
                    if (lastUnderscore <= 0 || secondLastUnderscore <= 0)
                    {
                        ConsoleOutput.PrintError($"无法解析对象代码条目名：\"{codeName}\"。", $"Failed to parse object code entry name: \"{codeName}\".");
                        continue;
                    }

                    // Extract object name, event type, and event subtype
                    var objectName = codeName.AsSpan(new Range(objectPrefix.Length, secondLastUnderscore));
                    var eventType = codeName.AsSpan(new Range(secondLastUnderscore + 1, lastUnderscore));
                    ConsoleOutput.PrintInfo($"对象名与事件类型：{objectName.ToString()} / {eventType.ToString()}", $"Object and event type: {objectName.ToString()} / {eventType.ToString()}");
                    if (!uint.TryParse(codeName.AsSpan(lastUnderscore + 1), out var eventSubtype))
                    {
                        // No number at the end of the name; parse it out as best as possible (may technically be ambiguous sometimes...).
                        // It should be a collision event, though.
                        manualLink = true;
                        var nameAfterPrefix = codeName.AsSpan(objectPrefix.Length);
                        const string collisionSeparator = "_Collision_";
                        var collisionSeparatorPos = nameAfterPrefix.LastIndexOf(collisionSeparator);
                        if (collisionSeparatorPos != -1)
                        {
                            // Split out the actual object name and the collision subtype
                            objectName = nameAfterPrefix[0..collisionSeparatorPos];
                            var collisionSubtype = nameAfterPrefix[(collisionSeparatorPos + collisionSeparator.Length)..];


                            // GameMaker 2.3+ uses the object name for the collision subtype
                            var objectIndex = data.GameObjects.IndexOfName(collisionSubtype);
                            if (objectIndex >= 0)
                            {
                                // Object already exists; use its ID as a subtype
                                eventSubtype = (uint)objectIndex;
                            }
                            else
                            {
                                // Need to create a new object
                                eventSubtype = (uint)data.GameObjects.Count;
                                data.GameObjects.Add(new UndertaleGameObject
                                {
                                    Name = data.Strings.MakeString(collisionSubtype.ToString())
                                });
                            }
                        }
                        else
                        {
                            ConsoleOutput.PrintError($"无法解析事件类型和子类型：\"{codeName}\"。", $"Failed to parse event type and subtype for \"{codeName}\".");
                            continue;
                        }
                    }

                    // If manually linking, do so
                    if (!manualLink)
                    {
                        
                     CodeImportGroup utdat = new(data);
                     utdat.QueueReplace(codeName, code);
                     var _result = utdat.Import();
                        if (!_result.Successful)
                        {
                            ConsoleOutput.PrintError("代码导入失败。", "Code import unsuccessful.");
                            ConsoleOutput.PrintError(_result.PrintAllErrors(false), _result.PrintAllErrors(false));
                        }
                        continue;
                    }
                    ;
                    // Create new object if necessary
                    var obj = data.GameObjects.ByName(objectName);
                    if (obj is null)
                    {
                        obj = new UndertaleGameObject
                        {
                            Name = data.Strings.MakeString(objectName.ToString())
                        };
                        data.GameObjects.Add(obj);
                    }


                    // Link to object's event with a blank code entry
                    var manualCode = UndertaleCode.CreateEmptyEntry(data, codeName);
                    CodeImportGroup.LinkEvent(obj, manualCode, EventType.Collision, eventSubtype, replaceGroup.MainThreadAction);
                    // Perform code import using manual code entry
                    replaceGroup.QueueReplace(manualCode, code);
                }
                else
                {
                    replaceGroup.QueueReplace(codeName, code);
                }
            }
            replaceGroup.Import();
        }
        if (!File.Exists(_patchFilePath)) return;
        CodeImportGroup group = new(data) { AutoCreateAssets = true };
        _globalDecompileContext = new GlobalDecompileContext(data);
        try
        {
            _patches = JsonSerializer.Deserialize<List<CodePatch>>(File.ReadAllText(_patchFilePath));
        }
        catch (JsonException e)
        {
            // codepatches.json 本身读不出来时，后面每一条补丁都无从谈起，直接中止。
            throw new CodePatchException(
                $"[{_modName}] 无法解析 codepatches.json：{e.Message}"
                + $" ([{_modName}] Failed to parse codepatches.json: {e.Message})");
        }

        if (_patches == null) return;

        var applied = 0;
        var missed = 0;

        for (var index = 0; index < _patches.Count; index++)
        {
            var patch = _patches[index];
            var label = DescribePatch(patch, index);

            // 定义层面的错误先于命中判断处理：这类问题一定是补丁写错了，
            // 不会随游戏版本变化而自愈，因此一律按强制错误中止。
            ValidateDefinition(patch, label);

            var code = data.Code.ByName(patch.Entry);
            if (code is null)
            {
                // 此处原本是 return，一个写错的 Entry 会让该模组后续所有补丁被静默丢弃。
                ReportMiss(patch, label,
                    $"代码条目 {patch.Entry} 不存在",
                    $"code entry {patch.Entry} does not exist");
                missed++;
                continue;
            }

            // 匿名函数子条目拿不到真实代码体，照常打补丁会把整个条目替换成一句注释。
            if (code.ParentEntry is not null)
            {
                Fail(label,
                    $"该条目是匿名函数引用，请改为对父条目 {code.ParentEntry.Name.Content} 打补丁。",
                    $"this entry is a reference to an anonymous function; patch its parent entry {code.ParentEntry.Name.Content} instead.");
            }

            if (!_cachedCodes.TryGetValue(patch.Entry, out string text))
            {
                text = GetDecompiledText(code);

                // 反编译失败时 text 是一段报错注释而非代码，继续打补丁等于把条目内容换成注释。
                if (text.StartsWith(DecompileFailureMarker, StringComparison.Ordinal))
                {
                    Fail(label,
                        $"代码条目 {patch.Entry} 反编译失败，无法安全打补丁。",
                        $"failed to decompile code entry {patch.Entry}; patching it would not be safe.");
                }

                _cachedCodes[patch.Entry] = text;
            }

            if (!string.IsNullOrEmpty(patch.ExternalFile))
            {
                patch.Value = File.ReadAllText(Path.Combine(_codePatchesPath, patch.ExternalFile));
            }

            if (!TryApply(patch, label, ref text))
            {
                ReportAnchorMiss(patch, label);
                missed++;
                continue;
            }

            applied++;
            _cachedCodes[patch.Entry] = text;
            group.QueueReplace(patch.Entry, text);
        }

        ReportSummary(applied, missed);

        var result = group.Import();
        if (!result.Successful)
        {
            ConsoleOutput.PrintError("代码导入失败。", "Code import unsuccessful.");
            ConsoleOutput.PrintError(result.PrintAllErrors(false), result.PrintAllErrors(false));
        }
    }

    public string GetDecompiledText(UndertaleCode code)
    {
        if (code.ParentEntry is not null)
            return $"// This code entry is a reference to an anonymous function within \"{code.ParentEntry.Name.Content}\", decompile that instead.";
        try
        {
            return new DecompileContext(_globalDecompileContext, code).DecompileToString();
        }
        catch (Exception e)
        {
            return DecompileFailureMarker + "\n\n" + e + "\n*/";
        }
    }

    public static string InsertCode(string gmlCode, string functionName, PatchType type, string codeToInsert)
    {
        return TryInsertCode(gmlCode, functionName, type, codeToInsert, out var result) ? result : gmlCode;
    }

    /// <summary>
    /// 与 <see cref="InsertCode"/> 相同，但把“没找到目标函数”和“插入成功但内容恰好没变”区分开，
    /// 让调用方能据此报告未命中，而不是靠比较前后文本来猜。
    /// </summary>
    public static bool TryInsertCode(string gmlCode, string functionName, PatchType type, string codeToInsert, out string result)
    {
        result = gmlCode;

        if (!TryFindFunctionBounds(gmlCode, functionName, out var openingBraceIndex, out var closingBraceIndex))
        {
            return false;
        }

        // 根据插入位置处理
        result = type switch
        {
            PatchType.InsertBefore => InsertAtPosition(gmlCode, openingBraceIndex + 1, codeToInsert + "\n"),
            PatchType.InsertAfter => InsertAtPosition(gmlCode, closingBraceIndex, "\n" + codeToInsert),
            _ => throw new ArgumentException("Unknown patch type")
        };
        return true;
    }

    private static bool TryFindFunctionBounds(string gmlCode, string functionName, out int openingBraceIndex, out int closingBraceIndex)
    {
        openingBraceIndex = -1;
        closingBraceIndex = -1;

        // 处理带命名空间的函数名 (如: object_name.event_name)
        string namePattern = Regex.Escape(functionName).Replace(@"\.", @"[\.:]");

        var functionMatch = Regex.Match(gmlCode, $@"\bfunction\s+{namePattern}\s*\(", RegexOptions.Singleline);
        if (!functionMatch.Success)
        {
            return false;
        }

        var openParenIndex = gmlCode.IndexOf('(', functionMatch.Index + functionMatch.Length - 1);
        if (openParenIndex == -1)
        {
            return false;
        }

        var closeParenIndex = FindMatchingParen(gmlCode, openParenIndex);
        if (closeParenIndex == -1)
        {
            return false;
        }

        var braceIndex = FindOpeningBrace(gmlCode, closeParenIndex + 1);
        if (braceIndex == -1)
        {
            return false;
        }

        var endBraceIndex = FindMatchingBrace(gmlCode, braceIndex);
        if (endBraceIndex == -1)
        {
            return false;
        }

        openingBraceIndex = braceIndex;
        closingBraceIndex = endBraceIndex;
        return true;
    }

    private static int FindMatchingParen(string code, int startIndex)
    {
        int depth = 1;
        bool inString = false;
        bool inComment = false;
        bool escapeNext = false;

        for (int i = startIndex + 1; i < code.Length; i++)
        {
            char c = code[i];

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (c == '"' && !inComment)
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '/' && i < code.Length - 1)
                {
                    if (code[i + 1] == '/') inComment = true;
                    else if (code[i + 1] == '*') inComment = true;
                }
                else if (c == '\n' && inComment)
                {
                    inComment = false;
                }
                else if (c == '*' && i < code.Length - 1 && code[i + 1] == '/')
                {
                    inComment = false;
                    i++;
                }
            }

            if (inString || inComment)
            {
                if (c == '\\') escapeNext = true;
                continue;
            }

            if (c == '(') depth++;
            else if (c == ')') depth--;

            if (depth == 0) return i;
        }

        return -1;
    }

    private static int FindOpeningBrace(string code, int startIndex)
    {
        bool inString = false;
        bool inComment = false;
        bool escapeNext = false;

        for (int i = startIndex; i < code.Length; i++)
        {
            char c = code[i];

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (c == '"' && !inComment)
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '/' && i < code.Length - 1)
                {
                    if (code[i + 1] == '/') inComment = true;
                    else if (code[i + 1] == '*') inComment = true;
                }
                else if (c == '\n' && inComment)
                {
                    inComment = false;
                }
                else if (c == '*' && i < code.Length - 1 && code[i + 1] == '/')
                {
                    inComment = false;
                    i++;
                }
            }

            if (inString || inComment)
            {
                if (c == '\\') escapeNext = true;
                continue;
            }

            if (c == '{') return i;
        }

        return -1;
    }
    private static int FindMatchingBrace(string code, int startIndex)
    {
        int depth = 1;
        bool inString = false;
        bool inComment = false;
        bool escapeNext = false;

        for (int i = startIndex + 1; i < code.Length; i++)
        {
            char c = code[i];
            char prev = i > 0 ? code[i - 1] : '\0';

            // 处理转义字符
            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            // 处理字符串
            if (c == '"' && !inComment)
            {
                inString = !inString;
                continue;
            }

            // 处理注释
            if (!inString)
            {
                if (c == '/' && i < code.Length - 1)
                {
                    if (code[i + 1] == '/') inComment = true;
                    else if (code[i + 1] == '*') inComment = true;
                }
                else if (c == '\n' && inComment)
                {
                    inComment = false;
                }
                else if (c == '*' && i < code.Length - 1 && code[i + 1] == '/')
                {
                    inComment = false;
                    i++;// 跳过*/
                }
            }

            if (inString || inComment)
            {
                if (c == '\\') escapeNext = true;
                continue;
            }

            // 处理大括号
            if (c == '{') depth++;
            else if (c == '}') depth--;

            // 找到匹配的结束大括号
            if (depth == 0) return i;
        }

        return -1;// 未找到匹配的结束大括号
    }

    private static string InsertAtPosition(string code, int position, string insertCode)
    {
        // 获取当前行的缩进
        int lineStart = code.LastIndexOf('\n', position) + 1;
        string indent = GetIndent(code, lineStart, position);

        // 格式化要插入的代码
        string formattedCode = FormatCodeWithIndent(insertCode, indent);

        // 插入代码并保持原有缩进
        return code.Insert(position, "\n" + formattedCode + indent);
    }

    private static string GetIndent(string code, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(code[i]))
            {
                return code.Substring(start, i - start);
            }
        }
        return "";
    }

    private static string FormatCodeWithIndent(string code, string baseIndent)
    {
        // 为每一行添加基本缩进
        string[] lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            // 保留原有缩进并添加基础缩进
            string trimmed = lines[i].TrimStart();
            string lineIndent = lines[i].Substring(0, lines[i].Length - trimmed.Length);
            lines[i] = baseIndent + lineIndent + trimmed;
        }
        return string.Join("\n", lines);
    }

    // ---- 补丁结果报告：提醒 / 警告 / 强制错误 ----

    /// <summary>
    /// 用“模组 + codepatches.json 中的序号 + 条目名”定位一条补丁，
    /// 让日志里的每一行都能直接对应回源文件的某一条。
    /// </summary>
    private string DescribePatch(CodePatch patch, int index)
    {
        var entry = string.IsNullOrWhiteSpace(patch.Entry) ? "?" : patch.Entry;
        return $"[{_modName}] codepatches.json #{index} ({entry})";
    }

    /// <summary>
    /// 校验补丁定义本身是否合法。这里的问题都属于补丁写错了，不可能靠重试或换版本解决，
    /// 因此一律按强制错误中止 —— 中止发生在写回 data.win 之前，游戏仍是干净的。
    /// </summary>
    private void ValidateDefinition(CodePatch patch, string label)
    {
        if (string.IsNullOrWhiteSpace(patch.Entry))
        {
            Fail(label, "缺少 Entry 字段。", "is missing the required Entry field.");
        }

        if (!Enum.IsDefined(patch.Type))
        {
            Fail(label,
                $"Type 的值 {(int)patch.Type} 不是受支持的补丁类型（有效值 0-3）。",
                $"has an unsupported Type value {(int)patch.Type} (valid values are 0-3).");
        }

        // Type 0/1 靠 Find 定位；Find 为空时 string.Replace 会直接抛异常，
        // 与其让它在半路炸掉，不如在这里说清楚是哪一条写错了。
        if (patch.Type is PatchType.Replace or PatchType.ReplaceOnce && string.IsNullOrEmpty(patch.Find))
        {
            Fail(label,
                "Type 为 0 或 1 时必须提供非空的 Find。",
                "must provide a non-empty Find when Type is 0 or 1.");
        }

        if (!string.IsNullOrEmpty(patch.ExternalFile))
        {
            var externalPath = Path.Combine(_codePatchesPath, patch.ExternalFile);
            if (!File.Exists(externalPath))
            {
                Fail(label,
                    $"ExternalFile 指向的文件不存在：{externalPath}",
                    $"references an ExternalFile that does not exist: {externalPath}");
            }
        }
    }

    /// <summary>
    /// 应用一条补丁；返回锚点是否命中。未命中时 <paramref name="text"/> 保持原样。
    /// </summary>
    private bool TryApply(CodePatch patch, string label, ref string text)
    {
        switch (patch.Type)
        {
            case PatchType.Replace:
            {
                var hits = CountOccurrences(text, patch.Find);
                if (hits == 0) return false;

                // 命中多处时 Type 0 会全部替换，作者未必是这个意思，提醒一句。
                if (hits > 1)
                {
                    ConsoleOutput.PrintNotice(
                        $"{label} 的 Find 命中 {hits} 处，已全部替换。",
                        $"{label}: Find matched {hits} times; all of them were replaced.");
                }

                text = text.Replace(patch.Find, patch.Value);
                return true;
            }
            case PatchType.ReplaceOnce:
            {
                var hits = CountOccurrences(text, patch.Find);
                if (hits == 0) return false;

                // 反过来，Type 1 只改第一处，剩下的原样留着，同样值得提一句。
                if (hits > 1)
                {
                    ConsoleOutput.PrintNotice(
                        $"{label} 的 Find 命中 {hits} 处，仅替换了第一处。",
                        $"{label}: Find matched {hits} times; only the first one was replaced.");
                }

                text = text.ReplaceFirst(patch.Find, patch.Value);
                return true;
            }
            case PatchType.InsertBefore:
            case PatchType.InsertAfter:
            {
                // 未指定 Function 时按整条 Entry 处理，没有锚点，不存在未命中。
                if (string.IsNullOrEmpty(patch.Function))
                {
                    text = patch.Type == PatchType.InsertBefore ? patch.Value + text : text + patch.Value;
                    return true;
                }

                if (!TryInsertCode(text, patch.Function, patch.Type, patch.Value, out var inserted))
                {
                    return false;
                }

                text = inserted;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// 锚点未命中的统一报告入口，按补丁类型说明是 Find 还是 Function 没找到。
    /// </summary>
    private void ReportAnchorMiss(CodePatch patch, string label)
    {
        if (patch.Type is PatchType.InsertBefore or PatchType.InsertAfter)
        {
            ReportMiss(patch, label,
                $"未找到目标函数 {patch.Function}",
                $"target function {patch.Function} was not found");
            return;
        }

        ReportMiss(patch, label,
            $"未找到 Find：{Summarize(patch.Find)}",
            $"Find was not matched: {Summarize(patch.Find)}");
    }

    /// <summary>
    /// 未命中意味着这条补丁没生效。按补丁自己声明的 OnMiss 级别分流：
    /// 提醒（可选补丁，知道就行）、警告（默认，功能大概率失效）、强制错误（缺了就不该继续装）。
    /// </summary>
    private void ReportMiss(CodePatch patch, string label, string reasonZh, string reasonEn)
    {
        var zh = $"{label} 未生效：{reasonZh}。可能与其它模组冲突，或游戏版本已更新。";
        var en = $"{label} was not applied: {reasonEn}. It may conflict with another mod, or the game may have been updated.";

        switch (patch.OnMiss)
        {
            case MissLevel.Notice:
                ConsoleOutput.PrintNotice(zh, en);
                break;

            case MissLevel.Error:
                ConsoleOutput.PrintError(zh, en);
                throw new CodePatchException(
                    $"{label} 未生效，且已标记 OnMiss=Error，修补已中止。"
                    + $" ({label} was not applied and is marked OnMiss=Error; patching has been aborted.)");

            default:
                ConsoleOutput.PrintWarning(zh, en);
                break;
        }
    }

    /// <summary>
    /// 汇总本模组的代码补丁结果。逐条日志容易被刷过去，这一行是最后的兜底。
    /// </summary>
    private void ReportSummary(int applied, int missed)
    {
        if (missed > 0)
        {
            ConsoleOutput.PrintWarning(
                $"[{_modName}] 代码补丁：{applied} 条生效，{missed} 条未生效。",
                $"[{_modName}] Code patches: {applied} applied, {missed} not applied.");
            return;
        }

        ConsoleOutput.PrintNotice(
            $"[{_modName}] 代码补丁：{applied} 条全部生效。",
            $"[{_modName}] Code patches: all {applied} applied.");
    }

    /// <summary>
    /// 补丁定义错误：先打印明确的双语错误，再抛给 Program 统一中止流程。
    /// </summary>
    [DoesNotReturn]
    private void Fail(string label, string zh, string en)
    {
        ConsoleOutput.PrintError($"{label} {zh}", $"{label} {en}");
        throw new CodePatchException($"{label} {zh} ({label} {en})");
    }

    /// <summary>
    /// 统计 needle 在 haystack 中出现的次数（Ordinal，不重叠）。
    /// </summary>
    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>
    /// Find 常常是一整段多行代码，日志里压成单行并截断，避免刷屏。
    /// </summary>
    private static string Summarize(string value, int maxLength = 80)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";

        var singleLine = Regex.Replace(value, @"\s+", " ").Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "…";
    }
}

/// <summary>
/// 代码补丁的致命错误：补丁定义非法，或标记为 OnMiss=Error 的补丁未命中。
/// 抛出时机早于写回 data.win，因此游戏文件仍停留在备份还原后的干净状态。
/// </summary>
public class CodePatchException(string message) : Exception(message);

public class CodePatch
{
    public string Entry { get; set; }
    public PatchType Type { get; set; }
    public string Find { get; set; }
    public string Value { get; set; }
    public string ExternalFile { get; set; }
    public string Function { get; set; }

    /// <summary>
    /// 该补丁未命中时的处理级别，缺省为 <see cref="MissLevel.Warn"/>。
    /// 可写名称（"Notice"/"Warn"/"Error"）或数字（0/1/2）。
    /// </summary>
    public MissLevel OnMiss { get; set; } = MissLevel.Warn;
}

/// <summary>
/// 补丁未命中时的严重程度。未命中通常意味着锚点被其它模组改掉了，或游戏更新后代码变了。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissLevel>))]
public enum MissLevel
{
    /// <summary>提醒：本就是可选补丁，没命中也在预期内，只记一行。</summary>
    Notice,

    /// <summary>警告：默认级别，未命中通常代表该功能失效，但其余补丁继续。</summary>
    Warn,

    /// <summary>强制错误：这条补丁是模组的前提，没命中就中止整个修补，不写回 data.win。</summary>
    Error
}

public enum PatchType
{
    Replace,
    ReplaceOnce,
    InsertBefore,
    InsertAfter
}