using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderVariantTester;

public sealed partial class ShaderVariantTesterView : UserControl
{
    public ShaderVariantTesterView()
    {
        InitializeComponent();
    }

    private void ActivateSelectedVariant_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShaderVariantTesterViewModel viewModel)
            return;

        viewModel.ActivateSelectedVariant();
    }

    private async void ExportVariantMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShaderVariantTesterViewModel viewModel)
            return;

        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("Shader variant metadata", [".json"]);
        picker.SuggestedFileName = "shader-variants";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        StorageFile file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        await FileIO.WriteTextAsync(file, viewModel.ExportVariantMetadata());
    }
}
