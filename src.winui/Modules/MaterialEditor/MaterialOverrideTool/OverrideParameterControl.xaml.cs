using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialOverrideTool;

public sealed partial class OverrideParameterControl : UserControl
{
    public OverrideParameterControl()
    {
        InitializeComponent();
    }

    private void AddParameter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialOverrideViewModel viewModel)
            return;

        viewModel.AddFirstMissingMaterialParameter();
    }

    private void ApplyOverrides_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialOverrideViewModel viewModel)
            return;

        viewModel.ApplyProfile();
    }

    private void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialOverrideViewModel viewModel)
            return;

        viewModel.ResetProfile();
    }

    private void RemoveParameter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialOverrideViewModel viewModel || sender is not FrameworkElement element || element.Tag is not EditableOverrideParameter parameter)
            return;

        viewModel.RemoveOverrideParameter(parameter);
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialOverrideViewModel viewModel)
            return;

        string payload = viewModel.ExportSelectedProfile();
        if (string.IsNullOrWhiteSpace(payload))
            return;

        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("Material override profile", [".json"]);
        picker.SuggestedFileName = "material-override-profile";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        StorageFile file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        await FileIO.WriteTextAsync(file, payload);
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialOverrideViewModel viewModel)
            return;

        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        StorageFile file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        string payload = await FileIO.ReadTextAsync(file);
        viewModel.ImportProfileJson(payload);
    }
}
