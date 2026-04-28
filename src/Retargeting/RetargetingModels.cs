using System.Numerics;
using System.Reflection;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Anim;

namespace OmegaAssetStudio.Retargeting;

public sealed class RetargetMesh
{
    public string SourcePath { get; set; } = string.Empty;
    public string MeshName { get; set; } = string.Empty;
    public float AppliedScale { get; set; } = 1.0f;
    public Quaternion AppliedOrientation { get; set; } = Quaternion.Identity;
    public List<RetargetSection> Sections { get; } = [];
    public List<RetargetBone> Bones { get; } = [];
    public List<RetargetTextureReference> Textures { get; } = [];
    public Dictionary<string, RetargetBone> BonesByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public UAnimSet AnimSet { get; set; }
    public VersionCompatibilityHints? VersionHints { get; set; }

    public int VertexCount => Sections.Sum(static section => section.Vertices.Count);
    public int TriangleCount => Sections.Sum(static section => section.Indices.Count / 3);
    public int MaxUvSets => Sections.Count == 0 ? 1 : Math.Max(1, Sections.Max(static section => section.Vertices.Count == 0 ? 1 : section.Vertices.Max(static vertex => vertex.UVs.Count)));

    public void RebuildBoneLookup()
    {
        BonesByName.Clear();
        foreach (RetargetBone bone in Bones)
            BonesByName[bone.Name] = bone;
    }

    public RetargetMesh DeepClone()
    {
        RetargetMesh clone = new()
        {
            SourcePath = SourcePath,
            MeshName = MeshName,
            AppliedScale = AppliedScale,
            AppliedOrientation = AppliedOrientation,
            VersionHints = VersionHints,
            AnimSet = AnimSet is null
                ? null
                : (UAnimSet)(AnimSet.GetType()
                    .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(AnimSet, null) ?? AnimSet)
        };

        foreach (RetargetSection section in Sections)
            clone.Sections.Add(section.DeepClone());

        foreach (RetargetBone bone in Bones)
            clone.Bones.Add(bone.DeepClone());

        foreach (RetargetTextureReference texture in Textures)
            clone.Textures.Add(texture with { });

        clone.RebuildBoneLookup();
        return clone;
    }
}

public sealed class RetargetSection
{
    public string Name { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public int MaterialIndex { get; set; }
    public List<RetargetVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
    public List<int> TriangleSmoothingGroups { get; } = [];

    public RetargetSection DeepClone()
    {
        RetargetSection clone = new()
        {
            Name = Name,
            MaterialName = MaterialName,
            MaterialIndex = MaterialIndex
        };

        foreach (RetargetVertex vertex in Vertices)
            clone.Vertices.Add(vertex.DeepClone());

        clone.Indices.AddRange(Indices);
        clone.TriangleSmoothingGroups.AddRange(TriangleSmoothingGroups);
        return clone;
    }
}

public sealed class RetargetVertex
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector3 Tangent { get; set; }
    public Vector3 Bitangent { get; set; }
    public List<Vector2> UVs { get; } = [];
    public FColor Color { get; set; } = new() { R = 255, G = 255, B = 255, A = 255 };
    public List<RetargetWeight> Weights { get; } = [];

    public RetargetVertex DeepClone()
    {
        RetargetVertex clone = new()
        {
            Position = Position,
            Normal = Normal,
            Tangent = Tangent,
            Bitangent = Bitangent,
            Color = new FColor { R = Color.R, G = Color.G, B = Color.B, A = Color.A }
        };

        clone.UVs.AddRange(UVs);
        clone.Weights.AddRange(Weights.Select(static weight => weight with { }));
        return clone;
    }
}

public sealed class RetargetBone
{
    public string Name { get; set; } = string.Empty;
    public int ParentIndex { get; set; } = -1;
    public Matrix4x4 LocalTransform { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 GlobalTransform { get; set; } = Matrix4x4.Identity;

    public RetargetBone DeepClone()
    {
        return new RetargetBone
        {
            Name = Name,
            ParentIndex = ParentIndex,
            LocalTransform = LocalTransform,
            GlobalTransform = GlobalTransform
        };
    }
}

public sealed class SkeletonDefinition
{
    public string SourcePath { get; set; } = string.Empty;
    public List<RetargetBone> Bones { get; } = [];
    public Dictionary<string, RetargetBone> BonesByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void RebuildBoneLookup()
    {
        BonesByName.Clear();
        foreach (RetargetBone bone in Bones)
            BonesByName[bone.Name] = bone;
    }
}

public sealed class BoneMappingResult
{
    public Dictionary<string, string> Mapping { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> UnmappedBones { get; } = [];
}

public sealed record VersionCompatibilityHints
{
    public int SourcePackageVersion { get; init; }
    public int SourceLicenseeVersion { get; init; }
    public string SourceVersionLabel { get; init; } = string.Empty;
    public bool RequiresCoordinateSwap { get; init; } = true;
}

public readonly record struct RetargetWeight(string BoneName, float Weight);

public readonly record struct RetargetTextureReference(string FilePath, string Name);



