using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    public class RaidStrategyWorker_GoblinCompositeSapperRaid : RaidStrategyWorker
    {
        private const float MinimumRaidPoints = 1800f;

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return MUGBMod.Settings?.enableGoblinSquadSystem == true
                && MUGBMod.Settings.enableGoblinCompositeRaids
                && parms?.points >= MinimumRaidPoints
                && groupKind == PawnGroupKindDefOf.Combat
                && Squads.MUGB_SquadRaidUtility.IsGoblinRaidFaction(parms.faction)
                && base.CanUseWith(parms, groupKind);
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            return MakeAssaultJob(parms, pawns.Any(IsDedicatedSapper));
        }

        public override void MakeLords(IncidentParms parms, List<Pawn> pawns)
        {
            Map map = parms?.target as Map;
            if (map == null || pawns.NullOrEmpty())
            {
                return;
            }

            Squads.MUGB_SquadRaidUtility.TryConsumeSquadLayout(parms, out _);
            List<Pawn> validPawns = pawns.Where(pawn => pawn?.Spawned == true && pawn.Map == map).ToList();
            if (validPawns.Count == 0)
            {
                return;
            }

            List<List<Pawn>> axes = IncidentParmsUtility.SplitIntoGroups(validPawns, parms.pawnGroups)
                .Where(group => !group.NullOrEmpty())
                .ToList();
            if (axes.Count != 2)
            {
                MakeLord(parms, map, validPawns, sappers: validPawns.Any(IsDedicatedSapper));
                return;
            }

            foreach (List<Pawn> axis in axes)
            {
                MakeLord(parms, map, axis, sappers: axis.Any(IsDedicatedSapper));
            }
        }

        private static bool IsDedicatedSapper(Pawn pawn)
        {
            return pawn?.kindDef?.defName == "MUGB_GoblinKind_Sapper";
        }

        private static void MakeLord(IncidentParms parms, Map map, List<Pawn> pawns, bool sappers)
        {
            Lord lord = LordMaker.MakeNewLord(parms.faction, MakeAssaultJob(parms, sappers), map, pawns);
            lord.inSignalLeave = parms.inSignalEnd;
            QuestUtility.AddQuestTag(lord, parms.questTag);
        }

        private static LordJob MakeAssaultJob(IncidentParms parms, bool sappers)
        {
            return new LordJob_AssaultColony(
                parms.faction,
                canKidnap: parms.canKidnap,
                canTimeoutOrFlee: parms.canTimeoutOrFlee,
                sappers: sappers,
                useAvoidGridSmart: sappers,
                canSteal: parms.canSteal);
        }
    }

    public class PawnsArrivalModeWorker_GoblinCompositeTwoDirections : PawnsArrivalModeWorker
    {
        private const float SapperAxisTargetFraction = 0.40f;

        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            Map map = parms?.target as Map;
            if (map == null
                || pawns.NullOrEmpty())
            {
                return;
            }

            if (pawns.Count < 2)
            {
                PawnsArrivalModeDefOf.EdgeWalkIn.Worker.Arrive(pawns, parms);
                return;
            }

            if (!Squads.MUGB_SquadRaidUtility.TryGetSquadLayout(parms, out List<int> squadSizes)
                || squadSizes.Count < 2
                || squadSizes.Sum() != pawns.Count)
            {
                int midpoint = Mathf.Clamp(pawns.Count / 2, 1, pawns.Count - 1);
                SpawnAxes(map, parms, pawns.GetRange(0, midpoint), pawns.GetRange(midpoint, pawns.Count - midpoint));
                return;
            }

            List<List<Pawn>> squads = SplitIntoSquads(pawns, squadSizes);
            List<List<Pawn>> sapperSquads = squads.Where(ContainsDedicatedSapper).ToList();
            List<List<Pawn>> normalSquads = squads.Where(squad => !ContainsDedicatedSapper(squad)).ToList();
            if (sapperSquads.Count == 0 || normalSquads.Count == 0)
            {
                List<Pawn> firstAxis = new List<Pawn>();
                List<Pawn> secondAxis = new List<Pawn>();
                foreach (List<Pawn> squad in squads.OrderByDescending(squad => squad.Count))
                {
                    (firstAxis.Count <= secondAxis.Count ? firstAxis : secondAxis).AddRange(squad);
                }
                SpawnAxes(map, parms, firstAxis, secondAxis);
                return;
            }

            List<Pawn> sapperAxis = sapperSquads.SelectMany(squad => squad).ToList();
            int targetCount = Mathf.RoundToInt(pawns.Count * SapperAxisTargetFraction);
            while (normalSquads.Count > 1 && sapperAxis.Count < targetCount)
            {
                List<Pawn> escort = normalSquads.OrderBy(squad => squad.Count).First();
                normalSquads.Remove(escort);
                sapperAxis.AddRange(escort);
            }

            List<Pawn> assaultAxis = normalSquads.SelectMany(squad => squad).ToList();
            SpawnAxes(map, parms, sapperAxis, assaultAxis);
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            parms.spawnRotation = Rot4.Random;
            return true;
        }

        private static List<List<Pawn>> SplitIntoSquads(List<Pawn> pawns, List<int> squadSizes)
        {
            List<List<Pawn>> squads = new List<List<Pawn>>(squadSizes.Count);
            int index = 0;
            foreach (int size in squadSizes)
            {
                squads.Add(pawns.GetRange(index, size));
                index += size;
            }
            return squads;
        }

        private static bool ContainsDedicatedSapper(IEnumerable<Pawn> squad)
        {
            return squad.Any(IsDedicatedSapperPawn);
        }

        private static bool IsDedicatedSapperPawn(Pawn pawn)
        {
            return pawn?.kindDef?.defName == "MUGB_GoblinKind_Sapper";
        }

        private static void SpawnAxes(Map map, IncidentParms parms, List<Pawn> firstAxis, List<Pawn> secondAxis)
        {
            List<Pair<List<Pawn>, IntVec3>> arrivals = new List<Pair<List<Pawn>, IntVec3>>(2);
            IntVec3 firstCenter = PawnsArrivalModeWorkerUtility.FindNewMapEdgeGroupCenter(map, arrivals, arriveInPods: false);
            arrivals.Add(new Pair<List<Pawn>, IntVec3>(firstAxis, firstCenter));
            IntVec3 secondCenter = PawnsArrivalModeWorkerUtility.FindNewMapEdgeGroupCenter(map, arrivals, arriveInPods: false);
            arrivals.Add(new Pair<List<Pawn>, IntVec3>(secondAxis, secondCenter));

            Pawn sapperTarget = firstAxis.FirstOrDefault(IsDedicatedSapperPawn)
                ?? secondAxis.FirstOrDefault(IsDedicatedSapperPawn);
            if (sapperTarget != null)
            {
                Squads.MUGB_CompositeSapperLetterTargetUtility.Register(parms, sapperTarget);
            }

            PawnsArrivalModeWorkerUtility.SetPawnGroupsInfo(parms, arrivals);
            foreach (Pair<List<Pawn>, IntVec3> arrival in arrivals)
            {
                foreach (Pawn pawn in arrival.First)
                {
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(arrival.Second, map, 8);
                    GenSpawn.Spawn(pawn, cell, map, parms.spawnRotation);
                }
            }
        }
    }

    [HarmonyPatch]
    public static class IncidentWorker_SendStandardLetter_GoblinCompositeTargetPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(IncidentWorker),
                "SendStandardLetter",
                new[]
                {
                    typeof(TaggedString),
                    typeof(TaggedString),
                    typeof(LetterDef),
                    typeof(IncidentParms),
                    typeof(LookTargets),
                    typeof(NamedArgument[])
                });
        }

        public static void Prefix(IncidentParms parms, ref LookTargets lookTargets)
        {
            if (parms?.raidStrategy == MUGBDefOf.MUGB_GoblinCompositeSapperRaid
                && Squads.MUGB_CompositeSapperLetterTargetUtility.TryConsume(parms, out Pawn sapper))
            {
                lookTargets = new LookTargets(sapper);
            }
        }
    }
}

namespace MUGB.Squads
{
    internal static class MUGB_CompositeSapperLetterTargetUtility
    {
        private static readonly Dictionary<IncidentParms, Pawn> PendingTargets =
            new Dictionary<IncidentParms, Pawn>();

        public static void Register(IncidentParms parms, Pawn sapper)
        {
            if (parms != null && sapper != null)
            {
                PendingTargets[parms] = sapper;
            }
        }

        public static bool TryConsume(IncidentParms parms, out Pawn sapper)
        {
            sapper = null;
            if (parms == null || !PendingTargets.TryGetValue(parms, out sapper))
            {
                return false;
            }

            PendingTargets.Remove(parms);
            return sapper != null && !sapper.Destroyed;
        }
    }
}
