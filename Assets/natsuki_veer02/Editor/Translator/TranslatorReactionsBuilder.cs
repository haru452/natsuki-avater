using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using C = TranslatorConfig;

/// <summary>
/// The pet's reactions to the wearer: where it waits (turning only while walking), facing the wearer
/// when looked at, and hearts when its head is petted. All of it is contacts and constraints, so it
/// runs the same on every player's screen.
/// </summary>
public static class TranslatorReactionsBuilder
{
    public const string LookZoneName = "LookZone";
    public const string PatZoneName = "PatZone";
    public const string GazeRayName = "GazeRay";

    /// <summary>
    /// Follows the avatar's position always, but its rotation only while the motion layer raises the
    /// second source's weight (walking). Lives in the world-frozen Pet_Root, so the self source is last frame's.
    /// </summary>
    public static void AddHeading(GameObject heading, Transform avatarSide)
    {
        var pos = heading.AddComponent<VRCPositionConstraint>();
        pos.Sources.Add(new VRCConstraintSource(avatarSide, 1f));
        pos.IsActive = true;
        pos.Locked = true;

        var rot = heading.AddComponent<VRCRotationConstraint>();
        rot.Sources.Add(new VRCConstraintSource(heading.transform, 1f));
        rot.Sources.Add(new VRCConstraintSource(avatarSide, 0f));
        rot.AffectsRotationX = false; // stay upright
        rot.AffectsRotationZ = false;
        rot.IsActive = true;
        rot.Locked = true;
    }

    /// <summary>A forward-pointing capsule on the wearer's head. Returns the head-side object the pet faces.</summary>
    public static GameObject AddGaze(Transform root)
    {
        var gaze = new GameObject(C.GazeName);
        gaze.transform.SetParent(root, false);
        gaze.transform.localPosition = new Vector3(0, C.SignFallbackHeight, 0);
        var proxy = gaze.AddComponent<ModularAvatarBoneProxy>();
        proxy.boneReference = HumanBodyBones.Head;
        proxy.attachmentMode = BoneProxyAttachmentMode.AsChildKeepRotation;

        // Contact capsules run along local Y; tip it forward so it points where the head looks.
        var ray = new GameObject(GazeRayName);
        ray.transform.SetParent(gaze.transform, false);
        ray.transform.localPosition = new Vector3(0, 0, 0.15f + C.GazeLength / 2);
        ray.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var s = ray.AddComponent<VRCContactSender>();
        s.shapeType = ContactBase.ShapeType.Capsule;
        s.radius = C.GazeRadius;
        s.height = C.GazeLength;
        s.collisionTags = new List<string> { C.GazeTag };
        return gaze;
    }

    /// <summary>Turns the robot (yaw only) toward the wearer's head; the motion layer fades GlobalWeight in and out.</summary>
    public static void AddFacing(GameObject robot, Transform gaze)
    {
        var aim = robot.AddComponent<VRCAimConstraint>();
        aim.Sources.Add(new VRCConstraintSource(gaze, 1f));
        aim.AimAxis = Vector3.forward;
        aim.UpAxis = Vector3.up;
        aim.WorldUp = VRCConstraintBase.WorldUpType.SceneUp;
        aim.AffectsRotationX = false;
        aim.AffectsRotationZ = false;
        aim.GlobalWeight = 0f;
        aim.IsActive = true;
        aim.Locked = true;

        Receiver(robot.transform, LookZoneName, new Vector3(0, C.RobotHeight * 0.6f, 0), C.LookZoneRadius,
            new[] { C.GazeTag }, C.LookedParam, allowOthers: false);
    }

    /// <summary>Any player's hand or finger on the head (including the wearer's own).</summary>
    public static void AddPatZone(GameObject robot) =>
        Receiver(robot.transform, PatZoneName, new Vector3(0, 0.3f, 0), C.PatRadius, C.PatTags, C.PatParam, allowOthers: true);

    static void Receiver(Transform parent, string name, Vector3 pos, float radius, string[] tags, string param, bool allowOthers)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var r = go.AddComponent<VRCContactReceiver>();
        r.shapeType = ContactBase.ShapeType.Sphere;
        r.radius = radius;
        r.collisionTags = new List<string>(tags);
        r.allowSelf = true;
        r.allowOthers = allowOthers;
        r.localOnly = false;
        r.receiverType = ContactReceiver.ReceiverType.Constant;
        r.parameter = param;
    }

    /// <summary>Pink hearts that drift up from the head. Emission starts off; the motion layer switches it.</summary>
    public static ParticleSystem AddHearts(Transform parent, string outputDir)
    {
        var go = new GameObject(C.HeartsName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, C.RobotHeight, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        main.gravityModifier = -0.03f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 20;

        var emission = ps.emission;
        emission.rateOverTime = 7f;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.08f;
        shape.rotation = new Vector3(-90, 0, 0);

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        var tex = TranslatorAssetGenerator.SavePng(DrawHeart(), outputDir + "/Pet_Heart.png", overwrite: true);
        var mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { mainTexture = tex };
        r.sharedMaterial = TranslatorAssetGenerator.SaveMaterial(mat, outputDir + "/Pet_Heart.mat");
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// <summary>A soft-edged pink heart.</summary>
    public static Texture2D DrawHeart()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pink = new Color(1f, 0.42f, 0.62f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Heart curve: (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0, scaled to the texture.
            float u = (x + 0.5f - size / 2f) / (size * 0.36f);
            float v = (y + 0.5f - size * 0.45f) / (size * 0.36f);
            float f = Mathf.Pow(u * u + v * v - 1f, 3f) - u * u * v * v * v;
            float a = Mathf.Clamp01(-f * 12f);
            tex.SetPixel(x, y, new Color(pink.r, pink.g, pink.b, a));
        }
        return tex;
    }
}
