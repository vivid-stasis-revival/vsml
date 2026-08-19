using System.Text.RegularExpressions;

namespace vividstasisModLoader;

public static partial class StringExtensions
{
    public static string ReplaceFirst(this string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(source)) return source;
        if (string.IsNullOrEmpty(oldValue)) return source;
        
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        return index == -1 
            ? source 
            : string.Concat(
                source.AsSpan(0, index),
                newValue,
                source.AsSpan(index + oldValue.Length)
                );
    }
    public static string StripPattern(this string pattern)
    {
        return StripPatternRegex().Replace(pattern, "");
    }

    [GeneratedRegex(@"\s*#.*|\s+", RegexOptions.Multiline)]
    private static partial Regex StripPatternRegex();
}