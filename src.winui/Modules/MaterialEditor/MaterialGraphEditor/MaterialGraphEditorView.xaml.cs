using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialGraphEditor;

public sealed partial class MaterialGraphEditorView : UserControl
{
    public MaterialGraphEditorView()
    {
        InitializeComponent();
    }

    private async void ExportLayout_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialGraphEditorViewModel viewModel)
            return;

        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("Material graph layout", [".json"]);
        picker.SuggestedFileName = "material-graph-layout";
        InitializePicker(picker);
        StorageFile file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        await FileIO.WriteTextAsync(file, viewModel.ExportLayoutMetadata());
        viewModel.SetStatus($"Layout exported: {file.Name}");
    }

    private async void ImportLayout_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialGraphEditorViewModel viewModel)
            return;

        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        StorageFile file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        string json = await FileIO.ReadTextAsync(file);
        int count = viewModel.ImportLayoutMetadata(json);
        viewModel.SetStatus(count == 0
            ? $"Layout import skipped: {file.Name}"
            : $"Layout imported for {count:N0} node(s) from {file.Name}");
    }

    private void ApplyNodeEdits_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MaterialGraphEditorViewModel viewModel)
            return;

        if (viewModel.ApplySelectedNodeEdits(out string message))
            return;

        viewModel.SetStatus(message);
    }

    private static void InitializePicker(object picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        if (picker is FileSavePicker savePicker)
            InitializeWithWindow.Initialize(savePicker, hwnd);
        else if (picker is FileOpenPicker openPicker)
            InitializeWithWindow.Initialize(openPicker, hwnd);
    }
}
