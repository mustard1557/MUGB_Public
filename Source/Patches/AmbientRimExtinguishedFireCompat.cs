using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch]
    public static class AmbientRimExtinguishedFireCompat
    {
        private const string CategoryTypeName = "ambient.Category.CampfireTorchViewCategory";
        private const string MapSoundsTypeName = "ambient.MapSounds";
        private static readonly HashSet<string> AmbientFireDefs = new HashSet<string>
        {
            "Campfire",
            "Brazier"
        };

        private static readonly Type CategoryType = AccessTools.TypeByName(CategoryTypeName);
        private static readonly Type MapSoundsType = AccessTools.TypeByName(MapSoundsTypeName);
        private static readonly FieldInfo CategoryMapField = CategoryType == null
            ? null
            : AccessTools.Field(CategoryType, "map");
        private static readonly MethodInfo ReloadSoundsMethod = MapSoundsType == null
            ? null
            : AccessTools.Method(MapSoundsType, "ReloadSounds");

        public static bool Prepare()
        {
            return TargetMethod() != null && CategoryMapField != null;
        }

        public static MethodBase TargetMethod()
        {
            return CategoryType == null
                ? null
                : AccessTools.Method(CategoryType, "TileConditionSuitable", new[] { typeof(IntVec3) });
        }

        public static void Postfix(object __instance, IntVec3 cell, ref bool __result)
        {
            if (!__result || !(CategoryMapField.GetValue(__instance) is Map map))
            {
                return;
            }

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                ThingWithComps building = things[i] as ThingWithComps;
                if (building?.def?.defName == null || !AmbientFireDefs.Contains(building.def.defName))
                {
                    continue;
                }

                CompRefuelable refuelable = building.GetComp<CompRefuelable>();
                CompFlickable flickable = building.GetComp<CompFlickable>();
                if (refuelable?.HasFuel == true && (flickable?.SwitchIsOn ?? true))
                {
                    return;
                }
            }

            __result = false;
        }

        public static void RefreshAfterExtinguishing(ThingWithComps building)
        {
            if (building?.Map == null
                || building.def?.defName == null
                || !AmbientFireDefs.Contains(building.def.defName)
                || MapSoundsType == null
                || ReloadSoundsMethod == null)
            {
                return;
            }

            MapComponent mapSounds = building.Map.GetComponent(MapSoundsType);
            if (mapSounds != null)
            {
                ReloadSoundsMethod.Invoke(mapSounds, null);
            }
        }
    }
}
