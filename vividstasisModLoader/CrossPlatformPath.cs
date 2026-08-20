namespace vividstasisModLoader;

/// <summary>
/// Normalizes user/mod-provided filesystem paths so both '\\' and '/' work on every OS.
/// .NET treats '\\' as a normal filename character on Unix, so Windows-authored relative
/// paths such as "folder\\file.gml" must be normalized before Path APIs are used.
/// </summary>
internal static class CrossPlatformPath
{
    internal static string NormalizeSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    internal static string GetFullPath(string path)
    {
        return Path.GetFullPath(NormalizeSeparators(path));
    }

    internal static string Combine(string basePath, string childPath)
    {
        return Path.Combine(basePath, NormalizeSeparators(childPath));
    }
}
