using HarmonyLib;
using RimWorld;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(PawnGroupMaker), nameof(PawnGroupMaker.GeneratePawns), new[] { typeof(PawnGroupMakerParms), typeof(bool) })]
    public static class GoblinTraderCaravanPointsCapPatch
    {
        public static void Prefix(PawnGroupMaker __instance, PawnGroupMakerParms parms)
        {
            if (__instance?.kindDef != PawnGroupKindDefOf.Trader || parms?.faction?.def == null)
            {
                return;
            }

            float cap = TraderPointCapFor(parms.faction.def);
            if (cap > 0f && parms.points > cap)
            {
                // KO intent: 바닐라 상단은 남은 points만큼 호위를 채우므로, 고블린처럼 값싼 폰은 20~30명까지 불어난다.
                // 상품 수는 유지하되 상단 인간형 호위 수만 줄이기 위해 고블린 상단의 Trader pawn group points를 캡한다.
                parms.points = cap;
            }
        }

        private static float TraderPointCapFor(FactionDef factionDef)
        {
            if (factionDef == MUGBDefOf.MUGB_GoblinCivilTribe)
            {
                return 220f;
            }

            if (factionDef == MUGBDefOf.MUGB_GoblinCivilMedieval)
            {
                return 280f;
            }

            return 0f;
        }
    }
}
