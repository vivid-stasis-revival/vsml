using System.Text.Json;
using System.Text.RegularExpressions;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace vividstasisModLoader;

public class CodePatcher(UndertaleData data, string modDir, IVsmlLogger? logger = null)
{
    private readonly IVsmlLogger _logger = logger ?? NullVsmlLogger.Instance;
    private string _patchFilePath = $"{modDir}/codepatches.json";
    private string _codeReplacePath = $"{modDir}/codes/";
    private string _codePatchesPath = $"{modDir}/codepatches/";
    private List<CodePatch>? _patches = [];
    private GlobalDecompileContext _globalDecompileContext;
    private Dictionary<string, string> _cachedCodes = [];
    private HashSet<string> _replacedEntries = [];

    const string objectPrefix = "gml_Object_";

    public int GetCodeFileCount()
    {
        int count = 0;
        if (Directory.Exists(_codeReplacePath))
            count += Directory.GetFiles(_codeReplacePath, "*.gml").Length;
        if (File.Exists(_patchFilePath))
            count += 1; // codepatches.json counts as one code patch set
        return count;
    }
public bool Exist()
    {
        return Directory.Exists(_codeReplacePath) || File.Exists(_patchFilePath);
    }

    public HashSet<string> GetReplacedEntries() => _replacedEntries;

    public void Execute()
    {
        if (!Exist()) return;

        if (Directory.Exists(_codeReplacePath))
        {
            var pendingFiles = Directory.GetFiles(_codeReplacePath, "*.gml").OrderBy(f => f).ToList();
            // Keep every GlobalScript and object event in one import group.
            // Underanalyzer registers queued GlobalScripts before compiling
            // object events, which is required for object code to resolve
            // functions introduced by the same mod (upstream VML 0.1.4).
            var importGroup = new CodeImportGroup(data) { AutoCreateAssets = true };

            foreach (var file in pendingFiles)
            {
                var code = File.ReadAllText(file);
                var codeName = Path.GetFileNameWithoutExtension(file);
                _logger.Info("code", $"处理代码文件: {codeName}", $"Processing code file: {codeName}", modDir, codeName);
                // replaceGroup.QueueReplace(codeName, code);
                var manualLink = false;
                if (codeName.StartsWith(objectPrefix, StringComparison.Ordinal))
                {
                    var lastUnderscore = codeName.LastIndexOf('_');
                    var secondLastUnderscore = codeName.LastIndexOf('_', lastUnderscore - 1);
                    // 调试时可在此观察下划线分割位置。
                    if (lastUnderscore <= 0 || secondLastUnderscore <= 0)
                    {
                        _logger.Error("code", $"无法解析对象代码条目名：\"{codeName}\"。", $"Failed to parse object code entry name: \"{codeName}\".", modDir, codeName);
                        continue;
                    }

                    // Extract object name, event type, and event subtype
                    var objectName = codeName.AsSpan(new Range(objectPrefix.Length, secondLastUnderscore));
                    var eventType = codeName.AsSpan(new Range(secondLastUnderscore + 1, lastUnderscore));
                    _logger.Info("code", $"对象名与事件类型：{objectName.ToString()} / {eventType.ToString()}", $"Object and event type: {objectName.ToString()} / {eventType.ToString()}", modDir, codeName);
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
                            _logger.Error("code", $"无法解析事件类型和子类型：\"{codeName}\"。", $"Failed to parse event type and subtype for \"{codeName}\".", modDir, codeName);
                            continue;
                        }
                    }

                    // If manually linking, do so
                    if (!manualLink)
                    {
                        importGroup.QueueReplace(codeName, code);
                        _replacedEntries.Add(codeName);
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
                    CodeImportGroup.LinkEvent(obj, manualCode, EventType.Collision, eventSubtype, importGroup.MainThreadAction);
                    // Perform code import using manual code entry
                    importGroup.QueueReplace(manualCode, code);
                    _replacedEntries.Add(codeName);
                }
                else
                {
                    importGroup.QueueReplace(codeName, code);
                    _replacedEntries.Add(codeName);
                }
            }
            if (pendingFiles.Count > 0)
            {
                var finalResult = importGroup.Import();
                if (!finalResult.Successful)
                {
                    var detail = finalResult.PrintAllErrors(false);
                    _logger.Error("code", "代码导入失败。", "Code import unsuccessful.", modDir);
                    _logger.Error("code", detail, detail, modDir);
                    throw new VsmlException("VSML_CODE_COMPILE_FAILED", "代码导入失败。", detail, modDir, file: _codeReplacePath);
                }
            }
        }
        if (!File.Exists(_patchFilePath)) return;
        CodeImportGroup group = new(data) { AutoCreateAssets = true };
        _globalDecompileContext = new GlobalDecompileContext(data);
        using var patchDocument = JsonDocument.Parse(File.ReadAllText(_patchFilePath));
        _patches = new List<CodePatch>();
        foreach (var element in patchDocument.RootElement.EnumerateArray())
        {
            _patches.Add(new CodePatch
            {
                Entry = ReadString(element, "Entry"),
                Type = (PatchType)ReadInt(element, "Type"),
                Find = ReadString(element, "Find"),
                Value = ReadString(element, "Value"),
                ExternalFile = ReadString(element, "ExternalFile"),
                Function = ReadString(element, "Function")
            });
        }
        if (_patches == null) return;
        foreach (var patch in _patches)
        {
            var code = data.Code.ByName(patch.Entry);
            if (code is null) {
                _logger.Warning("code", $"条目 {patch.Entry} 不存在。", $"Entry {patch.Entry} doesn't exist.", modDir, patch.Entry);
                return; }
                
            if (!_cachedCodes.TryGetValue(patch.Entry, out string text))
            {
                text = GetDecompiledText(code);
                _cachedCodes[patch.Entry] = text;
            }

            if (!string.IsNullOrEmpty(patch.ExternalFile))
            {
                patch.Value = File.ReadAllText(Path.Combine(_codePatchesPath, patch.ExternalFile));
            }

            switch (patch.Type)
            {
                case PatchType.Replace:
                    text = text.Replace(patch.Find, patch.Value);
                    break;
                case PatchType.ReplaceOnce:
                    text = text.ReplaceFirst(patch.Find, patch.Value);
                    break;
                case PatchType.InsertBefore:
                    if (string.IsNullOrEmpty(patch.Function))
                    {
                        text = patch.Value + text;
                    }
                    else
                    {
                        text = InsertCode(text, patch.Function, patch.Type, patch.Value);
                    }
                    break;
                case PatchType.InsertAfter:
                    if (string.IsNullOrEmpty(patch.Function))
                    {
                        text += patch.Value;
                    }
                    else
                    {
                        text = InsertCode(text, patch.Function, patch.Type, patch.Value);
                    }
                    break;
            }
            _cachedCodes[patch.Entry] = text;
            group.QueueReplace(patch.Entry, text);
            _replacedEntries.Add(patch.Entry);
        }

        var result = group.Import();
        if (!result.Successful)
        {
            var detail = result.PrintAllErrors(false);
            _logger.Error("code", "代码导入失败。", "Code import unsuccessful.", modDir);
            _logger.Error("code", detail, detail, modDir);
            throw new VsmlException("VSML_CODE_COMPILE_FAILED", "代码导入失败。", detail, modDir, file: _patchFilePath);
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
            return "/*\nDECOMPILER FAILED!\n\n" + e + "\n*/";
        }
    }

    public static string InsertCode(string gmlCode, string functionName, PatchType type, string codeToInsert)
    {
        if (!TryFindFunctionBounds(gmlCode, functionName, out var openingBraceIndex, out var closingBraceIndex))
        {
            return gmlCode;
        }

        // 根据插入位置处理
        switch (type)
        {
            case PatchType.InsertBefore:
                return InsertAtPosition(gmlCode, openingBraceIndex + 1, codeToInsert + "\n");
            case PatchType.InsertAfter:
                return InsertAtPosition(gmlCode, closingBraceIndex, "\n" + codeToInsert);
            default:
                throw new ArgumentException("Unknown patch type");
        }
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

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
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
}
public class CodePatch
{
    public string Entry { get; set; }
    public PatchType Type { get; set; }
    public string Find { get; set; }
    public string Value { get; set; }
    public string ExternalFile { get; set; }
    public string Function { get; set; }
}
public enum PatchType
{
    Replace,
    ReplaceOnce,
    InsertBefore,
    InsertAfter
}
