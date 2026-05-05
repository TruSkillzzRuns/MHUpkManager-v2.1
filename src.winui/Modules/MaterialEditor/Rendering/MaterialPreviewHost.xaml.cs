using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Rendering;

public sealed partial class MaterialPreviewHost : UserControl
{
    public static readonly DependencyProperty CurrentMaterialProperty = DependencyProperty.Register(
        nameof(CurrentMaterial),
        typeof(MaterialDefinition),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(null, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewConfigProperty = DependencyProperty.Register(
        nameof(PreviewConfig),
        typeof(MaterialPreviewConfig),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(null, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewMeshUpkPathProperty = DependencyProperty.Register(
        nameof(PreviewMeshUpkPath),
        typeof(string),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(string.Empty, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewMeshExportPathProperty = DependencyProperty.Register(
        nameof(PreviewMeshExportPath),
        typeof(string),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(string.Empty, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewLodIndexProperty = DependencyProperty.Register(
        nameof(PreviewLodIndex),
        typeof(int),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(0, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewRefreshTokenProperty = DependencyProperty.Register(
        nameof(PreviewRefreshToken),
        typeof(int),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(0, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewTextureSlotPathProperty = DependencyProperty.Register(
        nameof(PreviewTextureSlotPath),
        typeof(string),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(string.Empty, OnPreviewSourceChanged));

    public static readonly DependencyProperty PreviewTextureMipIndexProperty = DependencyProperty.Register(
        nameof(PreviewTextureMipIndex),
        typeof(int),
        typeof(MaterialPreviewHost),
        new PropertyMetadata(-1, OnPreviewSourceChanged));

    public static readonly DependencyProperty DiagnosticsTextProperty = DependencyProperty.Register(
        nameof(DiagnosticsText),
        typeof(string),
        typeof(MaterialPreviewHost),
        new PropertyMetadata("Preview idle."));

    private readonly MaterialPreviewRenderer renderer = new();
    private bool isAttached;
    private bool refreshQueued;
    private bool refreshPending;
    private bool isDragging;
    private bool isPanning;
    private Point lastPointerPosition;

    public MaterialPreviewHost()
    {
        InitializeComponent();
        Loaded += MaterialPreviewHost_Loaded;
        Unloaded += MaterialPreviewHost_Unloaded;
        renderer.LogMessage += Renderer_LogMessage;
        renderer.RenderCompleted += Renderer_RenderCompleted;
    }

    public MaterialDefinition? CurrentMaterial
    {
        get => (MaterialDefinition?)GetValue(CurrentMaterialProperty);
        set => SetValue(CurrentMaterialProperty, value);
    }

    public MaterialPreviewConfig? PreviewConfig
    {
        get => (MaterialPreviewConfig?)GetValue(PreviewConfigProperty);
        set => SetValue(PreviewConfigProperty, value);
    }

    public string PreviewMeshUpkPath
    {
        get => (string)GetValue(PreviewMeshUpkPathProperty);
        set => SetValue(PreviewMeshUpkPathProperty, value);
    }

    public string PreviewMeshExportPath
    {
        get => (string)GetValue(PreviewMeshExportPathProperty);
        set => SetValue(PreviewMeshExportPathProperty, value);
    }

    public int PreviewLodIndex
    {
        get => (int)GetValue(PreviewLodIndexProperty);
        set => SetValue(PreviewLodIndexProperty, value);
    }

    public int PreviewRefreshToken
    {
        get => (int)GetValue(PreviewRefreshTokenProperty);
        set => SetValue(PreviewRefreshTokenProperty, value);
    }

    public string PreviewTextureSlotPath
    {
        get => (string)GetValue(PreviewTextureSlotPathProperty);
        set => SetValue(PreviewTextureSlotPathProperty, value);
    }

    public int PreviewTextureMipIndex
    {
        get => (int)GetValue(PreviewTextureMipIndexProperty);
        set => SetValue(PreviewTextureMipIndexProperty, value);
    }

    public string DiagnosticsText
    {
        get => (string)GetValue(DiagnosticsTextProperty);
        set => SetValue(DiagnosticsTextProperty, value);
    }

    public SwapChainPanel Panel => PreviewPanel;

    public void ResetPreview()
    {
        renderer.ResetCamera();
    }

    private static void OnPreviewSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MaterialPreviewHost host)
            host.QueueRefresh();
    }

    private void MaterialPreviewHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (isAttached)
            return;

        isAttached = true;
        renderer.AttachToPanel(PreviewPanel, DispatcherQueue);
        QueueRefresh();
    }

    private void MaterialPreviewHost_Unloaded(object sender, RoutedEventArgs e)
    {
        isAttached = false;
        refreshQueued = false;
        isDragging = false;
        isPanning = false;
        renderer.ClearPreview();
        DiagnosticsText = "Preview unloaded.";
    }

    private void Renderer_LogMessage(string message)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            DiagnosticsText = message;
            return;
        }

        DispatcherQueue.TryEnqueue(() => DiagnosticsText = message);
    }

    private void Renderer_RenderCompleted(object? sender, EventArgs e)
    {
        Renderer_LogMessage(renderer.Diagnostics);
    }

    private void QueueRefresh()
    {
        if (!isAttached)
            return;

        if (refreshQueued)
        {
            refreshPending = true;
            return;
        }

        refreshQueued = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RefreshPreviewAsync().ConfigureAwait(true);
            }
            finally
            {
                refreshQueued = false;
                if (refreshPending)
                {
                    refreshPending = false;
                    QueueRefresh();
                }
            }
        });
    }

    private async Task RefreshPreviewAsync()
    {
        if (!isAttached)
            return;

        try
        {
            DiagnosticsText = "Updating material preview...";
            int? requestedMip = PreviewTextureMipIndex >= 0 ? PreviewTextureMipIndex : null;
            await renderer.UpdatePreviewAsync(CurrentMaterial, PreviewConfig, PreviewMeshUpkPath, PreviewMeshExportPath, PreviewLodIndex, PreviewTextureSlotPath, requestedMip).ConfigureAwait(true);
            DiagnosticsText = renderer.Diagnostics;
        }
        catch (Exception ex)
        {
            DiagnosticsText = $"Material preview failed: {ex.Message}";
            App.WriteDiagnosticsLog("MaterialEditor.PreviewHost", ex.ToString());
        }
    }

    private void PreviewPanel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(PreviewPanel);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed)
            return;

        isDragging = true;
        isPanning = point.Properties.IsRightButtonPressed;
        lastPointerPosition = point.Position;
        PreviewPanel.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PreviewPanel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!isDragging)
            return;

        PointerPoint point = e.GetCurrentPoint(PreviewPanel);
        float deltaX = (float)(point.Position.X - lastPointerPosition.X);
        float deltaY = (float)(point.Position.Y - lastPointerPosition.Y);
        lastPointerPosition = point.Position;

        if (isPanning)
            renderer.PanCamera(deltaX, deltaY);
        else
            renderer.OrbitCamera(deltaX, deltaY);

        e.Handled = true;
    }

    private void PreviewPanel_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        isDragging = false;
        isPanning = false;
        PreviewPanel.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void PreviewPanel_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        isDragging = false;
        isPanning = false;
        PreviewPanel.ReleasePointerCapture(e.Pointer);
    }

    private void PreviewPanel_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(PreviewPanel);
        renderer.ZoomCamera(point.Properties.MouseWheelDelta / 120.0f);
        e.Handled = true;
    }
}


