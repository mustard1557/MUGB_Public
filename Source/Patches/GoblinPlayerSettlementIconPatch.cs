using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandedIconScreenRect))]
    public static class GoblinPlayerSettlementIconPatch
    {
        private const float IconScale = 1.2f;

        public static void Postfix(WorldObject o, ref Rect __result)
        {
            if (!(o is Settlement settlement)
                || settlement.Faction?.def?.defName != "MUGB_PlayerGoblinFaction")
            {
                return;
            }

            Vector2 center = __result.center;
            __result.width *= IconScale;
            __result.height *= IconScale;
            __result.center = center;
        }
    }
}
