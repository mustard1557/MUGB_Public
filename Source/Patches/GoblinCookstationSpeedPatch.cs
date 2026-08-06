using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(Bill), nameof(Bill.GetWorkAmount))]
    public static class Bill_GetWorkAmount_GoblinCookstationPatch
    {
        private const float GoblinRecipeSpeedFactor = 1.1f;

        public static void Postfix(Bill __instance, ref float __result)
        {
            RecipeDef recipe = __instance?.recipe;
            if (recipe?.defName?.StartsWith("MUGB_") != true
                || (__instance.billStack?.billGiver as Thing)?.def != MUGBDefOf.MUGB_cookstation)
            {
                return;
            }

            __result /= GoblinRecipeSpeedFactor;
        }
    }
}
