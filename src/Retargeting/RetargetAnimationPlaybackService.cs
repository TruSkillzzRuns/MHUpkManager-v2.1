using System.Numerics;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Anim;
using UpkManager.Models.UpkFile.Tables;

namespace OmegaAssetStudio.Retargeting;

public sealed record RetargetAnimationSequenceInfo(
    string DisplayName,
    int Index,
    UAnimSequence Sequence,
    int TrackCount,
    int FrameCount,
    float SequenceLengthSeconds);

public sealed class RetargetAnimationPlaybackService
{
    public IReadOnlyList<RetargetAnimationSequenceInfo> GetSequences(UAnimSet animSet, Action<string>? log = null)
    {
        if (animSet is null)
            throw new ArgumentNullException(nameof(animSet));

        List<RetargetAnimationSequenceInfo> sequences = [];
        if (animSet.Sequences is null || animSet.Sequences.Count == 0)
            return sequences;

        for (int index = 0; index < animSet.Sequences.Count; index++)
        {
            FObject sequenceRef = animSet.Sequences[index];
            if (sequenceRef is null)
                continue;

            try
            {
                UAnimSequence? sequence = sequenceRef.LoadObject<UAnimSequence>();
                if (sequence is null)
                    continue;

                string displayName = BuildDisplayName(sequence, index);
                sequences.Add(new RetargetAnimationSequenceInfo(
                    displayName,
                    index,
                    sequence,
                    animSet.TrackBoneNames?.Count ?? 0,
                    sequence.NumFrames,
                    MathF.Max(0.0f, sequence.SequenceLength)));
            }
            catch (Exception ex)
            {
                log?.Invoke($"Anim playback: failed to load sequence {index}: {ex.Message}");
            }
        }

        log?.Invoke($"Anim playback: discovered {sequences.Count} AnimSequence export(s).");
        return sequences;
    }

    public RetargetMesh ApplySequence(
        RetargetMesh sourceMesh,
        UAnimSet animSet,
        UAnimSequence sequence,
        float playbackTimeSeconds,
        Action<string>? log = null)
    {
        if (sourceMesh is null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (animSet is null)
            throw new ArgumentNullException(nameof(animSet));
        if (sequence is null)
            throw new ArgumentNullException(nameof(sequence));

        RetargetMesh posedMesh = sourceMesh.DeepClone();
        posedMesh.RebuildBoneLookup();
        if (posedMesh.Bones.Count == 0)
            throw new InvalidOperationException("The selected mesh does not contain any bones for animation playback.");

        Matrix4x4[] bindGlobals = posedMesh.Bones.Select(static bone => bone.GlobalTransform).ToArray();
        Matrix4x4[] posedGlobals = new Matrix4x4[posedMesh.Bones.Count];
        for (int i = 0; i < posedMesh.Bones.Count; i++)
            posedGlobals[i] = Matrix4x4.Identity;
        Matrix4x4[] posedLocals = new Matrix4x4[posedMesh.Bones.Count];
        for (int i = 0; i < posedMesh.Bones.Count; i++)
            posedLocals[i] = posedMesh.Bones[i].LocalTransform;
        Dictionary<string, int> boneIndexByName = BuildBoneIndexLookup(posedMesh.Bones);

        float sequenceLength = sequence.SequenceLength > 1e-5f
            ? sequence.SequenceLength
            : Math.Max(1, sequence.NumFrames - 1) / 30.0f;
        float timeline = sequenceLength > 1e-5f
            ? playbackTimeSeconds % sequenceLength
            : playbackTimeSeconds;
        if (timeline < 0.0f)
            timeline += sequenceLength;

        float sampleFrame = sequenceLength > 1e-5f
            ? (timeline / sequenceLength) * Math.Max(1, sequence.NumFrames - 1)
            : 0.0f;

        int trackCount = Math.Max(
            animSet.TrackBoneNames?.Count ?? 0,
            Math.Max(
                sequence.RotationData?.Count ?? 0,
                Math.Max(sequence.TranslationData?.Count ?? 0, sequence.RawAnimationData?.Count ?? 0)));

        int appliedTracks = 0;
        for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            string? boneName = ResolveTrackBoneName(animSet, trackIndex);
            int boneIndex = -1;
            if (!string.IsNullOrWhiteSpace(boneName))
                boneIndexByName.TryGetValue(boneName, out boneIndex);

            if (boneIndex < 0 && trackIndex < posedMesh.Bones.Count)
                boneIndex = trackIndex;

            if (boneIndex < 0 || boneIndex >= posedMesh.Bones.Count)
                continue;

            Quaternion rotation = ConvertUe3Rotation(EvaluateRotation(sequence, trackIndex, sampleFrame));
            Vector3 translation = ConvertUe3Translation(EvaluateTranslation(sequence, trackIndex, sampleFrame));
            posedLocals[boneIndex] = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            appliedTracks++;
        }

        for (int i = 0; i < posedMesh.Bones.Count; i++)
        {
            int parentIndex = posedMesh.Bones[i].ParentIndex;
            Matrix4x4 parentGlobal = parentIndex >= 0 && parentIndex < posedGlobals.Length
                ? posedGlobals[parentIndex]
                : Matrix4x4.Identity;
            Matrix4x4 global = posedLocals[i] * parentGlobal;
            posedMesh.Bones[i].LocalTransform = posedLocals[i];
            posedMesh.Bones[i].GlobalTransform = global;
            posedGlobals[i] = global;
        }

        SkinVertices(posedMesh, bindGlobals, posedGlobals);
        posedMesh.RebuildBoneLookup();
        log?.Invoke($"Anim playback: applied sequence '{BuildDisplayName(sequence, 0)}' at {timeline:0.000}s with {appliedTracks} track(s).");
        return posedMesh;
    }

    private static string BuildDisplayName(UAnimSequence sequence, int index)
    {
        string name = sequence.SequenceName?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            name = $"Sequence {index}";

        return $"{name} | frames={Math.Max(1, sequence.NumFrames):N0} | length={MathF.Max(0.0f, sequence.SequenceLength):0.000}s";
    }

    private static Quaternion EvaluateRotation(UAnimSequence sequence, int trackIndex, float sampleFrame)
    {
        List<FQuat>? keys = GetRotationKeys(sequence, trackIndex);
        if (keys is null || keys.Count == 0)
            return Quaternion.Identity;

        List<float> times = GetRotationTimes(sequence, trackIndex, keys.Count);
        return SampleQuaternion(keys, times, sampleFrame);
    }

    private static Vector3 EvaluateTranslation(UAnimSequence sequence, int trackIndex, float sampleFrame)
    {
        List<FVector>? keys = GetTranslationKeys(sequence, trackIndex);
        if (keys is null || keys.Count == 0)
            return Vector3.Zero;

        List<float> times = GetTranslationTimes(sequence, trackIndex, keys.Count);
        return SampleVector(keys, times, sampleFrame);
    }

    private static List<FQuat>? GetRotationKeys(UAnimSequence sequence, int trackIndex)
    {
        if (sequence.RotationData is not null && trackIndex >= 0 && trackIndex < sequence.RotationData.Count)
        {
            List<FQuat> keys = sequence.RotationData[trackIndex].RotKeys?.ToList() ?? [];
            if (keys.Count > 0)
                return keys;
        }

        if (sequence.RawAnimationData is not null && trackIndex >= 0 && trackIndex < sequence.RawAnimationData.Count)
        {
            List<FQuat> keys = sequence.RawAnimationData[trackIndex].RotKeys?.ToList() ?? [];
            if (keys.Count > 0)
                return keys;
        }

        return null;
    }

    private static List<FVector>? GetTranslationKeys(UAnimSequence sequence, int trackIndex)
    {
        if (sequence.TranslationData is not null && trackIndex >= 0 && trackIndex < sequence.TranslationData.Count)
        {
            List<FVector> keys = sequence.TranslationData[trackIndex].PosKeys?.ToList() ?? [];
            if (keys.Count > 0)
                return keys;
        }

        if (sequence.RawAnimationData is not null && trackIndex >= 0 && trackIndex < sequence.RawAnimationData.Count)
        {
            List<FVector> keys = sequence.RawAnimationData[trackIndex].PosKeys?.ToList() ?? [];
            if (keys.Count > 0)
                return keys;
        }

        return null;
    }

    private static List<float> GetRotationTimes(UAnimSequence sequence, int trackIndex, int keyCount)
    {
        if (sequence.RotationData is not null && trackIndex >= 0 && trackIndex < sequence.RotationData.Count)
        {
            List<float> times = sequence.RotationData[trackIndex].Times?.ToList() ?? [];
            if (times.Count == keyCount && keyCount > 0)
                return times;
        }

        return BuildUniformTimes(sequence, keyCount);
    }

    private static List<float> GetTranslationTimes(UAnimSequence sequence, int trackIndex, int keyCount)
    {
        if (sequence.TranslationData is not null && trackIndex >= 0 && trackIndex < sequence.TranslationData.Count)
        {
            List<float> times = sequence.TranslationData[trackIndex].Times?.ToList() ?? [];
            if (times.Count == keyCount && keyCount > 0)
                return times;
        }

        return BuildUniformTimes(sequence, keyCount);
    }

    private static List<float> BuildUniformTimes(UAnimSequence sequence, int keyCount)
    {
        if (keyCount <= 1)
            return [0.0f];

        float maxFrame = Math.Max(1, sequence.NumFrames - 1);
        List<float> times = new(keyCount);
        for (int i = 0; i < keyCount; i++)
        {
            float fraction = (float)i / (keyCount - 1);
            times.Add(maxFrame * fraction);
        }

        return times;
    }

    private static Quaternion SampleQuaternion(IReadOnlyList<FQuat> keys, IReadOnlyList<float> times, float sampleFrame)
    {
        if (keys.Count == 1)
            return NormalizeQuaternion(keys[0]);

        int upper = FindUpperKeyIndex(times, sampleFrame);
        if (upper <= 0)
            return NormalizeQuaternion(keys[0]);

        if (upper >= keys.Count)
            return NormalizeQuaternion(keys[^1]);

        int lower = upper - 1;
        float lowerTime = times[lower];
        float upperTime = times[upper];
        float span = MathF.Max(1e-5f, upperTime - lowerTime);
        float factor = Math.Clamp((sampleFrame - lowerTime) / span, 0.0f, 1.0f);

        Quaternion start = NormalizeQuaternion(keys[lower]);
        Quaternion end = NormalizeQuaternion(keys[upper]);
        if (Quaternion.Dot(start, end) < 0.0f)
            end = new Quaternion(-end.X, -end.Y, -end.Z, -end.W);

        return Quaternion.Normalize(Quaternion.Slerp(start, end, factor));
    }

    private static Vector3 SampleVector(IReadOnlyList<FVector> keys, IReadOnlyList<float> times, float sampleFrame)
    {
        if (keys.Count == 1)
            return keys[0].ToVector3();

        int upper = FindUpperKeyIndex(times, sampleFrame);
        if (upper <= 0)
            return keys[0].ToVector3();

        if (upper >= keys.Count)
            return keys[^1].ToVector3();

        int lower = upper - 1;
        float lowerTime = times[lower];
        float upperTime = times[upper];
        float span = MathF.Max(1e-5f, upperTime - lowerTime);
        float factor = Math.Clamp((sampleFrame - lowerTime) / span, 0.0f, 1.0f);
        return Vector3.Lerp(keys[lower].ToVector3(), keys[upper].ToVector3(), factor);
    }

    private static int FindUpperKeyIndex(IReadOnlyList<float> times, float sampleFrame)
    {
        for (int i = 0; i < times.Count; i++)
        {
            if (sampleFrame <= times[i])
                return i;
        }

        return times.Count;
    }

    private static Quaternion NormalizeQuaternion(FQuat quat)
    {
        Quaternion value = quat.ToQuaternion();
        if (MathF.Abs(value.W) <= 1e-5f)
        {
            float squared = (value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z);
            if (squared <= 1.0f)
            {
                float w = MathF.Sqrt(MathF.Max(0.0f, 1.0f - squared));
                value = new Quaternion(value.X, value.Y, value.Z, w);
            }
        }

        float lengthSquared = (value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z) + (value.W * value.W);
        return lengthSquared > 1e-6f ? Quaternion.Normalize(value) : Quaternion.Identity;
    }

    private static Dictionary<string, int> BuildBoneIndexLookup(IReadOnlyList<RetargetBone> bones)
    {
        Dictionary<string, int> lookup = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bones.Count; i++)
            lookup[bones[i].Name] = i;
        return lookup;
    }

    private static string? ResolveTrackBoneName(UAnimSet animSet, int trackIndex)
    {
        if (animSet.TrackBoneNames is not null && trackIndex >= 0 && trackIndex < animSet.TrackBoneNames.Count)
        {
            string? trackName = animSet.TrackBoneNames[trackIndex]?.Name;
            if (!string.IsNullOrWhiteSpace(trackName))
                return trackName;
        }

        if (animSet.UseTranslationBoneNames is not null && trackIndex >= 0 && trackIndex < animSet.UseTranslationBoneNames.Count)
        {
            string? trackName = animSet.UseTranslationBoneNames[trackIndex]?.Name;
            if (!string.IsNullOrWhiteSpace(trackName))
                return trackName;
        }

        return null;
    }

    private static void SkinVertices(RetargetMesh mesh, Matrix4x4[] bindGlobals, Matrix4x4[] posedGlobals)
    {
        Dictionary<string, int> boneIndexByName = BuildBoneIndexLookup(mesh.Bones);

        foreach (RetargetSection section in mesh.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                if (vertex.Weights.Count == 0)
                    continue;

                Vector3 posedPosition = Vector3.Zero;
                Vector3 posedNormal = Vector3.Zero;
                Vector3 posedTangent = Vector3.Zero;
                Vector3 posedBitangent = Vector3.Zero;
                float totalWeight = 0.0f;

                foreach (RetargetWeight weight in vertex.Weights)
                {
                    if (weight.Weight <= 0.0f)
                        continue;

                    if (!boneIndexByName.TryGetValue(weight.BoneName, out int boneIndex) ||
                        boneIndex < 0 ||
                        boneIndex >= bindGlobals.Length ||
                        !Matrix4x4.Invert(bindGlobals[boneIndex], out Matrix4x4 bindInverse))
                    {
                        continue;
                    }

                    Matrix4x4 skinMatrix = bindInverse * posedGlobals[boneIndex];
                    posedPosition += Vector3.Transform(vertex.Position, skinMatrix) * weight.Weight;
                    posedNormal += Vector3.TransformNormal(vertex.Normal, skinMatrix) * weight.Weight;
                    posedTangent += Vector3.TransformNormal(vertex.Tangent, skinMatrix) * weight.Weight;
                    posedBitangent += Vector3.TransformNormal(vertex.Bitangent, skinMatrix) * weight.Weight;
                    totalWeight += weight.Weight;
                }

                if (totalWeight <= 1e-5f)
                    continue;

                float normalization = 1.0f / totalWeight;
                vertex.Position = posedPosition * normalization;
                vertex.Normal = NormalizeOrFallback(posedNormal * normalization, Vector3.UnitY);
                vertex.Tangent = NormalizeOrFallback(posedTangent * normalization, Vector3.UnitX);
                vertex.Bitangent = NormalizeOrFallback(posedBitangent * normalization, Vector3.UnitZ);
            }
        }
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : fallback;
    }

    private static Vector3 ConvertUe3Translation(Vector3 ue3) => new(ue3.X, ue3.Z, ue3.Y);

    private static Quaternion ConvertUe3Rotation(Quaternion ue3) => new(ue3.X, ue3.Z, ue3.Y, ue3.W);
}

