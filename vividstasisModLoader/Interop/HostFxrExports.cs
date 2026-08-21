using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace vividstasisModLoader.Interop;

/// <summary>
/// Stable UTF-8 JSON boundary loaded by BVOClient through hostfxr.
/// The returned strings are owned by VSML and must be released with FreeJson.
/// </summary>
public static class HostFxrExports
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static nint GetVersionJson()
        => AllocateUtf8(VsmlJson.Serialize(VsmlApi.GetVersion()));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static nint ReviewJson(nint requestJson)
    {
        try
        {
            var request = VsmlJson.DeserializeInstallRequest(ReadUtf8(requestJson));
            return AllocateUtf8(VsmlJson.Serialize(VsmlApi.Review(request)));
        }
        catch (Exception exception)
        {
            return AllocateUtf8(VsmlJson.Serialize(new VsmlReviewResult
            {
                Ok = false,
                Error = InvalidRequestError(exception)
            }));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static nint InstallJson(nint requestJson)
    {
        try
        {
            var request = VsmlJson.DeserializeInstallRequest(ReadUtf8(requestJson));
            return AllocateUtf8(VsmlJson.Serialize(VsmlApi.Install(request)));
        }
        catch (Exception exception)
        {
            return AllocateUtf8(VsmlJson.Serialize(new VsmlInstallResult
            {
                Ok = false,
                Error = InvalidRequestError(exception)
            }));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static nint RestoreJson(nint requestJson)
    {
        try
        {
            var request = VsmlJson.DeserializeRestoreRequest(ReadUtf8(requestJson));
            return AllocateUtf8(VsmlJson.Serialize(VsmlApi.Restore(request)));
        }
        catch (Exception exception)
        {
            return AllocateUtf8(VsmlJson.Serialize(new VsmlRestoreResult
            {
                Ok = false,
                Error = InvalidRequestError(exception)
            }));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static nint ValidateGameJson(nint gameDirectory)
    {
        try
        {
            return AllocateUtf8(VsmlJson.Serialize(VsmlApi.ValidateGame(ReadUtf8(gameDirectory))));
        }
        catch (Exception exception)
        {
            return AllocateUtf8(VsmlJson.Serialize(new VsmlValidationResult
            {
                Ok = false,
                Error = InvalidRequestError(exception)
            }));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static void FreeJson(nint json)
    {
        if (json != 0)
        {
            Marshal.FreeHGlobal(json);
        }
    }

    private static string ReadUtf8(nint value)
    {
        if (value == 0)
        {
            throw new ArgumentNullException(nameof(value), "The UTF-8 JSON pointer is null.");
        }

        return Marshal.PtrToStringUTF8(value)
            ?? throw new InvalidOperationException("The UTF-8 JSON request could not be decoded.");
    }

    private static nint AllocateUtf8(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var pointer = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        Marshal.WriteByte(pointer, bytes.Length, 0);
        return pointer;
    }

    private static VsmlError InvalidRequestError(Exception exception) => new()
    {
        Code = "VSML_INVALID_JSON_REQUEST",
        Message = "BVO Client 传入的 VSML JSON 请求无效。",
        Detail = exception.ToString()
    };
}
