using RimWorld;
using Verse;

namespace MUGB
{
    public class IncidentWorker_GoblinCultistSkipAbduction : IncidentWorker_PsychicRitualSiege
    {
        private const string SkipAbductionDefName = "SkipAbduction";

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!ModsConfig.AnomalyActive
                || DefDatabase<PsychicRitualDef>.GetNamedSilentFail(SkipAbductionDefName) == null)
            {
                return false;
            }

            Faction faction = FindCultistFaction();
            if (faction == null)
            {
                return false;
            }

            parms.faction = faction;
            return base.CanFireNowSub(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!ModsConfig.AnomalyActive)
            {
                return false;
            }

            PsychicRitualDef ritual = DefDatabase<PsychicRitualDef>.GetNamedSilentFail(SkipAbductionDefName);
            if (ritual == null)
            {
                return false;
            }

            parms.psychicRitualDef = ritual;
            return base.TryExecuteWorker(parms);
        }

        protected override bool TryResolveRaidFaction(IncidentParms parms)
        {
            Faction faction = FindCultistFaction();
            if (faction == null)
            {
                return false;
            }

            parms.faction = faction;
            return true;
        }

        protected override string GetLetterLabel(IncidentParms parms)
        {
            return "MUGB_GoblinCultistSkipAbductionLetterLabel".Translate();
        }

        private static Faction FindCultistFaction()
        {
            Faction faction = Find.FactionManager?.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCultists);
            return faction != null && !faction.defeated ? faction : null;
        }
    }
}
