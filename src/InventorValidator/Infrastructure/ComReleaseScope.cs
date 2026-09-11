using System.Runtime.InteropServices;

namespace InventorValidator.Infrastructure;

/// <summary>
/// Scoped tracker for COM objects to ensure deterministic, reverse-order release
/// of Runtime Callable Wrappers (RCWs).
/// </summary>
public sealed class ComReleaseScope : IDisposable
{
    private readonly Stack<object> _comObjects = new();
    private bool _disposed;

    /// <summary>
    /// Tracks a COM object and returns it for convenience.
    /// </summary>
    public T Track<T>(T comObject) where T : class
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            _comObjects.Push(comObject);
        }
        return comObject!;
    }

    /// <summary>
    /// Explicitly releases a specific tracked COM object immediately.
    /// </summary>
    public void Release(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
                // Suppress COM release errors during teardown
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        while (_comObjects.Count > 0)
        {
            var obj = _comObjects.Pop();
            try
            {
                if (Marshal.IsComObject(obj))
                {
                    Marshal.FinalReleaseComObject(obj);
                }
            }
            catch
            {
                // Best-effort release during unwinding
            }
        }
    }
}
