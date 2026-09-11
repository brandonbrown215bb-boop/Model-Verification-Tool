using System.IO;
using System.Text.Json;

namespace InventorValidator.Session;

public enum SessionStatus
{
    Created,
    Copying,
    Ready,
    Running,
    Completed,
    Failed,
    CleanedUp
}

public class SessionManifest
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string SourceDirectory { get; set; } = string.Empty;
    public string SourceIamPath { get; set; } = string.Empty;
    public string SourceCalculatorPath { get; set; } = string.Empty;
    public string WorkspaceDirectory { get; set; } = string.Empty;
    public string CopiedIamPath { get; set; } = string.Empty;
    public string CopiedCalculatorPath { get; set; } = string.Empty;
    public int TotalFilesCopied { get; set; }
    public long TotalBytesCopied { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public string? ErrorMessage { get; set; }

    public void Save()
    {
        try
        {
            if (!string.IsNullOrEmpty(WorkspaceDirectory) && Directory.Exists(WorkspaceDirectory))
            {
                var manifestPath = Path.Combine(WorkspaceDirectory, "session_manifest.json");
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(manifestPath, json);
            }
        }
        catch
        {
            // Suppress non-critical manifest writing error
        }
    }

    public static SessionManifest? Load(string workspaceDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(workspaceDirectory, "session_manifest.json");
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                return JsonSerializer.Deserialize<SessionManifest>(json);
            }
        }
        catch
        {
            // Suppress deserialization error
        }
        return null;
    }
}
