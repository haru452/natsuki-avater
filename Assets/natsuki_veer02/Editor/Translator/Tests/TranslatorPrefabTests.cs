using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;
using C = TranslatorConfig;

public class TranslatorPrefabTests
{
    const string Dir = TranslatorAssetTests.Dir;
    GameObject prefab;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        prefab = TranslatorPrefabBuilder.Build(Dir);
    }

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Dir);

    Transform Find(string path) => prefab.transform.Find(path);

    [Test]
    public void Prefab_HasExpectedHierarchy()
    {
        foreach (var path in new[]
                 {
                     "Pet_Target",
                     "Pet_Root/Pet_Follow/Pet_Hop/Pet_Block", "Pet_Root/Pet_Follow/Pet_Hop/Pet_Face",
                     "Pet_Root/Pet_Follow/Pet_Text/KillFrenzyAvatarText/Constraint/Text",
                     "Sign_Root/Sign_Board/Front", "Sign_Root/Sign_Board/Back",
                 })
            Assert.IsNotNull(Find(path), path);
        Assert.AreEqual(C.RootName, prefab.name);
    }

    [Test]
    public void Prefab_RootsStartInactive()
    {
        Assert.IsTrue(prefab.activeSelf);
        Assert.IsFalse(Find(C.PetRootName).gameObject.activeSelf);
        Assert.IsFalse(Find(C.SignRootName).gameObject.activeSelf);
        Assert.IsFalse(Find(C.PetTargetName).gameObject.activeSelf, "its contact is off while the toggle is off");
    }

    [Test]
    public void Prefab_BlockSizeAndPlacement()
    {
        Assert.AreEqual(new Vector3(0.5f, 0f, 0f), Find(C.PetTargetName).localPosition, "waits at the right");
        Assert.AreEqual(Vector3.zero, Find(C.PetRootName).localPosition);
        Assert.AreEqual(new Vector3(0.5f, 0f, 0f), Find("Pet_Root/Pet_Follow").localPosition, "starts where it waits");
        Assert.AreEqual(Vector3.zero, Find("Pet_Root/Pet_Follow/Pet_Hop").localPosition, "hops from the ground");
        var block = Find("Pet_Root/Pet_Follow/Pet_Hop/Pet_Block");
        Assert.AreEqual(Vector3.one * 0.25f, block.localScale);
        Assert.AreEqual(0f, block.localPosition.y - block.localScale.y / 2, 1e-5f, "bottom on the ground");
        Assert.AreEqual(0.25f + 0.15f, Find("Pet_Root/Pet_Follow/Pet_Text").localPosition.y, 1e-5f);
    }

    [Test]
    public void Prefab_PetRootIsFrozenToWorld()
    {
        var c = Find(C.PetRootName).GetComponent<VRCParentConstraint>();
        Assert.IsTrue(c.FreezeToWorld);
        Assert.IsTrue(c.IsActive);
        Assert.IsTrue(c.Locked);
    }

    [Test]
    public void Prefab_FollowConstraintBlendsSelfWithTarget()
    {
        var follow = Find("Pet_Root/Pet_Follow");
        var c = follow.GetComponent<VRCParentConstraint>();
        Assert.IsTrue(c.IsActive);
        Assert.IsTrue(c.Locked);
        Assert.IsFalse(c.FreezeToWorld);
        Assert.AreEqual(2, c.Sources.Count);
        Assert.AreEqual(follow, c.Sources[0].SourceTransform, "source 0 is itself: the pose from last frame");
        Assert.AreEqual(1f, c.Sources[0].Weight);
        Assert.AreEqual(Find(C.PetTargetName), c.Sources[1].SourceTransform);
        Assert.AreEqual(C.FollowWeight, c.Sources[1].Weight);
        Assert.AreEqual(2, prefab.GetComponentsInChildren<VRCParentConstraint>(true).Length, "two constraints in total");
    }

    [Test]
    public void Prefab_ContactNoticesWhenThePetIsLeftBehind()
    {
        var r = Find(C.PetTargetName).GetComponent<VRCContactReceiver>();
        Assert.AreEqual(ContactReceiver.ReceiverType.Constant, r.receiverType);
        Assert.AreEqual(C.NearParam, r.parameter);
        Assert.AreEqual(C.CatchUpDistance, r.radius);
        CollectionAssert.AreEqual(new[] { C.PetContactTag }, r.collisionTags);
        Assert.IsTrue(r.allowSelf);
        Assert.IsFalse(r.allowOthers, "other players cannot trigger it");
        Assert.IsFalse(r.localOnly, "every client moves its own copy of the pet");

        var s = Find("Pet_Root/Pet_Follow").GetComponent<VRCContactSender>();
        CollectionAssert.AreEqual(new[] { C.PetContactTag }, s.collisionTags);
        Assert.AreEqual(C.PetSenderRadius, s.radius);
    }

    [Test]
    public void Prefab_HasNoColliders()
    {
        Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true));
    }

    [Test]
    public void Prefab_KatTextUsesKatMaterial()
    {
        var r = Find("Pet_Root/Pet_Follow/Pet_Text/KillFrenzyAvatarText/Constraint/Text").GetComponent<MeshRenderer>();
        Assert.AreEqual(Resources.Load<Material>(C.KatTextMaterialResource), r.sharedMaterial);
        Assert.IsFalse(Find("Pet_Root/Pet_Follow/Pet_Text/KillFrenzyAvatarText").gameObject.activeSelf, "KAT starts hidden");
    }

    [Test]
    public void Prefab_SignHasTwoBoardsBackToBack()
    {
        var front = Find("Sign_Root/Sign_Board/Front");
        var back = Find("Sign_Root/Sign_Board/Back");
        Assert.AreEqual(180f, Quaternion.Angle(front.localRotation, back.localRotation), 0.01f);
        Assert.AreEqual(front.GetComponent<MeshRenderer>().sharedMaterial, back.GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void Prefab_SignUsesHeadBoneProxy()
    {
        var proxy = Find(C.SignRootName).GetComponent<ModularAvatarBoneProxy>();
        Assert.AreEqual(HumanBodyBones.Head, proxy.boneReference);
        Assert.AreEqual(BoneProxyAttachmentMode.AsChildKeepRotation, proxy.attachmentMode);
        Assert.GreaterOrEqual(Find(C.SignRootName).localPosition.y, 1.5f, "fallback height without a head bone");
    }

    [Test]
    public void Prefab_ParametersMatchSpec()
    {
        var ps = prefab.GetComponent<ModularAvatarParameters>().parameters;
        var active = ps.Single(p => p.nameOrPrefix == C.ActiveParam);
        Assert.AreEqual(ParameterSyncType.Bool, active.syncType);
        Assert.IsFalse(active.saved);
        Assert.AreEqual(0f, active.defaultValue);
        var ai = ps.Single(p => p.nameOrPrefix == C.LocalAIParam);
        Assert.AreEqual(ParameterSyncType.Bool, ai.syncType);
        Assert.IsTrue(ai.saved);
        Assert.AreEqual(ParameterSyncType.Bool, ps.Single(p => p.nameOrPrefix == "KAT_Visible").syncType);
        Assert.AreEqual(ParameterSyncType.Int, ps.Single(p => p.nameOrPrefix == "KAT_Pointer").syncType);
        for (int i = 0; i < C.KatSyncCount; i++)
            Assert.AreEqual(ParameterSyncType.Float, ps.Single(p => p.nameOrPrefix == "KAT_CharSync" + i).syncType);
        Assert.IsTrue(ps.All(p => !p.localOnly && !p.internalParameter));
    }

    [Test]
    public void Prefab_MenuHasSubmenuWithTwoToggles()
    {
        var menu = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
        var sub = menu.controls.Single();
        Assert.AreEqual("翻訳", sub.name);
        Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, sub.type);
        var toggles = sub.subMenu.controls;
        Assert.AreEqual(2, toggles.Count);
        Assert.IsTrue(toggles.All(t => t.type == VRCExpressionsMenu.Control.ControlType.Toggle));
        Assert.AreEqual(("翻訳", C.ActiveParam), (toggles[0].name, toggles[0].parameter.name));
        Assert.AreEqual(("ローカルAI", C.LocalAIParam), (toggles[1].name, toggles[1].parameter.name));
    }

    [Test]
    public void Prefab_MergesTwoFxControllers()
    {
        var merges = prefab.GetComponentsInChildren<ModularAvatarMergeAnimator>(true);
        Assert.AreEqual(3, merges.Length);
        Assert.IsTrue(merges.All(m => m.layerType == VRCAvatarDescriptor.AnimLayerType.FX));
        Assert.IsTrue(merges.All(m => m.pathMode == MergeAnimatorPathMode.Relative));
        Assert.IsTrue(merges.All(m => m.animator != null));
        var kat = merges.Single(m => m.animator.name == "Translator_KAT");
        Assert.AreEqual(Find("Pet_Root/Pet_Follow/Pet_Text").gameObject, kat.gameObject, "KAT controller sits where its clip paths start");
        var hop = merges.Single(m => m.animator.name == "Translator_Hop");
        Assert.AreEqual(Find("Pet_Root/Pet_Follow").gameObject, hop.gameObject, "hop clips animate Pet_Hop below it");
    }

    [Test]
    public void Build_IsIdempotent()
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
        int children = prefab.GetComponentsInChildren<Transform>(true).Length;
        int files = Directory.GetFiles(Dir).Length;

        var again = TranslatorPrefabBuilder.Build(Dir);

        Assert.AreEqual(children, again.GetComponentsInChildren<Transform>(true).Length);
        Assert.AreEqual(files, Directory.GetFiles(Dir).Length);
        Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(again)), "scene instances stay linked");
    }

    [TestCase(4, 43)]
    [TestCase(8, 75)]
    [TestCase(16, 139)]
    public void Budget_SyncedBitsMatchesParameters(int sync, int expected)
    {
        int counted = TranslatorPrefabBuilder.ParameterList(sync)
            .Sum(p => p.syncType == ParameterSyncType.Bool ? 1 : 8);
        Assert.AreEqual(expected, counted);
        Assert.AreEqual(expected, TranslatorBudget.SyncedBits(sync));
    }
}
