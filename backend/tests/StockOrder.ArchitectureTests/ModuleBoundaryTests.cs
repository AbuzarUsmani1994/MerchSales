using System.Reflection;
using Shouldly;
using Xunit;

namespace StockOrder.ArchitectureTests;

/// <summary>
/// Section 5.1: "A module references only SharedKernel, Infrastructure and other modules'
/// *.Contracts projects. Never another module's implementation project."
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] ModuleNames = ["Tenancy", "Identity", "Platform", "Inventory"];

    private static readonly Dictionary<string, Assembly> ImplementationAssemblies = new()
    {
        ["Tenancy"] = typeof(StockOrder.Modules.Tenancy.TenancyModule).Assembly,
        ["Identity"] = typeof(StockOrder.Modules.Identity.IdentityModule).Assembly,
        ["Platform"] = typeof(StockOrder.Modules.Platform.PlatformModule).Assembly,
        ["Inventory"] = typeof(StockOrder.Modules.Inventory.InventoryModule).Assembly,
    };

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Module_should_not_reference_another_modules_implementation_assembly(string moduleName, string otherModuleName)
    {
        var assembly = ImplementationAssemblies[moduleName];
        var otherImplementationAssemblyName = ImplementationAssemblies[otherModuleName].GetName().Name;

        var referencesOtherImplementation = assembly.GetReferencedAssemblies()
            .Any(a => a.Name == otherImplementationAssemblyName);

        referencesOtherImplementation.ShouldBeFalse(
            $"{moduleName}'s assembly ({assembly.GetName().Name}) must not reference {otherModuleName}'s " +
            $"implementation assembly ({otherImplementationAssemblyName}) — only its .Contracts assembly is allowed.");
    }

    public static IEnumerable<object[]> ModulePairs()
    {
        foreach (var module in ModuleNames)
        {
            foreach (var other in ModuleNames.Where(m => m != module))
            {
                yield return [module, other];
            }
        }
    }
}
