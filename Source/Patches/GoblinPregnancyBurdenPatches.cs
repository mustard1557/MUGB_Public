using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    public static class GoblinPregnancyBurdenUtility
    {
        private static readonly FieldInfo MentalBreakerPawnField = AccessTools.Field(typeof(MentalBreaker), "pawn");

        public static bool HasGoblinPregnancyDrain(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.HasHediff(MUGBDefOf.MUGB_GoblinPregnancyDrain) == true;
        }

        public static Pawn PawnFor(MentalBreaker breaker)
        {
            return MentalBreakerPawnField?.GetValue(breaker) as Pawn;
        }
    }

    [HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.InitiateSlaveRebellionMtbDays))]
    public static class SlaveRebellionUtility_InitiateSlaveRebellionMtbDays_GoblinPregnancyPatch
    {
        public static void Postfix(Pawn pawn, ref float __result)
        {
            if (__result > 0f && GoblinPregnancyBurdenUtility.HasGoblinPregnancyDrain(pawn))
            {
                __result *= 3f;
            }
        }
    }

    [HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.TryDoRandomMoodCausedMentalBreak))]
    public static class MentalBreaker_TryDoRandomMoodCausedMentalBreak_GoblinPregnancyPatch
    {
        public static bool Prefix(MentalBreaker __instance, ref bool __result)
        {
            Pawn pawn = GoblinPregnancyBurdenUtility.PawnFor(__instance);
            if (GoblinPregnancyBurdenUtility.HasGoblinPregnancyDrain(pawn) && Rand.Chance(0.5f))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
