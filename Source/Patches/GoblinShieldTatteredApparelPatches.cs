using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(ThoughtWorker_ApparelDamaged), "CurrentStateInternal")]
    public static class ThoughtWorker_ApparelDamaged_IgnoreGoblinShieldsPatch
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (p?.apparel?.WornApparel == null)
            {
                return;
            }

            int stage = -1;
            for (int i = 0; i < p.apparel.WornApparel.Count; i++)
            {
                Apparel apparel = p.apparel.WornApparel[i];
                if (apparel == null || GoblinRenderNodeUtility.IsShieldApparelDef(apparel.def))
                {
                    continue;
                }

                float hitPointPercent = apparel.HitPoints / (float)apparel.MaxHitPoints;
                if (hitPointPercent < ThoughtWorker_ApparelDamaged.MinForTattered)
                {
                    stage = 1;
                    break;
                }

                if (hitPointPercent < ThoughtWorker_ApparelDamaged.MinForFrayed)
                {
                    stage = 0;
                }
            }

            __result = stage >= 0 ? ThoughtState.ActiveAtStage(stage) : ThoughtState.Inactive;
        }
    }
}
