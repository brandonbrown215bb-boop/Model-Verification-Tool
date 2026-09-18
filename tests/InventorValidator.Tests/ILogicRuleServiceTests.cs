using InventorValidator.Inventor.ILogic;
using Xunit;

namespace InventorValidator.Tests;

public class ILogicRuleServiceTests
{
    [Fact]
    public void RunRule_WithNullOrEmptyName_ReturnsFalseWithError()
    {
        var service = new ILogicRuleService();
        bool success = service.RunRule(null!, string.Empty, out string? error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("No rule name specified", error);
    }

    [Fact]
    public void RunRule_WithNullDocument_ReturnsFalseWithError()
    {
        var service = new ILogicRuleService();
        bool success = service.RunRule(null!, "Name1", out string? error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Unable to access Autodesk Inventor Application", error);
    }

    [Fact]
    public void GetAssemblyRules_WithNullDocument_ReturnsEmptyList()
    {
        var service = new ILogicRuleService();
        var rules = service.GetAssemblyRules(null!);

        Assert.NotNull(rules);
        Assert.Empty(rules);
    }

    [Fact]
    public void GetAssemblyRules_WhenInvokedWithObjectAndToList_DoesNotThrowDynamicDispatchException()
    {
        var service = new ILogicRuleService();
        object? nullDoc = null;
        
        // This pattern previously threw RuntimeBinderException because of dynamic parameter typing
        var list = service.GetAssemblyRules(nullDoc).ToList();

        Assert.NotNull(list);
        Assert.Empty(list);
    }
}
