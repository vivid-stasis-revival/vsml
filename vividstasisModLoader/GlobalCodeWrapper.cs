using System.Text.Json;
using System.Text.RegularExpressions;
using Underanalyzer.Decompiler;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace vividstasisModLoader;

public class GlobalCodeWrapper(UndertaleData data, string modDir, HashSet<string> replacedEntries)
{
    private readonly string _configPath = Path.Combine(modDir, "global_patches.json");
    private GlobalDecompileContext _globalDecompileContext;
    private HashSet<string> _skipEntries = replacedEntries;

    public bool Exist() => File.Exists(_configPath);

    public void Execute()
    {
        if (!Exist()) return;
        ConsoleOutput.PrintSection("全局代码包装", "Global code wrapping");
        _globalDecompileContext = new GlobalDecompileContext(data);

        using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
        var root = doc.RootElement;

        var replacements = new List<(string find, string replace)>();
        if (root.TryGetProperty("replacements", out var reps))
        {
            foreach (var rep in reps.EnumerateArray())
            {
                var find = rep.GetProperty("find").GetString();
                var replace = rep.GetProperty("replace").GetString();
                if (!string.IsNullOrEmpty(find) && replace is not null)
                    replacements.Add((find, replace));
            }
        }

        string createInsert = "";
        string stepDrawInsert = "";
        if (root.TryGetProperty("create_insert", out var ci) && ci.ValueKind == JsonValueKind.String)
            createInsert = ci.GetString() ?? "";
        if (root.TryGetProperty("step_draw_insert", out var sdi) && sdi.ValueKind == JsonValueKind.String)
            stepDrawInsert = sdi.GetString() ?? "";

        var changed = new Dictionary<string, string>();
        int totalChanged = 0;
        const string objPrefix = "gml_Object_";

        foreach (var code in data.Code)
        {
            if (code.ParentEntry is not null) continue;
            var name = code.Name?.Content;
            if (string.IsNullOrEmpty(name)) continue;
            if (_skipEntries.Contains(name)) continue;
            if (name.Contains("tvo_", StringComparison.Ordinal)) continue;

            string text;
            try
            {
                text = new DecompileContext(_globalDecompileContext, code).DecompileToString();
            }
            catch { continue; }

            string original = text;

            foreach (var (find, replace) in replacements)
                text = Regex.Replace(text, find, replace);

            if (name.StartsWith(objPrefix, StringComparison.Ordinal))
            {
                var afterPrefixStr = name.Substring(objPrefix.Length);
                var lastUnderscore = afterPrefixStr.LastIndexOf('_');
                if (lastUnderscore > 0)
                {
                    var secondLastUnderscore = afterPrefixStr.LastIndexOf('_', lastUnderscore - 1);
                    if (secondLastUnderscore > 0)
                    {
                        var eventType = afterPrefixStr.Substring(secondLastUnderscore + 1, lastUnderscore - secondLastUnderscore - 1);
                        if (eventType == "Create" && createInsert.Length > 0)
                            text = createInsert + "\n" + text;
                        else if ((eventType == "Step" || eventType == "Draw") && stepDrawInsert.Length > 0)
                            text = stepDrawInsert + "\n" + text;
                    }
                }
            }

            if (text != original)
            {
                changed[name] = text;
                totalChanged++;
            }
        }

        ConsoleOutput.PrintInfo($"需包装代码条目: {totalChanged}", $"Code entries to wrap: {totalChanged}");
        if (totalChanged == 0) return;

        const int batchSize = 64;
        var ordered = changed.OrderBy(p => p.Key).ToArray();
        for (int offset = 0; offset < ordered.Length; offset += batchSize)
        {
            var batch = new CodeImportGroup(data) { AutoCreateAssets = true };
            foreach (var pair in ordered.Skip(offset).Take(batchSize))
                batch.QueueReplace(pair.Key, pair.Value);
            var result = batch.Import();
            if (!result.Successful)
            {
                ConsoleOutput.PrintWarning(
                    $"批次 {offset / batchSize + 1} 导入有误: {result.PrintAllErrors(false)}",
                    $"Batch {offset / batchSize + 1} import errors: {result.PrintAllErrors(false)}");
            }
        }
        ConsoleOutput.PrintSuccess("全局代码包装完成。", "Global code wrapping complete.");
    }
}
