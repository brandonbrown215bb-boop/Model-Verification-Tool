using System.IO;
using InventorValidator.Session;
using Xunit;

namespace InventorValidator.Tests;

public class OrphanSessionCleanerTests
{
    [Fact]
    public void PurgeStaleSessions_ShouldDeleteOldSessionsAndPreserveNewSessions()
    {
        var testTempRoot = Path.Combine(Path.GetTempPath(), $"iv_cleaner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testTempRoot);

        try
        {
            var oldSession = Path.Combine(testTempRoot, "old_session_1");
            var newSession = Path.Combine(testTempRoot, "new_session_2");

            Directory.CreateDirectory(oldSession);
            Directory.CreateDirectory(newSession);

            File.WriteAllText(Path.Combine(oldSession, "dummy.txt"), "old file");
            File.WriteAllText(Path.Combine(newSession, "dummy.txt"), "new file");

            // Backdate old session directory to 48 hours ago
            Directory.SetCreationTimeUtc(oldSession, DateTime.UtcNow.AddHours(-48));

            var cleaner = new OrphanSessionCleaner(testTempRoot);
            var result = cleaner.PurgeStaleSessions(TimeSpan.FromHours(24));

            Assert.Equal(1, result.CleanedCount);
            Assert.False(Directory.Exists(oldSession), "Old session directory should have been purged!");
            Assert.True(Directory.Exists(newSession), "New session directory should be preserved!");
        }
        finally
        {
            if (Directory.Exists(testTempRoot))
            {
                Directory.Delete(testTempRoot, recursive: true);
            }
        }
    }
}
