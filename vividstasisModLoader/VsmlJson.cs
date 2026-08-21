using System.Text.Json;
using System.Text.Json.Serialization;

namespace vividstasisModLoader;

internal static class VsmlJson
{
    internal static VsmlInstallRequest DeserializeInstallRequest(string json)
        => JsonSerializer.Deserialize(json, VsmlJsonContext.Default.VsmlInstallRequest)
            ?? throw new JsonException("VSML install request is null.");

    internal static VsmlRestoreRequest DeserializeRestoreRequest(string json)
        => JsonSerializer.Deserialize(json, VsmlJsonContext.Default.VsmlRestoreRequest)
            ?? throw new JsonException("VSML restore request is null.");

    internal static string Serialize(VsmlVersionInfo value)
        => JsonSerializer.Serialize(value, VsmlJsonContext.Default.VsmlVersionInfo);

    internal static string Serialize(VsmlInstallResult value)
        => JsonSerializer.Serialize(value, VsmlJsonContext.Default.VsmlInstallResult);

    internal static string Serialize(VsmlRestoreResult value)
        => JsonSerializer.Serialize(value, VsmlJsonContext.Default.VsmlRestoreResult);

    internal static string Serialize(VsmlReviewResult value)
        => JsonSerializer.Serialize(value, VsmlJsonContext.Default.VsmlReviewResult);

    internal static string Serialize(VsmlValidationResult value)
        => JsonSerializer.Serialize(value, VsmlJsonContext.Default.VsmlValidationResult);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(VsmlInstallRequest))]
[JsonSerializable(typeof(VsmlRestoreRequest))]
[JsonSerializable(typeof(VsmlVersionInfo))]
[JsonSerializable(typeof(VsmlInstallResult))]
[JsonSerializable(typeof(VsmlRestoreResult))]
[JsonSerializable(typeof(VsmlReviewResult))]
[JsonSerializable(typeof(VsmlValidationResult))]
internal partial class VsmlJsonContext : JsonSerializerContext
{
}
