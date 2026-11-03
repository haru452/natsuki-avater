using System;
using System.Linq;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;

/// <summary>Registers the synced "Fire" Bool parameter and its Expression menu toggle.</summary>
public static class FireMenuRegistrar
{
    const int BoolCost = 1;

    /// <exception cref="InvalidOperationException">Thrown before anything is written when there is no room.</exception>
    public static void Register(VRCExpressionParameters parameters, VRCExpressionsMenu menu)
    {
        bool hasParam = parameters.parameters.Any(p => p.name == FireConfig.ParamName);
        bool hasControl = menu.controls.Any(c => c.parameter != null && c.parameter.name == FireConfig.ParamName);

        // Validate everything first so a failure leaves both assets untouched.
        if (!hasParam && parameters.CalcTotalCost() + BoolCost > VRCExpressionParameters.MAX_PARAMETER_COST)
            throw new InvalidOperationException("[Fire] Expression parameters are full (" + VRCExpressionParameters.MAX_PARAMETER_COST + " bits). Free some space and run again.");
        if (!hasControl && menu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
            throw new InvalidOperationException("[Fire] Expression menu already has " + VRCExpressionsMenu.MAX_CONTROLS + " items. Free a slot and run again.");

        if (!hasParam)
        {
            parameters.parameters = parameters.parameters.Concat(new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = FireConfig.ParamName,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    saved = false,
                    defaultValue = 0f,
                    networkSynced = true,
                },
            }).ToArray();
        }

        if (!hasControl)
        {
            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = FireConfig.MenuLabel,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = FireConfig.ParamName },
            });
        }

        EditorUtility.SetDirty(parameters);
        EditorUtility.SetDirty(menu);
    }
}
