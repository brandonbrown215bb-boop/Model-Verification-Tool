using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor.Models;

namespace InventorValidator.Inventor;

/// <summary>
/// Manages the complete lifecycle of a dedicated Autodesk Inventor automation session on a persistent STA thread with a work queue.
/// Keeps the instance active for interactive inspection, and guarantees clean teardown of app-owned processes in their native apartment.
/// </summary>
public sealed class InventorAutomationSession : IDisposable
{
    private readonly InventorProcessLauncher _launcher;
    private readonly InventorModelInventoryService _inventoryService;
    private readonly ComReleaseScope _sessionScope = new();

    private readonly BlockingCollection<Action> _staWorkQueue = new();
    private readonly Thread _staThread;
    private readonly ManualResetEventSlim _threadReady = new(false);

    private dynamic? _inventorApp;
    private dynamic? _activeAssemblyDoc;
    private InventorProcessInfo? _processInfo;
    private bool _isDisposed;

    public InventorProcessInfo? ProcessInfo => _processInfo;
    public bool IsActive => _inventorApp != null && !_isDisposed;

    public InventorAutomationSession(
        InventorProcessLauncher? launcher = null,
        InventorModelInventoryService? inventoryService = null)
    {
        _launcher = launcher ?? new InventorProcessLauncher();
        _inventoryService = inventoryService ?? new InventorModelInventoryService();

        _staThread = new Thread(RunStaThread)
        {
            IsBackground = true,
            Name = "InventorAutomationSTA"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _threadReady.Wait();
    }

    private void RunStaThread()
    {
        _threadReady.Set();
        foreach (var action in _staWorkQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Error($"Error executing action on STA thread: {ex.Message}", ex);
            }
        }
    }

    private Task<T> ExecuteOnStaAsync<T>(Func<T> func)
    {
        if (_isDisposed || _staWorkQueue.IsAddingCompleted)
            throw new InvalidOperationException("Inventor automation session is closed.");

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _staWorkQueue.Add(() =>
        {
            try
            {
                var res = func();
                tcs.SetResult(res);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Starts dedicated Inventor, opens the copied assembly, and inventories the model on the dedicated STA thread.
    /// </summary>
    public async Task<AssemblyInventoryResult> StartAndInventoryAsync(
        string iamPath,
        string requestedVersion = "Automatic",
        bool isVisible = false,
        TimeSpan? timeout = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(300);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(effectiveTimeout);

        var task = ExecuteOnStaAsync(() =>
        {
            return StartAndInventoryInternal(iamPath, requestedVersion, isVisible, effectiveTimeout, progress, linkedCts.Token);
        });

        var timeoutTask = Task.Delay(effectiveTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            throw new TimeoutException($"Inventor automation session timed out after {effectiveTimeout.TotalSeconds:F0} seconds.");
        }

        return await task;
    }

    private AssemblyInventoryResult StartAndInventoryInternal(
        string iamPath,
        string requestedVersion,
        bool isVisible,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(iamPath))
        {
            throw new FileNotFoundException($"Assembly file not found: {iamPath}", iamPath);
        }

        int targetYear = _launcher.ResolveTargetVersion(requestedVersion);
        progress?.Report($"Starting dedicated Autodesk Inventor {targetYear}...");

        cancellationToken.ThrowIfCancellationRequested();

        // Launch dedicated instance directly on STA thread
        var (app, info) = _launcher.LaunchDedicatedInstance(
            targetYear,
            _sessionScope,
            isVisible,
            progress,
            cancellationToken);

        _inventorApp = app;
        _processInfo = info;

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report($"Opening copied assembly: {Path.GetFileName(iamPath)}...");
        DiagnosticsLogger.Instance.Info($"Opening assembly in Inventor PID {info.ProcessId} (Visible={isVisible}): {iamPath}");

        // Re-assert SilentOperation to ensure all dialogs are suppressed during document open
        try
        {
            _inventorApp.SilentOperation = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not assert SilentOperation = true: {ex.Message}");
        }

        dynamic documents = _sessionScope.Track<object>(_inventorApp.Documents);
        dynamic asmDoc = _sessionScope.Track<object>(documents.Open(iamPath, isVisible));

        if (asmDoc == null)
        {
            throw new InvalidOperationException($"Inventor failed to open assembly: {iamPath}");
        }
        _activeAssemblyDoc = asmDoc;

        cancellationToken.ThrowIfCancellationRequested();

        // Wait for document readiness / view fit if window is visible
        if (isVisible)
        {
            progress?.Report("Waiting for document graphics and model readiness...");
            try
            {
                // Activate view fit to verify viewport readiness
                dynamic activeView = _sessionScope.Track<object>(_inventorApp.ActiveView);
                if (activeView != null)
                {
                    activeView.Fit();
                }
            }
            catch
            {
                // Best effort view fit
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Perform deep recursive inventory
        var inventory = _inventoryService.InventoryAssembly(
            asmDoc,
            info.VersionString,
            info.ProcessId,
            _sessionScope,
            progress,
            cancellationToken);

        return inventory;
    }

    /// <summary>
    /// Updates the visibility of the dedicated Inventor window on demand.
    /// </summary>
    public Task<bool> SetVisibleAsync(bool visible)
    {
        if (_inventorApp == null || _isDisposed)
            return Task.FromResult(false);

        return ExecuteOnStaAsync(() =>
        {
            try
            {
                dynamic? app = _inventorApp;
                if (app != null)
                {
                    app.Visible = visible;
                    if (visible)
                    {
                        try
                        {
                            dynamic activeView = app.ActiveView;
                            if (activeView != null)
                            {
                                activeView.Fit();
                            }
                        }
                        catch { }
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not set Inventor Visible = {visible}: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Closes the active assembly document without saving and cleanly terminates
    /// the application-owned Inventor process on its native STA thread.
    /// </summary>
    public void CloseSession()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        DiagnosticsLogger.Instance.Info("Closing Inventor automation session...");

        if (Thread.CurrentThread == _staThread)
        {
            DoCloseInternal();
        }
        else if (!_staWorkQueue.IsAddingCompleted)
        {
            try
            {
                var closeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _staWorkQueue.Add(() =>
                {
                    try
                    {
                        DoCloseInternal();
                    }
                    finally
                    {
                        closeTcs.SetResult(true);
                    }
                });

                closeTcs.Task.Wait(8000);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Error during STA CloseSession: {ex.Message}");
            }
            finally
            {
                _staWorkQueue.CompleteAdding();
            }
        }

        // Wait briefly for STA thread shutdown
        if (_staThread.IsAlive)
        {
            _staThread.Join(3000);
        }

        // Strict PID confirmation cleanup
        if (_processInfo != null && _processInfo.IsApplicationOwned)
        {
            EnsureProcessTerminated(_processInfo.ProcessId);
        }
    }

    private void DoCloseInternal()
    {
        try
        {
            if (_activeAssemblyDoc != null)
            {
                try
                {
                    DiagnosticsLogger.Instance.Info("Closing assembly document without saving...");
                    try
                    {
                        if (_inventorApp != null) _inventorApp.SilentOperation = true;
                    }
                    catch { }
                    _activeAssemblyDoc.Close(true);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Error closing active assembly document: {ex.Message}");
                }
                _activeAssemblyDoc = null;
            }

            InventorProcessInfo? info = _processInfo;
            dynamic? app = _inventorApp;

            if (app is not null && info is not null && info.IsApplicationOwned)
            {
                try
                {
                    DiagnosticsLogger.Instance.Info($"Quitting dedicated Inventor instance (PID {info.ProcessId})...");
                    app.Quit();
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Error calling app.Quit(): {ex.Message}");
                }
            }
        }
        finally
        {
            _sessionScope.Dispose();
            _inventorApp = null;
        }
    }

    private static void EnsureProcessTerminated(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                bool exited = proc.WaitForExit(5000);
                if (!exited)
                {
                    DiagnosticsLogger.Instance.Warn($"Inventor process PID {pid} did not exit cleanly within 5s. Terminating...");
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
        }
        catch
        {
            // Process already terminated
        }
    }

    public void Dispose()
    {
        CloseSession();
    }
}
