using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ColorVariantGenerator;

public sealed partial class ColorVariantGeneratorView : UserControl
{
    public ColorVariantGeneratorView()
    {
        InitializeComponent();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorVariantGeneratorViewModel viewModel || PresetComboBox.SelectedItem is not string presetName)
            return;

        viewModel.ApplyPreset(presetName);
    }

    private void GenerateVariant_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorVariantGeneratorViewModel viewModel)
            return;

        viewModel.GenerateVariantFromHsv();
    }

    private void ApplyVariantToMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorVariantGeneratorViewModel viewModel)
            return;

        viewModel.ApplySelectedVariantToMaterial();
    }

    private async void ExportVariants_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorVariantGeneratorViewModel viewModel)
            return;

        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("Color variants", [".json"]);
        picker.SuggestedFileName = "color-variants";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        StorageFile file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        await FileIO.WriteTextAsync(file, viewModel.ExportVariants());
    }

    private async void ImportVariants_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorVariantGeneratorViewModel viewModel)
            return;

        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        StorageFile file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        string payload = await FileIO.ReadTextAsync(file);
        viewModel.ImportVariants(payload);
    }

    private void RevertLastChange_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorVariantGeneratorViewModel viewModel)
            return;

        viewModel.RevertLastChange();
    }
}
