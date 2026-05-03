using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OmegaAssetStudio.WinUI.Modules.Workflows;

public sealed class WorkflowsViewModel : INotifyPropertyChanged
{
    private string meshFusionLabText = string.Empty;
    private string textures2Text = string.Empty;
    private string retargetText = string.Empty;
    private string materialEditorText = string.Empty;
    private string upkMigrationText = string.Empty;
    private string autoFixEngineText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MeshFusionLabText
    {
        get => meshFusionLabText;
        private set => SetField(ref meshFusionLabText, value);
    }

    public string Textures2Text
    {
        get => textures2Text;
        private set => SetField(ref textures2Text, value);
    }

    public string RetargetText
    {
        get => retargetText;
        private set => SetField(ref retargetText, value);
    }

    public string MaterialEditorText
    {
        get => materialEditorText;
        private set => SetField(ref materialEditorText, value);
    }

    public string UpkMigrationText
    {
        get => upkMigrationText;
        private set => SetField(ref upkMigrationText, value);
    }

    public string AutoFixEngineText
    {
        get => autoFixEngineText;
        private set => SetField(ref autoFixEngineText, value);
    }

    public WorkflowsViewModel()
    {
        string workflowRoot = Path.Combine(AppContext.BaseDirectory, "Modules", "Workflows", "WorkflowText");
        MeshFusionLabText = LoadWorkflowText(Path.Combine(workflowRoot, "MFL.txt"));
        Textures2Text = LoadWorkflowText(Path.Combine(workflowRoot, "Textures2.txt"));
        RetargetText = LoadWorkflowText(Path.Combine(workflowRoot, "Retarget.txt"));
        MaterialEditorText = LoadWorkflowText(Path.Combine(workflowRoot, "MaterialEditor.txt"));
        UpkMigrationText = LoadWorkflowText(Path.Combine(workflowRoot, "UpkMigration.txt"));
        AutoFixEngineText = LoadWorkflowText(Path.Combine(workflowRoot, "AutoFixEngine.txt"));
    }

    private static string LoadWorkflowText(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
