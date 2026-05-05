using System.Collections.ObjectModel;
using System.Globalization;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialGraphEditor;

public sealed class MaterialGraphEditorViewModel : MaterialToolViewModelBase
{
    private readonly MaterialGraphService graphService;
    private MhMaterialInstance? selectedMaterial;
    private MhMaterialGraphNode? selectedNode;
    private readonly Dictionary<string, MaterialGraphService.GraphNodeLayoutEntry> nodeLayout = new(StringComparer.OrdinalIgnoreCase);
    private string selectedNodeLabelText = string.Empty;
    private string selectedNodeValueText = string.Empty;

    public MaterialGraphEditorViewModel()
        : this(new MaterialCoreServices())
    {
    }

    public MaterialGraphEditorViewModel(MaterialCoreServices services)
    {
        graphService = services.Graph;
        Title = "Material Graph Editor";
        Nodes = [];
        Connections = [];
    }

    public ObservableCollection<MhMaterialGraphNode> Nodes { get; }

    public ObservableCollection<MhMaterialGraphConnection> Connections { get; }

    public MhMaterialGraphNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (!SetProperty(ref selectedNode, value))
                return;

            selectedNodeLabelText = value?.Label ?? string.Empty;
            selectedNodeValueText = ResolveNodeValueText(value);
            RaisePropertyChanged(nameof(SelectedNodeSummaryText));
            RaisePropertyChanged(nameof(SelectedNodeLabelText));
            RaisePropertyChanged(nameof(SelectedNodeValueText));
            RaisePropertyChanged(nameof(SelectedNodeOutputPreviewText));
            RaisePropertyChanged(nameof(CanEditSelectedNodeParameter));
        }
    }

    public string NodeCountText => $"{Nodes.Count:N0} node(s)";

    public string ConnectionCountText => $"{Connections.Count:N0} connection(s)";

    public string SelectedNodeSummaryText => SelectedNode is null
        ? "No material graph node selected."
        : $"Node: {SelectedNode.Label} | Type: {SelectedNode.NodeType}";

    public string SelectedNodeOutputPreviewText
    {
        get
        {
            if (SelectedNode is null)
                return "Node Output: n/a";

            string outputs = SelectedNode.Outputs.Count == 0 ? "none" : string.Join(", ", SelectedNode.Outputs);
            string value = ResolveNodeValueText(SelectedNode);
            if (string.IsNullOrWhiteSpace(value))
                value = "n/a";
            return $"Node Output: {outputs} | Value: {value}";
        }
    }

    public string SelectedNodeLabelText
    {
        get => selectedNodeLabelText;
        set
        {
            selectedNodeLabelText = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public string SelectedNodeValueText
    {
        get => selectedNodeValueText;
        set
        {
            selectedNodeValueText = value ?? string.Empty;
            RaisePropertyChanged();
        }
    }

    public bool CanEditSelectedNodeParameter => SelectedNode is not null && TryFindMappedParameter(SelectedNode, out _);

    public IReadOnlyDictionary<string, MaterialGraphService.GraphNodeLayoutEntry> NodeLayout => nodeLayout;

    public void LoadMaterial(MhMaterialInstance? material)
    {
        selectedMaterial = material;
        Nodes.Clear();
        Connections.Clear();
        nodeLayout.Clear();

        if (material is null)
        {
            SelectedNode = null;
            StatusText = "No material selected.";
            RaisePropertyChanged(nameof(NodeCountText));
            RaisePropertyChanged(nameof(ConnectionCountText));
            return;
        }

        foreach (MhMaterialGraphNode node in graphService.BuildNodes(material.GraphNodes))
            Nodes.Add(node);

        foreach (MhMaterialGraphConnection connection in graphService.BuildConnections(material.GraphConnections))
            Connections.Add(connection);

        foreach (var entry in graphService.BuildDefaultLayout(Nodes))
            nodeLayout[entry.Key] = entry.Value;

        SelectedNode = Nodes.FirstOrDefault();
        StatusText = $"{Nodes.Count:N0} node(s), {Connections.Count:N0} connection(s).";
        RaisePropertyChanged(nameof(NodeCountText));
        RaisePropertyChanged(nameof(ConnectionCountText));
    }

    public MaterialGraphService.GraphNodeLayoutEntry GetNodeLayout(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !nodeLayout.TryGetValue(nodeId, out MaterialGraphService.GraphNodeLayoutEntry? entry))
            return new MaterialGraphService.GraphNodeLayoutEntry(nodeId, 0f, 0f);

        return entry;
    }

    public void UpdateNodeLayout(string nodeId, float x, float y)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return;

        nodeLayout[nodeId] = new MaterialGraphService.GraphNodeLayoutEntry(nodeId, x, y);
    }

    public bool IsNodeActive(string nodeId)
    {
        if (SelectedNode is null || string.IsNullOrWhiteSpace(nodeId))
            return false;

        if (string.Equals(SelectedNode.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            return true;

        return Connections.Any(connection =>
            (string.Equals(connection.FromNodeId, SelectedNode.NodeId, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(connection.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) ||
            (string.Equals(connection.ToNodeId, SelectedNode.NodeId, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(connection.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase)));
    }

    public string GetNodeOutputPreview(string nodeId)
    {
        MhMaterialGraphNode? node = Nodes.FirstOrDefault(candidate => string.Equals(candidate.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
            return "n/a";

        string outputs = node.Outputs.Count == 0 ? "none" : string.Join(", ", node.Outputs);
        string value = ResolveNodeValueText(node);
        if (string.IsNullOrWhiteSpace(value))
            value = "n/a";
        return $"{outputs} | {value}";
    }

    public bool ApplySelectedNodeEdits(out string message)
    {
        message = string.Empty;
        if (SelectedNode is null)
        {
            message = "No graph node selected.";
            return false;
        }

        SelectedNode.Label = selectedNodeLabelText.Trim();
        if (string.IsNullOrWhiteSpace(SelectedNode.Label))
            SelectedNode.Label = SelectedNode.NodeId;

        if (!TryFindMappedParameter(SelectedNode, out MhMaterialParameter? parameter))
        {
            StatusText = "Node label updated.";
            RaisePropertyChanged(nameof(SelectedNodeOutputPreviewText));
            return true;
        }

        if (parameter is null)
        {
            message = "Node parameter could not be resolved.";
            return false;
        }

        if (!TrySetMappedParameterValue(parameter, selectedNodeValueText, out string error))
        {
            message = error;
            return false;
        }

        StatusText = "Node parameter updated.";
        RaisePropertyChanged(nameof(SelectedNodeOutputPreviewText));
        return true;
    }

    public string ExportLayoutMetadata()
    {
        string materialPath = selectedMaterial?.Path ?? string.Empty;
        return graphService.ExportLayoutMetadata(materialPath, Nodes, nodeLayout);
    }

    public int ImportLayoutMetadata(string layoutJson)
    {
        IReadOnlyDictionary<string, MaterialGraphService.GraphNodeLayoutEntry> imported = graphService.ImportLayoutMetadata(layoutJson);
        int count = 0;
        foreach (var pair in imported)
        {
            if (!nodeLayout.ContainsKey(pair.Key))
                continue;

            nodeLayout[pair.Key] = pair.Value;
            count++;
        }

        return count;
    }

    public void SetStatus(string text)
    {
        StatusText = text ?? string.Empty;
    }

    private string ResolveNodeValueText(MhMaterialGraphNode? node)
    {
        if (node is null || !TryFindMappedParameter(node, out MhMaterialParameter? parameter))
            return string.Empty;

        return parameter switch
        {
            MhScalarParameter scalar => scalar.Value.ToString("0.###", CultureInfo.InvariantCulture),
            MhVectorParameter vector => vector.Value,
            MhTextureParameter texture => texture.TexturePath,
            MhSwitchParameter toggle => toggle.Value ? "true" : "false",
            _ => string.Empty
        };
    }

    private bool TrySetMappedParameterValue(MhMaterialParameter parameter, string valueText, out string error)
    {
        error = string.Empty;
        valueText ??= string.Empty;
        switch (parameter)
        {
            case MhScalarParameter scalar:
                if (!float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float scalarValue))
                {
                    error = "Scalar value must be numeric.";
                    return false;
                }

                scalar.Value = scalarValue;
                return true;

            case MhVectorParameter vector:
                vector.Value = valueText.Trim();
                return true;

            case MhTextureParameter texture:
                texture.TexturePath = valueText.Trim();
                return true;

            case MhSwitchParameter toggle:
                if (!bool.TryParse(valueText.Trim(), out bool flag))
                {
                    error = "Switch value must be true or false.";
                    return false;
                }

                toggle.Value = flag;
                return true;
        }

        error = $"Unsupported parameter type {parameter.GetType().Name}.";
        return false;
    }

    private bool TryFindMappedParameter(MhMaterialGraphNode node, out MhMaterialParameter? parameter)
    {
        parameter = null;
        if (selectedMaterial is null)
            return false;

        string prefix = string.Empty;
        if (node.NodeId.StartsWith("scalar:", StringComparison.OrdinalIgnoreCase))
            prefix = "scalar:";
        else if (node.NodeId.StartsWith("vector:", StringComparison.OrdinalIgnoreCase))
            prefix = "vector:";
        else if (node.NodeId.StartsWith("texture:", StringComparison.OrdinalIgnoreCase))
            prefix = "texture:";
        else if (node.NodeId.StartsWith("switch:", StringComparison.OrdinalIgnoreCase))
            prefix = "switch:";

        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        string name = node.NodeId[prefix.Length..];
        parameter = selectedMaterial.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return parameter is not null;
    }
}
