using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using nadena.dev.modular_avatar.core;
using C = TranslatorConfig;
using M = TranslatorMotionBuilder;
using P = TranslatorPetModel;

/// <summary>
/// The pet as a humanoid model (Muryotaisu): walks and idles with its own motions baked to plain bone animation,
/// gets bored (IdleB) after a still, silent minute, jumps when the wearer comes back, smiles with hearts when petted,
/// and is sized to 0.6 of the avatar. Skipped when the model is not in the project (the BOOTH robot build).
/// </summary>
public class TranslatorPetModelTests
{
    const string Dir = TranslatorAssetTests.Dir;
    const string RobotPath = "Pet_Root/Pet_Follow/Pet_Hop/Pet_Robot";
    GameObject model;
    GameObject prefab;
    AnimatorController c;

    [OneTimeSetUp]
    public void BuildOnce()
    {
        model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mryotaisu/Models/Muryotaisu.fbx");
        if (model == null) Assert.Ignore("pet model not in this project");
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        prefab = TranslatorPrefabBuilder.Build(Dir, petModel: model);
        c = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "/" + M.ControllerName);
    }

    [OneTimeTearDown]
    public void CleanUp() => AssetDatabase.DeleteAsset(Dir);

    Transform Find(string path) => prefab.transform.Find(path);
    AnimatorStateMachine Layer(string name) => c.layers.Single(l => l.name == name).stateMachine;
    AnimatorState State(string layer, string name) => Layer(layer).states.Select(s => s.state).Single(s => s.name == name);
    static AnimationClip Clip(AnimatorState s) => (AnimationClip)s.motion;

    static string Transitions(AnimatorState s) => string.Join(" | ", s.transitions.Select(t =>
        t.destinationState.name + ":" + string.Join("&", t.conditions.Select(k => k.parameter + k.mode + k.threshold))
        + (t.hasExitTime ? "@" + t.exitTime.ToString("0.###") : "")));

    [Test]
    public void Prefab_ModelReplacesRobot_WithoutItsAnimator()
    {
        var robot = Find(RobotPath);
        Assert.IsNotNull(robot);
        Assert.Greater(robot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, 0);
        Assert.IsEmpty(robot.GetComponentsInChildren<Animator>(true), "no second Animator inside the avatar");
        Assert.IsNull(robot.Find(TranslatorRobotBuilder.HeadPivotName), "robot parts are not built");
        Assert.IsNull(Find("Pet_Root/Pet_Follow/" + C.ZzzName), "no Zzz: this pet does not sleep");
        Assert.AreEqual("Head", robot.GetComponentsInChildren<Transform>(true).Single(t => t.name == TranslatorReactionsBuilder.PatZoneName).parent.name,
            "petting the head");
    }

    [Test]
    public void Prefab_UsesTheTexturedMaterials_NotTheFbxDefaults()
    {
        foreach (var r in Find(RobotPath).GetComponentsInChildren<SkinnedMeshRenderer>(true))
        foreach (var m in r.sharedMaterials)
        {
            string path = AssetDatabase.GetAssetPath(m);
            StringAssert.EndsWith(".mat", path, r.name + " uses " + path);
            Assert.IsNotNull(m.mainTexture, r.name + " has its colour texture");
        }
    }

    [Test]
    public void Prefab_HeartsSitAboveTheHead()
    {
        float eye = P.EyeHeight(Find(RobotPath));
        Assert.Greater(eye, 0.3f);
        Assert.Greater(Find("Pet_Root/Pet_Follow/" + C.HeartsName).localPosition.y, eye);
    }

    [Test]
    public void Motion_BodyLookPatBlink_NoHopNoSleep()
    {
        CollectionAssert.AreEqual(new[] { P.BodyLayerName, M.LookLayerName, M.PatLayerName, TranslatorPetWalker.BlinkLayerName, P.SideLayerName },
            c.layers.Select(l => l.name));
        Assert.IsTrue(c.layers.SelectMany(l => l.stateMachine.states).All(s => !s.state.writeDefaultValues));
        Assert.AreEqual(AnimatorControllerParameterType.Float, c.parameters.Single(p => p.name == C.VoiceParam).type);
    }

    [Test]
    public void Body_WalksWhenMoving_BoredAfterAQuietMinute_JumpsWhenBack()
    {
        string still = $"{C.VelocityParam}Less{C.SleepStillSpeed}&{C.VoiceParam}Less{C.SleepVoiceLevel}";
        string back = $"{C.VelocityParam}Greater{C.SleepStillSpeed} | {{0}}:{C.VoiceParam}Greater{C.SleepVoiceLevel}";
        Assert.AreEqual("Idle", Layer(P.BodyLayerName).defaultState.name);
        // Only once the wearer is no longer close does it set off (walking up to it never pushes it away).
        Assert.AreEqual($"Walk:{C.CloseParam}IfNot0&{C.VelocityParam}Greater{C.HopStartSpeed} | Approach:{C.CloseParam}IfNot0 | "
                        + $"Approach:{C.ArrivedParam}IfNot0 | Drowsy:{still}",
            Transitions(State(P.BodyLayerName, "Idle")));
        Assert.AreEqual($"Approach:{C.VelocityParam}Less{C.HopStopSpeed}", Transitions(State(P.BodyLayerName, "Walk")));
        // Near the spot it jumps the last bit instead of sliding in.
        Assert.AreEqual($"Walk:{C.VelocityParam}Greater{C.HopStartSpeed} | Land:{C.ArrivedParam}If0",
            Transitions(State(P.BodyLayerName, "Approach")));
        var land = State(P.BodyLayerName, "Land");
        Assert.AreEqual("Idle", land.transitions.Single().destinationState.name);
        Assert.AreEqual(C.LandSeconds, land.transitions.Single().exitTime * Clip(land).length, 0.02f, "a few steps, not a jump");
        Assert.IsTrue(AnimationUtility.GetCurveBindings(Clip(land)).Select(b => b.path).SequenceEqual(
            AnimationUtility.GetCurveBindings(Clip(State(P.BodyLayerName, "Walk"))).Select(b => b.path)), "the walk motion");

        var drowsy = State(P.BodyLayerName, "Drowsy");
        var toBored = drowsy.transitions.Single(t => t.destinationState.name == "Bored");
        Assert.AreEqual(C.SleepAfterSeconds, toBored.exitTime * Clip(drowsy).length, 0.01f, "one minute");
        Assert.AreEqual("Idle:" + string.Format(back, "Idle"),
            string.Join(" | ", drowsy.transitions.Where(t => t != toBored).Select(t =>
                t.destinationState.name + ":" + string.Join("&", t.conditions.Select(k => k.parameter + k.mode + k.threshold)))));
        Assert.AreEqual("Wake:" + string.Format(back, "Wake"), Transitions(State(P.BodyLayerName, "Bored")));
        Assert.AreEqual("Idle:@1", Transitions(State(P.BodyLayerName, "Wake")));
    }

    [Test]
    public void BakedClips_AreBoneRotations_NotMuscles_AndKeepTheBlink()
    {
        var idle = Clip(State(P.BodyLayerName, "Idle"));
        var bindings = AnimationUtility.GetCurveBindings(idle);
        Assert.IsTrue(bindings.Any(b => b.type == typeof(Transform) && b.path.StartsWith(RobotPath.Substring("Pet_Root/".Length) + "/") && b.path.EndsWith("/Hips")
                                        && b.propertyName == "m_LocalRotation.x"), "Hips rotation");
        Assert.IsFalse(bindings.Any(b => b.type == typeof(Animator)), "no humanoid muscle curves");
        Assert.IsFalse(bindings.Any(b => b.propertyName == "blendShape.Smile"), "smile belongs to petting");
        Assert.IsFalse(bindings.Any(b => b.propertyName == "blendShape.Blink" || b.propertyName == "blendShape.EyeClose"),
            "eyelids belong to the blink layer");
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(idle).loopTime);
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(Clip(State(P.BodyLayerName, "Bored"))).loopTime);
        Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(Clip(State(P.BodyLayerName, "Wake"))).loopTime);

        float Heading(string state) => AnimationUtility.GetEditorCurve(Clip(State(P.BodyLayerName, state)),
            EditorCurveBinding.FloatCurve(C.PetHeadingName, typeof(VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint), C.HeadingWeightProperty)).Evaluate(0f);
        Assert.AreEqual(C.PetModelHeadingWeight, Heading("Walk"), "turns with the wearer (gradually) while walking");
        Assert.AreEqual(0f, Heading("Idle"));
    }

    // The eye layer owns every eye shape, so the smile can close the eyes (the lashes' smile alone sat over open
    // eyes and looked like eyebrows) without the blink fighting it.
    static readonly string Lash = M.RobotPath + "/EyeLash";

    float Shape(AnimationClip clip, string mesh, string shape, float t) => AnimationUtility.GetEditorCurve(clip,
        EditorCurveBinding.FloatCurve(M.RobotPath + "/" + mesh, typeof(SkinnedMeshRenderer), "blendShape." + shape)).Evaluate(t);

    float Highlight(AnimationClip clip, float t) => AnimationUtility.GetEditorCurve(clip,
        EditorCurveBinding.FloatCurve(M.RobotPath + "/EyeLight", typeof(SkinnedMeshRenderer), "m_Enabled")).Evaluate(t);

    [Test]
    public void Look_SmilesForAMoment_EyesClosed_OncePerLook()
    {
        var eyes = TranslatorPetWalker.BlinkLayerName;
        var glance = State(eyes, "Glance");
        var clip = Clip(glance);
        float end = clip.length, mid = end * 0.4f;
        Assert.AreEqual(0f, Shape(clip, "EyeLash", "Smile", 0f));
        Assert.AreEqual(100f, Shape(clip, "EyeLash", "Smile", mid), 1f, "smiles");
        Assert.AreEqual(100f, Shape(clip, "Face", "EyeClose", mid), 1f, "eyes closed under the smile");
        Assert.AreEqual(100f, Shape(clip, "Eye", "EyeClose", mid), 1f);
        Assert.AreEqual(0f, Highlight(clip, mid), "no highlights floating on closed eyes");
        Assert.AreEqual(0f, Shape(clip, "EyeLash", "Smile", end), "and stops");
        Assert.AreEqual(1f, Highlight(clip, end));
        Assert.GreaterOrEqual(end, 2.4f, "long enough to notice");
        Assert.LessOrEqual(end, 3f, "but not a fixed grin");
        Assert.AreEqual(100f, Shape(clip, "EyeLash", "Smile", end - 0.5f), 1f, "still smiling near the end");
        Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        StringAssert.StartsWith($"Happy:{C.PatParam}If0 | Glance:{C.LookedParam}If0", Transitions(State(eyes, "Blink")));
        Assert.AreEqual($"Happy:{C.PatParam}If0 | Seen:@1", Transitions(glance));
        Assert.AreEqual($"Happy:{C.PatParam}If0 | Blink:{C.LookedParam}IfNot0", Transitions(State(eyes, "Seen")));
    }

    [Test]
    public void Pat_SmilesWithEyesClosed_AndHearts()
    {
        var happy = Clip(State(TranslatorPetWalker.BlinkLayerName, "Happy"));
        Assert.AreEqual(100f, Shape(happy, "EyeLash", "Smile", 0f));
        Assert.AreEqual(100f, Shape(happy, "Face", "EyeClose", 0f));
        Assert.AreEqual(100f, Shape(happy, "Eye", "EyeClose", 0f));
        Assert.AreEqual(0f, Shape(happy, "EyeLash", "Blink", 0f));
        Assert.AreEqual(0f, Highlight(happy, 0f));
        StringAssert.StartsWith("Blink:" + C.PatParam + "IfNot0", Transitions(State(TranslatorPetWalker.BlinkLayerName, "Happy")));
        var blink = Clip(State(TranslatorPetWalker.BlinkLayerName, "Blink"));
        Assert.AreEqual(0f, Shape(blink, "EyeLash", "Smile", 0f));
        Assert.AreEqual(1f, Highlight(blink, 0f));
        // The pat layer keeps only the hearts.
        Assert.IsFalse(AnimationUtility.GetCurveBindings(Clip(State(M.PatLayerName, "Hearts"))).Any(b => b.propertyName.StartsWith("blendShape.")));
    }

    [Test]
    public void Place_SizesThePetToSixTenthsOfTheAvatar()
    {
        var avatar = TestAvatar.Create();
        try
        {
            var d = avatar.AddComponent<VRCAvatarDescriptor>();
            d.ViewPosition = new Vector3(0, 1.4f, 0.07f);
            var placed = TranslatorSetup.Place(avatar, prefab);
            var robot = placed.transform.Find(RobotPath);
            float petEye = P.EyeHeight(robot) * robot.localScale.y;
            Assert.AreEqual(0.6f * 1.4f, petEye, 0.01f);

            // The bubble rides her head, so it resizes with her and stays above the name tag.
            var head = robot.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
            Assert.Greater(head.Find(C.TextName).position.y, head.Find(C.NameTagName).position.y, "bubble above the resized head");
        }
        finally
        {
            Object.DestroyImmediate(avatar);
        }
    }

    // ---- Walking toward its spot, facing the way it goes; blinking ----

    [Test]
    public void Prefab_NoTrail_HopFacesItsSpotOrTheWayTheWearerFaced()
    {
        Assert.IsNull(Find("Pet_Root/Pet_Trail"), "walks to its spot, not along the wearer's path");
        var follow = Find("Pet_Root/Pet_Follow").GetComponent<VRCParentConstraint>();
        Assert.AreEqual(3, follow.Sources.Count);
        Assert.AreEqual(Find("Pet_Root/Pet_Heading/Pet_Side/Pet_Target"), follow.Sources[2].SourceTransform, "extra pull while landing");
        Assert.AreEqual(0f, follow.Sources[2].Weight);
        var ahead = Find("Pet_Root/Pet_Heading/" + TranslatorPetWalker.AheadName);
        Assert.Greater(ahead.localPosition.z, C.PetOffset.z + 5f, "far in front, along the wearer's heading");
        Assert.AreEqual(0f, ahead.localPosition.x, "straight ahead: the same whichever side she waits on");
        var aim = Find("Pet_Root/Pet_Follow/Pet_Hop").GetComponent<VRCAimConstraint>();
        Assert.IsNotNull(aim);
        CollectionAssert.AreEqual(new[] { Find("Pet_Root/Pet_Heading/Pet_Side/Pet_Target"), ahead },
            Enumerable.Range(0, aim.Sources.Count).Select(i => aim.Sources[i].SourceTransform));
        Assert.IsFalse(aim.AffectsRotationX);
        Assert.IsFalse(aim.AffectsRotationZ);
    }

    [Test]
    public void Prefab_WaitsWhereTheRobotDoes_AndKnowsWhenItArrived()
    {
        Assert.AreEqual(C.PetOffset, Find("Pet_Root/Pet_Heading/Pet_Side/Pet_Target").localPosition);
        Assert.AreEqual(C.PetOffset, Find("Pet_Root/Pet_Follow").localPosition);
        var arrive = Find("Pet_Root/Pet_Heading/Pet_Side/Pet_Target").GetComponents<VRCContactReceiver>().Single(r => r.parameter == C.ArrivedParam);
        Assert.AreEqual(C.ArriveRadius, arrive.radius);
        CollectionAssert.AreEqual(new[] { C.PetContactTag }, arrive.collisionTags);
        Assert.IsFalse(arrive.allowOthers);

        var close = Find("Pet_Root/Pet_Follow").GetComponents<VRCContactReceiver>().Single(r => r.parameter == C.CloseParam);
        Assert.AreEqual(C.CloseRadius, close.radius);
        CollectionAssert.AreEqual(new[] { C.WearerContactTag }, close.collisionTags);
        Assert.IsFalse(close.allowOthers);
        Assert.IsTrue(prefab.GetComponents<VRCContactSender>().Any(s => s.collisionTags.Contains(C.WearerContactTag)), "the wearer's sender");
    }

    [Test]
    public void Prefab_SpotFollowsTheWearerOnlyWhenTheBodyLayerSaysSo()
    {
        var pos = Find("Pet_Root/Pet_Heading").GetComponent<VRCPositionConstraint>();
        Assert.AreEqual(prefab.transform, pos.Sources[0].SourceTransform);
        Assert.AreEqual(pos.transform, pos.Sources[1].SourceTransform, "stays where it was otherwise");
    }

    static float Weight(AnimationClip clip, string path, System.Type type, int source) =>
        AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, "Sources.source" + source + ".Weight")).Evaluate(0f);

    [TestCase("Walk", 1f, 0f, 1000f, 0f)]
    [TestCase("Approach", 1f, 0f, 1000f, 0f)]
    [TestCase("Land", 1f, 0f, 0f, 0.15f)]
    [TestCase("Idle", 0f, 1f, 0f, 0f)]
    [TestCase("Bored", 0f, 1f, 0f, 0f)]
    public void Body_GoesToItsSpotFacingIt_WaitsFacingTheWayTheWearerFaced(string state, float spot, float ahead, float spotFollows, float pull)
    {
        var clip = Clip(State(P.BodyLayerName, state));
        const string hop = "Pet_Follow/Pet_Hop";
        Assert.AreEqual(spot, Weight(clip, hop, typeof(VRCAimConstraint), 0), "faces the waiting spot");
        Assert.AreEqual(ahead, Weight(clip, hop, typeof(VRCAimConstraint), 1), "faces the way the wearer faced");
        Assert.AreEqual(spotFollows, Weight(clip, C.PetHeadingName, typeof(VRCPositionConstraint), 0), "spot follows the wearer");
        Assert.AreEqual(pull, Weight(clip, C.PetFollowName, typeof(VRCParentConstraint), 2), "landing pull");
        foreach (var axis in new[] { 0, 1, 2 })
            Assert.AreEqual(C.PetOffset[axis], AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(M.TargetPath, typeof(Transform), "m_LocalPosition." + "xyz"[axis])).Evaluate(0f), 1e-4f, "spot " + axis);
    }

    [Test]
    public void NameTag_FloatsAboveHerHead_FacingWhoeverLooks()
    {
        var head = Find(RobotPath).GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
        var tag = head.Find(C.NameTagName);
        Assert.IsNotNull(tag, "on her head, so it moves with her");
        Assert.Greater(tag.position.y, head.position.y + 0.1f, "above the head");
        var mr = tag.GetComponent<MeshRenderer>();
        Assert.AreEqual(TranslatorNameTag.ShaderName, mr.sharedMaterial.shader.name, "billboard: always faces the viewer");
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(C.NameTagTexture), mr.sharedMaterial.mainTexture);
        Assert.IsNull(tag.GetComponent<Collider>());
        var size = tag.lossyScale;
        var robotScale = Find(RobotPath).lossyScale.x;
        Assert.AreEqual(C.NameTagWidth, size.x / robotScale, 0.01f);
        Assert.AreEqual(C.NameTagWidth * 256f / 768f, size.y / robotScale, 0.01f, "the picture's shape");
    }

    [Test]
    public void Subtitle_IsAChatBubbleOverHerHead_FacingWhoeverLooks()
    {
        var head = Find(RobotPath).GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
        var text = head.Find(C.TextName);
        Assert.IsNotNull(text, "rides her head");
        Assert.IsNull(Find("Pet_Root/Pet_Follow/" + C.TextName), "not the robot's place");
        Assert.Greater(text.position.y, head.Find(C.NameTagName).position.y, "above the name tag");
        var quad = text.Find(C.KatObjectName + "/" + C.KatConstraintName + "/" + C.KatTextName);
        Assert.IsNotNull(quad, "KAT's own hierarchy, so its clips still find the text");
        var mat = quad.GetComponent<MeshRenderer>().sharedMaterial;
        Assert.AreEqual(TranslatorChatBubble.ShaderName, mat.shader.name);
        Assert.IsTrue(mat.HasProperty("_Char0") && mat.HasProperty("_Char127"), "KAT writes the characters here");
        Assert.AreEqual(Resources.Load<Material>(C.KatTextMaterialResource).mainTexture, mat.mainTexture, "KAT's letters");
        Assert.IsNotNull(text.GetComponent<ModularAvatarMergeAnimator>(), "KAT's animator merged here");
    }

    // Crouching, the wearer's face is lower than hers: her head tilts toward it (not only her body turning).
    [Test]
    public void Look_HerHeadTiltsTowardTheWearersFace()
    {
        var robot = Find(RobotPath);
        var head = robot.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
        var aim = head.GetComponent<VRCAimConstraint>();
        Assert.IsNotNull(aim, "on the head bone");
        Assert.AreEqual(Find(C.GazeName), aim.Sources[0].SourceTransform, "the wearer's face");
        Assert.IsTrue(aim.AffectsRotationX && aim.AffectsRotationY, "up/down too");
        Assert.AreEqual(0f, aim.GlobalWeight, "off until looked at");
        Assert.Greater(Vector3.Dot(head.TransformDirection(aim.AimAxis), robot.forward), 0.99f, "aims with her face");
        Assert.Greater(Vector3.Dot(head.TransformDirection(aim.UpAxis), Vector3.up), 0.99f);

        string headPath = AnimationUtility.CalculateTransformPath(head, Find("Pet_Root"));
        float Weight(string state) => AnimationUtility.GetEditorCurve(Clip(State(M.LookLayerName, state)),
            EditorCurveBinding.FloatCurve(headPath, typeof(VRCAimConstraint), "GlobalWeight")).Evaluate(0f);
        Assert.AreEqual(C.HeadLookWeight, Weight("Look"));
        Assert.AreEqual(0f, Weight("Away"));
        Assert.Less(C.HeadLookWeight, 1f, "part of the way, so the neck never bends too far");
    }

    // ---- Left or right, whichever is nearer ----

    [Test]
    public void Prefab_KnowsWhichSideOfTheWearerSheIsOn()
    {
        foreach (var (param, sign) in new[] { (C.PetLeftParam, -1f), (C.PetRightParam, 1f) })
        {
            var r = prefab.GetComponentsInChildren<VRCContactReceiver>(true).Single(c => c.parameter == param);
            Assert.AreEqual(prefab.transform, r.transform.parent, param + ": moves and turns with the wearer");
            Assert.AreEqual(sign, Mathf.Sign(r.transform.localPosition.x), param);
            Assert.Greater(Mathf.Abs(r.transform.localPosition.x) - r.radius, 0.05f, "a gap in the middle, so she does not flip-flop behind the wearer");
            CollectionAssert.AreEqual(new[] { C.PetContactTag }, r.collisionTags);
            Assert.IsFalse(r.allowOthers);
            Assert.AreEqual(ContactBase.ShapeType.Capsule, r.shapeType);
            Assert.Greater(Mathf.Abs(Vector3.Dot(r.transform.up, prefab.transform.forward)), 0.99f, "lies front to back: covers her behind the wearer too");
            Assert.GreaterOrEqual(r.height, 8f);
        }
    }

    [Test]
    public void Side_SpotIsMirroredOnTheLeft_SwitchedOnlyWhenSettingOff()
    {
        Assert.AreEqual("Pet_Side", Find("Pet_Root/Pet_Heading/Pet_Side/Pet_Target").parent.name);
        float Scale(string state) => AnimationUtility.GetEditorCurve(Clip(State(P.SideLayerName, state)),
            EditorCurveBinding.FloatCurve(C.PetHeadingName + "/" + C.PetSideName, typeof(Transform), "m_LocalScale.x")).Evaluate(0f);
        Assert.AreEqual(1f, Scale("Right"));
        Assert.AreEqual(-1f, Scale("Left"));
        Assert.AreEqual("Right", Layer(P.SideLayerName).defaultState.name);
        Assert.AreEqual($"Left:{C.PetLeftParam}If0&{C.CloseParam}IfNot0", Transitions(State(P.SideLayerName, "Right")));
        Assert.AreEqual($"Right:{C.PetRightParam}If0&{C.CloseParam}IfNot0", Transitions(State(P.SideLayerName, "Left")));
        foreach (var state in new[] { "Right", "Left" })
            foreach (var axis in new[] { "y", "z" })
                Assert.AreEqual(1f, AnimationUtility.GetEditorCurve(Clip(State(P.SideLayerName, state)),
                    EditorCurveBinding.FloatCurve(C.PetHeadingName + "/" + C.PetSideName, typeof(Transform), "m_LocalScale." + axis)).Evaluate(0f),
                    "every axis written (a missing one becomes 0)");
    }

    [Test]
    public void Blink_EveryFewSeconds()
    {
        var blink = Layer(TranslatorPetWalker.BlinkLayerName).defaultState;
        var clip = Clip(blink);
        Assert.AreEqual(C.BlinkSeconds, clip.length, 1e-3f);
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        var lash = AnimationUtility.GetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(M.RobotPath + "/EyeLash", typeof(SkinnedMeshRenderer), "blendShape.Blink"));
        Assert.AreEqual(0f, lash.Evaluate(0f), "open most of the time");
        Assert.AreEqual(0f, lash.Evaluate(C.BlinkSeconds / 2));
        float peak = Enumerable.Range(0, 400).Max(i => lash.Evaluate(C.BlinkSeconds * i / 400f));
        Assert.AreEqual(100f, peak, 1f, "closes fully once");
        foreach (var mesh in new[] { "Face", "Eye" })
            Assert.IsNotNull(AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(M.RobotPath + "/" + mesh, typeof(SkinnedMeshRenderer), "blendShape.EyeClose")), mesh);
    }
}
