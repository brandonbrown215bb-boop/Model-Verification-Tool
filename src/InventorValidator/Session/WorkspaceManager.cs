using System.IO;
using InventorValidator.Infrastructure;

namespace InventorValidator.Session;

public class WorkspaceManager
{
    public static readonly string DefaultTempRoot = Path.Combine(
        Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
        "Temp",
        "InventorValidator"
    );

    private readonly string _tempRoot;

    public WorkspaceManager(string? tempRoot = null)
    {
        _tempRoot = tempRoot ?? DefaultTempRoot;
    }

    public async Task<SessionManifest> CreateWorkspaceAsync(
        string sourceIamPath,
        string sourceCalculatorPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceIamPath))
            throw new ArgumentException("Source IAM path must be specified.", nameof(sourceIamPath));

        if (!File.Exists(sourceIamPath))
            throw new FileNotFoundException($"Source IAM file not found: {sourceIamPath}", sourceIamPath);

        if (!string.IsNullOrWhiteSpace(sourceCalculatorPath) && !File.Exists(sourceCalculatorPath))
            throw new FileNotFoundException($"Source calculator file not found: {sourceCalculatorPath}", sourceCalculatorPath);

        var sourceDir = Path.GetDirectoryName(sourceIamPath)
            ?? throw new InvalidOperationException($"Unable to resolve directory for {sourceIamPath}");

        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        var workspaceDir = Path.Combine(_tempRoot, sessionId);

        var manifest = new SessionManifest
        {
            SessionId = sessionId,
            CreatedUtc = DateTime.UtcNow,
            SourceDirectory = sourceDir,
            SourceIamPath = sourceIamPath,
            SourceCalculatorPath = sourceCalculatorPath,
            WorkspaceDirectory = workspaceDir,
            Status = SessionStatus.Copying
        };

        progress?.Report("Creating temporary workspace folder...");
        Directory.CreateDirectory(workspaceDir);

        try
        {
            await Task.Run(() =>
            {
                var filesCopied = 0;
                long bytesCopied = 0;

                progress?.Report("Scanning assembly directory files...");
                var allFiles = Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories);

                foreach (var filePath in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fi = new FileInfo(filePath);

                    // Skip reparse points / junctions
                    if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    var relativePath = Path.GetRelativePath(sourceDir, filePath);
                    var destinationPath = Path.Combine(workspaceDir, relativePath);

                    var destSubDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destSubDir) && !Directory.Exists(destSubDir))
                    {
                        Directory.CreateDirectory(destSubDir);
                    }

                    progress?.Report($"Copying: {Path.GetFileName(filePath)}");
                    File.Copy(filePath, destinationPath, overwrite: true);

                    // Strip ReadOnly attribute on copied file
                    var copiedFi = new FileInfo(destinationPath);
                    if (copiedFi.IsReadOnly)
                    {
                        copiedFi.IsReadOnly = false;
                    }

                    filesCopied++;
                    bytesCopied += fi.Length;
                }

                var iamRelative = Path.GetRelativePath(sourceDir, sourceIamPath);
                manifest.CopiedIamPath = Path.Combine(workspaceDir, iamRelative);

                if (!string.IsNullOrWhiteSpace(sourceCalculatorPath))
                {
                    if (sourceCalculatorPath.StartsWith(sourceDir, StringComparison.OrdinalIgnoreCase))
                    {
                        var calcRelative = Path.GetRelativePath(sourceDir, sourceCalculatorPath);
                        manifest.CopiedCalculatorPath = Path.Combine(workspaceDir, calcRelative);
                    }
                    else
                    {
                        var calcFileName = Path.GetFileName(sourceCalculatorPath);
                        var targetCalcPath = Path.Combine(workspaceDir, calcFileName);
                        File.Copy(sourceCalculatorPath, targetCalcPath, overwrite: true);
                        var copiedCalcFi = new FileInfo(targetCalcPath);
                        if (copiedCalcFi.IsReadOnly)
                        {
                            copiedCalcFi.IsReadOnly = false;
                        }
                        manifest.CopiedCalculatorPath = targetCalcPath;
                        filesCopied++;
                        bytesCopied += new FileInfo(sourceCalculatorPath).Length;
                    }
                }

                manifest.TotalFilesCopied = filesCopied;
                manifest.TotalBytesCopied = bytesCopied;
                manifest.Status = SessionStatus.Ready;
                manifest.Save();
            }, cancellationToken);

            DiagnosticsLogger.Instance.Success($"Created temporary workspace: {workspaceDir} ({manifest.TotalFilesCopied} files, {manifest.TotalBytesCopied / (1024 * 1024):N1} MB)");
            return manifest;
        }
        catch (OperationCanceledException)
        {
            DiagnosticsLogger.Instance.Warn($"Workspace creation cancelled by user. Cleaning up {workspaceDir}...");
            CleanupWorkspace(manifest);
            manifest.Status = SessionStatus.CleanedUp;
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Error($"Failed to create workspace: {ex.Message}", ex);
            manifest.Status = SessionStatus.Failed;
            manifest.ErrorMessage = ex.Message;
            CleanupWorkspace(manifest);
            throw;
        }
    }

    public void CleanupWorkspace(SessionManifest manifest)
    {
        if (string.IsNullOrEmpty(manifest.WorkspaceDirectory) || !Directory.Exists(manifest.WorkspaceDirectory))
            return;

        try
        {
            // Remove read-only flags from all files in workspace to allow clean deletion
            var dir = new DirectoryInfo(manifest.WorkspaceDirectory);
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    file.Attributes = FileAttributes.Normal;
                }
                catch
                {
                    // Ignore per-file attribute reset failure
                }
            }

            Directory.Delete(manifest.WorkspaceDirectory, recursive: true);
            manifest.Status = SessionStatus.CleanedUp;
            DiagnosticsLogger.Instance.Info($"Cleaned up temporary workspace: {manifest.WorkspaceDirectory}");
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not cleanly delete workspace '{manifest.WorkspaceDirectory}': {ex.Message}");
        }
    }

    public void ExportWorkspace(SessionManifest manifest, string destinationDirectory, bool overwrite = false)
    {
        if (string.IsNullOrEmpty(manifest.WorkspaceDirectory) || !Directory.Exists(manifest.WorkspaceDirectory))
            throw new DirectoryNotFoundException("Source workspace directory does not exist.");

        if (Directory.Exists(destinationDirectory) && !overwrite && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
            throw new InvalidOperationException($"Destination directory is not empty: {destinationDirectory}");

        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(manifest.WorkspaceDirectory, "*.*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(manifest.WorkspaceDirectory, file);
            var destPath = Path.Combine(destinationDirectory, relPath);
            var subDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(subDir) && !Directory.Exists(subDir))
            {
                Directory.CreateDirectory(subDir);
            }
            File.Copy(file, destPath, overwrite: true);
        }

        DiagnosticsLogger.Instance.Success($"Exported validated workspace to: {destinationDirectory}");
    }
}
