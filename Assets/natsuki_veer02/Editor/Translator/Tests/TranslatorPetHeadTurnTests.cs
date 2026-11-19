using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using C = TranslatorConfig;
using H = TranslatorPetHeadTurn;
using M = TranslatorMotionBuilder;

/// <summary>
/// Looking at the wearer: her head alone turns while they are not far round; further round, her body turns too.
/// </summary>
public class TranslatorPetHeadTurnTests
{
    const string Dir = TranslatorAssetTests.Dir;
    GameObject prefab;
    AnimatorController c;

    [OneTimeSetUp]
    public void BuildOnce()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(C.PetModelPath);
        if (model == null) Assert.Ignore("pet model not in this project");
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        prefab = TranslatorPrefabBuilder.Build(Dir, petModel: model);
        c = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "/" + M.ControllerName);
    }

    [OneTimeTearDown]
    public void CleanUp() => AssetDatabase.DeleteAsset(Dir);

    Transform Hop => prefab.transform.Find(C.PetRootName + "/" + M.HopPath);

    AnimatorState State(string name) => c.layers.Single(l => l.name == M.LookLayerName).stateMachine.states
        .Select(s => s.state).Single(s => s.name == name);

    [Test]
    public void HowFarRound_IsMeasuredFromWhereSheFaces_WithoutLooking()
    {
        var dir = Hop.Find(H.DirName);
        Assert.IsNotNull(dir, "on Pet_Hop: not turned by her own look (Pet_Robot)");
        var aim = dir.GetComponent<VRCAimConstraint>();
        Assert.AreEqual(prefab.transform.Find(C.GazeName), aim.Sources.Single().SourceTransform, "the wearer's head");
        Assert.IsTrue(aim.AffectsRotationY && !aim.AffectsRotationX && !aim.AffectsRotationZ, "yaw only");
        Assert.IsTrue(aim.IsActive && aim.Locked);

        var probe = dir.Find(H.ProbeName);
        Assert.AreEqual(Vector3.forward, probe.localPosition);
        CollectionAssert.AreEqual(new[] { H.ProbeTag }, probe.GetComponent<VRCContactSender>().collisionTags);

        var zone = Hop.Find(H.ZoneName).GetComponent<VRCContactReceiver>();
        Assert.AreEqual(Vector3.forward, zone.transform.localPosition);
        Assert.AreEqual(ContactReceiver.ReceiverType.Proximity, zone.receiverType);
        Assert.AreEqual(C.InViewParam, zone.parameter);
        CollectionAssert.AreEqual(new[] { H.ProbeTag }, zone.collisionTags);
        Assert.IsTrue(zone.allowSelf && !zone.allowOthers && !zone.localOnly);
    }

    [TestCase(0f, true)]
    [TestCase(45f, true)]
    [TestCase(-79f, true)]
    [TestCase(81f, false)]
    [TestCase(180f, false)]
    public void TheZone_ReachesTheProbe_UpToBodyTurnDegrees(float degrees, bool reached)
    {
        var probe = Quaternion.Euler(0, degrees, 0) * Vector3.forward; // where the probe is, round from her front
        Assert.AreEqual(reached, Vector3.Distance(probe, Vector3.forward) < H.ZoneRadius);
    }

    [Test]
    public void HeadOnly_FromHeadTurnDegrees_BodyToo_FromBodyTurnDegrees()
    {
        Assert.Less(C.HeadTurnDegrees, C.BodyTurnDegrees);
        float Proximity(float degrees) => 1f - H.Chord(degrees) / H.ZoneRadius;
        Assert.AreEqual(H.HeadOnlyFrom, Proximity(C.HeadTurnDegrees), 1e-5f);
        Assert.AreEqual(0f, Proximity(C.BodyTurnDegrees), 1e-5f);
        Assert.Greater(H.HeadOnlyFrom, 0f);
        Assert.Less(H.HeadOnlyFrom, 1f);
    }

    [TestCase("Look")]
    [TestCase("Called")]
    [TestCase("Facing")]
    public void TheLook_BlendsBodyAndHead_ByHowFarRound(string state)
    {
        var tree = State(state).motion as BlendTree;
        Assert.IsNotNull(tree, state);
        Assert.AreEqual(C.InViewParam, tree.blendParameter);
        Assert.AreEqual(new[] { 0f, H.HeadOnlyFrom }, tree.children.Select(k => k.threshold).ToArray());
        Assert.IsTrue(c.parameters.Any(p => p.name == C.InViewParam && p.type == AnimatorControllerParameterType.Float));

        var head = TranslatorPetModel.Head(prefab.transform.Find(C.PetRootName));
        string headPath = AnimationUtility.CalculateTransformPath(head, prefab.transform.Find(C.PetRootName));
        float Weight(int child, string path) => AnimationUtility.GetEditorCurve((AnimationClip)tree.children[child].motion,
            EditorCurveBinding.FloatCurve(path, typeof(VRCAimConstraint), "GlobalWeight")).Evaluate(0f);
        Assert.AreEqual(1f, Weight(0, M.RobotPath), "far round: her body turns to the wearer");
        Assert.AreEqual(0f, Weight(1, M.RobotPath), "not far round: her body stays");
        Assert.AreEqual(C.HeadLookWeight, Weight(0, headPath));
        Assert.AreEqual(C.HeadLookWeight, Weight(1, headPath), "her head turns either way");
        Assert.AreEqual(((AnimationClip)tree.children[0].motion).length, ((AnimationClip)tree.children[1].motion).length, 1e-4f,
            "the same length, so Called still ends on time");
    }
}
