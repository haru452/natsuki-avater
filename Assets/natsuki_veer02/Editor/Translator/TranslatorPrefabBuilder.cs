using System.Collections.Generic;
using System.IO;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using C = TranslatorConfig;

/// <summary>
/// Builds TranslatorPet.prefab. Menus, parameters and FX layers are Modular Avatar components,
/// merged at build time, so the avatar's own assets are never edited.
/// </summary>
public static class TranslatorPrefabBuilder
{
    public const string MenuAssetName = "Translator_Menu.asset";
    public const string SubMenuAssetName = "Translator_SubMenu.asset";
    public const string FrontBoardName = "Front";
    public const string BackBoardName = "Back";

    /// <summary>Builds (or rebuilds in place, keeping the asset's GUID) the prefab and returns the asset.</summary>
    public static GameObject Build(string outputDir, int katSyncCount = C.KatSyncCount)
    {
        Directory.CreateDirectory(outputDir);
        var root = new GameObject(C.RootName);
        try
        {
            AddParameters(root, katSyncCount);
            root.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = BuildMenus(outputDir);
            AddMerge(root, TranslatorAnimationBuilder.BuildToggleController(outputDir));

            var target = Child(root.transform, C.PetTargetName, C.PetOffset);
            var pet = Child(root.transform, C.PetRootName, Vector3.zero);
            var follow = Child(pet.transform, C.PetFollowName, C.PetOffset);
            AddFollowConstraints(pet, follow, target.transform);
            AddCatchUpContact(target, follow);
            var hop = Child(follow.transform, C.PetHopName, Vector3.zero);
            BuildBlock(hop.transform, outputDir);
            AddMerge(follow, TranslatorMotionBuilder.BuildHopController(outputDir));
            var text = Child(follow.transform, C.TextName, new Vector3(0, C.BlockSize + C.TextHeightAboveBlock, 0));
            BuildKatText(text.transform);
            AddMerge(text, TranslatorAnimationBuilder.BuildKatController(outputDir, katSyncCount));

            var sign = Child(root.transform, C.SignRootName, new Vector3(0, C.SignFallbackHeight, 0));
            var proxy = sign.AddComponent<ModularAvatarBoneProxy>();
            proxy.boneReference = HumanBodyBones.Head;
            // Keep the avatar-aligned rotation: head bones are rolled on many non-VRoid rigs.
            proxy.attachmentMode = BoneProxyAttachmentMode.AsChildKeepRotation;
            BuildSign(sign.transform, outputDir);

            pet.SetActive(false);
            sign.SetActive(false);
            target.SetActive(false);

            string path = outputDir + "/" + C.PrefabName;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Damped follow, as in VRLabs Follower: the container is frozen in world space, so the pet's own pose is
    /// last frame's. Blending that (weight 1) with the target (FollowWeight) moves it part of the way each frame.
    /// </summary>
    static void AddFollowConstraints(GameObject container, GameObject follow, Transform target)
    {
        var world = container.AddComponent<VRCParentConstraint>();
        world.FreezeToWorld = true;
        world.IsActive = true;
        world.Locked = true;

        var damp = follow.AddComponent<VRCParentConstraint>();
        damp.Sources.Add(new VRCConstraintSource(follow.transform, 1f));
        damp.Sources.Add(new VRCConstraintSource(target, C.FollowWeight));
        damp.IsActive = true;
        damp.Locked = true;
    }

    /// <summary>
    /// Teleport detection: a Constant receiver on Pet_Target is on while the pet's sender is within
    /// CatchUpDistance. When it goes off, the toggle layer snaps the pet over. Only this avatar's own sender counts.
    /// </summary>
    static void AddCatchUpContact(GameObject target, GameObject follow)
    {
        var r = target.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = C.CatchUpDistance;
        r.collisionTags = new List<string> { C.PetContactTag };
        r.allowSelf = true;
        r.allowOthers = false;
        r.localOnly = false;
        r.receiverType = ContactReceiver.ReceiverType.Constant;
        r.parameter = C.NearParam;

        var s = follow.AddComponent<VRCContactSender>();
        s.shapeType = ContactBase.ShapeType.Sphere;
        s.radius = C.PetSenderRadius;
        s.collisionTags = new List<string> { C.PetContactTag };
    }

    /// <summary>The parameters the prefab declares, in the order they appear in MA Parameters.</summary>
    public static List<ParameterConfig> ParameterList(int katSyncCount)
    {
        var list = new List<ParameterConfig>
        {
            Param(C.ActiveParam, ParameterSyncType.Bool, saved: false),
            Param(C.LocalAIParam, ParameterSyncType.Bool, saved: true),
            Param(C.KatVisibleParam, ParameterSyncType.Bool, saved: false),
            Param(C.KatPointerParam, ParameterSyncType.Int, saved: false),
        };
        for (int i = 0; i < katSyncCount; i++)
            list.Add(Param(C.KatCharSyncPrefix + i, ParameterSyncType.Float, saved: false));
        return list;
    }

    static ParameterConfig Param(string name, ParameterSyncType type, bool saved) => new ParameterConfig
    {
        nameOrPrefix = name,
        syncType = type,
        saved = saved,
        defaultValue = 0f,
        hasExplicitDefaultValue = true,
    };

    static void AddParameters(GameObject root, int katSyncCount) =>
        root.AddComponent<ModularAvatarParameters>().parameters = ParameterList(katSyncCount);

    static void AddMerge(GameObject go, RuntimeAnimatorController controller)
    {
        var merge = go.AddComponent<ModularAvatarMergeAnimator>();
        merge.animator = controller;
        merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
        merge.pathMode = MergeAnimatorPathMode.Relative;
        merge.matchAvatarWriteDefaults = false;
    }

    /// <summary>A menu holding one submenu, "翻訳", with the on/off toggle and the AI switch.</summary>
    static VRCExpressionsMenu BuildMenus(string outputDir)
    {
        var sub = SaveMenu(outputDir + "/" + SubMenuAssetName, new List<VRCExpressionsMenu.Control>
        {
            Toggle(C.ActiveLabel, C.ActiveParam),
            Toggle(C.LocalAILabel, C.LocalAIParam),
        });
        return SaveMenu(outputDir + "/" + MenuAssetName, new List<VRCExpressionsMenu.Control>
        {
            new VRCExpressionsMenu.Control
            {
                name = C.MenuLabel,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = sub,
            },
        });
    }

    static VRCExpressionsMenu.Control Toggle(string label, string param) => new VRCExpressionsMenu.Control
    {
        name = label,
        type = VRCExpressionsMenu.Control.ControlType.Toggle,
        parameter = new VRCExpressionsMenu.Control.Parameter { name = param },
        value = 1f,
    };

    static VRCExpressionsMenu SaveMenu(string path, List<VRCExpressionsMenu.Control> controls)
    {
        var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
        if (menu == null)
        {
            menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = controls;
            AssetDatabase.CreateAsset(menu, path);
        }
        menu.controls = controls;
        EditorUtility.SetDirty(menu);
        // Write now: later steps delete and import other assets in this folder before the final save.
        AssetDatabase.SaveAssetIfDirty(menu);
        return menu;
    }

    static void BuildBlock(Transform pet, string outputDir)
    {
        var block = Primitive(PrimitiveType.Cube, C.BlockName, pet);
        block.transform.localPosition = new Vector3(0, C.BlockSize / 2, 0);
        block.transform.localScale = Vector3.one * C.BlockSize;
        block.GetComponent<MeshRenderer>().sharedMaterial = TranslatorAssetGenerator.BlockMaterial(outputDir);

        // Avatars face +Z; a Quad shows its face toward -Z, so turn it around to face the viewer.
        var face = Primitive(PrimitiveType.Quad, C.FaceName, pet);
        face.transform.localPosition = new Vector3(0, C.BlockSize / 2, C.BlockSize / 2 + 0.002f);
        face.transform.localRotation = Quaternion.Euler(0, 180, 0);
        face.transform.localScale = Vector3.one * C.BlockSize * 0.9f;
        face.GetComponent<MeshRenderer>().sharedMaterial = TranslatorAssetGenerator.FaceMaterial(outputDir);
    }

    /// <summary>The same hierarchy KAT's installer makes, so KAT's clips find "KillFrenzyAvatarText/Constraint/Text".</summary>
    static void BuildKatText(Transform parent)
    {
        var kat = Child(parent, C.KatObjectName, Vector3.zero);
        var constraint = Child(kat.transform, C.KatConstraintName, Vector3.zero);
        var quad = Primitive(PrimitiveType.Quad, C.KatTextName, constraint.transform);
        quad.transform.localScale = new Vector3(C.TextSize.x, C.TextSize.y, 1f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>(C.KatTextMaterialResource);
        kat.SetActive(false);
    }

    /// <summary>Two boards back to back so the sign reads correctly from the front and from behind.</summary>
    static void BuildSign(Transform signRoot, string outputDir)
    {
        var mat = TranslatorAssetGenerator.SignMaterial(outputDir);
        float height = C.SignWidth * TranslatorAssetGenerator.SignAspect(mat.mainTexture);
        var board = Child(signRoot, C.SignBoardName, new Vector3(0, C.SignHeightAboveHeadBone, 0));
        foreach (var (name, yaw) in new[] { (FrontBoardName, 180f), (BackBoardName, 0f) })
        {
            var quad = Primitive(PrimitiveType.Quad, name, board.transform);
            quad.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            quad.transform.localScale = new Vector3(C.SignWidth, height, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    static GameObject Child(Transform parent, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go;
    }

    static GameObject Primitive(PrimitiveType type, string name, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        return go;
    }
}
