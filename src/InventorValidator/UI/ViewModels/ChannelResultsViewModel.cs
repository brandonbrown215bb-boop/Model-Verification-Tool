using InventorValidator.Infrastructure;

namespace InventorValidator.UI.ViewModels;

public class ChannelResultsViewModel : ViewModelBase
{
    private string _statusMessage = "Channel location geometry validation results will appear here after extraction and matching (Phase 5/6).";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
}
