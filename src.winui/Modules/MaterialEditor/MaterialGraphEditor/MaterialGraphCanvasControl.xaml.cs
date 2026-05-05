using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialGraphEditor;

public sealed partial class MaterialGraphCanvasControl : UserControl
{
    private readonly Dictionary<string, Border> nodeBorders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> nodeValueLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Line> connectionLines = [];
    private bool dragging;
    private string draggingNodeId = string.Empty;
    private Point dragAnchor;
    private Vector2 dragOrigin;

    public MaterialGraphCanvasControl()
    {
        InitializeComponent();
        Loaded += MaterialGraphCanvasControl_Loaded;
        Unloaded += MaterialGraphCanvasControl_Unloaded;
        DataContextChanged += MaterialGraphCanvasControl_DataContextChanged;
    }

    private void MaterialGraphCanvasControl_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel();
        RenderGraph();
    }

    private void MaterialGraphCanvasControl_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel();
    }

    private void MaterialGraphCanvasControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachViewModel();
        AttachViewModel();
        RenderGraph();
    }

    private void AttachViewModel()
    {
        if (DataContext is MaterialGraphEditorViewModel viewModel)
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void DetachViewModel()
    {
        if (DataContext is MaterialGraphEditorViewModel viewModel)
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialGraphEditorViewModel.SelectedNode) or nameof(MaterialGraphEditorViewModel.SelectedNodeOutputPreviewText))
        {
            UpdateNodeVisualState();
            return;
        }

        if (e.PropertyName is nameof(MaterialGraphEditorViewModel.NodeCountText))
            RenderGraph();
    }

    private void RenderGraph()
    {
        if (DataContext is not MaterialGraphEditorViewModel viewModel)
            return;

        GraphCanvas.Children.Clear();
        nodeBorders.Clear();
        nodeValueLabels.Clear();
        connectionLines.Clear();

        foreach (MhMaterialGraphConnection connection in viewModel.Connections)
            DrawConnection(viewModel, connection);

        foreach (MhMaterialGraphNode node in viewModel.Nodes)
            DrawNode(viewModel, node);

        UpdateNodeVisualState();
    }

    private void DrawConnection(MaterialGraphEditorViewModel viewModel, MhMaterialGraphConnection connection)
    {
        var from = viewModel.GetNodeLayout(connection.FromNodeId);
        var to = viewModel.GetNodeLayout(connection.ToNodeId);
        Line line = new()
        {
            X1 = from.X + 268f,
            Y1 = from.Y + 58f,
            X2 = to.X + 12f,
            Y2 = to.Y + 58f,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 123, 153)),
            StrokeThickness = 2f,
            StrokeDashArray = [2, 2]
        };
        connectionLines.Add(line);
        GraphCanvas.Children.Insert(0, line);
    }

    private void DrawNode(MaterialGraphEditorViewModel viewModel, MhMaterialGraphNode node)
    {
        Border border = new()
        {
            Width = 280,
            MinHeight = 116,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 48, 64)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 81, 92, 117)),
            Tag = node.NodeId
        };

        TextBlock value = new()
        {
            Opacity = 0.8,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        nodeValueLabels[node.NodeId] = value;
        UpdateNodeValueLabel(viewModel, node.NodeId);

        StackPanel panel = new()
        {
            Spacing = 2
        };
        panel.Children.Add(new TextBlock
        {
            Text = node.Label,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        panel.Children.Add(new TextBlock
        {
            Text = node.NodeType,
            Opacity = 0.78,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        panel.Children.Add(value);
        border.Child = panel;

        border.PointerPressed += NodeBorder_PointerPressed;
        border.PointerMoved += NodeBorder_PointerMoved;
        border.PointerReleased += NodeBorder_PointerReleased;
        border.PointerCanceled += NodeBorder_PointerCanceled;

        var layout = viewModel.GetNodeLayout(node.NodeId);
        Canvas.SetLeft(border, layout.X);
        Canvas.SetTop(border, layout.Y);
        GraphCanvas.Children.Add(border);
        nodeBorders[node.NodeId] = border;
    }

    private void UpdateNodeValueLabel(MaterialGraphEditorViewModel viewModel, string nodeId)
    {
        if (!nodeValueLabels.TryGetValue(nodeId, out TextBlock? label))
            return;

        label.Text = viewModel.GetNodeOutputPreview(nodeId);
    }

    private void NodeBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not MaterialGraphEditorViewModel viewModel || sender is not Border border || border.Tag is not string nodeId)
            return;

        dragging = true;
        draggingNodeId = nodeId;
        dragAnchor = e.GetCurrentPoint(GraphCanvas).Position;
        dragOrigin = new Vector2((float)Canvas.GetLeft(border), (float)Canvas.GetTop(border));
        border.CapturePointer(e.Pointer);
        viewModel.SelectedNode = viewModel.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        e.Handled = true;
    }

    private void NodeBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging || DataContext is not MaterialGraphEditorViewModel viewModel || sender is not Border border || border.Tag is not string nodeId)
            return;

        Point point = e.GetCurrentPoint(GraphCanvas).Position;
        float x = dragOrigin.X + (float)(point.X - dragAnchor.X);
        float y = dragOrigin.Y + (float)(point.Y - dragAnchor.Y);
        x = Math.Clamp(x, 0f, (float)Math.Max(0d, GraphCanvas.Width - border.Width));
        y = Math.Clamp(y, 0f, (float)Math.Max(0d, GraphCanvas.Height - border.MinHeight));
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        viewModel.UpdateNodeLayout(nodeId, x, y);
        RedrawConnections(viewModel);
        e.Handled = true;
    }

    private void NodeBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        dragging = false;
        if (sender is Border border)
            border.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void NodeBorder_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        dragging = false;
        if (sender is Border border)
            border.ReleasePointerCapture(e.Pointer);
    }

    private void UpdateNodeVisualState()
    {
        if (DataContext is not MaterialGraphEditorViewModel viewModel)
            return;

        foreach (var pair in nodeBorders)
        {
            bool active = viewModel.IsNodeActive(pair.Key);
            pair.Value.BorderBrush = active
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 177, 82))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 81, 92, 117));
        }

        foreach (string nodeId in nodeValueLabels.Keys)
            UpdateNodeValueLabel(viewModel, nodeId);
    }

    private void RedrawConnections(MaterialGraphEditorViewModel viewModel)
    {
        foreach (Line line in connectionLines)
            GraphCanvas.Children.Remove(line);

        connectionLines.Clear();
        foreach (MhMaterialGraphConnection connection in viewModel.Connections)
            DrawConnection(viewModel, connection);
    }
}

