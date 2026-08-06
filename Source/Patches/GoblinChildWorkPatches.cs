using HarmonyLib;
using Verse;

namespace MUGB
{
    [HarmonyPatch(typeof(LifeStageWorkSettings), nameof(LifeStageWorkSettings.IsDisabled))]
    public static class LifeStageWorkSettings_IsDisabled_GoblinChildWorkPatch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result || pawn == null || !GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            __result = false;
        }
    }
}
