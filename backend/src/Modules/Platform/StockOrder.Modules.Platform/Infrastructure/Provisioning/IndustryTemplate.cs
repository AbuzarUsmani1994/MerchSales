using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

/// <summary>
/// Deserializes an industry template JSON file (section 11). Applied so far: roles, features,
/// settings, number series, and the catalog basics (units, product types, tax codes, default
/// price list — see <see cref="TemplateCatalogSeeder"/>). The inventory and finance sections
/// follow with their modules.
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

    [JsonPropertyName("units")]
    public List<UnitTemplate> Units { get; set; } = [];

    [JsonPropertyName("productTypes")]
    public List<ProductTypeTemplate> ProductTypes { get; set; } = [];

    [JsonPropertyName("taxCodes")]
    public List<TaxCodeTemplate> TaxCodes { get; set; } = [];

    /// <summary>Payment method codes (CASH, CARD, …); name and kind come from <see cref="PaymentMethodKinds"/>.</summary>
    [JsonPropertyName("paymentMethods")]
    public List<string> PaymentMethods { get; set; } = [];

    [JsonPropertyName("records")]
    public RecordsTemplate Records { get; set; } = new();

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

public sealed class UnitTemplate
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("decimalPlaces")]
    public byte DecimalPlaces { get; set; }
}

/// <summary>
/// The template's <c>trackingMode</c> is catalog.ProductType.Behaviour (Stocked, Recipe...) and
/// its <c>batchMode</c> is DefaultTracking (None, Batch, BatchExpiry).
/// </summary>
public sealed class ProductTypeTemplate
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("trackingMode")]
    public string Behaviour { get; set; } = "Stocked";

    [JsonPropertyName("batchMode")]
    public string DefaultTracking { get; set; } = "None";

    [JsonPropertyName("issueRule")]
    public string IssueRule { get; set; } = "FIFO";
}

public sealed class TaxCodeTemplate
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("ratePercent")]
    public decimal RatePercent { get; set; }
}

public sealed class RecordsTemplate
{
    [JsonPropertyName("defaultPriceList")]
    public NamedRecordTemplate? DefaultPriceList { get; set; }

    [JsonPropertyName("walkInCustomer")]
    public NamedRecordTemplate? WalkInCustomer { get; set; }

    [JsonPropertyName("openAccountingPeriodForCurrentMonth")]
    public bool OpenAccountingPeriodForCurrentMonth { get; set; }
}

/// <summary>Display name and finance.PaymentMethod.Kind for each template payment method code.</summary>
public static class PaymentMethodKinds
{
    public static readonly IReadOnlyDictionary<string, (string Name, string Kind)> ByCode =
        new Dictionary<string, (string Name, string Kind)>(StringComparer.Ordinal)
        {
            ["CASH"] = ("Cash", "Cash"),
            ["CARD"] = ("Card", "Card"),
            ["WALLET"] = ("Wallet (JazzCash / Easypaisa)", "Wallet"),
            ["BANK"] = ("Bank transfer", "BankTransfer"),
            ["CHEQUE"] = ("Cheque", "Cheque"),
            ["CREDIT"] = ("On credit", "Credit"),
        };
}

public sealed class NamedRecordTemplate
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
