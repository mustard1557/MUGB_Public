using MUGB.Squads;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    public static class MUGBGoblinCaravanAmbushUtility
    {
        public const float MinimumAmbushPoints = 200f;
        public const int NearbySettlementDistance = 7;

        public static List<Faction> EligibleFactions()
        {
            return Find.FactionManager.AllFactionsListForReading
                .Where(IsEligibleFaction)
                .ToList();
        }

        public static bool IsEligibleFaction(Faction faction)
        {
            return faction != null
                && !faction.IsPlayer
                && !faction.Hidden
                && !faction.temporary
                && !faction.defeated
                && !faction.deactivated
                && !faction.def.raidsForbidden
                && faction.HostileTo(Faction.OfPlayer)
                && MUGB_SquadRaidUtility.IsGoblinRaidFaction(faction);
        }

        public static bool TryChooseFaction(PlanetTile tile, Faction requested, out Faction faction)
        {
            if (IsEligibleFaction(requested))
            {
                faction = requested;
                return true;
            }

            List<Faction> eligible = EligibleFactions();
            if (eligible.Count == 0)
            {
                faction = null;
                return false;
            }

            List<Faction> nearby = NearbySettlements(tile, eligible)
                .Select(settlement => settlement.Faction)
                .Distinct()
                .ToList();
            faction = nearby.Count > 0 ? nearby.RandomElement() : eligible.RandomElement();
            return true;
        }

        public static bool HasNearbyHostileGoblinSettlement(PlanetTile tile)
        {
            return tile.Valid && NearbySettlements(tile, EligibleFactions()).Any();
        }

        public static bool HasNearbySettlementForFaction(PlanetTile tile, Faction faction)
        {
            return tile.Valid
                && IsEligibleFaction(faction)
                && NearbySettlements(tile, new List<Faction> { faction }).Any();
        }

        private static IEnumerable<Settlement> NearbySettlements(PlanetTile tile, List<Faction> factions)
        {
            if (!tile.Valid || factions.NullOrEmpty())
            {
                yield break;
            }

            HashSet<Faction> allowed = new HashSet<Faction>(factions);
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement?.Faction == null || !allowed.Contains(settlement.Faction))
                {
                    continue;
                }

                if (Find.WorldGrid.ApproxDistanceInTiles(tile, settlement.Tile) <= NearbySettlementDistance)
                {
                    yield return settlement;
                }
            }
        }
    }

    public class IncidentWorker_GoblinCaravanAmbush : IncidentWorker_Ambush
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Caravan) || !base.CanFireNowSub(parms))
            {
                return false;
            }

            return MUGBGoblinCaravanAmbushUtility.EligibleFactions().Count > 0;
        }

        protected override List<Pawn> GeneratePawns(IncidentParms parms)
        {
            if (!MUGBGoblinCaravanAmbushUtility.TryChooseFaction(parms.target.Tile, parms.faction, out Faction faction))
            {
                Log.Error("[MUGB] Could not find a hostile goblin faction for caravan ambush.");
                return new List<Pawn>();
            }

            float originalPoints = Mathf.Max(0f, parms.points);
            parms.faction = faction;
            parms.points = Mathf.Max(originalPoints, MUGBGoblinCaravanAmbushUtility.MinimumAmbushPoints);
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;

            PawnGroupMakerParms groupParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Combat,
                parms);
            groupParms.generateFightersOnly = true;
            groupParms.dontUseSingleUseRocketLaunchers = true;
            groupParms.raidStrategy = parms.raidStrategy;

            MUGB_SquadRaidContext.Push(
                parms,
                debugTest: false,
                caravanAmbush: true,
                weakCaravanAmbush: originalPoints < MUGBGoblinCaravanAmbushUtility.MinimumAmbushPoints);
            try
            {
                return PawnGroupMakerUtility.GeneratePawns(groupParms).ToList();
            }
            finally
            {
                MUGB_SquadRaidContext.Pop();
            }
        }

        protected override LordJob CreateLordJob(List<Pawn> generatedPawns, IncidentParms parms)
        {
            return new LordJob_AssaultColony(parms.faction, canKidnap: true, canTimeoutOrFlee: false);
        }

        protected override string GetLetterText(Pawn anyPawn, IncidentParms parms)
        {
            Caravan caravan = parms.target as Caravan;
            string text = def.letterText.Formatted(
                    caravan?.Name ?? "yourCaravan".TranslateSimple(),
                    parms.faction.def.pawnsPlural,
                    parms.faction.NameColored)
                .Resolve()
                .CapitalizeFirst();

            if (MUGBGoblinCaravanAmbushUtility.HasNearbySettlementForFaction(parms.target.Tile, parms.faction))
            {
                text += "\n\n" + "MUGB_GoblinCaravanAmbushNearbySettlement".Translate();
            }

            if (MUGB_SquadRaidUtility.TryConsumeSummary(parms, out string summary))
            {
                text += "\n\n" + "MUGB_SquadRaidReport".Translate(summary);
            }
            return text;
        }
    }

    public class StorytellerComp_MUGBGoblinCaravanAmbush : StorytellerComp
    {
        private StorytellerCompProperties_MUGBGoblinCaravanAmbush Props =>
            (StorytellerCompProperties_MUGBGoblinCaravanAmbush)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (!(target is Caravan caravan) || Props.incident?.mtbDaysByBiome == null)
            {
                yield break;
            }

            BiomeDef biome = Find.WorldGrid[target.Tile].PrimaryBiome;
            MTBByBiome entry = Props.incident.mtbDaysByBiome.FirstOrDefault(value => value.biome == biome);
            if (entry == null)
            {
                yield break;
            }

            float mtbDays = entry.mtbDays * Mathf.Max(0.01f, Props.extraMtbFactor);
            if (MUGBGoblinCaravanAmbushUtility.HasNearbyHostileGoblinSettlement(target.Tile))
            {
                mtbDays *= 0.5f;
            }
            if (Props.applyCaravanVisibility)
            {
                mtbDays /= Mathf.Max(0.05f, caravan.Visibility);
            }

            if (!Rand.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, 1000f))
            {
                yield break;
            }

            IncidentParms parms = GenerateParms(Props.incident.category, target);
            if (Props.incident.Worker.CanFireNow(parms))
            {
                yield return new FiringIncident(Props.incident, this, parms);
            }
        }
    }

    public class StorytellerCompProperties_MUGBGoblinCaravanAmbush : StorytellerCompProperties
    {
        public IncidentDef incident;
        public float extraMtbFactor = 2f;
        public bool applyCaravanVisibility = true;

        public StorytellerCompProperties_MUGBGoblinCaravanAmbush()
        {
            compClass = typeof(StorytellerComp_MUGBGoblinCaravanAmbush);
        }
    }
}
