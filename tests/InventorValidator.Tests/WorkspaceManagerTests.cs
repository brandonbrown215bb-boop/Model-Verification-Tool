using System.IO;
using InventorValidator.Session;
using Xunit;

namespace InventorValidator.Tests;

public class WorkspaceManagerTests
{
    [Fact]
    public async Task CreateWorkspaceAsync_ShouldCopyFilesAndStripReadOnly()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"iv_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "SourceAssembly");
        var tempWorkspaceRoot = Path.Combine(testRoot, "TempWorkspaces");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(tempWorkspaceRoot);

        try
        {
            // Create dummy IAM and child IPT files
            var iamFile = Path.Combine(sourceDir, "MainAssembly.iam");
            var iptFile = Path.Combine(sourceDir, "ChildPart.ipt");
            var subDir = Path.Combine(sourceDir, "SubFolder");
            Directory.CreateDirectory(subDir);
            var nestedFile = Path.Combine(subDir, "NestedPart.ipt");
            var calcFile = Path.Combine(sourceDir, "Calc.xlsx");

            File.WriteAllText(iamFile, "dummy iam content");
            File.WriteAllText(iptFile, "dummy ipt content");
            File.WriteAllText(nestedFile, "dummy nested content");
            File.WriteAllText(calcFile, "dummy calc content");

            // Set ReadOnly attribute on child part (simulating Vault read-only file)
            File.SetAttributes(iptFile, FileAttributes.ReadOnly);
            Assert.True(new FileInfo(iptFile).IsReadOnly);

            var manager = new WorkspaceManager(tempWorkspaceRoot);
            var manifest = await manager.CreateWorkspaceAsync(iamFile, calcFile);

            Assert.NotNull(manifest);
            Assert.Equal(SessionStatus.Ready, manifest.Status);
            Assert.True(Directory.Exists(manifest.WorkspaceDirectory));
            Assert.Equal(4, manifest.TotalFilesCopied);

            // Verify copied IAM and IPT exist
            Assert.True(File.Exists(manifest.CopiedIamPath));
            var copiedIpt = Path.Combine(manifest.WorkspaceDirectory, "ChildPart.ipt");
            Assert.True(File.Exists(copiedIpt));

            // CRITICAL TEST: ReadOnly attribute MUST be stripped on the copy
            var copiedIptInfo = new FileInfo(copiedIpt);
            Assert.False(copiedIptInfo.IsReadOnly, "Copied file should not be ReadOnly!");

            // Verify original file remained ReadOnly
            Assert.True(new FileInfo(iptFile).IsReadOnly, "Source file ReadOnly flag must be preserved!");

            // Verify manifest was saved
            var loadedManifest = SessionManifest.Load(manifest.WorkspaceDirectory);
            Assert.NotNull(loadedManifest);
            Assert.Equal(manifest.SessionId, loadedManifest.SessionId);

            // Verify Cleanup deletes workspace cleanly
            manager.CleanupWorkspace(manifest);
            Assert.False(Directory.Exists(manifest.WorkspaceDirectory));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                // Reset any read-only flags in test root before deleting
                foreach (var f in new DirectoryInfo(testRoot).GetFiles("*", SearchOption.AllDirectories))
                {
                    f.Attributes = FileAttributes.Normal;
                }
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
