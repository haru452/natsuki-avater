using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Builds the On/Off clips and the "Fire" FX layer that toggles every flame site.</summary>
public static class FireAnimationBuilder
{
    public static void Apply(AnimatorController fx, GameObject avatarRoot, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var paths = FireObjectBuilder.FindAll(avatarRoot)
            .Select(g => FireObjectBuilder.PathOf(avatarRoot, g))
            .OrderBy(p => p)
            .ToList();

        var on = WriteClip(outputDir, "Fire_On", paths, 1f);
        var off = WriteClip(outputDir, "Fire_Off", paths, 0f);

        EnsureBoolParameter(fx);
        RemoveFireLayer(fx);
        bool writeDefaults = ExistingWriteDefaultsMajority(fx);

        fx.AddLayer(FireConfig.LayerName);
        var layers = fx.layers;
        int index = layers.Length - 1;
        layers[index].defaultWeight = 1f;
        fx.layers = layers;

        var sm = fx.layers[index].stateMachine;
        var offState = sm.AddState("Off");
        var onState = sm.AddState("On");
        offState.motion = off;
        onState.motion = on;
        offState.writeDefaultValues = writeDefaults;
        onState.writeDefaultValues = writeDefaults;
        sm.defaultState = offState;

        AddToggle(offState, onState, AnimatorConditionMode.If);
        AddToggle(onState, offState, AnimatorConditionMode.IfNot);
        EditorUtility.SetDirty(fx);
        AssetDatabase.SaveAssets();
    }

    static AnimationClip WriteClip(string dir, string name, List<string> paths, float value)
    {
        string assetPath = Path.Combine(dir, name + ".anim").Replace("\\", "/");
        AssetDatabase.DeleteAsset(assetPath);
        var clip = new AnimationClip { frameRate = 60f };
        foreach (var p in paths)
        {
            var binding = EditorCurveBinding.DiscreteCurve(p, typeof(GameObject), "m_IsActive");
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    static void EnsureBoolParameter(AnimatorController fx)
    {
        var existing = fx.parameters.FirstOrDefault(p => p.name == FireConfig.ParamName);
        if (existing != null && existing.type == AnimatorControllerParameterType.Bool) return;
        if (existing != null) fx.RemoveParameter(existing);
        fx.AddParameter(FireConfig.ParamName, AnimatorControllerParameterType.Bool);
    }

    static void RemoveFireLayer(AnimatorController fx)
    {
        for (int i = fx.layers.Length - 1; i >= 0; i--)
        {
            if (fx.layers[i].name == FireConfig.LayerName) fx.RemoveLayer(i);
        }
    }

    // Match the existing layers so the new states do not change what other layers write by default.
    static bool ExistingWriteDefaultsMajority(AnimatorController fx)
    {
        int on = 0, off = 0;
        foreach (var layer in fx.layers)
        {
            foreach (var s in AllStates(layer.stateMachine))
            {
                if (s.writeDefaultValues) on++; else off++;
            }
        }
        return on > off;
    }

    static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine sm)
    {
        foreach (var s in sm.states) yield return s.state;
        foreach (var child in sm.stateMachines)
            foreach (var s in AllStates(child.stateMachine)) yield return s;
    }

    static void AddToggle(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.exitTime = 0f;
        t.hasFixedDuration = true;
        t.duration = 0f;
        t.AddCondition(mode, 0f, FireConfig.ParamName);
    }
}
