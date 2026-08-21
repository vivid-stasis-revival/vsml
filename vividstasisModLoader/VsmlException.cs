namespace vividstasisModLoader;

public sealed class VsmlException : Exception
{
    public VsmlException(
        string code,
        string message,
        string? detail = null,
        string? mod = null,
        string? entry = null,
        string? file = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Detail = detail;
        Mod = mod;
        Entry = entry;
        File = file;
    }

    public string Code { get; }
    public string? Detail { get; }
    public string? Mod { get; }
    public string? Entry { get; }
    public string? File { get; }

    internal VsmlError ToError() => new()
    {
        Code = Code,
        Message = Message,
        Detail = Detail,
        Mod = Mod,
        Entry = Entry,
        File = File
    };
}
