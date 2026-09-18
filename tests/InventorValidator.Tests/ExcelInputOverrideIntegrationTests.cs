using System.IO;
using InventorValidator.Excel;
using Xunit;

namespace InventorValidator.Tests;

public class ExcelInputOverrideIntegrationTests
{
    private const string SampleCalc = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\Calc_10006_023.xls";

    [Fact]
    public async Task RecalculateAndParseAsync_WithInputOverrides_UpdatesDownstreamValues()
    {
        if (!File.Exists(SampleCalc))
        {
            // Skip test in environments where live sample is not present
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"iv_override_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempCalc = Path.Combine(tempDir, Path.GetFileName(SampleCalc));
        File.Copy(SampleCalc, tempCalc, overwrite: true);

        try
        {
            var session = new ExcelAutomationSession();
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IH"] = "124"
            };

            var result = await session.RecalculateAndParseAsync(
                tempCalc,
                inputOverrides: overrides,
                timeout: TimeSpan.FromSeconds(60)
            );

            Assert.NotNull(result);

            // Verify the overridden parameter in DataInputs
            var dataIh = result.DataInputs.FirstOrDefault(d => d.ParameterName.Equals("IH", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(dataIh);
            Assert.Equal("124", dataIh.DisplayedValue.Trim());

            // Verify downstream parameter in Sheet1
            var sheet1Ih = result.Sheet1Parameters.FirstOrDefault(p => p.ParameterName.Equals("IH", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(sheet1Ih);
            Assert.Equal("124", sheet1Ih.DisplayedValue.Trim());
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }
}
