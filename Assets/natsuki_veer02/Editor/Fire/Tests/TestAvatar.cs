using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds a minimal valid Humanoid avatar hierarchy for editor tests.</summary>
public static class TestAvatar
{
    public static GameObject Create(bool includeChest = true, bool includeMeshes = true)
    {
        var root = new GameObject("TestAvatar");
        var map = new Dictionary<HumanBodyBones, Transform>();
        var skeleton = new List<SkeletonBone>();

        Transform Add(string name, Transform parent, Vector3 localPos, HumanBodyBones bone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            map[bone] = go.transform;
            return go.transform;
        }

        var hips = Add("Hips", root.transform, new Vector3(0, 1f, 0), HumanBodyBones.Hips);
        var spine = Add("Spine", hips, new Vector3(0, 0.15f, 0), HumanBodyBones.Spine);
        Transform top = spine;
        if (includeChest) top = Add("Chest", spine, new Vector3(0, 0.15f, 0), HumanBodyBones.Chest);
        var neck = Add("Neck", top, new Vector3(0, 0.2f, 0), HumanBodyBones.Neck);
        Add("Head", neck, new Vector3(0, 0.1f, 0), HumanBodyBones.Head);

        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float s = left ? 1f : -1f;
            string n = left ? "Left" : "Right";
            var ua = Add(n + "UpperArm", top, new Vector3(0.15f * s, 0.1f, 0), left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var la = Add(n + "LowerArm", ua, new Vector3(0.25f * s, 0, 0), left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Add(n + "Hand", la, new Vector3(0.25f * s, 0, 0), left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var ul = Add(n + "UpperLeg", hips, new Vector3(0.1f * s, -0.05f, 0), left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var ll = Add(n + "LowerLeg", ul, new Vector3(0, -0.4f, 0), left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Add(n + "Foot", ll, new Vector3(0, -0.4f, 0), left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        }

        skeleton.Add(new SkeletonBone { name = root.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one });
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            if (t == root.transform) continue;
            skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
        }

        var human = new List<HumanBone>();
        foreach (var kv in map)
        {
            human.Add(new HumanBone
            {
                humanName = HumanTrait.BoneName[(int)kv.Key],
                boneName = kv.Value.name,
                limit = new HumanLimit { useDefaultValues = true },
            });
        }

        var desc = new HumanDescription
        {
            human = human.ToArray(),
            skeleton = skeleton.ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
        Debug.Assert(avatar.isValid && avatar.isHuman, "TestAvatar: humanoid avatar failed to build");
        root.AddComponent<Animator>().avatar = avatar;

        if (includeMeshes)
        {
            foreach (var name in new[] { "Body", "Cloth", "Hair", "Face" })
                AddSkinnedQuad(root.transform, hips, name);
        }
        return root;
    }

    /// <summary>A one-quad skinned mesh bound to a single bone; enough for mesh-shape emission settings.</summary>
    static SkinnedMeshRenderer AddSkinnedQuad(Transform root, Transform bone, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var mesh = new Mesh
        {
            name = name,
            vertices = new[] { new Vector3(-0.2f, 0.8f, 0), new Vector3(0.2f, 0.8f, 0), new Vector3(-0.2f, 1.4f, 0), new Vector3(0.2f, 1.4f, 0) },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        var w = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = new[] { w, w, w, w };
        mesh.bindposes = new[] { bone.worldToLocalMatrix * root.localToWorldMatrix };
        mesh.RecalculateNormals();
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = new[] { bone };
        smr.rootBone = bone;
        return smr;
    }
}
