using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.ViewModels;

public partial class Popup : ObservableValidator
{
    public object? View { get; set; }

    [ObservableProperty]
    public bool inProgress;

    [ObservableProperty]
    public string? progressDescription;

    [RelayCommand]
    public void Confirm() => App.ProcessPopup();

    [RelayCommand]
    public void Cancel() => App.CancelPopup();

    public bool Validate()
    {
        ValidateAllProperties();

        return !HasErrors;
    }

    public virtual Task<bool> ProcessAsync()
    {
        return Task.FromResult(true);
    }
}