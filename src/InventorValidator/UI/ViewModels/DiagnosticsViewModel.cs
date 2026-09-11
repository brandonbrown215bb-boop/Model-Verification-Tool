using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using InventorValidator.Infrastructure;

namespace InventorValidator.UI.ViewModels;

public class DiagnosticsViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand ClearCommand { get; }

    public DiagnosticsViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        CopyDiagnosticsCommand = new RelayCommand(ExecuteCopyDiagnostics);
        ClearCommand = new RelayCommand(ExecuteClear);

        // Load existing entries
        foreach (var entry in DiagnosticsLogger.Instance.Entries)
        {
            LogEntries.Add(entry);
        }

        DiagnosticsLogger.Instance.EntryLogged += OnEntryLogged;
    }

    private void OnEntryLogged(LogEntry entry)
    {
        if (_dispatcher.CheckAccess())
        {
            LogEntries.Add(entry);
        }
        else
        {
            _dispatcher.BeginInvoke(() => LogEntries.Add(entry));
        }
    }

    private void ExecuteCopyDiagnostics()
    {
        try
        {
            var text = DiagnosticsLogger.Instance.ExportToText();
            Clipboard.SetText(text);
            DiagnosticsLogger.Instance.Info("Diagnostics log copied to clipboard.");
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Failed to copy diagnostics: {ex.Message}");
        }
    }

    private void ExecuteClear()
    {
        DiagnosticsLogger.Instance.Clear();
        LogEntries.Clear();
    }
}
