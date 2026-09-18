using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor.Models;
using Microsoft.Win32;

namespace InventorValidator.Inventor;

/// <summary>
/// Handles launching and connecting to dedicated Autodesk Inventor 2020 and 2024 instances.
/// Ensures process isolation, strict PID tracking, and prevents interference with existing user sessions.
/// </summary>
public class InventorProcessLauncher
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable pprot);

    private readonly InventorVersionDetector _versionDetector;

    public InventorProcessLauncher(InventorVersionDetector? versionDetector = null)
    {
        _versionDetector = versionDetector ?? new InventorVersionDetector();
    }

    /// <summary>
    /// Resolves which Inventor version to use based on user selection and installed software.
    /// Default policy: Default to Inventor 2020 if installed; otherwise Inventor 2024.
    /// </summary>
    public int ResolveTargetVersion(string requestedVersion)
    {
        if (requestedVersion.Contains("2020", StringComparison.OrdinalIgnoreCase))
        {
            if (!_versionDetector.Info2020.IsInstalled)
                throw new InvalidOperationException("Autodesk Inventor 2020 is not detected on this machine.");
            return 2020;
        }

        if (requestedVersion.Contains("2024", StringComparison.OrdinalIgnoreCase))
        {
            if (!_versionDetector.Info2024.IsInstalled)
                throw new InvalidOperationException("Autodesk Inventor 2024 is not detected on this machine.");
            return 2024;
        }

        // Automatic mode: Default to Inventor 2020 if installed, otherwise 2024
        if (_versionDetector.Info2020.IsInstalled)
            return 2020;

        if (_versionDetector.Info2024.IsInstalled)
            return 2024;

        throw new InvalidOperationException("No supported Autodesk Inventor installation (2020 or 2024) was detected.");
    }

    /// <summary>
    /// Launches a dedicated, application-owned Inventor process and returns its COM application object
    /// alongside its verified process metadata.
    /// </summary>
    public (dynamic App, InventorProcessInfo Info) LaunchDedicatedInstance(
        int targetYear,
        ComReleaseScope scope,
        bool isVisible = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report($"Preparing to launch Autodesk Inventor {targetYear}...");
        DiagnosticsLogger.Instance.Info($"Launching dedicated Autodesk Inventor {targetYear} instance (Visible={isVisible})...");

        if (targetYear == 2020)
        {
            return LaunchInventor2020(scope, isVisible, progress, cancellationToken);
        }
        else if (targetYear == 2024)
        {
            return LaunchInventor2024(scope, isVisible, progress, cancellationToken);
        }
        else
        {
            throw new ArgumentException($"Unsupported Inventor version: {targetYear}", nameof(targetYear));
        }
    }

    private (dynamic App, InventorProcessInfo Info) LaunchInventor2020(
        ComReleaseScope scope,
        bool isVisible,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Instantiating Inventor 2020 via COM...");
        
        // Record existing Inventor PIDs before launching
        var initialPids = Process.GetProcessesByName("Inventor").Select(p => p.Id).ToHashSet();

        // 2020 ProgID: check versioned ProgID first, then generic
        Type? invType = Type.GetTypeFromProgID("Inventor.Application.24") 
                     ?? Type.GetTypeFromProgID("Inventor.Application");

        if (invType == null)
        {
            throw new InvalidOperationException("Cannot resolve Inventor 2020 COM ProgID (Inventor.Application / Inventor.Application.24).");
        }

        cancellationToken.ThrowIfCancellationRequested();
        dynamic app = Activator.CreateInstance(invType)
            ?? throw new InvalidOperationException("Failed to instantiate Inventor 2020 COM application object.");

        scope.Track<object>(app);

        // Suppress all modal dialogs and prompts during automation
        try
        {
            app.SilentOperation = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not set Inventor SilentOperation = true: {ex.Message}");
        }

        // Set visibility based on preference (default: headless / invisible background mode)
        try
        {
            app.Visible = isVisible;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not set Inventor Visible = {isVisible}: {ex.Message}");
        }

        // Retrieve process ID
        int pid = ResolveInventorPid(app, initialPids);
        if (pid <= 0)
        {
            try { app.Quit(); } catch { }
            throw new InvalidOperationException("Failed to determine a distinct Process ID for the launched Inventor 2020 session. Aborting to protect existing user sessions.");
        }

        ValidateInventorVersion(app, 2020);
        DiagnosticsLogger.Instance.Info($"Dedicated Inventor 2020 instantiated with PID {pid}");

        string softwareVersion = GetSoftwareVersion(app);
        var info = new InventorProcessInfo(
            ProcessId: pid,
            VersionString: softwareVersion,
            MajorVersion: 2020,
            IsApplicationOwned: true,
            StartTime: DateTime.UtcNow
        );

        return (app, info);
    }

    private (dynamic App, InventorProcessInfo Info) LaunchInventor2024(
        ComReleaseScope scope,
        bool isVisible,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Starting Inventor 2024 dedicated process...");
        var initialPids = Process.GetProcessesByName("Inventor").Select(p => p.Id).ToHashSet();

        // Check if versioned ProgID is registered and points directly to 2024
        Type? inv24Type = Type.GetTypeFromProgID("Inventor.Application.28");
        if (inv24Type != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic? directApp = Activator.CreateInstance(inv24Type);

            if (directApp != null)
            {
                scope.Track<object>(directApp);
                try
                {
                    directApp.SilentOperation = true;
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Could not set Inventor 2024 SilentOperation = true: {ex.Message}");
                }

                try
                {
                    directApp.Visible = isVisible;
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Could not set Inventor 2024 Visible = {isVisible}: {ex.Message}");
                }

                int directPid = ResolveInventorPid(directApp, initialPids);
                if (directPid <= 0)
                {
                    try { directApp.Quit(); } catch { }
                    throw new InvalidOperationException("Failed to determine a distinct Process ID for the launched Inventor 2024 session. Aborting to protect existing user sessions.");
                }

                ValidateInventorVersion(directApp, 2024);
                string ver = GetSoftwareVersion(directApp);
                DiagnosticsLogger.Instance.Info($"Dedicated Inventor 2024 instantiated via ProgID with PID {directPid} ({ver})");
                return (directApp, new InventorProcessInfo(directPid, ver, 2024, true, DateTime.UtcNow));
            }
        }

        // Otherwise launch via Process.Start with /Automation switch and bind to ROT
        string exePath = _versionDetector.Info2024.ExecutablePath 
            ?? @"C:\Program Files\Autodesk\Inventor 2024\Bin\Inventor.exe";

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Inventor 2024 executable not found at: {exePath}", exePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "/Automation",
            UseShellExecute = true
        };

        progress?.Report("Launching Inventor 2024 executable (/Automation)...");
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch Inventor 2024 process.");

        int launchedPid = process.Id;
        DiagnosticsLogger.Instance.Info($"Started Inventor.exe process with PID {launchedPid}");

        // Wait for Inventor ROT registration with polling (up to 90 seconds timeout)
        progress?.Report("Waiting for Inventor 2024 COM readiness...");
        var stopwatch = Stopwatch.StartNew();
        dynamic? boundApp = null;

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(90))
        {
            cancellationToken.ThrowIfCancellationRequested();

            boundApp = TryGetAppFromRotOrActiveObject(launchedPid, scope);
            if (boundApp != null)
            {
                break;
            }

            Thread.Sleep(1000);
        }

        if (boundApp == null)
        {
            // Terminate the launched process if it timed out before becoming ready
            try { process.Kill(); } catch { }
            throw new TimeoutException($"Inventor 2024 (PID {launchedPid}) did not register in ROT within 90 seconds.");
        }

        try
        {
            boundApp.SilentOperation = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not set Inventor 2024 SilentOperation = true: {ex.Message}");
        }

        try
        {
            boundApp.Visible = isVisible;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not set Inventor 2024 Visible = {isVisible}: {ex.Message}");
        }

        ValidateInventorVersion(boundApp, 2024);
        string fullVer = GetSoftwareVersion(boundApp);
        DiagnosticsLogger.Instance.Info($"Bound to dedicated Inventor 2024 process PID {launchedPid} ({fullVer})");

        var procInfo = new InventorProcessInfo(
            ProcessId: launchedPid,
            VersionString: fullVer,
            MajorVersion: 2024,
            IsApplicationOwned: true,
            StartTime: DateTime.UtcNow
        );

        return (boundApp, procInfo);
    }

    private static dynamic? TryGetAppFromRotOrActiveObject(int targetPid, ComReleaseScope scope)
    {
        try
        {
            // Inspect Running Object Table
            int hr = GetRunningObjectTable(0, out var rot);
            if (hr != 0 || rot == null) return null;

            hr = CreateBindCtx(0, out var bindCtx);
            if (hr != 0 || bindCtx == null) return null;

            rot.EnumRunning(out var enumMoniker);
            if (enumMoniker == null) return null;

            IMoniker[] monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                monikers[0].GetDisplayName(bindCtx, null, out var displayName);
                if (!string.IsNullOrEmpty(displayName) && displayName.Contains("Inventor", StringComparison.OrdinalIgnoreCase))
                {
                    rot.GetObject(monikers[0], out var comObj);
                    if (comObj != null)
                    {
                        try
                        {
                            dynamic candidateApp = comObj;
                            IntPtr hwnd = (IntPtr)candidateApp.MainFrameHWND;
                            GetWindowThreadProcessId(hwnd, out uint pid);
                            if (pid == targetPid)
                            {
                                scope.Track<object>(comObj);
                                return candidateApp;
                            }
                        }
                        catch
                        {
                            // Continue checking other monikers
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through
        }

        return null;
    }

    private static int ResolveInventorPid(dynamic app, HashSet<int> initialPids)
    {
        try
        {
            IntPtr hwnd = (IntPtr)app.MainFrameHWND;
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                    return (int)pid;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not get PID from MainFrameHWND: {ex.Message}");
        }

        // Fallback: detect newly appeared process
        var currentPids = Process.GetProcessesByName("Inventor").Select(p => p.Id).ToList();
        var newPids = currentPids.Except(initialPids).ToList();
        if (newPids.Count == 1)
        {
            return newPids[0];
        }

        // Fail-closed: Never guess ownership of pre-existing user sessions
        DiagnosticsLogger.Instance.Error($"Failed to uniquely resolve launched Inventor PID (initial: {initialPids.Count}, current: {currentPids.Count}, new: {newPids.Count}).");
        return -1;
    }

    internal static void ValidateInventorVersion(dynamic app, int expectedYear)
    {
        int expectedMajor = expectedYear == 2020 ? 24 : (expectedYear == 2024 ? 28 : -1);
        int actualMajor = -1;
        string displayName = string.Empty;

        try
        {
            dynamic softwareVer = app.SoftwareVersion;
            actualMajor = (int)softwareVer.Major;
            displayName = (string)softwareVer.DisplayName;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not read Inventor SoftwareVersion details: {ex.Message}");
        }

        bool matches = (actualMajor != -1 && actualMajor == expectedMajor) ||
                       (!string.IsNullOrEmpty(displayName) && displayName.Contains(expectedYear.ToString()));

        if (!matches)
        {
            throw new InvalidOperationException(
                $"Launched Inventor instance does not match requested version {expectedYear}. " +
                $"(Detected major version: {actualMajor}, Display: '{displayName}'). Aborting to protect model integrity.");
        }
    }

    private static string GetSoftwareVersion(dynamic app)
    {
        try
        {
            dynamic softwareVer = app.SoftwareVersion;
            return $"{softwareVer.DisplayName} (Build {softwareVer.BuildNumber})";
        }
        catch
        {
            return "Autodesk Inventor";
        }
    }
}
