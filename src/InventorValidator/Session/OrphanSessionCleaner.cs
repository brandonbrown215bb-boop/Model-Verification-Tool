using System.IO;
using InventorValidator.Infrastructure;

namespace InventorValidator.Session;

public record CleanupResult(int CleanedCount, int FailedCount, long FreedBytes);

public class OrphanSessionCleaner
{
    private readonly string _tempRoot;

    public OrphanSessionCleaner(string? tempRoot = null)
    {
        _tempRoot = tempRoot ?? WorkspaceManager.DefaultTempRoot;
    }

    public CleanupResult PurgeStaleSessions(TimeSpan? maxAge = null, bool purgeAll = false)
    {
        var age = maxAge ?? TimeSpan.FromHours(24);
        var cutoff = DateTime.UtcNow - age;

        if (!Directory.Exists(_tempRoot))
        {
            return new CleanupResult(0, 0, 0);
        }

        int cleaned = 0;
        int failed = 0;
        long freedBytes = 0;

        try
        {
            var subDirs = Directory.GetDirectories(_tempRoot);
            foreach (var dir in subDirs)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    var creationUtc = dirInfo.CreationTimeUtc;

                    if (!purgeAll && creationUtc > cutoff)
                    {
                        // Directory is newer than cutoff, keep it
                        continue;
                    }

                    // Compute size before deleting
                    long dirSize = 0;
                    foreach (var fi in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            dirSize += fi.Length;
                            fi.Attributes = FileAttributes.Normal;
                        }
                        catch
                        {
                            // Suppress file read failure during size check
                        }
                    }

                    Directory.Delete(dir, recursive: true);
                    cleaned++;
                    freedBytes += dirSize;
                }
                catch (Exception ex)
                {
                    failed++;
                    DiagnosticsLogger.Instance.Warn($"Could not purge stale temp folder '{dir}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Error during orphan workspace scan: {ex.Message}");
        }

        if (cleaned > 0)
        {
            DiagnosticsLogger.Instance.Info($"Orphan session cleaner removed {cleaned} stale temporary folders ({freedBytes / (1024 * 1024):N1} MB freed).");
        }

        return new CleanupResult(cleaned, failed, freedBytes);
    }
}
