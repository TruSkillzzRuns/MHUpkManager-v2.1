using System.Numerics;

namespace OmegaAssetStudio.Retargeting;

public sealed record PoseAdjustment(
    string BoneName,
    string Description,
    float Degrees,
    Vector3 Axis);

public sealed class PoseAlignmentService
{
    public IReadOnlyList<PoseAdjustment> Analyze(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string>? boneMapping = null,
        Action<string>? log = null)
    {
        if (sourceMesh is null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (targetSkeleton is null)
            throw new ArgumentNullException(nameof(targetSkeleton));

        sourceMesh.RebuildBoneLookup();
        targetSkeleton.RebuildBoneLookup();

        List<PoseAdjustment> adjustments = [];
        PoseBasis sourceBasis = BuildBasis(sourceMesh.Bones);
        PoseBasis targetBasis = BuildBasis(targetSkeleton.Bones);

        AddSpineAdjustments(sourceMesh, sourceBasis, targetBasis, adjustments);
        AddArmAdjustments(sourceMesh, sourceBasis, targetBasis, adjustments);
        adjustments.AddRange(ZeroTwistBones(sourceMesh, sourceMesh.Bones.Select(static bone => bone.GlobalTransform).ToList()));

        log?.Invoke($"Pose alignment analysis found {adjustments.Count} corrective step(s).");
        return adjustments;
    }

    public RetargetMesh Apply(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string>? boneMapping = null,
        Action<string>? log = null)
    {
        if (sourceMesh is null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (targetSkeleton is null)
            throw new ArgumentNullException(nameof(targetSkeleton));

        sourceMesh.RebuildBoneLookup();
        targetSkeleton.RebuildBoneLookup();

        PoseBasis sourceBasis = BuildBasis(sourceMesh.Bones);
        PoseBasis targetBasis = BuildBasis(targetSkeleton.Bones);
        RetargetMesh aligned = sourceMesh.DeepClone();
        List<Matrix4x4> globals = aligned.Bones.Select(static bone => bone.GlobalTransform).ToList();
        List<PoseAdjustment> adjustments = [];

        int pelvisIndex = FindBoneIndexByRegion(aligned.Bones, BoneRegion.Pelvis);
        if (pelvisIndex < 0)
            pelvisIndex = FindBoneIndex(aligned.Bones, "g_pelvis", "pelvis", "hips");

        int spineIndex = FindBoneIndexByRegion(aligned.Bones, BoneRegion.Spine);
        if (spineIndex < 0)
            spineIndex = FindBoneIndex(aligned.Bones, "g_spine01", "spine01", "spine");

        int leftShoulderIndex = FindBoneIndexByRegion(aligned.Bones, BoneRegion.Shoulder, BoneSide.Left);
        if (leftShoulderIndex < 0)
            leftShoulderIndex = FindBoneIndex(aligned.Bones, "g_l_shoulder", "leftshoulder", "l_clavical");

        int rightShoulderIndex = FindBoneIndexByRegion(aligned.Bones, BoneRegion.Shoulder, BoneSide.Right);
        if (rightShoulderIndex < 0)
            rightShoulderIndex = FindBoneIndex(aligned.Bones, "g_r_shoulder", "rightshoulder", "r_clavical");

        adjustments.AddRange(AlignChainToBasis(aligned, globals, pelvisIndex, sourceBasis.Up, targetBasis.Up, "Spine"));
        adjustments.AddRange(AlignChainToBasis(aligned, globals, spineIndex, sourceBasis.Up, targetBasis.Up, "Spine"));
        adjustments.AddRange(AlignArmToRelaxedPose(aligned, globals, leftShoulderIndex, sourceBasis, targetBasis, true));
        adjustments.AddRange(AlignArmToRelaxedPose(aligned, globals, rightShoulderIndex, sourceBasis, targetBasis, false));
        adjustments.AddRange(ZeroTwistBones(aligned, globals));

        for (int i = 0; i < aligned.Bones.Count; i++)
        {
            aligned.Bones[i].GlobalTransform = globals[i];
            if (aligned.Bones[i].ParentIndex >= 0 &&
                aligned.Bones[i].ParentIndex < globals.Count &&
                Matrix4x4.Invert(globals[aligned.Bones[i].ParentIndex], out Matrix4x4 parentInverse))
            {
                aligned.Bones[i].LocalTransform = globals[i] * parentInverse;
            }
            else
            {
                aligned.Bones[i].LocalTransform = globals[i];
            }
        }

        aligned.RebuildBoneLookup();
        log?.Invoke($"Applied pose alignment with {adjustments.Count} corrective step(s).");
        return aligned;
    }

    private static IEnumerable<PoseAdjustment> AlignChainToBasis(
        RetargetMesh mesh,
        List<Matrix4x4> globals,
        int boneIndex,
        Vector3 currentAxis,
        Vector3 targetAxis,
        string description)
    {
        if (boneIndex < 0 || boneIndex >= mesh.Bones.Count || currentAxis.LengthSquared() <= 1e-6f || targetAxis.LengthSquared() <= 1e-6f)
            return Array.Empty<PoseAdjustment>();

        Quaternion rotation = FromToRotation(currentAxis, targetAxis);
        if (IsIdentity(rotation))
            return Array.Empty<PoseAdjustment>();

        RotateDescendants(mesh.Bones, globals, boneIndex, rotation);
        return new[]
        {
            new PoseAdjustment(
                mesh.Bones[boneIndex].Name,
                $"Aligned {description.ToLowerInvariant()} chain toward target up axis.",
                DegreesFromQuaternion(rotation),
                Vector3.Normalize(Vector3.Cross(currentAxis, targetAxis)))
        };
    }

    private static IEnumerable<PoseAdjustment> AlignArmToRelaxedPose(
        RetargetMesh mesh,
        List<Matrix4x4> globals,
        int shoulderIndex,
        PoseBasis sourceBasis,
        PoseBasis targetBasis,
        bool leftSide)
    {
        if (shoulderIndex < 0 || shoulderIndex >= mesh.Bones.Count)
            return Array.Empty<PoseAdjustment>();

        int elbowIndex = FindPrimaryChildIndex(mesh.Bones, shoulderIndex);
        if (elbowIndex < 0)
            return Array.Empty<PoseAdjustment>();

        Vector3 shoulder = globals[shoulderIndex].Translation;
        Vector3 elbow = globals[elbowIndex].Translation;
        Vector3 armDirection = elbow - shoulder;
        if (armDirection.LengthSquared() <= 1e-6f)
            return Array.Empty<PoseAdjustment>();

        Vector3 sideAxis = leftSide ? -targetBasis.Right : targetBasis.Right;
        Vector3 relaxedDirection = Vector3.Normalize(sideAxis - (targetBasis.Up * 0.18f));
        Quaternion shoulderRotation = FromToRotation(Vector3.Normalize(armDirection), relaxedDirection);
        if (IsIdentity(shoulderRotation))
            return Array.Empty<PoseAdjustment>();

        RotateDescendants(mesh.Bones, globals, shoulderIndex, shoulderRotation);

        int spineIndex = FindBoneIndex(mesh.Bones, "g_spine01", "spine01", "spine");
        if (spineIndex >= 0)
        {
            Vector3 spineDirection = GetChildDirection(mesh.Bones, globals, spineIndex);
            if (spineDirection.LengthSquared() > 1e-6f)
            {
                Quaternion spineCorrection = FromToRotation(Vector3.Normalize(spineDirection), targetBasis.Up);
                if (!IsIdentity(spineCorrection))
                    RotateDescendants(mesh.Bones, globals, spineIndex, Quaternion.Slerp(Quaternion.Identity, spineCorrection, 0.35f));
            }
        }

        string label = leftSide ? "Left arm" : "Right arm";
        Vector3 axis = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(armDirection), relaxedDirection));
        return new[]
        {
            new PoseAdjustment(
                mesh.Bones[shoulderIndex].Name,
                $"Aligned {label.ToLowerInvariant()} into relaxed target pose.",
                DegreesFromQuaternion(shoulderRotation),
                axis)
        };
    }

    private static IEnumerable<PoseAdjustment> ZeroTwistBones(RetargetMesh mesh, List<Matrix4x4> globals)
    {
        List<PoseAdjustment> adjustments = [];
        for (int i = 0; i < mesh.Bones.Count; i++)
        {
            if (!BoneNameHeuristics.IsTwistBone(mesh.Bones[i].Name))
                continue;

            if (mesh.Bones[i].ParentIndex < 0 || mesh.Bones[i].ParentIndex >= globals.Count)
                continue;

            if (!Matrix4x4.Decompose(globals[i], out Vector3 scale, out _, out Vector3 translation))
                continue;

            globals[i] = Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(Quaternion.Identity) *
                Matrix4x4.CreateTranslation(translation);
            adjustments.Add(new PoseAdjustment(mesh.Bones[i].Name, "Zeroed twist bone rotation.", 0.0f, Vector3.Zero));
        }

        return adjustments;
    }

    private static void AddSpineAdjustments(RetargetMesh mesh, PoseBasis sourceBasis, PoseBasis targetBasis, List<PoseAdjustment> adjustments)
    {
        int spineCount = 0;
        int pelvisIndex = FindBoneIndexByRegion(mesh.Bones, BoneRegion.Pelvis);
        if (pelvisIndex < 0)
            pelvisIndex = FindBoneIndex(mesh.Bones, "g_pelvis", "pelvis", "hips");

        int spineIndex = FindBoneIndexByRegion(mesh.Bones, BoneRegion.Spine);
        if (spineIndex < 0)
            spineIndex = FindBoneIndex(mesh.Bones, "g_spine01", "spine01", "spine");

        foreach (int index in new[] { pelvisIndex, spineIndex })
        {
            if (index < 0)
                continue;

            Vector3 current = GetChildDirection(mesh.Bones, mesh.Bones.Select(static bone => bone.GlobalTransform).ToList(), index);
            if (current.LengthSquared() <= 1e-6f)
                continue;

            Vector3 desired = targetBasis.Up;
            Quaternion rotation = FromToRotation(Vector3.Normalize(current), desired);
            if (IsIdentity(rotation))
                continue;

            adjustments.Add(new PoseAdjustment(mesh.Bones[index].Name, "Detected relaxed spine tilt and aligned it toward the target up axis.", DegreesFromQuaternion(rotation), Vector3.Normalize(Vector3.Cross(current, desired))));
            spineCount++;
        }

        if (spineCount == 0)
        {
            Vector3 shoulderLine = sourceBasis.Right;
            Vector3 targetLine = targetBasis.Right;
            Quaternion rotation = FromToRotation(shoulderLine, targetLine);
            if (!IsIdentity(rotation))
                adjustments.Add(new PoseAdjustment("spine", "Projected spine/shoulder frame toward the target basis.", DegreesFromQuaternion(rotation), Vector3.Normalize(Vector3.Cross(shoulderLine, targetLine))));
        }
    }

    private static void AddArmAdjustments(RetargetMesh mesh, PoseBasis sourceBasis, PoseBasis targetBasis, List<PoseAdjustment> adjustments)
    {
        int left = FindBoneIndexByRegion(mesh.Bones, BoneRegion.Shoulder, BoneSide.Left);
        if (left < 0)
            left = FindBoneIndex(mesh.Bones, "g_l_shoulder", "leftshoulder", "g_l_clavical", "l_clavical");
        int right = FindBoneIndexByRegion(mesh.Bones, BoneRegion.Shoulder, BoneSide.Right);
        if (right < 0)
            right = FindBoneIndex(mesh.Bones, "g_r_shoulder", "rightshoulder", "g_r_clavical", "r_clavical");
        if (left >= 0)
        {
            Vector3 current = GetChildDirection(mesh.Bones, mesh.Bones.Select(static bone => bone.GlobalTransform).ToList(), left);
            Vector3 desired = Vector3.Normalize(-targetBasis.Right - (targetBasis.Up * 0.18f));
            Quaternion rotation = FromToRotation(current, desired);
            if (!IsIdentity(rotation))
                adjustments.Add(new PoseAdjustment(mesh.Bones[left].Name, "Aligned left arm away from the relaxed bind pose and toward the target arm line.", DegreesFromQuaternion(rotation), Vector3.Normalize(Vector3.Cross(current, desired))));
        }

        if (right >= 0)
        {
            Vector3 current = GetChildDirection(mesh.Bones, mesh.Bones.Select(static bone => bone.GlobalTransform).ToList(), right);
            Vector3 desired = Vector3.Normalize(targetBasis.Right - (targetBasis.Up * 0.18f));
            Quaternion rotation = FromToRotation(current, desired);
            if (!IsIdentity(rotation))
                adjustments.Add(new PoseAdjustment(mesh.Bones[right].Name, "Aligned right arm away from the relaxed bind pose and toward the target arm line.", DegreesFromQuaternion(rotation), Vector3.Normalize(Vector3.Cross(current, desired))));
        }
    }

    private static void RotateDescendants(IReadOnlyList<RetargetBone> bones, List<Matrix4x4> globals, int boneIndex, Quaternion rotation)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count || IsIdentity(rotation))
            return;

        Vector3 pivot = globals[boneIndex].Translation;
        for (int i = 0; i < bones.Count; i++)
        {
            if (!IsDescendantOf(bones, i, boneIndex))
                continue;

            globals[i] = RotateGlobalTransform(globals[i], pivot, rotation);
        }
    }

    private static Matrix4x4 RotateGlobalTransform(Matrix4x4 current, Vector3 pivot, Quaternion rotation)
    {
        Matrix4x4.Decompose(current, out Vector3 scale, out Quaternion currentRotation, out Vector3 translation);
        Vector3 rotatedOffset = Vector3.Transform(translation - pivot, rotation);
        Quaternion combinedRotation = Quaternion.Normalize(rotation * currentRotation);
        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(combinedRotation) *
               Matrix4x4.CreateTranslation(pivot + rotatedOffset);
    }

    private static bool IsDescendantOf(IReadOnlyList<RetargetBone> bones, int index, int ancestorIndex)
    {
        int current = index;
        while (current >= 0 && current < bones.Count)
        {
            if (current == ancestorIndex)
                return true;
            current = bones[current].ParentIndex;
        }

        return false;
    }

    private static Vector3 GetChildDirection(IReadOnlyList<RetargetBone> bones, List<Matrix4x4> globals, int boneIndex)
    {
        int childIndex = FindPrimaryChildIndex(bones, boneIndex);
        if (childIndex < 0 || childIndex >= globals.Count)
            return Vector3.Zero;

        return globals[childIndex].Translation - globals[boneIndex].Translation;
    }

    private static int FindPrimaryChildIndex(IReadOnlyList<RetargetBone> bones, int parentIndex)
    {
        for (int i = 0; i < bones.Count; i++)
        {
            if (bones[i].ParentIndex == parentIndex)
                return i;
        }

        return -1;
    }

    private static int FindBoneIndex(IReadOnlyList<RetargetBone> bones, params string[] aliases)
    {
        for (int i = 0; i < bones.Count; i++)
        {
            string normalizedBone = BoneNameHeuristics.Normalize(bones[i].Name);
            foreach (string alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                    continue;

                string normalizedAlias = BoneNameHeuristics.Normalize(alias);
                if (normalizedBone.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                    normalizedAlias.Contains(normalizedBone, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindBoneIndexByRegion(IReadOnlyList<RetargetBone> bones, BoneRegion region, BoneSide side = BoneSide.Unknown)
    {
        for (int i = 0; i < bones.Count; i++)
        {
            if (BoneNameHeuristics.IsHelperBone(bones[i].Name))
                continue;

            BoneRegion boneRegion = BoneNameHeuristics.GetRegion(bones[i].Name);
            if (boneRegion != region)
                continue;

            if (side != BoneSide.Unknown)
            {
                BoneSide boneSide = BoneNameHeuristics.GetSide(bones[i].Name);
                if (boneSide != side)
                    continue;
            }

            return i;
        }

        return -1;
    }

    private static PoseBasis BuildBasis(IReadOnlyList<RetargetBone> bones)
    {
        int pelvisIndex = FindBoneIndexByRegion(bones, BoneRegion.Pelvis);
        if (pelvisIndex < 0)
            pelvisIndex = FindBoneIndex(bones, "g_pelvis", "pelvis", "hips");
        int headIndex = FindBoneIndex(bones, "g_head", "head");
        int leftShoulderIndex = FindBoneIndexByRegion(bones, BoneRegion.Shoulder, BoneSide.Left);
        if (leftShoulderIndex < 0)
            leftShoulderIndex = FindBoneIndex(bones, "g_l_shoulder", "leftshoulder", "g_l_clavical", "l_clavical");
        int rightShoulderIndex = FindBoneIndexByRegion(bones, BoneRegion.Shoulder, BoneSide.Right);
        if (rightShoulderIndex < 0)
            rightShoulderIndex = FindBoneIndex(bones, "g_r_shoulder", "rightshoulder", "g_r_clavical", "r_clavical");

        Vector3 pelvis = pelvisIndex >= 0 ? bones[pelvisIndex].GlobalTransform.Translation : Vector3.Zero;
        Vector3 head = headIndex >= 0 ? bones[headIndex].GlobalTransform.Translation : Vector3.UnitY;
        Vector3 leftShoulder = leftShoulderIndex >= 0 ? bones[leftShoulderIndex].GlobalTransform.Translation : -Vector3.UnitX;
        Vector3 rightShoulder = rightShoulderIndex >= 0 ? bones[rightShoulderIndex].GlobalTransform.Translation : Vector3.UnitX;

        Vector3 up = NormalizeOrFallback(head - pelvis, Vector3.UnitY);
        Vector3 right = NormalizeOrFallback(rightShoulder - leftShoulder, Vector3.UnitX);
        Vector3 forward = NormalizeOrFallback(Vector3.Cross(right, up), Vector3.UnitZ);
        right = NormalizeOrFallback(Vector3.Cross(up, forward), Vector3.UnitX);

        return new PoseBasis(up, right, forward);
    }

    private static bool IsIdentity(Quaternion rotation)
    {
        return MathF.Abs(rotation.X) < 1e-5f &&
               MathF.Abs(rotation.Y) < 1e-5f &&
               MathF.Abs(rotation.Z) < 1e-5f &&
               MathF.Abs(rotation.W - 1.0f) < 1e-5f;
    }

    private static float DegreesFromQuaternion(Quaternion rotation)
    {
        float clamped = Math.Clamp(rotation.W, -1.0f, 1.0f);
        float degrees = 2.0f * MathF.Acos(clamped) * 180.0f / MathF.PI;
        return degrees > 180.0f ? 360.0f - degrees : degrees;
    }

    private static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() <= 1e-6f || to.LengthSquared() <= 1e-6f)
            return Quaternion.Identity;

        Vector3 source = Vector3.Normalize(from);
        Vector3 target = Vector3.Normalize(to);
        float dot = Math.Clamp(Vector3.Dot(source, target), -1.0f, 1.0f);

        if (dot > 0.9999f)
            return Quaternion.Identity;

        if (dot < -0.9999f)
        {
            Vector3 axis = MathF.Abs(source.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
            axis = Vector3.Normalize(Vector3.Cross(source, axis));
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }

        Vector3 cross = Vector3.Cross(source, target);
        float s = MathF.Sqrt((1.0f + dot) * 2.0f);
        float invS = 1.0f / s;
        return Quaternion.Normalize(new Quaternion(cross * invS, s * 0.5f));
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : fallback;
    }

    private readonly record struct PoseBasis(Vector3 Up, Vector3 Right, Vector3 Forward);
}

