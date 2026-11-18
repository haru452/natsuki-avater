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
/// The pet as a humanoid model (TranslatorConfig.Pet): walks and idles with its own motions baked to plain bone animation,
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
        model = AssetDatabase.LoadAssetAtPath<GameObject>(C.PetModelPath);
        if (model == null) Assert.Ignore("pet model not in this project");
        AssetDatabase.DeleteAsset(Dir);
        AssetDatabase.CreateFolder("Assets", "__TranslatorTest");
        prefab = TranslatorPrefabBuilder.Build(Dir, petModel: model);
        c = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "/" + M.ControllerName);
    }

    [OneTimeTearDown]
    public void CleanUp() => AssetDatabase.DeleteAsset(Dir);

    Transform Find(string path) => prefab.transform.Find(path);
    SkinnedMeshRenderer RobotMesh(string mesh) => Find(RobotPath + "/" + mesh)?.GetComponent<SkinnedMeshRenderer>();
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
        Assert.AreEqual(C.Pet.HeadBone, robot.GetComponentsInChildren<Transform>(true).Single(t => t.name == TranslatorReactionsBuilder.PatZoneName).parent.name,
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
        CollectionAssert.AreEqual(new[] { P.BodyLayerName, M.LookLayerName, M.PatLayerName, TranslatorPetWalker.BlinkLayerName, P.SideLayerName,
                TranslatorPetCommands.WaitLayerName, TranslatorPetCommands.TurnLayerName },
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
        string stay = $"&{C.WaitParam}IfNot0";
        Assert.AreEqual($"SitDown:{C.SitParam}If0 | Walk:{C.CloseParam}IfNot0&{C.VelocityParam}Greater{C.HopStartSpeed}{stay} | Approach:{C.CloseParam}IfNot0{stay} | "
                        + $"Approach:{C.ArrivedParam}IfNot0{stay} | Drowsy:{still}",
            Transitions(State(P.BodyLayerName, "Idle")));
        Assert.AreEqual($"Approach:{C.VelocityParam}Less{C.HopStopSpeed} | Run:{C.VelocityParam}Greater{C.RunStartSpeed}",
            Transitions(State(P.BodyLayerName, "Walk")));
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
        Assert.AreEqual($"SitDown:{C.SitParam}If0 | Idle:" + string.Format(back, "Idle"),
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
        Assert.IsTrue(bindings.Any(b => b.type == typeof(Transform) && b.path.StartsWith(RobotPath.Substring("Pet_Root/".Length) + "/") && b.path.EndsWith("/" + HipsName)
                                        && b.propertyName == "m_LocalRotation.x"), "Hips rotation");
        Assert.IsFalse(bindings.Any(b => b.type == typeof(Animator)), "no humanoid muscle curves");
        Assert.IsFalse(bindings.Any(b => C.Pet.Smile.Any(s => b.propertyName == "blendShape." + s.shape)), "smile belongs to petting");
        Assert.IsFalse(bindings.Any(b => C.Pet.Blink.Any(s => b.propertyName == "blendShape." + s.shape)),
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
    string HipsName => model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).name;
    Transform Head => P.Head(Find(RobotPath));

    float Shape(AnimationClip clip, (string mesh, string shape) s, float t) => AnimationUtility.GetEditorCurve(clip,
        EditorCurveBinding.FloatCurve(M.RobotPath + "/" + s.mesh, typeof(SkinnedMeshRenderer), "blendShape." + s.shape)).Evaluate(t);

    /// <summary>Every smile shape (and every eye shut under it) at t equals expected; the other eyelids stay open.</summary>
    void Smiling(AnimationClip clip, float t, float expected, string message)
    {
        foreach (var s in C.Pet.Smile) Assert.AreEqual(expected, Shape(clip, s, t), 1f, message + " " + s);
        foreach (var s in C.Pet.ShutUnderSmile) Assert.AreEqual(expected, Shape(clip, s, t), 1f, "eyes closed under the smile " + s);
        foreach (var s in C.Pet.Blink.Except(C.Pet.ShutUnderSmile)) Assert.AreEqual(0f, Shape(clip, s, t), 1f, "the blink opens " + s);
        if (C.Pet.Highlight != null) Assert.AreEqual(expected > 50f ? 0f : 1f, Highlight(clip, t), "no highlights floating on closed eyes");
    }

    float Highlight(AnimationClip clip, float t) => AnimationUtility.GetEditorCurve(clip,
        EditorCurveBinding.FloatCurve(M.RobotPath + "/" + C.Pet.Highlight, typeof(SkinnedMeshRenderer), "m_Enabled")).Evaluate(t);

    [Test]
    public void Look_SmilesForAMoment_EyesClosed_OncePerLook()
    {
        var eyes = TranslatorPetWalker.BlinkLayerName;
        var glance = State(eyes, "Glance");
        var clip = Clip(glance);
        float end = clip.length, mid = end * 0.4f;
        Smiling(clip, 0f, 0f, "not yet");
        Smiling(clip, mid, 100f, "smiles");
        Smiling(clip, end, 0f, "and stops");
        Assert.GreaterOrEqual(end, 2.4f, "long enough to notice");
        Assert.LessOrEqual(end, 3f, "but not a fixed grin");
        Smiling(clip, end - 0.5f, 100f, "still smiling near the end");
        Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        StringAssert.StartsWith($"Happy:{C.PatParam}If0 | Glance:{C.LookedParam}If0", Transitions(State(eyes, "Blink")));
        Assert.AreEqual($"Happy:{C.PatParam}If0 | Seen:@1", Transitions(glance));
        Assert.AreEqual($"Happy:{C.PatParam}If0 | Blink:{C.LookedParam}IfNot0 | Glance:{C.CalledParam}If0", Transitions(State(eyes, "Seen")));
    }

    [Test]
    public void Pat_SmilesWithEyesClosed_AndHearts()
    {
        var happy = Clip(State(TranslatorPetWalker.BlinkLayerName, "Happy"));
        Smiling(happy, 0f, 100f, "smiles");
        StringAssert.StartsWith("Blink:" + C.PatParam + "IfNot0", Transitions(State(TranslatorPetWalker.BlinkLayerName, "Happy")));
        var blink = Clip(State(TranslatorPetWalker.BlinkLayerName, "Blink"));
        foreach (var s in C.Pet.Smile) Assert.AreEqual(0f, Shape(blink, s, 0f), "no smile " + s);
        if (C.Pet.Highlight != null) Assert.AreEqual(1f, Highlight(blink, 0f));
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
            var head = P.Head(robot);
            Assert.Greater(BubbleBottom(head.Find(C.TextName)), BelowBubble(head), "bubble above the resized head");
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
    [TestCase("Run", 1f, 0f, 1000f, 0.15f)]
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
        var head = Head;
        var tag = head.Find(C.NameTagName);
        if (!C.ShowNameTag)
        {
            Assert.IsNull(tag, "off for now (ShowNameTag)");
            return;
        }
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

    /// <summary>What the bubble must clear: the name tag's centre, or without it a point 0.2 m (at her scale) above the head bone.</summary>
    float BelowBubble(Transform head) => C.ShowNameTag
        ? head.Find(C.NameTagName).position.y
        : head.position.y + 0.2f * head.lossyScale.y;

    /// <summary>Where the shader puts the bubble's bottom: the text quad's origin plus _LiftY of its heights, straight up.</summary>
    static float BubbleBottom(Transform text)
    {
        var quad = text.Find(C.KatObjectName + "/" + C.KatConstraintName + "/" + C.KatTextName);
        return quad.position.y + quad.lossyScale.y * TranslatorChatBubble.Lift;
    }

    [Test]
    public void Subtitle_IsAChatBubbleOverHerHead_FacingWhoeverLooks()
    {
        var head = Head;
        var text = head.Find(C.TextName);
        Assert.IsNotNull(text, "rides her head");
        Assert.IsNull(Find("Pet_Root/Pet_Follow/" + C.TextName), "not the robot's place");
        Assert.Less(Vector3.Distance(head.position, text.position), 1e-4f, "on the head bone itself: nothing to swing round");
        Assert.Greater(BubbleBottom(text), BelowBubble(head), "above the name tag (or, without it, well above her head)");
        var quad = text.Find(C.KatObjectName + "/" + C.KatConstraintName + "/" + C.KatTextName);
        Assert.IsNotNull(quad, "KAT's own hierarchy, so its clips still find the text");
        var mat = quad.GetComponent<MeshRenderer>().sharedMaterial;
        Assert.AreEqual(TranslatorChatBubble.ShaderName, mat.shader.name);
        Assert.AreEqual(TranslatorChatBubble.Lift, mat.GetFloat(TranslatorChatBubble.LiftProperty), 1e-4f, "lifted straight up by the shader");
        Assert.AreEqual(0f, mat.GetFloat("_GrowEnabled"), "a fixed size: growing with distance made it wobble");
        Assert.IsTrue(mat.HasProperty("_Char0") && mat.HasProperty("_Char127"), "KAT writes the characters here");
        Assert.AreEqual(Resources.Load<Material>(C.KatTextMaterialResource).mainTexture, mat.mainTexture, "KAT's letters");
        Assert.IsNotNull(text.GetComponent<ModularAvatarMergeAnimator>(), "KAT's animator merged here");
    }

    // Crouching, the wearer's face is lower than hers: her head tilts toward it (not only her body turning).
    [Test]
    public void Look_HerHeadTiltsTowardTheWearersFace()
    {
        var robot = Find(RobotPath);
        var head = Head;
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
        Assert.AreEqual(1f, Scale("RightMirror"));
        Assert.AreEqual(-1f, Scale("LeftMirror"));
        Assert.AreEqual("Right", Layer(P.SideLayerName).defaultState.name);
        Assert.AreEqual($"Left:{C.PetLeftParam}If0&{C.CloseParam}IfNot0&{C.WaitParam}IfNot0 | RightMirror:{C.MirrorParam}If0", Transitions(State(P.SideLayerName, "Right")));
        Assert.AreEqual($"Right:{C.PetRightParam}If0&{C.CloseParam}IfNot0&{C.WaitParam}IfNot0 | LeftMirror:{C.MirrorParam}If0", Transitions(State(P.SideLayerName, "Left")));
        Assert.AreEqual($"LeftMirror:{C.PetLeftParam}If0&{C.CloseParam}IfNot0&{C.WaitParam}IfNot0 | Right:{C.MirrorParam}IfNot0", Transitions(State(P.SideLayerName, "RightMirror")));
        Assert.AreEqual($"RightMirror:{C.PetRightParam}If0&{C.CloseParam}IfNot0&{C.WaitParam}IfNot0 | Left:{C.MirrorParam}IfNot0", Transitions(State(P.SideLayerName, "LeftMirror")));
        float Get(string state, string prop) => AnimationUtility.GetEditorCurve(Clip(State(P.SideLayerName, state)),
            EditorCurveBinding.FloatCurve(C.PetHeadingName + "/" + C.PetSideName, typeof(Transform), prop)).Evaluate(0f);
        foreach (var state in new[] { "Right", "Left", "RightMirror", "LeftMirror" })
        {
            Assert.AreEqual(1f, Get(state, "m_LocalScale.y"), "every axis written (a missing one becomes 0)");
            Assert.AreEqual(1f, Get(state, "m_LocalScale.z"));
            Assert.AreEqual(0f, Get(state, "m_LocalPosition.x"));
            Assert.AreEqual(0f, Get(state, "m_LocalPosition.y"));
            // Mirror mode: the spot comes back level with the wearer (right beside them, not in front, so not in the mirror).
            Assert.AreEqual(state.EndsWith("Mirror") ? -C.PetOffset.z : 0f, Get(state, "m_LocalPosition.z"), 1e-4f, state);
        }
    }

    [Test]
    public void MirrorMode_IsASyncedToggleInTheMenu()
    {
        var p = prefab.GetComponent<ModularAvatarParameters>().parameters.Single(x => x.nameOrPrefix == C.MirrorParam);
        Assert.AreEqual(ParameterSyncType.Bool, p.syncType, "everyone sees her beside the wearer");
        Assert.IsFalse(p.saved);
        var menu = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend.controls.Single().subMenu;
        var toggle = menu.controls.Single(c => c.name == C.MirrorLabel);
        Assert.AreEqual(C.MirrorParam, toggle.parameter.name);
        Assert.IsTrue(c.parameters.Any(x => x.name == C.MirrorParam));
        Assert.AreEqual(TranslatorBudget.SyncedBits(8) + 5, TranslatorBudget.AddedBits(null, 8, mirror: true), "the budget check counts it (and the call, sit, wait and turn)");
    }

    // ---- Called by name (the PC app hears it and sets CalledParam for a moment) ----

    [Test]
    public void Called_IsASyncedFlag_NotInTheMenu()
    {
        var p = prefab.GetComponent<ModularAvatarParameters>().parameters.Single(x => x.nameOrPrefix == C.CalledParam);
        Assert.AreEqual(ParameterSyncType.Bool, p.syncType, "everyone sees her turn");
        Assert.IsFalse(p.saved);
        var menu = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend.controls.Single().subMenu;
        Assert.IsFalse(menu.controls.Any(x => x.parameter != null && x.parameter.name == C.CalledParam), "the app sets it, not the wearer");
        Assert.IsTrue(c.parameters.Any(x => x.name == C.CalledParam));
    }

    [Test]
    public void Called_SheTurnsToTheWearerForAMoment_AndSmiles()
    {
        var look = M.LookLayerName;
        var called = State(look, "Called");
        string front = $"&{C.TurnParam}IfNot0";
        Assert.AreEqual($"Look:{C.LookedParam}If0{front} | Called:{C.CalledParam}If0{front}", Transitions(State(look, "Away")));
        Assert.AreEqual($"Look:{C.LookedParam}If0{front} | Away:@1 | Away:{C.TurnParam}If0", Transitions(called), "looked at meanwhile: keeps looking");
        Assert.AreEqual(C.CalledSeconds, Clip(called).length, 0.01f);
        var lookClip = Clip(State(look, "Look"));
        foreach (var b in AnimationUtility.GetCurveBindings(lookClip))
            Assert.AreEqual(AnimationUtility.GetEditorCurve(lookClip, b).Evaluate(0f),
                AnimationUtility.GetEditorCurve(Clip(called), b).Evaluate(C.CalledSeconds * 0.9f), b.path + " " + b.propertyName + ": faces her like a look");

        StringAssert.Contains($"Glance:{C.CalledParam}If0", Transitions(State(TranslatorPetWalker.BlinkLayerName, "Blink")), "smiles");
    }

    // ---- Spoken commands after her name: sit (座って) and wait (待ってて) ----

    [Test]
    public void Commands_AreSyncedFlags_NotInTheMenu()
    {
        var ps = prefab.GetComponent<ModularAvatarParameters>().parameters;
        foreach (var name in new[] { C.SitParam, C.WaitParam })
        {
            var p = ps.Single(x => x.nameOrPrefix == name);
            Assert.AreEqual(ParameterSyncType.Bool, p.syncType, name + ": everyone sees her sit or wait");
            Assert.IsFalse(p.saved, name);
            Assert.IsTrue(c.parameters.Any(x => x.name == name), name);
        }
        var menu = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend.controls.Single().subMenu;
        Assert.IsFalse(menu.controls.Any(x => x.parameter != null && (x.parameter.name == C.SitParam || x.parameter.name == C.WaitParam)));
    }

    [Test]
    public void Sit_OnTheFloor_UntilToldOrTheWearerWalksOff()
    {
        var sit = State(P.BodyLayerName, "Sit");
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(Clip(sit)).loopTime);
        Assert.AreEqual(0f, Weight(Clip(sit), C.PetHeadingName, typeof(VRCPositionConstraint), 0), "stays where she is");
        Assert.AreEqual($"StandUp:{C.SitParam}IfNot0 | Rise:{C.CloseParam}IfNot0&{C.VelocityParam}Greater{C.HopStartSpeed}&{C.WaitParam}IfNot0",
            Transitions(sit));
        var rise = State(P.BodyLayerName, "Rise");
        var driver = rise.behaviours.OfType<VRCAvatarParameterDriver>().Single();
        Assert.IsTrue(driver.localOnly, "the wearer's client clears it; the synced flag tells everyone");
        Assert.AreEqual((C.SitParam, 0f), (driver.parameters.Single().name, driver.parameters.Single().value));
        Assert.AreEqual("Idle:@1", Transitions(rise), "once she is up (Sit already cleared)");
    }

    [Test]
    public void SitDown_SquatsOnTheWay_AndBackUp()
    {
        Assert.AreEqual($"Sit:@1 | StandUp:{C.SitParam}IfNot0", Transitions(State(P.BodyLayerName, "SitDown")), "told to stand mid-way: back up");
        Assert.AreEqual("Idle:@1", Transitions(State(P.BodyLayerName, "StandUp")));
        var down = Clip(State(P.BodyLayerName, "SitDown"));
        Assert.AreEqual(C.SitDownSeconds, down.length, 0.01f);
        Assert.IsFalse(AnimationUtility.GetAnimationClipSettings(down).loopTime, "once");
        var (start, startLow) = Posed(down, 0f);
        var (squat, squatLow) = Posed(down, C.SitDownSeconds * 0.45f);
        var (end, endLow) = Posed(down, C.SitDownSeconds);
        var (sitting, _) = Posed(Clip(State(P.BodyLayerName, "Sit")), 0f);
        Assert.Less(squat, start - 0.1f, "her head comes down in the squat");
        Assert.AreEqual(sitting, end, 0.01f, "and ends as she sits");
        Assert.AreEqual(0f, squatLow, 0.05f, "feet on the floor while squatting");
        Assert.AreEqual(0f, startLow, 0.05f);
        Assert.AreEqual(0f, endLow, 0.03f);
        var up = Clip(State(P.BodyLayerName, "StandUp"));
        Assert.AreEqual(sitting, Posed(up, 0f).head, 0.01f, "stands up from where she sits");
        Assert.AreEqual(start, Posed(up, up.length).head, 0.02f, "to standing");
    }

    /// <summary>Her head's height and her lowest point above the floor, posed by the clip at t (on a copy).</summary>
    (float head, float lowest) Posed(AnimationClip clip, float t)
    {
        var robot = (GameObject)Object.Instantiate(Find(RobotPath).gameObject);
        try
        {
            clip.SampleAnimation(Wrap(robot), t);
            float min = float.MaxValue;
            var mesh = new Mesh();
            foreach (var r in robot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.BakeMesh(mesh, true);
                foreach (var v in mesh.vertices) min = Mathf.Min(min, robot.transform.InverseTransformPoint(r.transform.TransformPoint(v)).y);
            }
            Object.DestroyImmediate(mesh);
            return (robot.transform.InverseTransformPoint(P.Head(robot.transform).position).y, min);
        }
        finally
        {
            Object.DestroyImmediate(robot.transform.root.gameObject);
        }
    }

    [Test]
    public void Sit_TheBakedPoseRestsOnTheFloor()
    {
        var robot = (GameObject)Object.Instantiate(Find(RobotPath).gameObject);
        try
        {
            Clip(State(P.BodyLayerName, "Sit")).SampleAnimation(Wrap(robot), 0f);
            float min = float.MaxValue;
            var mesh = new Mesh();
            foreach (var r in robot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.BakeMesh(mesh, true);
                foreach (var v in mesh.vertices) min = Mathf.Min(min, robot.transform.InverseTransformPoint(r.transform.TransformPoint(v)).y);
            }
            Object.DestroyImmediate(mesh);
            Assert.AreEqual(0f, min, 0.02f, "neither floating nor sunk");
            float head = robot.transform.InverseTransformPoint(P.Head(robot.transform).position).y;
            Assert.Less(head, P.EyeHeight(Find(RobotPath)) * 0.8f, "her head is lower than standing: she sits");
        }
        finally
        {
            Object.DestroyImmediate(robot.transform.root.gameObject);
        }
    }

    /// <summary>The clip's paths start at Pet_Robot's parent chain (Pet_Follow/Pet_Hop/Pet_Robot): rebuild it around a copy.</summary>
    static GameObject Wrap(GameObject robot)
    {
        var root = new GameObject("Pet_Root_Copy");
        var follow = new GameObject(C.PetFollowName).transform;
        follow.SetParent(root.transform, false);
        var hop = new GameObject("Pet_Hop").transform;
        hop.SetParent(follow, false);
        robot.name = C.RobotName;
        robot.transform.SetParent(hop, false);
        robot.transform.localPosition = Vector3.zero;
        robot.transform.localRotation = Quaternion.identity;
        robot.transform.localScale = Vector3.one;
        return root;
    }

    [Test]
    public void Turn_SheFacesAway_UntilToldOrSheSetsOff()
    {
        var p = prefab.GetComponent<ModularAvatarParameters>().parameters.Single(x => x.nameOrPrefix == C.TurnParam);
        Assert.AreEqual(ParameterSyncType.Bool, p.syncType, "everyone sees her turned round");
        var layer = TranslatorPetCommands.TurnLayerName;
        float Yaw(string state) => AnimationUtility.GetEditorCurve(Clip(State(layer, state)),
            EditorCurveBinding.FloatCurve(M.HopPath, typeof(VRCAimConstraint), TranslatorPetCommands.TurnProperty)).Evaluate(0f);
        Assert.AreEqual(0f, Yaw("Front"));
        Assert.AreEqual(180f, Yaw("Back"), "a half-turn on top of where she faces");
        Assert.AreEqual(0f, Yaw("Clear"));
        Assert.AreEqual("Front", Layer(layer).defaultState.name);
        Assert.AreEqual($"Back:{C.TurnParam}If0", Transitions(State(layer, "Front")));
        Assert.AreEqual($"Front:{C.TurnParam}IfNot0 | Clear:{C.CloseParam}IfNot0&{C.VelocityParam}Greater{C.HopStartSpeed}&{C.WaitParam}IfNot0",
            Transitions(State(layer, "Back")), "sets off after the wearer: faces front (not walking backwards)");
        Assert.AreEqual(C.TurnSeconds, State(layer, "Front").transitions.Single().duration, 1e-4f, "turns, not snaps");
        var driver = State(layer, "Clear").behaviours.OfType<VRCAvatarParameterDriver>().Single();
        Assert.AreEqual((C.TurnParam, 0f), (driver.parameters.Single().name, driver.parameters.Single().value));
        Assert.IsTrue(Layer(M.LookLayerName).states.Select(s => s.state).Single(s => s.name == "Look").transitions
            .Any(t => t.destinationState.name == "Away" && t.conditions.Any(k => k.parameter == C.TurnParam)), "turned round, she stops looking back");
    }

    [Test]
    public void Wait_SheStaysPut_UntilCalledBackOr20mAway()
    {
        var inRange = Find("Pet_Root/Pet_Follow").GetComponents<VRCContactReceiver>().Single(r => r.parameter == C.InRangeParam);
        Assert.AreEqual(20f, inRange.radius);
        CollectionAssert.AreEqual(new[] { C.WearerContactTag }, inRange.collisionTags);
        Assert.IsFalse(inRange.allowOthers);

        var layer = TranslatorPetCommands.WaitLayerName;
        Assert.AreEqual("Free", Layer(layer).defaultState.name);
        Assert.AreEqual($"Waiting:{C.WaitParam}If0", Transitions(State(layer, "Free")));
        Assert.AreEqual($"Free:{C.WaitParam}IfNot0 | TooFar:{C.InRangeParam}IfNot0", Transitions(State(layer, "Waiting")));
        var driver = State(layer, "TooFar").behaviours.OfType<VRCAvatarParameterDriver>().Single();
        Assert.IsTrue(driver.localOnly);
        CollectionAssert.AreEquivalent(new[] { (C.WaitParam, 0f), (C.SitParam, 0f) }, driver.parameters.Select(x => (x.name, x.value)));
        Assert.AreEqual($"Free:{C.WaitParam}IfNot0", Transitions(State(layer, "TooFar")));
    }

    // ---- Running with the wearer ----

    [Test]
    public void Run_WhileTheWearerRuns_AtTheirPace_PulledClose()
    {
        var run = State(P.BodyLayerName, "Run");
        Assert.AreEqual($"Walk:{C.VelocityParam}Less{C.RunStopSpeed}", Transitions(run));
        Assert.Less(C.RunStopSpeed, C.RunStartSpeed, "no flicker between walking and running");
        Assert.IsTrue(run.speedParameterActive);
        Assert.AreEqual(C.VelocityParam, run.speedParameter, "the faster the wearer, the faster her legs");
        Assert.AreEqual(1f / C.RunClipSpeed, run.speed, 1e-6f);
        Assert.Greater(C.RunPull, 0f, "pulled toward her spot so she keeps up");
        Assert.Greater(C.CatchUpDistance, 10f, "running never sets off the catch-up");
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(Clip(run)).loopTime);
    }

    // VRChat's proxy_ clips are one-frame stand-ins (VRChat swaps in its own motions at run time): baked, they
    // freeze. Her own motions (TranslatorPetMotions) really move.
    [TestCase("Idle", true)]
    [TestCase("Bored", true)]
    [TestCase("Walk", true)]
    [TestCase("Run", true)]
    [TestCase("Jump", false)]
    public void OwnMotions_AreRealHumanoidMotions(string name, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(TranslatorPetMotions.Path(name));
        Assert.IsNotNull(clip, name);
        Assert.IsTrue(clip.humanMotion, name + " is humanoid, so it fits any model");
        Assert.Greater(clip.length, 0.5f, name + " moves (not a one-frame pose)");
        Assert.AreEqual(loop, clip.isLooping, name);
    }

    [Test]
    public void Run_IsAMovingMotion() => Assert.Greater(Clip(State(P.BodyLayerName, "Run")).length, 0.3f);

    [Test]
    public void Run_PlaysOnTheSpot()
    {
        var clip = Clip(State(P.BodyLayerName, "Run"));
        var hips = AnimationUtility.GetCurveBindings(clip).First(b => b.propertyName == "m_LocalPosition.x" && b.type == typeof(Transform) && b.path.StartsWith(M.RobotPath + "/")).path;
        Vector3 At(float t) => new Vector3(
            AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(hips, typeof(Transform), "m_LocalPosition.x")).Evaluate(t),
            AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(hips, typeof(Transform), "m_LocalPosition.y")).Evaluate(t),
            AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(hips, typeof(Transform), "m_LocalPosition.z")).Evaluate(t));
        Assert.Less(Vector3.Distance(At(0f), At(clip.length)), 0.01f, "ends where it starts");
        float spread = Enumerable.Range(0, 20).Max(i => Vector3.Distance(At(0f), At(clip.length * i / 20f)));
        Assert.Less(spread, 0.3f, "no sliding forward over the loop");
    }

    [Test]
    public void Blink_EveryFewSeconds()
    {
        var blink = Layer(TranslatorPetWalker.BlinkLayerName).defaultState;
        var clip = Clip(blink);
        Assert.AreEqual(C.BlinkSeconds, clip.length, 1e-3f);
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
        foreach (var (mesh, shape) in C.Pet.Blink)
        {
            Assert.IsNotNull(RobotMesh(mesh), "the model has " + mesh);
            Assert.GreaterOrEqual(RobotMesh(mesh).sharedMesh.GetBlendShapeIndex(shape), 0, mesh + " has " + shape);
            var lid = AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(M.RobotPath + "/" + mesh, typeof(SkinnedMeshRenderer), "blendShape." + shape));
            Assert.AreEqual(0f, lid.Evaluate(0f), "open most of the time");
            Assert.AreEqual(0f, lid.Evaluate(C.BlinkSeconds / 2));
            float peak = Enumerable.Range(0, 400).Max(i => lid.Evaluate(C.BlinkSeconds * i / 400f));
            Assert.AreEqual(100f, peak, 1f, "closes fully once");
        }
        foreach (var (mesh, shape) in C.Pet.Smile)
            Assert.GreaterOrEqual(RobotMesh(mesh).sharedMesh.GetBlendShapeIndex(shape), 0, mesh + " has " + shape);
    }
}
