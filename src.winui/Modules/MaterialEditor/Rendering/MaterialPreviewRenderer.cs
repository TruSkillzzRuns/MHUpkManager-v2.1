using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Rendering;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;
using UpkManager.Repository;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Rendering;

public sealed class MaterialPreviewRenderer : IDisposable
{
    private readonly MeshPreviewD3D11Renderer renderer = new();
    private readonly MaterialPreviewScene scene = new();
    private readonly TextureToMaterialConverter textureSlotResolver = new();
    private readonly UpkTextureLoader textureLoader = new();
    private readonly UpkFileRepository previewRepository = new();
    private readonly List<TexturePreviewTexture> loadedTextures = [];
    private bool disposed;
    private UnrealHeader? cachedPreviewHeader;
    private string cachedPreviewUpkPath = string.Empty;
    private DateTime cachedPreviewWriteTimeUtc;
    private int cachedPreviewLodIndex;

    public event Action<string>? LogMessage;

    public event EventHandler? RenderCompleted;

    public string Diagnostics => renderer.Diagnostics;

    public MaterialPreviewRenderer()
    {
        renderer.RenderCompleted += OnRendererRenderCompleted;
    }

    public void AttachToPanel(SwapChainPanel panel, DispatcherQueue dispatcherQueue)
    {
        renderer.AttachToPanel(panel, dispatcherQueue);
    }

    public void ClearPreview()
    {
        if (disposed)
            return;

        DisposeLoadedTextures();
        scene.Clear();
        renderer.RenderAttachedPanel();
        LogMessage?.Invoke("Material preview cleared.");
    }

    public void OrbitCamera(float deltaX, float deltaY)
    {
        if (disposed)
            return;

        scene.Camera.Orbit(deltaX, deltaY);
        RenderCurrentFrame();
    }

    public void PanCamera(float deltaX, float deltaY)
    {
        if (disposed)
            return;

        scene.Camera.Pan(deltaX, deltaY);
        RenderCurrentFrame();
    }

    public void ZoomCamera(float wheelDelta)
    {
        if (disposed)
            return;

        scene.Camera.Zoom(wheelDelta);
        RenderCurrentFrame();
    }

    public void ResetCamera()
    {
        if (disposed)
            return;

        scene.ResetCamera();
        RenderCurrentFrame();
    }

    public async Task UpdatePreviewAsync(
        MaterialDefinition? material,
        MaterialPreviewConfig? config,
        string previewMeshUpkPath,
        string previewMeshExportPath,
        int previewLodIndex,
        string selectedTextureSlotPath,
        int? selectedTextureMipIndex)
    {
        if (disposed)
            return;

        MeshPreviewGameMaterial gameMaterial = BuildMaterial(material);

        scene.ApplyConfig(config);
        bool sourcePathsPresent = !string.IsNullOrWhiteSpace(previewMeshUpkPath) && !string.IsNullOrWhiteSpace(previewMeshExportPath);
        bool sourceChanged = !string.Equals(scene.PreviewMesh?.SourceUpkPath, previewMeshUpkPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(scene.PreviewMesh?.SourceMeshExportPath, previewMeshExportPath, StringComparison.OrdinalIgnoreCase);
        bool lodChanged = cachedPreviewLodIndex != previewLodIndex;
        MaterialPreviewMesh? previewMesh = scene.PreviewMesh;

        DisposeLoadedTextures();

        if (sourcePathsPresent && (sourceChanged || previewMesh is null || lodChanged))
        {
            try
            {
                LogMessage?.Invoke($"Material preview loading skeletal mesh '{previewMeshExportPath}' (LOD {previewLodIndex}) from '{Path.GetFileName(previewMeshUpkPath)}'.");
                MaterialPreviewMesh loadedPreviewMesh = await LoadPreviewMeshAsync(previewMeshUpkPath, previewMeshExportPath, previewLodIndex).ConfigureAwait(true);
                loadedPreviewMesh.SourceUpkPath = previewMeshUpkPath;
                loadedPreviewMesh.SourceMeshExportPath = previewMeshExportPath;
                loadedPreviewMesh.ApplyMaterial(gameMaterial);
                ApplyPreviewMeshDefaults(loadedPreviewMesh);
                scene.SetPreviewMesh(loadedPreviewMesh, resetCamera: sourceChanged || previewMesh is null);
                previewMesh = loadedPreviewMesh;
                cachedPreviewLodIndex = previewLodIndex;

                try
                {
                    await LoadTexturesAsync(material, previewMeshUpkPath, gameMaterial, selectedTextureSlotPath, selectedTextureMipIndex).ConfigureAwait(true);
                }
                catch (Exception textureEx)
                {
                    LogMessage?.Invoke($"Material preview textures unavailable: {textureEx.Message}");
                    App.WriteDiagnosticsLog("MaterialEditor.PreviewTextures", textureEx.ToString());
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Material preview mesh unavailable: {ex.Message}");
                App.WriteDiagnosticsLog("MaterialEditor.Preview", ex.ToString());
            }
        }
        else if (previewMesh is not null)
        {
            LogMessage?.Invoke("Material preview reusing the current skeletal mesh.");
            previewMesh.ApplyMaterial(gameMaterial);
            ApplyPreviewMeshDefaults(previewMesh);
            scene.SetPreviewMesh(previewMesh, resetCamera: false);

            try
            {
                await LoadTexturesAsync(material, string.Empty, gameMaterial, selectedTextureSlotPath, selectedTextureMipIndex).ConfigureAwait(true);
            }
            catch (Exception textureEx)
            {
                LogMessage?.Invoke($"Material preview textures unavailable: {textureEx.Message}");
                App.WriteDiagnosticsLog("MaterialEditor.PreviewTextures", textureEx.ToString());
            }
        }
        else
        {
            LogMessage?.Invoke("Material preview mesh not available. Preview will stay GPU-only and render without a mesh.");

            try
            {
                await LoadTexturesAsync(material, string.Empty, gameMaterial, selectedTextureSlotPath, selectedTextureMipIndex).ConfigureAwait(true);
            }
            catch (Exception textureEx)
            {
                LogMessage?.Invoke($"Material preview textures unavailable: {textureEx.Message}");
                App.WriteDiagnosticsLog("MaterialEditor.PreviewTextures", textureEx.ToString());
            }
        }

        if (previewMesh is not null)
        {
            previewMesh.ApplyMaterial(gameMaterial);
            renderer.SetFrame(scene.NativeScene, scene.Camera);
            renderer.RenderAttachedPanel();
        }
        else
        {
            renderer.SetFrame(scene.NativeScene, scene.Camera);
            renderer.RenderAttachedPanel();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        renderer.RenderCompleted -= OnRendererRenderCompleted;
        DisposeLoadedTextures();
        scene.Dispose();
        renderer.Dispose();
    }

    private void OnRendererRenderCompleted(object? sender, EventArgs e)
    {
        RenderCompleted?.Invoke(this, e);
        LogMessage?.Invoke(renderer.Diagnostics);
    }

    private void RenderCurrentFrame()
    {
        if (disposed)
            return;

        renderer.SetFrame(scene.NativeScene, scene.Camera);
        renderer.RenderAttachedPanel();
    }

    private void ApplyPreviewMeshDefaults(MaterialPreviewMesh previewMesh)
    {
        if (previewMesh.NativeMesh.Sections.Count == 0)
            return;

        foreach (MeshPreviewSection section in previewMesh.NativeMesh.Sections)
        {
            section.GameMaterial ??= new MeshPreviewGameMaterial
            {
                Enabled = true,
                DiffuseColor = new Vector3(0.65f, 0.65f, 0.65f),
                SpecularColor = Vector3.One
            };
            section.GameMaterial.Enabled = true;
        }
    }

    private MeshPreviewGameMaterial BuildMaterial(MaterialDefinition? material)
    {
        MeshPreviewGameMaterial gameMaterial = new()
        {
            Enabled = true,
            MaterialPath = material?.Path ?? string.Empty,
            DiffuseColor = new Vector3(0.65f, 0.65f, 0.65f),
            SpecularColor = Vector3.One,
            LambertAmbient = new Vector3(0.16f, 0.16f, 0.16f),
            ShadowAmbientColor = new Vector3(0.08f, 0.08f, 0.08f),
            FillLightColor = new Vector3(0.22f, 0.22f, 0.22f),
            LightingAmbient = 0.75f,
            SpecMult = 0.75f,
            SpecMultLq = 0.50f,
            SpecularPower = 18.0f,
            TwoSidedLighting = material is not null && material.Type.Contains("TwoSided", StringComparison.OrdinalIgnoreCase) ? 1.0f : 0.0f,
            TwoSided = material is not null && material.Type.Contains("TwoSided", StringComparison.OrdinalIgnoreCase)
        };

        if (material is null)
            return gameMaterial;

        ApplyScalarParameters(material, gameMaterial);
        ApplyVectorParameters(material, gameMaterial);
        return gameMaterial;
    }

    private void ApplyScalarParameters(MaterialDefinition material, MeshPreviewGameMaterial gameMaterial)
    {
        foreach (MaterialParameter parameter in material.ScalarParameters)
        {
            float value = parameter.ScalarValue ?? parameter.DefaultScalarValue ?? 0.0f;
            string name = parameter.Name.Trim().ToLowerInvariant();

            if (name.Contains("ambient"))
                gameMaterial.LightingAmbient = value;
            else if (name.Contains("lambert") && name.Contains("power"))
                gameMaterial.LambertDiffusePower = value;
            else if (name.Contains("phong") && name.Contains("power"))
                gameMaterial.PhongDiffusePower = value;
            else if (name.Contains("normal"))
                gameMaterial.NormalStrength = value;
            else if (name.Contains("reflection"))
                gameMaterial.ReflectionMult = value;
            else if (name.Contains("specmultlq"))
                gameMaterial.SpecMultLq = value;
            else if (name.Contains("specmult"))
                gameMaterial.SpecMult = value;
            else if (name.Contains("specular") && name.Contains("power"))
                gameMaterial.SpecularPower = value;
            else if (name.Contains("rim") && name.Contains("falloff"))
                gameMaterial.RimFalloff = value;
            else if (name.Contains("rim"))
                gameMaterial.RimColorMult = value;
            else if (name.Contains("screen") && name.Contains("amount"))
                gameMaterial.ScreenLightAmount = value;
            else if (name.Contains("screen") && name.Contains("mult"))
                gameMaterial.ScreenLightMult = value;
            else if (name.Contains("screen") && name.Contains("power"))
                gameMaterial.ScreenLightPower = value;
            else if (name.Contains("skin"))
                gameMaterial.SkinScatterStrength = value;
            else if (name.Contains("two") && name.Contains("sided"))
                gameMaterial.TwoSidedLighting = value;
        }
    }

    private void ApplyVectorParameters(MaterialDefinition material, MeshPreviewGameMaterial gameMaterial)
    {
        foreach (MaterialParameter parameter in material.VectorParameters)
        {
            Vector4 value = parameter.VectorValue ?? parameter.DefaultVectorValue ?? Vector4.Zero;
            string name = parameter.Name.Trim().ToLowerInvariant();
            Vector3 rgb = new(value.X, value.Y, value.Z);

            if (name.Contains("diffuse"))
                gameMaterial.DiffuseColor = rgb;
            else if (name.Contains("specular"))
                gameMaterial.SpecularColor = rgb;
            else if (name.Contains("ambient") || name.Contains("lambert"))
                gameMaterial.LambertAmbient = rgb;
            else if (name.Contains("shadow"))
                gameMaterial.ShadowAmbientColor = rgb;
            else if (name.Contains("fill"))
                gameMaterial.FillLightColor = rgb;
            else if (name.Contains("subsurface") && name.Contains("inscatter"))
                gameMaterial.SubsurfaceInscatteringColor = rgb;
            else if (name.Contains("subsurface") && name.Contains("absorb"))
                gameMaterial.SubsurfaceAbsorptionColor = rgb;
        }
    }

    private async Task LoadTexturesAsync(MaterialDefinition? material, string previewMeshUpkPath, MeshPreviewGameMaterial gameMaterial, string selectedTextureSlotPath, int? selectedTextureMipIndex)
    {
        if (material is null)
            return;

        string sourceUpkPath = !string.IsNullOrWhiteSpace(material.SourceUpkPath) ? material.SourceUpkPath : previewMeshUpkPath;
        if (string.IsNullOrWhiteSpace(sourceUpkPath) || !File.Exists(sourceUpkPath))
        {
            LogMessage?.Invoke("Material preview textures skipped: source UPK not available.");
            return;
        }

        foreach (MaterialTextureSlot slot in material.TextureSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.TexturePath))
                continue;

            TexturePreviewMaterialSlot textureSlot = textureSlotResolver.ResolveSlot(slot.SlotName ?? slot.TextureName, TexturePreviewMaterialSlot.Diffuse);
            LogMessage?.Invoke($"Loading texture {slot.TexturePath} for {slot.SlotName}.");
            try
            {
                int? requestedMip = string.Equals(slot.TexturePath, selectedTextureSlotPath, StringComparison.OrdinalIgnoreCase)
                    ? selectedTextureMipIndex
                    : null;
                TexturePreviewTexture texture = await textureLoader.LoadFromUpkAsync(sourceUpkPath, slot.TexturePath, textureSlot, message => LogMessage?.Invoke(message), requestedMip).ConfigureAwait(true);
                loadedTextures.Add(texture);
                gameMaterial.SetTexture(MapGameTextureSlot(textureSlot), texture);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Texture load failed for {slot.TexturePath}: {ex.Message}");
            }
        }
    }

    private async Task<MaterialPreviewMesh> LoadPreviewMeshAsync(string upkPath, string exportPath)
    {
        return await LoadPreviewMeshAsync(upkPath, exportPath, cachedPreviewLodIndex).ConfigureAwait(true);
    }

    private async Task<MaterialPreviewMesh> LoadPreviewMeshAsync(string upkPath, string exportPath, int lodIndex)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(upkPath);
        if (!string.Equals(cachedPreviewUpkPath, upkPath, StringComparison.OrdinalIgnoreCase) || cachedPreviewWriteTimeUtc != writeTimeUtc)
        {
            cachedPreviewHeader = null;
            cachedPreviewUpkPath = upkPath;
            cachedPreviewWriteTimeUtc = writeTimeUtc;
        }

        cachedPreviewHeader ??= await previewRepository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await cachedPreviewHeader.ReadTablesAsync(null).ConfigureAwait(true);
        LogMessage?.Invoke($"Material preview resolving SkeletalMesh export '{exportPath}' at LOD {lodIndex}.");

        UnrealExportTableEntry export = cachedPreviewHeader.ExportTable
            .FirstOrDefault(entry => string.Equals(entry.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{exportPath}'.");

        if (export.UnrealObject is null)
        {
            await cachedPreviewHeader.ReadExportObjectAsync(export, null).ConfigureAwait(true);
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
        }

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{exportPath}' is not a SkeletalMesh.");

        LogMessage?.Invoke($"Material preview SkeletalMesh '{exportPath}' resolved with {skeletalMesh.LODModels.Count} LOD(s).");
        int safeLodIndex = Math.Clamp(lodIndex, 0, Math.Max(0, skeletalMesh.LODModels.Count - 1));
        return MaterialPreviewMesh.CreateFromSkeletalMesh(skeletalMesh, Path.GetFileNameWithoutExtension(exportPath), safeLodIndex, message => LogMessage?.Invoke(message));
    }

    private static MeshPreviewGameTextureSlot MapGameTextureSlot(TexturePreviewMaterialSlot slot)
    {
        return slot switch
        {
            TexturePreviewMaterialSlot.Normal => MeshPreviewGameTextureSlot.Normal,
            TexturePreviewMaterialSlot.Specular => MeshPreviewGameTextureSlot.SpecColor,
            TexturePreviewMaterialSlot.Emissive => MeshPreviewGameTextureSlot.Espa,
            TexturePreviewMaterialSlot.Mask => MeshPreviewGameTextureSlot.Smspsk,
            _ => MeshPreviewGameTextureSlot.Diffuse
        };
    }

    private void DisposeLoadedTextures()
    {
        foreach (TexturePreviewTexture texture in loadedTextures)
            texture.Dispose();

        loadedTextures.Clear();
    }
}


