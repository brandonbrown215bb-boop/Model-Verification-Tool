using InventorValidator.Infrastructure;

namespace InventorValidator.UI.ViewModels;

public class ParameterPreviewViewModel : ViewModelBase
{
    private string _statusMessage = "Parameters mapped between Excel and Inventor will appear here for review before applying (Phase 4).";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
}
