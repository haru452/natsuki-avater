using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A motion from one baked pose to another through a third (stand → crouch → sit, and back): each bone eases from
/// pose to pose. The poses are the first frames of baked clips (TranslatorPetModel.Bake), so they fit the model.
/// </summary>
public static class TranslatorPetPoses
{
    /// <summary>(clip, time): the pose at each time, in order; the result runs from the first time to the last.</summary>
    public static AnimationClip Through(params (AnimationClip pose, float time)[] keys)
    {
        var clip = new AnimationClip { frameRate = 30f };
        var bindings = keys.SelectMany(k => AnimationUtility.GetCurveBindings(k.pose))
            .Where(b => b.type == typeof(Transform))
            .GroupBy(b => (b.path, b.propertyName)).Select(g => g.First()).ToList();

        // Rotations: per bone, keep each key's quaternion on the same side as the one before (no spinning the long way).
        foreach (var bone in bindings.Where(b => b.propertyName == "m_LocalRotation.x").Select(b => b.path))
        {
            var quats = new List<Quaternion>();
            foreach (var (pose, _) in keys)
            {
                float At(string axis) => Value(pose, bone, "m_LocalRotation." + axis, keys);
                var q = new Quaternion(At("x"), At("y"), At("z"), At("w"));
                if (quats.Count > 0 && Quaternion.Dot(q, quats[quats.Count - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                quats.Add(q);
            }
            for (int k = 0; k < 4; k++)
                Set(clip, bone, "m_LocalRotation." + "xyzw"[k], keys.Select((key, i) => (key.time, quats[i][k])));
        }
        foreach (var b in bindings.Where(b => b.propertyName.StartsWith("m_LocalPosition.")))
            Set(clip, b.path, b.propertyName, keys.Select(key => (key.time, Value(key.pose, b.path, b.propertyName, keys))));
        return clip;
    }

    /// <summary>The pose's value at its first frame; a pose that does not have it takes it from the first that does.</summary>
    static float Value(AnimationClip pose, string path, string property, (AnimationClip pose, float time)[] keys)
    {
        var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
        var curve = AnimationUtility.GetEditorCurve(pose, binding)
                    ?? keys.Select(k => AnimationUtility.GetEditorCurve(k.pose, binding)).First(c => c != null);
        return curve.Evaluate(0f);
    }

    /// <summary>Eases in and out of every key (flat tangents): a short rest in the middle pose, smooth at both ends.</summary>
    static void Set(AnimationClip clip, string path, string property, IEnumerable<(float time, float value)> keys) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
            new AnimationCurve(keys.Select(k => new Keyframe(k.time, k.value, 0f, 0f)).ToArray()));
}
