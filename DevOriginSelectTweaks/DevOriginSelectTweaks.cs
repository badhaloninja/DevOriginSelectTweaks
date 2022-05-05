using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Linq;
using BaseX;
using FrooxEngine.LogiX;
using System.Reflection;
using System.Collections.Generic;

namespace DevOriginSelectTweaks
{
    public class DevOriginSelectTweaks : NeosMod
    {
        public override string Name => "DevOriginSelectTweaks";
        public override string Author => "badhaloninja";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/badhaloninja/DevOriginSelectTweaks";


		private static ModConfiguration config;


        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> enabled = new ModConfigurationKey<bool>("enabled", "Enabled", () => true);


        [AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<int> originSnipingMode = new ModConfigurationKey<int>("originSnipingMode", "<size=200%>Origin Sniping Behavior\n0: None - Laser only | 1: Auto - Original Behavior | 2: Colliders - Colliders and Origins | 3: Only - Origin sniping only</size>", () => 1, valueValidator: (i) => i.IsBetween(0, 3)); //Desc scaled for NeosModSettings 
		/* 
         * OriginSniping Behavior
         * 0: None - Laser only
         * 1: Auto - Original Behavior
         * 2: Colliders - Colliders and Origins
         * 3: Only - Origin sniping only
         */


		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<float> originSnipeRange = new ModConfigurationKey<float>("originSnipeRange", "Origin snipe range", () => 0.1f);

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> rangeRespectScale = new ModConfigurationKey<bool>("rangeRespectScale", "Respect user scale", () => true);


        public static MethodInfo onGizmoReplacedMethod;
        
        public override void OnEngineInit()
        {
			config = GetConfiguration();

            onGizmoReplacedMethod = typeof(DevToolTip).GetMethod("OnGizmoReplaced", BindingFlags.NonPublic | BindingFlags.Instance); // Fetch method

            Harmony harmony = new Harmony("me.badhaloninja.DevOriginSelectTweaks");
            harmony.PatchAll();
        }


        [HarmonyPatch(typeof(DevToolTip), "GenerateMenuItems")]
        class MenuItems
        {
            public static void Postfix(ContextMenu menu)
            {
                if (!config.GetValue(enabled)) return; // Lazy man's disabled
                Uri originMode = new Uri("neosdb:///b778eb36757426a5a3e669844e064a2a3d80f54a4a58e3978ad28781bfebaf81.png"); // OriginMode.png
                ContextMenuItem contextMenuItem = menu.AddItem("What", originMode, default(color));

                // Config sync
                var field = contextMenuItem.Slot.AttachComponent<ValueField<int>>(); // Relay field
                field.Value.Value = config.GetValue(originSnipingMode); // Set to current value
                field.Value.OnValueChange += (vf) => // Hook into change to update config
                    config.Set(originSnipingMode, vf.Value);

                contextMenuItem.SetupValueCycle(field.Value, new List<OptionDescription<int>> {
                        new OptionDescription<int>(0, "Laser only", new color?(color.Red), NeosAssets.Graphics.Icons.Tool.RayMode),
						new OptionDescription<int>(1, "Auto", new color?(color.Orange), NeosAssets.Graphics.Icons.Tool.RayMode2),
						new OptionDescription<int>(2, "Colliders and Origins", new color?(color.Yellow), NeosAssets.Graphics.Icons.Tool.AreaMode),
                        new OptionDescription<int>(3, "Origins Only", new color?(color.Green), originMode)
                    });
            }
        }



        [HarmonyPatch(typeof(DevToolTip), "TryOpenGizmo")]
        static class DevToolTip_TryOpenGizmo_Patch
        {
            public static bool Prefix(DevToolTip __instance, SyncRef<Slot> ____currentGizmo, SyncRef<Slot> ____previousGizmo)
            { // That moment when you don't feel like transpiling the entire method ;-;
                if (!config.GetValue(enabled)) return true; // Lazy man's disabled

                float3 tip = __instance.Tip;
				float maxDist = float.MaxValue;
				int selectedComponentCount = 0;
				Slot targetSlot = null;


				// Snipe range
				float snipeRange = config.GetValue(originSnipeRange);
				if (config.GetValue(rangeRespectScale)) // Multiply by user scale
					snipeRange *= __instance.LocalUserRoot.GlobalScale;
                

                int currentMode = config.GetValue(originSnipingMode); // Get current mode

                UserRoot activeUserRoot = __instance.Slot.ActiveUserRoot;
                Slot activeUserRootSlot = activeUserRoot?.Slot; // Get active user root slot



                RaycastHit? hit = null;
                if (currentMode < 2)
                    hit = GetHit(__instance); // Dont get hit if mode is 2


                if (currentMode == 0 && hit == null)
                    return false; // Skip if laser only and no hit

                if (hit != null)
                { // If hit 
                    RaycastHit value = hit.Value;
                    targetSlot = value.Collider.Slot;
                    maxDist = value.Distance;
                }

                if (currentMode > 0)
                { // If not laser only
                    if (targetSlot == null && currentMode != 3)
                    { // If no hit and not origins only
                        PhysicsManager physics = __instance.Physics;
                        float3 tip2 = __instance.Tip;
                        foreach (ICollider collider in physics.SphereOverlap(tip2, 0.025f))
                        { // Check for colliders in range
                            if (!collider.Slot.IsChildOf(activeUserRootSlot ?? __instance.Slot, true))
                                continue; // Make sure it's not a child of the active user or the tooltip
                            // Found a collider in range
                            maxDist = 0.025f;
                            targetSlot = collider.Slot;
                            break;
                        }
                    }
                    foreach (Slot slotCheck in __instance.World.AllSlots)
                    {
                        float3 globalPosition;
                        try
                        { // Try to get global position
                            globalPosition = slotCheck.GlobalPosition;
                        }
                        catch (Exception ex)
                        { // If it fails, skip
                            string str = "Exception getting global position for:\n";
                            string str2 = slotCheck?.ParentHierarchyToString();
                            UniLog.Error(str + str2 + "\n" + ex?.ToString(), false);
                            continue;
                        }
                        // Check if in range
                        float slotDist = MathX.Distance(globalPosition, tip);
                        if (slotDist <= snipeRange // 0.1f
                            && (slotDist < maxDist
                                || (MathX.Approximately(slotDist, maxDist) && slotCheck.Components.Count() > selectedComponentCount))
                                && !slotCheck.IsChildOf(activeUserRootSlot ?? __instance.Slot, true) // Make sure it's not a child of the active user or the tooltip
                                && (slotCheck.GetComponentInParents<IComponentGizmo>() ?? slotCheck.GetComponentInChildren<IComponentGizmo>()) == null) // Make sure it's not a gizmo
                        {
                            if (slotCheck.GetComponent((LogixNode n) => n.ActiveVisual == null) != null)
                                continue;
                            // Found a slot in range
                            maxDist = slotDist;
                            targetSlot = slotCheck;
                            selectedComponentCount = slotCheck.Components.Count();
                        }
                    }
				}

                // If no slot found, skip
                if (targetSlot == null)
					return false;

                
				if (____currentGizmo.Target == targetSlot)
                { // If it is current gizmo already
                    if (__instance.SelectionMode.Value == DevToolTip.Selection.Single)
                    { // If single selection mode clear it
                        ____currentGizmo.Target.RemoveGizmo();
						____currentGizmo.Target = null;
						return false;
					}
					SlotGizmo slotGizmo = ____currentGizmo.Target.TryGetGizmo<SlotGizmo>();
					if (slotGizmo != null)
                    { // If it has a gizmo
                        if (slotGizmo.IsFolded)
                        { // If it's buttons are hidden destroy it
                            slotGizmo.Slot.Destroy();
							return false;
						}
                        // If it's not hidden, hide it
                        slotGizmo.IsFolded = true;
						return false;
					}
				}
				else
				{
					SlotGizmo slotGizmo;
					if (____currentGizmo.Target != null)
                    { // If something is already selected
                        if (__instance.SelectionMode.Value == DevToolTip.Selection.Single)
                        { // If single selection mode
                            Slot target = ____previousGizmo.Target;
							if (target != null)
                            { // If previous gizmo exists, clear it
                                target.RemoveGizmo();
							}
							slotGizmo = ____currentGizmo.Target.TryGetGizmo<SlotGizmo>();
							if (slotGizmo != null)
                            { // If current gizmo exists, hide it's buttons
                                slotGizmo.IsFolded = true;
							}
						}
                        // Make current gizmo previous gizmo
                        ____previousGizmo.Target = ____currentGizmo.Target;
					}
                    // Assign current gizmo to target
                    ____currentGizmo.Target = targetSlot;
                    // Create gizmo
                    slotGizmo = ____currentGizmo.Target.GetGizmo<SlotGizmo>();
                    slotGizmo.IsFolded = false; // Show buttons

                    // Generate delegate for gizmo to call
                    var gizmoDelegate = (SlotGizmo.SlotGizmoReplacement)Delegate.CreateDelegate(typeof(SlotGizmo.SlotGizmoReplacement), __instance, onGizmoReplacedMethod); // Create delegat from that method
					slotGizmo.GizmoReplaced.Target = gizmoDelegate; // Set the delegate
				}
				return false;
			}

			[HarmonyReversePatch]
			[HarmonyPatch(typeof(ToolTip), "GetHit")]
			public static RaycastHit? GetHit(ToolTip instance)
			{
				// its a stub so it has no initial content
				throw new NotImplementedException("It's a stub");
			}
		}
    }
}