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
                     "Pet_Root/Pet_Heading/Pet_Target", "Pet_Gaze/GazeRay",
                     "Pet_Root/Pet_Follow/Pet_Hearts", "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/PatZone",
                     "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/Head", "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/Visor",
                     "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/EyeL", "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/EyeR",
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
        Assert.IsFalse(Find(C.GazeName).gameObject.activeSelf, "its contact is off while the toggle is off");
    }

    [Test]
    public void Prefab_RobotSizeAndPlacement()
    {
        Assert.AreEqual(new Vector3(0.5f, 0f, 0f), Find("Pet_Root/Pet_Heading/Pet_Target").localPosition, "waits at the right");
        Assert.AreEqual(Vector3.zero, Find(C.PetRootName).localPosition);
        Assert.AreEqual(new Vector3(0.5f, 0f, 0f), Find("Pet_Root/Pet_Follow").localPosition, "starts where it waits");
        Assert.AreEqual(Vector3.zero, Find("Pet_Root/Pet_Follow/Pet_Hop").localPosition, "hops from the ground");

        var robot = Find("Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot");
        var bounds = new Bounds(robot.position, Vector3.zero);
        foreach (var r in robot.GetComponentsInChildren<MeshRenderer>(true)) bounds.Encapsulate(MeshBounds(r));
        Assert.AreEqual(0f, bounds.min.y - robot.position.y, 0.005f, "feet on the ground");
        Assert.AreEqual(C.RobotHeight, bounds.max.y - robot.position.y, 0.005f, "top of the head");
        Assert.AreEqual(C.RobotHeight + 0.12f, Find("Pet_Root/Pet_Follow/Pet_Text").localPosition.y, 1e-5f,
            "text floats above the head");
    }

    [Test]
    public void Prefab_RobotLooksForward()
    {
        const string robot = "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/";
        var head = Find(robot + "Head");
        foreach (var eye in new[] { "EyeL", "EyeR" })
        {
            var t = Find(robot + eye);
            Assert.Greater(t.localPosition.z, head.localPosition.z + 0.1f, eye + " sits on the face");
            Assert.Greater(Vector3.Dot(-t.forward, Vector3.forward), 0.99f, eye + " shows toward the front");
        }
        var l = Find(robot + "EyeL").localPosition;
        var r = Find(robot + "EyeR").localPosition;
        Assert.AreEqual(new Vector3(-r.x, r.y, r.z), l, "eyes are mirrored");
    }

    /// <summary>World bounds from mesh and transform; renderer bounds are empty on prefab assets.</summary>
    static Bounds MeshBounds(MeshRenderer r)
    {
        var b = r.GetComponent<MeshFilter>().sharedMesh.bounds;
        var world = new Bounds(r.transform.TransformPoint(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            world.Encapsulate(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return world;
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
        Assert.AreEqual(Find("Pet_Root/Pet_Heading/Pet_Target"), c.Sources[1].SourceTransform);
        Assert.AreEqual(C.FollowWeight, c.Sources[1].Weight);
        Assert.AreEqual(2, prefab.GetComponentsInChildren<VRCParentConstraint>(true).Length, "two constraints in total");
    }

    [Test]
    public void Prefab_ContactNoticesWhenThePetIsLeftBehind()
    {
        var r = Find("Pet_Root/Pet_Heading/Pet_Target").GetComponent<VRCContactReceiver>();
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
    public void Prefab_HeadingFollowsPositionAndTurnsOnlyWhenAllowed()
    {
        var heading = Find("Pet_Root/Pet_Heading");
        var pos = heading.GetComponent<VRCPositionConstraint>();
        Assert.AreEqual(prefab.transform, pos.Sources.Single().SourceTransform);
        Assert.AreEqual(1f, pos.Sources[0].Weight);
        var rot = heading.GetComponent<VRCRotationConstraint>();
        Assert.AreEqual(2, rot.Sources.Count);
        Assert.AreEqual((heading, 1f), (rot.Sources[0].SourceTransform, rot.Sources[0].Weight), "holds its own direction");
        Assert.AreEqual((prefab.transform, 0f), (rot.Sources[1].SourceTransform, rot.Sources[1].Weight), "the motion layer raises this while walking");
        Assert.IsFalse(rot.AffectsRotationX || rot.AffectsRotationZ, "stays upright");
        Assert.IsTrue(pos.IsActive && pos.Locked && rot.IsActive && rot.Locked);
    }

    [Test]
    public void Prefab_GazeReachesForwardFromTheHead()
    {
        var gaze = Find(C.GazeName);
        var proxy = gaze.GetComponent<ModularAvatarBoneProxy>();
        Assert.AreEqual(HumanBodyBones.Head, proxy.boneReference);
        Assert.AreEqual(BoneProxyAttachmentMode.AsChildKeepRotation, proxy.attachmentMode);
        var ray = Find("Pet_Gaze/GazeRay");
        var s = ray.GetComponent<VRCContactSender>();
        Assert.AreEqual(ContactBase.ShapeType.Capsule, s.shapeType);
        CollectionAssert.AreEqual(new[] { C.GazeTag }, s.collisionTags);
        Assert.Greater(Vector3.Dot(ray.up, Vector3.forward), 0.99f, "capsule axis points forward");
        Assert.Greater(ray.localPosition.z, 0.5f, "starts in front of the face");
    }

    [Test]
    public void Prefab_RobotFacesTheWearerWhenLookedAt()
    {
        var robot = Find("Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot");
        var aim = robot.GetComponent<VRCAimConstraint>();
        Assert.AreEqual(Find(C.GazeName), aim.Sources.Single().SourceTransform);
        Assert.AreEqual(Vector3.forward, aim.AimAxis);
        Assert.IsTrue(aim.AffectsRotationY);
        Assert.IsFalse(aim.AffectsRotationX || aim.AffectsRotationZ, "turns its body, does not tip over");
        Assert.AreEqual(0f, aim.GlobalWeight, "faces ahead until looked at");

        var look = robot.Find("LookZone").GetComponent<VRCContactReceiver>();
        CollectionAssert.AreEqual(new[] { C.GazeTag }, look.collisionTags);
        Assert.AreEqual(C.LookedParam, look.parameter);
        Assert.IsTrue(look.allowSelf);
        Assert.IsFalse(look.allowOthers, "only the wearer's own gaze");
    }

    [Test]
    public void Prefab_AnyonesHandOnTheHeadMakesHearts()
    {
        var pat = Find("Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot/PatZone").GetComponent<VRCContactReceiver>();
        CollectionAssert.AreEquivalent(new[] { "Hand", "Finger" }, pat.collisionTags);
        Assert.IsTrue(pat.allowSelf && pat.allowOthers, "the wearer and other players");
        Assert.AreEqual(C.PatParam, pat.parameter);
        Assert.AreEqual(ContactReceiver.ReceiverType.Constant, pat.receiverType);
        Assert.Greater(pat.transform.localPosition.y, 0.2f, "on the head");

        var hearts = Find("Pet_Root/Pet_Follow/Pet_Hearts").GetComponent<ParticleSystem>();
        Assert.IsFalse(hearts.emission.enabled, "quiet until petted");
        Assert.LessOrEqual(hearts.main.maxParticles, 20);
        Assert.IsTrue(AnimationUtility.GetAnimatableBindings(hearts.gameObject, prefab)
            .Any(b => b.type == typeof(ParticleSystem) && b.propertyName == "EmissionModule.enabled"),
            "the motion layer can switch emission");
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
        var motion = merges.Single(m => m.animator.name == "Translator_Motion");
        Assert.AreEqual(Find(C.PetRootName).gameObject, motion.gameObject, "motion clips are relative to Pet_Root");
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
