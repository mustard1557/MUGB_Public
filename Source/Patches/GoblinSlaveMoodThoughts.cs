using RimWorld;
using Verse;

namespace MUGB
{
    internal static class GoblinSlaveMoodUtility
    {
        public static bool IsEligibleNonGoblinSlave(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true
                && pawn.IsSlaveOfColony
                && !GoblinUtility.IsGoblin(pawn)
                && Faction.OfPlayer?.def == MUGBDefOf.MUGB_PlayerGoblinFaction;
        }
    }

    public class ThoughtWorker_SurvivedGoblinCaptivity : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!GoblinSlaveMoodUtility.IsEligibleNonGoblinSlave(p)
                || GoblinPheromonePreferenceUtility.HasPreference(p))
            {
                return ThoughtState.Inactive;
            }

            HediffDef exposureDef = MUGBDefOf.MUGB_ToxicPheromoneExposure;
            return exposureDef != null
                && p.health?.hediffSet?.GetFirstHediffOfDef(exposureDef)?.Severity > 0f
                    ? ThoughtState.ActiveDefault
                    : ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_GoblinSlaveMood : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            return GoblinSlaveMoodUtility.IsEligibleNonGoblinSlave(p)
                && GoblinPheromonePreferenceUtility.HasPreference(p)
                    ? ThoughtState.ActiveDefault
                    : ThoughtState.Inactive;
        }
    }
}
