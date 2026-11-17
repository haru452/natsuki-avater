using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using C = TranslatorConfig;
using Object = UnityEngine.Object;

/// <summary>Menu entries: build the prefab, and drop it onto an avatar in the scene.</summary>
public static class TranslatorSetup
{
    const string Title = "Translator Pet";

    public static string PrefabPath => C.OutputDir + "/" + C.PrefabName;

    [MenuItem("Tools/Translator Pet/Build Prefab")]
    public static void BuildPrefab() => Guard(() =>
    {
        Preflight();
        var prefab = TranslatorPrefabBuilder.Build(C.OutputDir, petModel: TranslatorPetModel.Configured());
        EditorGUIUtility.PingObject(prefab);
        Debug.Log("[Translator] プレハブを作りました: " + PrefabPath);
    });

    [MenuItem("Tools/Translator Pet/Add To Avatar")]
    public static void AddToAvatar() => Guard(() =>
    {
        Preflight();
        var avatar = ResolveAvatar(Selection.activeObject)
            ?? throw new InvalidOperationException("[Translator] 対象のアバターが決められませんでした。Hierarchy でアバターを選んでから、もう一度実行してください。");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? TranslatorPrefabBuilder.Build(C.OutputDir, petModel: TranslatorPetModel.Configured());
        var placed = Place(avatar.gameObject, prefab);
        Selection.activeGameObject = placed;
        Debug.Log("[Translator] " + avatar.name + " に置きました。");
    });

    /// <summary>Throws with what to install when Modular Avatar or KAT is missing.</summary>
    public static void Preflight()
    {
        if (TranslatorAnimationBuilder.FindKatType("KillFrenzy.AvatarTextTools.Utility.KatAnimatorInstaller") == null
            || Resources.Load<Material>(C.KatTextMaterialResource) == null)
            throw new InvalidOperationException("[Translator] KAT（KillFrenzy Avatar Text）が見つかりません。KAT v1.3.2 の unitypackage をインポートしてください。");
        if (Type.GetType("nadena.dev.modular_avatar.core.ModularAvatarParameters, nadena.dev.modular-avatar.core") == null)
            throw new InvalidOperationException("[Translator] Modular Avatar が見つかりません。VCC でこのプロジェクトに Modular Avatar を追加してください。");
    }

    /// <summary>The avatar the selection belongs to, else the only avatar in the open scenes.</summary>
    public static VRCAvatarDescriptor ResolveAvatar(Object selected)
    {
        var go = selected as GameObject;
        if (go != null && go.scene.IsValid())
        {
            var d = go.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (d != null) return d;
        }
        var all = Object.FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return all.Length == 1 ? all[0] : null;
    }

    /// <summary>
    /// Puts one instance of the prefab directly under the avatar root and returns it.
    /// An existing instance is returned as is. Throws, placing nothing, when synced parameters would exceed 256 bits.
    /// </summary>
    public static GameObject Place(GameObject avatarRoot, GameObject prefab, int katSyncCount = C.KatSyncCount)
    {
        var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
        if (descriptor == null) // Unity's == null, not ??: a missing component is a fake-null object
            throw new InvalidOperationException("[Translator] " + avatarRoot.name + " は VRChat のアバターではありません（VRC Avatar Descriptor がありません）。");

        var existing = avatarRoot.transform.Cast<Transform>()
            .Select(t => t.gameObject)
            .FirstOrDefault(g => PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab);
        if (existing != null)
        {
            TranslatorPetModel.FitTo(existing, descriptor);
            return existing;
        }

        RefuseExistingKat(avatarRoot, descriptor);
        int used = TranslatorBudget.UsedBits(descriptor.expressionParameters);
        int total = used + TranslatorBudget.AddedBits(descriptor.expressionParameters, katSyncCount);
        if (total > TranslatorBudget.Limit)
            throw new InvalidOperationException(
                $"[Translator] 同期パラメータが {total} / {TranslatorBudget.Limit} ビットになり、上限を超えます（今 {used} ビット使用中）。"
                + "TranslatorConfig.KatSyncCount を小さくして同期設定を下げるか、ほかのパラメータを減らしてください。");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatarRoot.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        TranslatorPetModel.FitTo(instance, descriptor);
        Undo.RegisterCreatedObjectUndo(instance, "Add Translator Pet");
        if (avatarRoot.scene.IsValid()) EditorSceneManager.MarkSceneDirty(avatarRoot.scene);
        return instance;
    }

    /// <summary>
    /// An avatar with its own KAT would share KAT_Pointer and KAT_CharSync* with ours and garble both,
    /// and our Off state would hide its text. Refuse rather than merge two KATs.
    /// </summary>
    static void RefuseExistingKat(GameObject avatarRoot, VRCAvatarDescriptor descriptor)
    {
        bool hasParams = descriptor.expressionParameters != null && descriptor.expressionParameters.parameters != null
            && descriptor.expressionParameters.parameters.Any(p => p != null && p.name != null && p.name.StartsWith("KAT_"));
        bool hasObject = avatarRoot.GetComponentsInChildren<Transform>(true)
            .Any(t => t.name == C.KatObjectName);
        if (hasParams || hasObject)
            throw new InvalidOperationException(
                "[Translator] このアバターにはすでに KAT が入っています。KAT のインストーラー（Tools → KillFrenzy Avatar Text）で KAT を外してから、もう一度置いてください。");
    }

    static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            var inner = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
            Debug.LogError(inner.Message);
            if (Application.isBatchMode) throw;
            EditorUtility.DisplayDialog(Title, inner.Message, "OK");
        }
    }
}
