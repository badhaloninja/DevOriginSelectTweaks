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

		public override void OnEngineInit()
        {
			config = GetConfiguration();
            Harmony harmony = new Harmony("me.badhaloninja.DevOriginSelectTweaks");
            harmony.PatchAll();
        }


        [HarmonyPatch(typeof(DevToolTip), "GenerateMenuItems")]
        class MenuItems
        {
            public static void Postfix(ContextMenu menu)
            {
                if (!config.GetValue(enabled)) return; // Lazy man's disabled
                Uri icon = new Uri("neosdb:///b778eb36757426a5a3e669844e064a2a3d80f54a4a58e3978ad28781bfebaf81.png");
                ContextMenuItem contextMenuItem = menu.AddItem("What", icon, default(color));

                var field = contextMenuItem.Slot.AttachComponent<ValueField<int>>();
                field.Value.Value = config.GetValue(originSnipingMode);
                field.Value.OnValueChange += (vf) =>
                {  // Yeah
                    config.Set(originSnipingMode, vf.Value);
                };

                //contextMenuItem.AttachOptionDescriptionDriver<int>();
                contextMenuItem.SetupValueCycle(field.Value, new List<OptionDescription<int>> {
                        new OptionDescription<int>(0, "Laser only", new color?(color.Red), NeosAssets.Graphics.Icons.Tool.RayMode),
						new OptionDescription<int>(1, "Auto", new color?(color.Orange), NeosAssets.Graphics.Icons.Tool.RayMode2),
						new OptionDescription<int>(2, "Colliders and Origins", new color?(color.Yellow), NeosAssets.Graphics.Icons.Tool.AreaMode),
                        new OptionDescription<int>(3, "Origins Only", new color?(color.Green), icon)
                    });
            }
        }



        [HarmonyPatch(typeof(DevToolTip), "TryOpenGizmo")]
        class DevToolTip_TryOpenGizmo_Patch
        {
            public static bool Prefix(DevToolTip __instance, SyncRef<Slot> ____currentGizmo, SyncRef<Slot> ____previousGizmo)
            { // That moment when you don't see any way other than replacing the entire method ;-;
                if (!config.GetValue(enabled)) return true; // Lazy man's disabled

                float3 tip = __instance.Tip;
				float maxDist = float.MaxValue;
				int selectedComponentCount = 0;
				Slot targetSlot = null;


				// Snipe range
				float snipeRange = config.GetValue(originSnipeRange);
				if (config.GetValue(rangeRespectScale))
				{ // Multiply by user scale
					snipeRange *= __instance.LocalUserRoot.GlobalScale;
                }

                int currentMode = config.GetValue(originSnipingMode);

                UserRoot activeUserRoot = __instance.Slot.ActiveUserRoot;
                Slot activeUserRootSlot = (activeUserRoot != null) ? activeUserRoot.Slot : null;



                RaycastHit? hit = null;
                if (currentMode < 2)
                    hit = GetHit(__instance); // Dont get hit if mode is 2


                if (currentMode == 0 && hit == null)
                { // Stop if hit missed and if mode is 0
                    return false;
                }

                if (hit != null)
                {
                    RaycastHit value = hit.Value;
                    targetSlot = value.Collider.Slot;
                    maxDist = value.Distance;
                }

                if (currentMode > 0)
                {
                    if (targetSlot == null && currentMode != 3)
                    {
                        PhysicsManager physics = __instance.Physics;
                        float3 tip2 = __instance.Tip;
                        foreach (ICollider collider in physics.SphereOverlap(tip2, 0.025f))
                        {
                            if (!collider.Slot.IsChildOf(activeUserRootSlot ?? __instance.Slot, true))
                            {
                                maxDist = 0.025f;
                                targetSlot = collider.Slot;
                                break;
                            }
                        }
                    }
                    foreach (Slot slotCheck in __instance.World.AllSlots)
                    {
                        float3 globalPosition;
                        try
                        {
                            globalPosition = slotCheck.GlobalPosition;
                        }
                        catch (Exception ex)
                        {
                            string str = "Exception getting global position for:\n";
                            string str2 = (slotCheck != null) ? slotCheck.ParentHierarchyToString() : null;
                            string str3 = "\n";
                            UniLog.Error(str + str2 + str3 + ((ex != null) ? ex.ToString() : null), false);
                            continue;
                        }
                        float slotDist = MathX.Distance(globalPosition, tip);
                        if (slotDist <= snipeRange // 0.1f
                            && (slotDist < maxDist
                                || (MathX.Approximately(slotDist, maxDist) && slotCheck.Components.Count() > selectedComponentCount))
                                && !slotCheck.IsChildOf(activeUserRootSlot ?? __instance.Slot, true)
                                && (slotCheck.GetComponentInParents<IComponentGizmo>() ?? slotCheck.GetComponentInChildren<IComponentGizmo>()) == null)
                        {
                            if (slotCheck.GetComponent((LogixNode n) => n.ActiveVisual == null) == null)
                            {
                                maxDist = slotDist;
                                targetSlot = slotCheck;
                                selectedComponentCount = slotCheck.Components.Count();
                            }
                        }
                    }
				}
				if (targetSlot == null)
				{
					return false;
				}
				if (____currentGizmo.Target == targetSlot)
				{
					if (__instance.SelectionMode.Value == DevToolTip.Selection.Single)
					{
						____currentGizmo.Target.RemoveGizmo();
						____currentGizmo.Target = null;
						return false;
					}
					SlotGizmo slotGizmo = ____currentGizmo.Target.TryGetGizmo<SlotGizmo>();
					if (slotGizmo != null)
					{
						if (slotGizmo.IsFolded)
						{
							slotGizmo.Slot.Destroy();
							return false;
						}
						slotGizmo.IsFolded = true;
						return false;
					}
				}
				else
				{
					SlotGizmo slotGizmo;
					if (____currentGizmo.Target != null)
					{
						if (__instance.SelectionMode.Value == DevToolTip.Selection.Single)
						{
							Slot target = ____previousGizmo.Target;
							if (target != null)
							{
								target.RemoveGizmo();
							}
							slotGizmo = ____currentGizmo.Target.TryGetGizmo<SlotGizmo>();
							if (slotGizmo != null)
							{
								slotGizmo.IsFolded = true;
							}
						}
						____previousGizmo.Target = ____currentGizmo.Target;
					}
					____currentGizmo.Target = targetSlot;
					slotGizmo = ____currentGizmo.Target.GetGizmo<SlotGizmo>();
					slotGizmo.IsFolded = false;

					MethodInfo method = typeof(DevToolTip).GetMethod("OnGizmoReplaced", BindingFlags.NonPublic | BindingFlags.Instance); // Fetch method
					var gizmoDelegate = (SlotGizmo.SlotGizmoReplacement)Delegate.CreateDelegate(typeof(SlotGizmo.SlotGizmoReplacement), __instance, method); // Create delegat from that method
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