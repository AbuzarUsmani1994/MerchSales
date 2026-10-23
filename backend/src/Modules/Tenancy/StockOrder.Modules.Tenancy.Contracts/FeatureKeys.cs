namespace StockOrder.Modules.Tenancy.Contracts;

/// <summary>
/// The one place every feature key is spelled out (section 11): "unknown keys in a template
/// fail provisioning." Add a key here before using it in an industry template JSON file.
/// </summary>
public static class FeatureKeys
{
    public const string InventoryApprovals = "Inventory.Approvals";
    public const string InventoryBatchTracking = "Inventory.BatchTracking";
    public const string InventoryExpiryTracking = "Inventory.ExpiryTracking";
    public const string InventoryFefo = "Inventory.FEFO";
    public const string PurchasingApprovals = "Purchasing.Approvals";
    public const string FinanceCashSessions = "Finance.CashSessions";
    public const string SalesCreditControl = "Sales.CreditControl";
    public const string SalesBackOrders = "Sales.BackOrders";
    public const string MobileOffline = "Mobile.Offline";
    public const string RestaurantTables = "Restaurant.Tables";
    public const string RestaurantKitchen = "Restaurant.Kitchen";
    public const string RestaurantRecipes = "Restaurant.Recipes";
    public const string RestaurantModifiers = "Restaurant.Modifiers";
    public const string PharmaSchemes = "Pharma.Schemes";
    public const string PharmaRoutes = "Pharma.Routes";
    public const string PharmaRecall = "Pharma.Recall";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
    [
        InventoryApprovals, InventoryBatchTracking, InventoryExpiryTracking, InventoryFefo,
        PurchasingApprovals, FinanceCashSessions, SalesCreditControl, SalesBackOrders, MobileOffline,
        RestaurantTables, RestaurantKitchen, RestaurantRecipes, RestaurantModifiers,
        PharmaSchemes, PharmaRoutes, PharmaRecall,
    ], StringComparer.Ordinal);
}
