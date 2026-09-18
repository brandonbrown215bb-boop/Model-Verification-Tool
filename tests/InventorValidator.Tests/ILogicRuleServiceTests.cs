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

    [Fact]
    public void KnownILogicAddInGuids_ContainsStandardInventor2020And2024ClassId()
    {
        Assert.Equal("{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}", ILogicRuleService.ILogicAddInGuid);
        Assert.Contains("{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}", ILogicRuleService.KnownILogicAddInGuids);
    }

    [Fact]
    public void ParseRuleListBytes_WithValidBinaryData_CorrectlyExtractsRuleNames()
    {
        // Build binary buffer simulating Inventor iLogicRuleList attribute
        var ms = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(ms))
        {
            bw.Write((byte)1); // Version
            bw.Write(2);       // 2 rules

            // Rule 1: "Part Suppression" / "iLogicRule_Name1"
            byte[] name1 = System.Text.Encoding.Unicode.GetBytes("Part Suppression");
            byte[] intName1 = System.Text.Encoding.Unicode.GetBytes("iLogicRule_Name1");
            bw.Write(name1.Length);
            bw.Write(name1);
            bw.Write(intName1.Length);
            bw.Write(intName1);

            // Rule 2: "Home View" / "iLogicRule_Name4"
            byte[] name2 = System.Text.Encoding.Unicode.GetBytes("Home View");
            byte[] intName2 = System.Text.Encoding.Unicode.GetBytes("iLogicRule_Name4");
            bw.Write(name2.Length);
            bw.Write(name2);
            bw.Write(intName2.Length);
            bw.Write(intName2);
        }

        var parsed = ILogicRuleService.ParseRuleListBytes(ms.ToArray());

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Part Suppression", parsed[0]);
        Assert.Equal("Home View", parsed[1]);
    }

    [Fact]
    public void ParseRuleListBytes_WithEmptyOrCorruptData_ReturnsEmptyWithoutThrowing()
    {
        Assert.Empty(ILogicRuleService.ParseRuleListBytes(null!));
        Assert.Empty(ILogicRuleService.ParseRuleListBytes(Array.Empty<byte>()));
        Assert.Empty(ILogicRuleService.ParseRuleListBytes(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void RunAllRules_WithNullDoc_ReturnsFalseWithError()
    {
        var service = new ILogicRuleService();
        bool success = service.RunAllRules(null!, out string? error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("No iLogic rules detected", error);
    }

    [Fact]
    public void GetAssemblyRules_WithRealSampleViaApprentice_DiscoversPartSuppressionAndHomeView()
    {
        string samplePath = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\391-10006-023.iam";
        if (!System.IO.File.Exists(samplePath))
            return;

        dynamic? apprentice = null;
        dynamic? doc = null;
        try
        {
            Type? apprenticeType = Type.GetTypeFromProgID("Inventor.ApprenticeServer");
            if (apprenticeType == null) return;
            apprentice = Activator.CreateInstance(apprenticeType);
            doc = apprentice.Open(samplePath);

            var service = new ILogicRuleService();
            var rules = service.GetAssemblyRules(doc);

            Assert.NotNull(rules);
            Assert.Contains("Part Suppression", rules);
            Assert.Contains("Home View", rules);
        }
        finally
        {
            try { apprentice?.Close(); } catch { }
        }
    }

    [Fact]
    public void RunRule_WhenAppHasNoILogicAddIn_ReturnsFalseWithDescriptiveError()
    {
        var service = new ILogicRuleService();
        
        // Document mock with Parent pointing to empty app object
        var mockDoc = new MockDocWithoutAddIn();
        bool success = service.RunRule(mockDoc, "Part Suppression", out string? error);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Autodesk Inventor iLogic Add-in is not available", error);
    }

    public class MockDocWithoutAddIn
    {
        public object Parent => new MockAppWithoutAddIn();
        public string DisplayName => "TestAssembly.iam";
        public object Views => new MockViews();
        public void Activate() { }
    }

    public class MockAppWithoutAddIn
    {
        public object ApplicationAddIns => new MockAddIns();
    }

    public class MockAddIns
    {
        public int Count => 0;
        public object? this[string id] => null;
    }

    public class MockViews
    {
        public int Count => 0;
        public object Add() => new object();
    }
}
