using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(RitualAttachableOutcomeEffectWorker_NearbyFactionGoodwill), nameof(RitualAttachableOutcomeEffectWorker_NearbyFactionGoodwill.Apply))]
    public static class RitualAttachableOutcomeEffectWorker_NearbyFactionGoodwill_MUGBSingleOutcomePatch
    {
        private const string SlaveMarriageRitual = "MUGB_SlaveMarriageCeremony";
        private const string GoblinBeaconRitual = "MUGB_GoblinBeaconSignal";
        private const int CivilGoblinGoodwillGain = 6;
        private const int SupremacistGoblinGoodwillGain = 3;

        private static readonly HashSet<string> SingleOutcomeRituals = new HashSet<string>
        {
            SlaveMarriageRitual,
            GoblinBeaconRitual
        };

        public static bool Prefix(
            RitualAttachableOutcomeEffectWorker_NearbyFactionGoodwill __instance,
            Dictionary<Pawn, int> totalPresence,
            LordJob_Ritual jobRitual,
            RitualOutcomePossibility outcome,
            ref string extraOutcomeDesc,
            ref LookTargets letterLookTargets)
        {
            RitualOutcomeEffectDef outcomeDef = jobRitual?.Ritual?.outcomeEffect?.def;
            if (outcomeDef == null
                || !SingleOutcomeRituals.Contains(outcomeDef.defName)
                || outcomeDef.outcomeChances?.Count != 1)
            {
                return true;
            }

            extraOutcomeDesc = null;
            Map map = jobRitual.Map;
            Faction player = Faction.OfPlayer;
            if (map == null || player == null)
            {
                return false;
            }

            Settlement nearest = null;
            int nearestGoodwillGain = 0;
            float nearestDistance = float.PositiveInfinity;
            foreach (Settlement settlement in Find.WorldObjects.AllSettlementsOnLayer(map.Tile.Layer).OfType<Settlement>())
            {
                Faction faction = settlement?.Faction;
                int goodwillGain = GoodwillGainFor(outcomeDef.defName, faction, player);
                if (goodwillGain <= 0 || !faction.CanChangeGoodwillFor(player, goodwillGain))
                {
                    continue;
                }

                float distance = Find.WorldGrid.ApproxDistanceInTiles(map.Tile, settlement.Tile);
                if (distance < nearestDistance)
                {
                    nearest = settlement;
                    nearestGoodwillGain = goodwillGain;
                    nearestDistance = distance;
                }
            }

            if (nearest != null
                && nearest.Faction.TryAffectGoodwillWith(
                    player,
                    nearestGoodwillGain,
                    canSendMessage: true,
                    canSendHostilityLetter: true,
                    HistoryEventDefOf.RitualDone))
            {
                letterLookTargets = new LookTargets(
                    (letterLookTargets.targets ?? new List<GlobalTargetInfo>())
                    .Concat(Gen.YieldSingle((GlobalTargetInfo)nearest)));
                extraOutcomeDesc = __instance.def.letterInfoText.Formatted(
                    nearestGoodwillGain.Named("AMOUNT"),
                    nearest.Faction.Named("FACTION"));
            }

            return false;
        }

        private static int GoodwillGainFor(string ritualDefName, Faction faction, Faction player)
        {
            if (faction == null
                || faction == player
                || !MGBFactionInjectionComponent.IsGoblinFaction(faction))
            {
                return 0;
            }

            Ideo ideo = faction.ideos?.PrimaryIdeo;
            bool hasBlinia = MUGBDefOf.MUGB_ChildrenOfBlinia != null
                && ideo?.HasMeme(MUGBDefOf.MUGB_ChildrenOfBlinia) == true;
            if (!hasBlinia)
            {
                return 0;
            }

            bool hasSupremacy = MUGBDefOf.MUGB_GoblinSupremacy != null
                && ideo.HasMeme(MUGBDefOf.MUGB_GoblinSupremacy);
            if (ritualDefName == GoblinBeaconRitual)
            {
                return hasSupremacy ? 0 : CivilGoblinGoodwillGain;
            }

            if (ritualDefName != SlaveMarriageRitual)
            {
                return 0;
            }

            if (!hasSupremacy)
            {
                return CivilGoblinGoodwillGain;
            }

            return SupremacistGoblinGoodwillGain;
        }
    }
}
