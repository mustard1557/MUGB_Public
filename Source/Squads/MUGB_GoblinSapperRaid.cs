using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    public class RaidStrategyWorker_GoblinSapperRaid : RaidStrategyWorker
    {
        private const float MinimumRaidPoints = 850f;

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return MUGBMod.Settings?.enableGoblinSquadSystem == true
                && parms?.points >= MinimumRaidPoints
                && groupKind == PawnGroupKindDefOf.Combat
                && Squads.MUGB_SquadRaidUtility.IsGoblinRaidFaction(parms.faction)
                && base.CanUseWith(parms, groupKind);
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            return MakeAssaultLordJob(parms, sappers: false);
        }

        public override void MakeLords(IncidentParms parms, List<Pawn> pawns)
        {
            Map map = parms?.target as Map;
            if (map == null || pawns.NullOrEmpty())
            {
                return;
            }

            // Consume the generated layout so no stale IncidentParms entry survives, but keep the whole
            // raid in one vanilla sapper Lord: all non-sappers then escort the shared breaching group.
            Squads.MUGB_SquadRaidUtility.TryConsumeSquadLayout(parms, out _);
            List<Pawn> validPawns = pawns.Where(p => p != null && p.Spawned && p.Map == map).ToList();
            if (validPawns.Count > 0)
            {
                MakeLord(parms, map, validPawns, sappers: validPawns.Any(IsDedicatedGoblinSapper));
            }
        }

        private static bool IsDedicatedGoblinSapper(Pawn pawn)
        {
            string kindDefName = pawn?.kindDef?.defName;
            return kindDefName == "MUGB_GoblinKind_Sapper";
        }

        private static void MakeLord(IncidentParms parms, Map map, List<Pawn> pawns, bool sappers)
        {
            Lord lord = LordMaker.MakeNewLord(parms.faction, MakeAssaultLordJob(parms, sappers), map, pawns);
            lord.inSignalLeave = parms.inSignalEnd;
            QuestUtility.AddQuestTag(lord, parms.questTag);
        }

        private static LordJob MakeAssaultLordJob(IncidentParms parms, bool sappers)
        {
            return new LordJob_AssaultColony(
                parms.faction,
                canKidnap: parms.canKidnap,
                canTimeoutOrFlee: parms.canTimeoutOrFlee,
                sappers: sappers,
                useAvoidGridSmart: true,
                canSteal: parms.canSteal);
        }
    }

    public class RaidStrategyWorker_GoblinSuicideSapperRaid : RaidStrategyWorker
    {
        private const float MinimumRaidPoints = 850f;

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return MUGBMod.Settings?.enableGoblinSquadSystem == true
                && parms?.points >= MinimumRaidPoints
                && groupKind == PawnGroupKindDefOf.Combat
                && Squads.MUGB_SquadRaidUtility.IsGoblinRaidFaction(parms.faction)
                && base.CanUseWith(parms, groupKind);
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            return MakeSuicideAssaultLordJob(parms);
        }

        public override void MakeLords(IncidentParms parms, List<Pawn> pawns)
        {
            Map map = parms?.target as Map;
            if (map == null || pawns.NullOrEmpty())
            {
                return;
            }

            List<Pawn> validPawns = pawns.Where(p => p != null && p.Spawned && p.Map == map).ToList();
            if (validPawns.Count == 0)
            {
                return;
            }

            List<List<Pawn>> squads = SplitByPendingLayout(parms, validPawns);
            List<List<Pawn>> bomberSquads = squads.Where(MUGB_SuicideSapperUtility.ContainsSuicideBomber).ToList();
            if (bomberSquads.Count == 0)
            {
                MakeLord(parms, map, validPawns, sappers: false);
                return;
            }

            List<List<Pawn>> groups = bomberSquads.Select(squad => new List<Pawn>(squad)).ToList();
            int escortIndex = 0;
            foreach (List<Pawn> escort in squads.Where(squad => !MUGB_SuicideSapperUtility.ContainsSuicideBomber(squad)))
            {
                groups[escortIndex++ % groups.Count].AddRange(escort);
            }

            foreach (List<Pawn> group in groups)
            {
                MakeLord(parms, map, group, sappers: true);
            }
        }

        private static List<List<Pawn>> SplitByPendingLayout(IncidentParms parms, List<Pawn> pawns)
        {
            if (!Squads.MUGB_SquadRaidUtility.TryConsumeSquadLayout(parms, out List<int> sizes)
                || sizes.Sum() != pawns.Count
                || sizes.Any(size => size < 3))
            {
                return new List<List<Pawn>> { pawns };
            }

            List<List<Pawn>> result = new List<List<Pawn>>();
            int index = 0;
            foreach (int size in sizes)
            {
                result.Add(pawns.GetRange(index, size));
                index += size;
            }
            return result;
        }

        private static void MakeLord(IncidentParms parms, Map map, List<Pawn> pawns, bool sappers)
        {
            LordJob job = sappers
                ? MakeSuicideAssaultLordJob(parms)
                : new LordJob_AssaultColony(parms.faction, parms.canKidnap, parms.canTimeoutOrFlee, false, true, parms.canSteal);
            Lord lord = LordMaker.MakeNewLord(parms.faction, job, map, pawns);
            lord.inSignalLeave = parms.inSignalEnd;
            QuestUtility.AddQuestTag(lord, parms.questTag);
        }

        public static LordJob MakeSuicideAssaultLordJob(IncidentParms parms)
        {
            // A bomber group stays committed while it still has boomsticks. Vanilla sapper duties
            // handle path selection and escort behavior; the Harmony filter below selects one bomber.
            return new LordJob_AssaultColony(
                parms.faction,
                canKidnap: parms.canKidnap,
                canTimeoutOrFlee: false,
                sappers: true,
                useAvoidGridSmart: true,
                canSteal: parms.canSteal,
                breachers: false,
                canPickUpOpportunisticWeapons: false);
        }
    }

    public static class MUGB_SuicideSapperUtility
    {
        public static bool IsSuicideBomber(Pawn pawn)
        {
            string defName = pawn?.kindDef?.defName;
            return defName == "MUGB_GoblinKind_SlaveBoomstickSapper"
                || defName == "MUGB_GoblinKind_BoomstickShockEliteSapper";
        }

        public static bool ContainsSuicideBomber(IEnumerable<Pawn> pawns)
        {
            return pawns != null && pawns.Any(IsSuicideBomber);
        }

        public static bool HasUsableBoomstick(Pawn pawn)
        {
            return IsSuicideBomber(pawn)
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.equipment?.Primary?.def?.defName == "MUGB_GoblinBoomstick";
        }

        public static Pawn PendingBomberFor(Lord lord, Map map)
        {
            if (lord == null || map == null || MUGBDefOf.MUGB_GoblinBoomstickWick == null)
            {
                return null;
            }

            return map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinBoomstickWick)
                .OfType<GoblinBoomstickWick>()
                .Select(wick => wick.instigator)
                .FirstOrDefault(pawn => pawn != null && pawn.GetLord() == lord && IsSuicideBomber(pawn));
        }

        public static void NotifyBoomstickDetonated(Lord lord)
        {
            lord?.CurLordToil?.UpdateAllDuties();
        }
    }

    [HarmonyPatch(typeof(SappersUtility), nameof(SappersUtility.HasBuildingDestroyerWeapon))]
    public static class SappersUtility_HasBuildingDestroyerWeapon_MUGBGoblinPickaxePatch
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (MUGB_SuicideSapperUtility.IsSuicideBomber(p))
            {
                __result = true;
                return;
            }

            if (p?.kindDef?.defName != "MUGB_GoblinKind_Sapper")
            {
                return;
            }

            string weaponDefName = p.equipment?.Primary?.def?.defName;
            __result = weaponDefName == "DankPyon_MeleeWeapon_Pickaxe"
                || weaponDefName == "DankPyon_MeleeWeapon_MilitaryPick";
        }
    }

    [HarmonyPatch(typeof(SappersUtility), nameof(SappersUtility.IsGoodSapper))]
    public static class SappersUtility_IsGoodSapper_MUGBBoomstickSequencePatch
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (!MUGB_SuicideSapperUtility.IsSuicideBomber(p))
            {
                return;
            }

            Lord lord = p.GetLord();
            if (lord == null)
            {
                __result = false;
                return;
            }

            Pawn pendingBomber = MUGB_SuicideSapperUtility.PendingBomberFor(lord, p.Map);
            if (pendingBomber != null)
            {
                __result = pendingBomber == p;
                return;
            }

            Pawn activeBomber = lord.ownedPawns
                .Where(MUGB_SuicideSapperUtility.HasUsableBoomstick)
                .OrderBy(candidate => candidate.thingIDNumber)
                .FirstOrDefault();
            __result = activeBomber == p;
        }
    }
}
