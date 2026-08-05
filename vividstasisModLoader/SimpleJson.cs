using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace vividstasisModLoader
{
    /// <summary>
    /// 零反射、AOT 安全的轻量 JSON 解析器。
    /// </summary>
    internal static class SimpleJson
    {
        // ── 公共 API ──────────────────────────────────────────

        public static ModLoaderConfig ParseModLoaderConfig(string json)
        {
            var dict = ParseObject(json);
            return new ModLoaderConfig
            {
                GamePath = GetString(dict, "GamePath") ?? ""
            };
        }

        public static string ToJson(ModLoaderConfig c) =>
            WriteObject(("GamePath", (object?)c.GamePath));

        public static GamePathConfig ParseGamePathConfig(string json)
        {
            var dict = ParseObject(json);
            return new GamePathConfig
            {
                GamePath = GetString(dict, "game_path") ?? @"C:\example\path\",
                ForceUseCustomPath = GetBool(dict, "force_use_custom_path"),
                ForceCustomPath = GetNullableBool(dict, "force_custom_path")
            };
        }

        public static string ToJson(GamePathConfig c)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            WritePair(sb, "game_path", c.GamePath);
            sb.Append(',');
            WritePair(sb, "force_use_custom_path", c.ForceUseCustomPath);
            sb.Append(',');
            WritePair(sb, "force_custom_path", c.ForceCustomPath);
            sb.Append('}');
            return sb.ToString();
        }

        public static List<CodePatch> ParseCodePatchList(string json)
        {
            var list = ParseArray(json);
            var result = new List<CodePatch>(list.Count);
            foreach (var item in list)
            {
                if (item is Dictionary<string, object?> dict)
                {
                    result.Add(new CodePatch
                    {
                        Entry = GetString(dict, "Entry") ?? "",
                        Type = (PatchType)GetInt(dict, "Type"),
                        Find = GetString(dict, "Find"),
                        Value = GetString(dict, "Value"),
                        ExternalFile = GetString(dict, "ExternalFile") ?? "",
                        Function = GetString(dict, "Function") ?? ""
                    });
                }
            }
            return result;
        }

        public static ObjectPatch ParseObjectPatch(string json)
        {
            var dict = ParseObject(json);
            return new ObjectPatch
            {
                Name = GetString(dict, "Name") ?? "",
                Parent = GetString(dict, "Parent") ?? "",
                Awake = GetBool(dict, "Awake")
            };
        }

        public static string ToExceptionJson(string exceptionType, string message, string details)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            WritePair(sb, "exception_type", exceptionType);
            sb.Append(',');
            WritePair(sb, "message", message);
            sb.Append(',');
            WritePair(sb, "details", details);
            sb.Append('}');
            return sb.ToString();
        }

        // ── 公共解析入口 ──────────────────────────────────────

        public static Dictionary<string, object?> ParseObject(string json)
        {
            int pos = 0;
            return ParseObjectInternal(json, ref pos);
        }

        public static List<object?> ParseArray(string json)
        {
            int pos = 0;
            return ParseArrayInternal(json, ref pos);
        }

        // ── 内部解析（带位置追踪，支持嵌套） ─────────────────

        private static Dictionary<string, object?> ParseObjectInternal(string json, ref int pos)
        {
            pos = SkipWs(json, pos);
            if (pos >= json.Length || json[pos] != '{')
                throw new FormatException($"Expected '{{' at pos {pos}");
            pos = SkipWs(json, pos + 1);

            var dict = new Dictionary<string, object?>();

            if (pos < json.Length && json[pos] == '}')
            {
                pos = SkipWs(json, pos + 1);
                return dict;
            }

            while (true)
            {
                pos = SkipWs(json, pos);
                if (pos >= json.Length || json[pos] != '"')
                    throw new FormatException($"Expected string key at pos {pos}");
                var key = ReadString(json, ref pos);

                pos = SkipWs(json, pos);
                if (pos >= json.Length || json[pos] != ':')
                    throw new FormatException($"Expected ':' at pos {pos}");
                pos = SkipWs(json, pos + 1);

                dict[key] = ReadValue(json, ref pos);

                pos = SkipWs(json, pos);
                if (pos >= json.Length)
                    throw new FormatException("Unexpected end of JSON");
                if (json[pos] == '}')
                {
                    pos = SkipWs(json, pos + 1);
                    break;
                }
                if (json[pos] != ',')
                    throw new FormatException($"Expected ',' or '}}' at pos {pos}");
                pos = SkipWs(json, pos + 1);
            }

            return dict;
        }

        private static List<object?> ParseArrayInternal(string json, ref int pos)
        {
            pos = SkipWs(json, pos);
            if (pos >= json.Length || json[pos] != '[')
                throw new FormatException($"Expected '[' at pos {pos}");
            pos = SkipWs(json, pos + 1);

            var list = new List<object?>();

            if (pos < json.Length && json[pos] == ']')
            {
                pos = SkipWs(json, pos + 1);
                return list;
            }

            while (true)
            {
                pos = SkipWs(json, pos);
                list.Add(ReadValue(json, ref pos));

                pos = SkipWs(json, pos);
                if (pos >= json.Length)
                    throw new FormatException("Unexpected end of JSON array");
                if (json[pos] == ']')
                {
                    pos = SkipWs(json, pos + 1);
                    break;
                }
                if (json[pos] != ',')
                    throw new FormatException($"Expected ',' or ']' at pos {pos}");
                pos = SkipWs(json, pos + 1);
            }

            return list;
        }

        // ── 值读取 ────────────────────────────────────────────

        private static object? ReadValue(string json, ref int pos)
        {
            pos = SkipWs(json, pos);
            if (pos >= json.Length)
                throw new FormatException("Unexpected end of JSON");

            char c = json[pos];
            return c switch
            {
                '"' => ReadString(json, ref pos),
                '{' => ParseObjectInternal(json, ref pos),
                '[' => ParseArrayInternal(json, ref pos),
                't' or 'T' => ReadLiteral(json, ref pos, "true", true),
                'f' or 'F' => ReadLiteral(json, ref pos, "false", false),
                'n' or 'N' => ReadNull(json, ref pos),
                _ => ReadNumber(json, ref pos)
            };
        }

        private static string ReadString(string json, ref int pos)
        {
            if (json[pos] != '"')
                throw new FormatException($"Expected '\"' at pos {pos}");
            pos++;
            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '"')
                {
                    pos++;
                    return sb.ToString();
                }
                if (c == '\\')
                {
                    pos++;
                    if (pos >= json.Length) break;
                    sb.Append(json[pos] switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'u' => ReadUnicode(json, ref pos),
                        _ => json[pos]
                    });
                }
                else
                {
                    sb.Append(c);
                }
                pos++;
            }
            throw new FormatException("Unterminated string");
        }

        private static char ReadUnicode(string json, ref int pos)
        {
            if (pos + 4 >= json.Length)
                throw new FormatException("Invalid unicode escape");
            int code = 0;
            for (int i = 0; i < 4; i++)
            {
                pos++;
                code = (code << 4) | HexVal(json[pos]);
            }
            return (char)code;
        }

        private static object? ReadNumber(string json, ref int pos)
        {
            int start = pos;
            if (pos < json.Length && json[pos] == '-') pos++;
            while (pos < json.Length && char.IsAsciiDigit(json[pos])) pos++;
            bool isFloat = false;
            if (pos < json.Length && json[pos] == '.')
            {
                isFloat = true;
                pos++;
                while (pos < json.Length && char.IsAsciiDigit(json[pos])) pos++;
            }
            if (pos < json.Length && (json[pos] == 'e' || json[pos] == 'E'))
            {
                isFloat = true;
                pos++;
                if (pos < json.Length && (json[pos] == '+' || json[pos] == '-')) pos++;
                while (pos < json.Length && char.IsAsciiDigit(json[pos])) pos++;
            }

            string numStr = json[start..pos];
            if (isFloat)
                return double.Parse(numStr, CultureInfo.InvariantCulture);
            return int.Parse(numStr, CultureInfo.InvariantCulture);
        }

        private static object? ReadLiteral(string json, ref int pos, string literal, object? value)
        {
            if (json.AsSpan(pos).StartsWith(literal, StringComparison.OrdinalIgnoreCase))
            {
                pos += literal.Length;
                return value;
            }
            throw new FormatException($"Expected '{literal}' at pos {pos}");
        }

        private static object? ReadNull(string json, ref int pos)
        {
            return ReadLiteral(json, ref pos, "null", null);
        }

        // ── 辅助方法 ──────────────────────────────────────────

        private static int SkipWs(string json, int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
            return pos;
        }

        private static int HexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => throw new FormatException($"Invalid hex char: {c}")
        };

        private static string? GetString(Dictionary<string, object?> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v is string s ? s : null;
        }

        private static int GetInt(Dictionary<string, object?> dict, string key)
        {
            if (dict.TryGetValue(key, out var v))
            {
                if (v is int i) return i;
                if (v is double d) return (int)d;
            }
            return 0;
        }

        private static bool GetBool(Dictionary<string, object?> dict, string key)
        {
            if (dict.TryGetValue(key, out var v))
            {
                if (v is bool b) return b;
                if (v is int i) return i != 0;
            }
            return false;
        }

        private static bool? GetNullableBool(Dictionary<string, object?> dict, string key)
        {
            if (dict.TryGetValue(key, out var v))
            {
                if (v == null) return null;
                if (v is bool b) return b;
                if (v is int i) return i != 0;
            }
            return null;
        }

        // ── JSON 写入辅助 ─────────────────────────────────────

        private static string WriteObject(params (string key, object? value)[] pairs)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                WritePair(sb, pairs[i].key, pairs[i].value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void WritePair(StringBuilder sb, string key, object? value)
        {
            WriteString(sb, key);
            sb.Append(':');
            WriteValue(sb, value);
        }

        private static void WriteValue(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i);
                    break;
                case double d:
                    sb.Append(d.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                sb.Append(c switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => c.ToString()
                });
            }
            sb.Append('"');
        }
    }
}