using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

/// <summary>
/// Deserializes an industry template JSON file (section 11). Phase 0 only applies
/// <see cref="Roles"/>, <see cref="Features"/>, <see cref="Settings"/> and
/// <see cref="NumberSeries"/> — the catalog/inventory/finance sections are read but not yet
/// written, since those modules don't exist until Phase 1.
/// </summary>
public sealed class IndustryTemplate
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    [JsonPropertyName("industry")]
    public string Industry { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<RoleTemplate> Roles { get; set; } = [];

    [JsonPropertyName("features")]
    public Dictionary<string, bool> Features { get; set; } = [];

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = [];

    [JsonPropertyName("numberSeries")]
    public List<NumberSeriesTemplate> NumberSeries { get; set; } = [];

    public static IndustryTemplate Load(string industry)
    {
        var assembly = typeof(IndustryTemplate).Assembly;
        var resourceName = $"StockOrder.Modules.Platform.Templates.{industry}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"No embedded template found for industry '{industry}' (expected resource '{resourceName}').");

        return JsonSerializer.Deserialize<IndustryTemplate>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Template for industry '{industry}' is empty.");
    }
}

public sealed class RoleTemplate
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("isSystem")]
    public bool IsSystem { get; set; }

    /// <summary>Either the literal string "ALL_NON_PLATFORM" or a JSON array of permission codes.</summary>
    [JsonPropertyName("permissions")]
    public JsonElement Permissions { get; set; }

    public bool IsAllNonPlatform =>
        Permissions.ValueKind == JsonValueKind.String && Permissions.GetString() == "ALL_NON_PLATFORM";

    public IReadOnlyList<string> ExplicitPermissionCodes =>
        Permissions.ValueKind == JsonValueKind.Array
            ? Permissions.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
}

public sealed class NumberSeriesTemplate
{
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("paddingLength")]
    public byte PaddingLength { get; set; } = 6;
}
