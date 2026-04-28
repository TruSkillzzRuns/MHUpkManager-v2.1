using System.Numerics;

namespace OmegaAssetStudio.Retargeting;

public sealed record MirrorFixAnalysis(
    bool ShouldApply,
    string Axis,
    float Determinant,
    string Reason,
    IReadOnlyList<string> Notes);

public sealed class MirrorFixService
{
    public MirrorFixAnalysis Analyze(RetargetMesh mesh, IReadOnlyDictionary<string, string>? mapping = null, Action<string>? log = null)
    {
        if (mesh is null)
            throw new ArgumentNullException(nameof(mesh));

        if (mesh.Bones.Count == 0)
        {
        string noBoneReason = "No bones were present, so mirroring could not be analyzed.";
        log?.Invoke(noBoneReason);
        return new MirrorFixAnalysis(false, "X", 1.0f, noBoneReason, [noBoneReason]);
        }

        RetargetBone root = mesh.Bones.FirstOrDefault(static bone => bone.ParentIndex < 0) ?? mesh.Bones[0];
        float determinant = root.GlobalTransform.GetDeterminant();
        bool mirroredByDeterminant = determinant < 0.0f;

        string? leftName = FindBoneName(mesh, mapping, true);
        string? rightName = FindBoneName(mesh, mapping, false);
        float leftAverageX = GetAverageSide(mesh, leftName, includeLeft: true);
        float rightAverageX = GetAverageSide(mesh, rightName, includeLeft: false);

        bool mirroredBySides = leftAverageX > rightAverageX && leftAverageX != 0.0f && rightAverageX != 0.0f;
        bool shouldApply = mirroredByDeterminant || mirroredBySides;

        string reason = mirroredByDeterminant
            ? "Root handedness appears mirrored from the bone determinant."
            : mirroredBySides
                ? "Left/right bone placement suggests a mirrored source."
                : "No mirrored handedness detected.";

        List<string> notes = [];
        notes.Add($"Root determinant: {determinant:0.###}");
        if (!string.IsNullOrWhiteSpace(leftName))
            notes.Add($"Left-side bone sample: {leftName} at X={leftAverageX:0.###}");
        if (!string.IsNullOrWhiteSpace(rightName))
            notes.Add($"Right-side bone sample: {rightName} at X={rightAverageX:0.###}");

        log?.Invoke($"Mirror analysis: apply={shouldApply}, axis=X, determinant={determinant:0.###}. {reason}");
        return new MirrorFixAnalysis(shouldApply, "X", determinant, reason, notes);
    }

    public RetargetMesh Apply(RetargetMesh mesh, MirrorFixAnalysis analysis, Action<string>? log = null)
    {
        if (mesh is null)
            throw new ArgumentNullException(nameof(mesh));

        if (!analysis.ShouldApply)
        {
            log?.Invoke("Mirror fix skipped because no mirrored handedness was detected.");
            return mesh.DeepClone();
        }

        Matrix4x4 mirror = GetMirrorMatrix(analysis.Axis);
        RetargetMesh mirrored = mesh.DeepClone();
        mirrored.AppliedOrientation = mesh.AppliedOrientation;

        foreach (RetargetSection section in mirrored.Sections)
        {
            for (int i = 0; i < section.Vertices.Count; i++)
            {
                RetargetVertex vertex = section.Vertices[i];
                vertex.Position = Vector3.Transform(vertex.Position, mirror);
                vertex.Normal = NormalizeOrUnit(Vector3.TransformNormal(vertex.Normal, mirror));
                vertex.Tangent = NormalizeOrUnit(Vector3.TransformNormal(vertex.Tangent, mirror));
                vertex.Bitangent = NormalizeOrUnit(Vector3.TransformNormal(vertex.Bitangent, mirror));
            }

            for (int i = 0; i + 2 < section.Indices.Count; i += 3)
            {
                (section.Indices[i + 1], section.Indices[i + 2]) = (section.Indices[i + 2], section.Indices[i + 1]);
            }
        }

        for (int i = 0; i < mirrored.Bones.Count; i++)
        {
            RetargetBone bone = mirrored.Bones[i];
            bone.GlobalTransform = MirrorTransform(bone.GlobalTransform, mirror);
            bone.LocalTransform = MirrorTransform(bone.LocalTransform, mirror);
        }

        mirrored.RebuildBoneLookup();
        log?.Invoke($"Mirror fix applied using {analysis.Axis}-axis reflection.");
        return mirrored;
    }

    private static Matrix4x4 GetMirrorMatrix(string axis)
    {
        return axis.Trim().ToUpperInvariant() switch
        {
            "Y" => Matrix4x4.CreateScale(1.0f, -1.0f, 1.0f),
            "Z" => Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f),
            _ => Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f)
        };
    }

    private static Matrix4x4 MirrorTransform(Matrix4x4 transform, Matrix4x4 mirror)
    {
        return mirror * transform * mirror;
    }

    private static Vector3 NormalizeOrUnit(Vector3 value)
    {
        return value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : Vector3.UnitY;
    }

    private static string? FindBoneName(RetargetMesh mesh, IReadOnlyDictionary<string, string>? mapping, bool leftSide)
    {
        IEnumerable<RetargetBone> candidates = mesh.Bones.Where(static bone => !string.IsNullOrWhiteSpace(bone.Name));
        if (mapping is not null && mapping.Count > 0)
        {
            string[] mappedNames = mapping.Keys.Concat(mapping.Values).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            candidates = candidates.Where(bone => mappedNames.Any(name => bone.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (RetargetBone bone in candidates)
        {
            string name = bone.Name;
            bool matches =
                leftSide
                    ? name.Contains("left", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("_l", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(" l", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(".l", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith("-l", StringComparison.OrdinalIgnoreCase)
                    : name.Contains("right", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("_r", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(" r", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(".r", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith("-r", StringComparison.OrdinalIgnoreCase);
            if (matches)
                return name;
        }

        return null;
    }

    private static float GetAverageSide(RetargetMesh mesh, string? boneName, bool includeLeft)
    {
        List<float> xValues = [];
        foreach (RetargetBone bone in mesh.Bones)
        {
            if (string.IsNullOrWhiteSpace(bone.Name))
                continue;

            bool isLeft = bone.Name.Contains("left", StringComparison.OrdinalIgnoreCase) ||
                          bone.Name.Contains("_l_", StringComparison.OrdinalIgnoreCase) ||
                          bone.Name.EndsWith("_l", StringComparison.OrdinalIgnoreCase);
            bool isRight = bone.Name.Contains("right", StringComparison.OrdinalIgnoreCase) ||
                           bone.Name.Contains("_r_", StringComparison.OrdinalIgnoreCase) ||
                           bone.Name.EndsWith("_r", StringComparison.OrdinalIgnoreCase);

            if (includeLeft && isLeft)
                xValues.Add(bone.GlobalTransform.Translation.X);
            else if (!includeLeft && isRight)
                xValues.Add(bone.GlobalTransform.Translation.X);
        }

        return xValues.Count == 0 ? 0.0f : xValues.Average();
    }
}

