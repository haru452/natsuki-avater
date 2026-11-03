using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

/// <summary>Entry point: registers the parameter and menu, then builds the flames and the FX layer.</summary>
public static class FireSetup
{
    const string BackupRoot = "Assets/natsuki_veer02/FireBackup";

    public static void Run(GameObject avatarRoot, AnimatorController fx, VRCExpressionParameters parameters,
        VRCExpressionsMenu menu, string outputDir, string backupDir)
    {
        // Fail on an unusable avatar before anything is registered or written.
        FireObjectBuilder.Preflight(avatarRoot);

        // Back up first: everything below rewrites existing assets.
        AssetDatabase.SaveAssets();
        Backup(fx, backupDir);
        Backup(parameters, backupDir);
        Backup(menu, backupDir);

        // Registration validates before it writes, so a full parameter list or menu aborts here
        // before any flames or layers exist.
        FireMenuRegistrar.Register(parameters, menu);

        var sheet = FireSpriteGenerator.Generate(outputDir);
        var ember = FireSpriteGenerator.GenerateEmber(outputDir);
        FireObjectBuilder.Build(avatarRoot, sheet, ember, outputDir);
        FireAnimationBuilder.Apply(fx, avatarRoot, outputDir);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// The selected prefab asset, or the source prefab of a selected scene instance (or any child of one).
    /// Otherwise the prefab of the only avatar in the open scenes. Null when that is ambiguous or absent.
    /// </summary>
    public static string ResolvePrefabPath(UnityEngine.Object selected)
    {
        var go = selected as GameObject;
        if (go != null)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(go)) return AssetDatabase.GetAssetPath(go);
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!string.IsNullOrEmpty(source)) return source;
        }

        var scenePrefabs = UnityEngine.Object
            .FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(d => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(d.gameObject))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();
        return scenePrefabs.Count == 1 ? scenePrefabs[0] : null;
    }

    /// <summary>Clips hold avatar-specific bone paths, so each avatar gets its own output folder.</summary>
    public static string OutputDirFor(string prefabPath)
    {
        return FireConfig.OutputDir + "/" + Path.GetFileNameWithoutExtension(prefabPath);
    }

    [MenuItem("Tools/Fire Gimmick/Setup")]
    public static void Setup()
    {
        string backupDir = BackupRoot + "/" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        try
        {
            string prefabPath = ResolvePrefabPath(Selection.activeObject);
            if (prefabPath == null)
                throw new InvalidOperationException("[Fire] 対象のアバターが決められませんでした。Hierarchy でアバター（プレハブのインスタンス）を選んでから、もう一度実行してください。");
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Fire Gimmick",
                    "この対象に炎を追加します:\n" + prefabPath, "実行", "キャンセル"))
                return;
            SetupPrefab(prefabPath, OutputDirFor(prefabPath), backupDir);
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            if (Application.isBatchMode) throw;
            EditorUtility.DisplayDialog("Fire Gimmick", e.Message, "OK");
        }
    }

    public static void SetupPrefab(string prefabPath, string outputDir, string backupDir)
    {
        // The avatar assets are not under version control, so the prefab gets a copy too.
        BackupPath(prefabPath, backupDir);

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) throw new InvalidOperationException("[Fire] VRCAvatarDescriptor not found on " + prefabPath);

            var fxLayer = descriptor.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            var fx = fxLayer.animatorController as AnimatorController;
            if (fx == null) throw new InvalidOperationException("[Fire] The FX layer has no custom AnimatorController");
            if (descriptor.expressionParameters == null || descriptor.expressionsMenu == null)
                throw new InvalidOperationException("[Fire] Expression parameters / menu are not set on the avatar descriptor");

            Run(root, fx, descriptor.expressionParameters, descriptor.expressionsMenu, outputDir, backupDir);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[Fire] Setup done: " + FireObjectBuilder.FindAll(root).Count + " flame sites. Backups in " + backupDir + ", assets in " + outputDir);
        }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Backup(UnityEngine.Object asset, string backupDir)
    {
        BackupPath(AssetDatabase.GetAssetPath(asset), backupDir);
    }

    static void BackupPath(string path, string backupDir)
    {
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(backupDir);
        string dest = Path.Combine(backupDir, Path.GetFileName(path)).Replace("\\", "/");
        File.Copy(path, dest, true);
        AssetDatabase.ImportAsset(dest);
    }
}
