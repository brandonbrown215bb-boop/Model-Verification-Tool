using System.IO;
using InventorValidator.Session;
using Xunit;

namespace InventorValidator.Tests;

public class RealSampleWorkspaceIntegrationTests
{
    private const string SampleIam = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\391-10006-023.iam";
    private const string SampleCalc = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\Calc_10006_023.xls";

    [Fact]
    public async Task CreateWorkspaceAsync_WithRealSample_ShouldSucceedAndStripReadOnly()
    {
        if (!File.Exists(SampleIam))
        {
            // Skip if run in environment without access to sample
            return;
        }

        var customTempRoot = Path.Combine(Path.GetTempPath(), $"iv_sample_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(customTempRoot);

        try
        {
            var manager = new WorkspaceManager(customTempRoot);
            var manifest = await manager.CreateWorkspaceAsync(SampleIam, SampleCalc);

            Assert.NotNull(manifest);
            Assert.Equal(SessionStatus.Ready, manifest.Status);
            Assert.True(Directory.Exists(manifest.WorkspaceDirectory));
            Assert.True(manifest.TotalFilesCopied > 50, $"Expected >50 files, got {manifest.TotalFilesCopied}");
            Assert.True(manifest.TotalBytesCopied > 35 * 1024 * 1024, $"Expected >35MB, got {manifest.TotalBytesCopied} bytes");

            // Verify OldVersions and .idw drawing files were cleanly excluded
            var excludedFiles = Directory.GetFiles(manifest.WorkspaceDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.Contains("OldVersions", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".idw", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Empty(excludedFiles);

            // Verify copied IAM exists
            Assert.True(File.Exists(manifest.CopiedIamPath));

            // Verify copied files do NOT have ReadOnly attribute set
            var copiedFiles = Directory.GetFiles(manifest.WorkspaceDirectory, "*.*", SearchOption.AllDirectories);
            foreach (var file in copiedFiles)
            {
                var fi = new FileInfo(file);
                Assert.False(fi.IsReadOnly, $"Copied file '{file}' should not have ReadOnly attribute!");
            }

            // Verify original file still exists and unchanged
            Assert.True(File.Exists(SampleIam));

            // Verify cleanup removes the workspace
            manager.CleanupWorkspace(manifest);
            Assert.False(Directory.Exists(manifest.WorkspaceDirectory));
        }
        finally
        {
            if (Directory.Exists(customTempRoot))
            {
                foreach (var f in new DirectoryInfo(customTempRoot).GetFiles("*", SearchOption.AllDirectories))
                {
                    f.Attributes = FileAttributes.Normal;
                }
                Directory.Delete(customTempRoot, recursive: true);
            }
        }
    }
}
