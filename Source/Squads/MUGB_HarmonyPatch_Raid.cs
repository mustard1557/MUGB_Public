using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MUGB.Squads
{
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    public static class IncidentWorker_Raid_TryGenerateRaidInfo_MUGBSquadContextPatch
    {
        public static void Prefix(IncidentParms parms, bool debugTest)
        {
            MUGB_SquadRaidContext.Push(parms, debugTest);
        }

        public static void Finalizer()
        {
            MUGB_SquadRaidContext.Pop();
        }

        public static void Postfix(IncidentParms parms, bool debugTest, bool __result)
        {
            if (debugTest || !__result)
            {
                MUGB_SquadRaidUtility.ClearPendingSquadLayout(parms);
            }
        }
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints))]
    public static class PawnGroupMakerUtility_ChoosePawnGenOptionsByPoints_MUGBSquadPatch
    {
        public static void Postfix(float pointsTotal, List<PawnGenOption> options, PawnGroupMakerParms groupParms, ref IEnumerable<PawnGenOptionWithXenotype> __result)
        {
            IncidentParms raidParms = MUGB_SquadRaidContext.CurrentParms;
            if (!MUGB_SquadRaidUtility.ShouldProcess(groupParms, raidParms, MUGB_SquadRaidContext.DebugTest))
            {
                return;
            }

            if (groupParms.seed.HasValue)
            {
                Rand.PushState(groupParms.seed.Value);
            }

            bool made;
            List<PawnGenOptionWithXenotype> squadOptions;
            string summary;
            List<int> squadSizes;
            try
            {
                made = MUGB_SquadRaidUtility.TryMakeSquadOptions(
                    pointsTotal, groupParms, out squadOptions, out summary, out squadSizes);
            }
            finally
            {
                if (groupParms.seed.HasValue)
                {
                    Rand.PopState();
                }
            }

            if (made)
            {
                __result = squadOptions;
                MUGB_SquadRaidUtility.SetPendingSummary(raidParms, summary);
                MUGB_SquadRaidUtility.SetPendingSquadLayout(raidParms, squadSizes);
            }
        }
    }

    [HarmonyPatch(typeof(RaidStrategyWorker), nameof(RaidStrategyWorker.MakeLords))]
    public static class RaidStrategyWorker_MakeLords_MUGBSquadPatch
    {
        private static readonly System.Type[] MakeLordJobParameters =
        {
            typeof(IncidentParms), typeof(Map), typeof(List<Pawn>), typeof(int)
        };

        public static bool Prefix(RaidStrategyWorker __instance, IncidentParms parms, List<Pawn> pawns)
        {
            if (MUGBMod.Settings?.enableGoblinSquadSystem != true
                || !MUGB_SquadRaidUtility.IsGoblinRaidFaction(parms?.faction)
                || !IsStandardSquadStrategy(parms.raidStrategy)
                || pawns.NullOrEmpty()
                || !(parms.target is Map map)
                || !MUGB_SquadRaidUtility.TryConsumeSquadLayout(parms, out List<int> squadSizes)
                || squadSizes.Any(size => size < 3 || size > 6)
                || squadSizes.Sum() != pawns.Count)
            {
                return true;
            }

            MethodInfo makeLordJob = AccessTools.Method(__instance.GetType(), "MakeLordJob", MakeLordJobParameters);
            if (makeLordJob == null)
            {
                return true;
            }

            int raidSeed = Rand.Int;
            List<List<Pawn>> squads = SplitPawnsIntoSquads(pawns, squadSizes);
            List<LordJob> jobs = new List<LordJob>(squads.Count);
            try
            {
                foreach (List<Pawn> squad in squads)
                {
                    LordJob job = makeLordJob.Invoke(__instance, new object[] { parms, map, squad, raidSeed }) as LordJob;
                    if (job == null)
                    {
                        return true;
                    }
                    jobs.Add(job);
                }
            }
            catch (System.Exception exception)
            {
                Log.Warning($"[MUGB] Could not preserve goblin squad Lords; using the vanilla raid Lord instead. {exception.GetType().Name}: {exception.Message}");
                return true;
            }

            for (int i = 0; i < squads.Count; i++)
            {
                Lord lord = LordMaker.MakeNewLord(parms.faction, jobs[i], map, squads[i]);
                lord.inSignalLeave = parms.inSignalEnd;
                QuestUtility.AddQuestTag(lord, parms.questTag);
            }
            return false;
        }

        private static bool IsStandardSquadStrategy(RaidStrategyDef strategy)
        {
            string defName = strategy?.defName;
            return defName == "ImmediateAttack"
                || defName == "ImmediateAttackSmart"
                || defName == "StageThenAttack";
        }

        private static List<List<Pawn>> SplitPawnsIntoSquads(List<Pawn> pawns, List<int> squadSizes)
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
    }

    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkInGroups), nameof(PawnsArrivalModeWorker_EdgeWalkInGroups.Arrive))]
    public static class PawnsArrivalModeWorker_EdgeWalkInGroups_MUGBSquadPatch
    {
        public static bool Prefix(List<Pawn> pawns, IncidentParms parms)
        {
            return !MUGB_MultiDirectionSquadArrivalUtility.TryArriveBySquad(
                pawns,
                parms,
                allowSingleGroup: false,
                maxGroups: 3);
        }
    }

    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkInDistributedGroups), nameof(PawnsArrivalModeWorker_EdgeWalkInDistributedGroups.Arrive))]
    public static class PawnsArrivalModeWorker_EdgeWalkInDistributedGroups_MUGBSquadPatch
    {
        public static bool Prefix(List<Pawn> pawns, IncidentParms parms)
        {
            return !MUGB_MultiDirectionSquadArrivalUtility.TryArriveBySquad(
                pawns,
                parms,
                allowSingleGroup: true,
                maxGroups: 4);
        }
    }

    internal static class MUGB_MultiDirectionSquadArrivalUtility
    {
        public static bool TryArriveBySquad(
            List<Pawn> pawns,
            IncidentParms parms,
            bool allowSingleGroup,
            int maxGroups)
        {
            Map map = parms?.target as Map;
            if (pawns.NullOrEmpty()
                || map == null
                || !MUGB_SquadRaidUtility.IsGoblinRaidFaction(parms.faction)
                || !MUGB_SquadRaidUtility.TryGetSquadLayout(parms, out List<int> squadSizes)
                || squadSizes.NullOrEmpty()
                || squadSizes.Any(size => size <= 0)
                || squadSizes.Sum() != pawns.Count)
            {
                return false;
            }

            List<List<Pawn>> squads = SplitPawnsIntoSquads(pawns, squadSizes);
            if (squads.Count == 0)
            {
                return false;
            }

            int groupLimit = Mathf.Clamp(maxGroups, 1, squads.Count);
            int groupCount = groupLimit == 1
                ? 1
                : Rand.RangeInclusive(allowSingleGroup ? 1 : 2, groupLimit);

            List<List<Pawn>> pawnGroups = AssignSquadsToBalancedGroups(squads, groupCount);
            List<Pair<List<Pawn>, IntVec3>> arrivals = new List<Pair<List<Pawn>, IntVec3>>(pawnGroups.Count);
            for (int i = 0; i < pawnGroups.Count; i++)
            {
                IntVec3 center = PawnsArrivalModeWorkerUtility.FindNewMapEdgeGroupCenter(
                    map,
                    arrivals,
                    arriveInPods: false);
                arrivals.Add(new Pair<List<Pawn>, IntVec3>(pawnGroups[i], center));
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

            return true;
        }

        private static List<List<Pawn>> SplitPawnsIntoSquads(List<Pawn> pawns, List<int> squadSizes)
        {
            List<List<Pawn>> squads = new List<List<Pawn>>(squadSizes.Count);
            int pawnIndex = 0;
            foreach (int squadSize in squadSizes)
            {
                List<Pawn> squad = new List<Pawn>(squadSize);
                for (int i = 0; i < squadSize; i++)
                {
                    squad.Add(pawns[pawnIndex++]);
                }
                squads.Add(squad);
            }
            return squads;
        }

        private static List<List<Pawn>> AssignSquadsToBalancedGroups(List<List<Pawn>> squads, int groupCount)
        {
            List<List<Pawn>> groups = new List<List<Pawn>>(groupCount);
            int[] groupSizes = new int[groupCount];
            for (int i = 0; i < groupCount; i++)
            {
                groups.Add(new List<Pawn>());
            }

            foreach (List<Pawn> squad in squads.OrderByDescending(squad => squad.Count))
            {
                int targetGroup = 0;
                for (int i = 1; i < groupSizes.Length; i++)
                {
                    if (groupSizes[i] < groupSizes[targetGroup])
                    {
                        targetGroup = i;
                    }
                }

                groups[targetGroup].AddRange(squad);
                groupSizes[targetGroup] += squad.Count;
            }

            return groups;
        }
    }

    public static class IncidentWorker_Raid_GetLetterText_MUGBSquadSummaryPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.AllTypes()
                .Where(type => type != null
                    && !type.IsAbstract
                    && typeof(IncidentWorker_Raid).IsAssignableFrom(type))
                .Select(type => AccessTools.Method(type, "GetLetterText", new[] { typeof(IncidentParms), typeof(List<Pawn>) }))
                .Where(method => method != null && !method.IsAbstract);
        }

        public static void Postfix(IncidentParms parms, List<Pawn> pawns, ref string __result)
        {
            if (MUGB_SquadRaidUtility.TryConsumeSummary(parms, out string summary))
            {
                __result += "\n\n" + "MUGB_SquadRaidReport".Translate(summary);
            }
        }
    }

    public static class MUGB_SquadRaidContext
    {
        [System.ThreadStatic]
        private static Stack<Context> contextStack;

        public static IncidentParms CurrentParms => contextStack != null && contextStack.Count > 0 ? contextStack.Peek().Parms : null;
        public static bool DebugTest => contextStack != null && contextStack.Count > 0 && contextStack.Peek().DebugTest;
        public static bool CaravanAmbush => contextStack != null && contextStack.Count > 0 && contextStack.Peek().CaravanAmbush;
        public static bool WeakCaravanAmbush => contextStack != null && contextStack.Count > 0 && contextStack.Peek().WeakCaravanAmbush;

        public static void Push(
            IncidentParms parms,
            bool debugTest,
            bool caravanAmbush = false,
            bool weakCaravanAmbush = false)
        {
            if (contextStack == null)
            {
                contextStack = new Stack<Context>();
            }
            contextStack.Push(new Context(parms, debugTest, caravanAmbush, weakCaravanAmbush));
        }

        public static void Pop()
        {
            if (contextStack == null || contextStack.Count == 0)
            {
                return;
            }
            contextStack.Pop();
        }

        private readonly struct Context
        {
            public readonly IncidentParms Parms;
            public readonly bool DebugTest;
            public readonly bool CaravanAmbush;
            public readonly bool WeakCaravanAmbush;

            public Context(IncidentParms parms, bool debugTest, bool caravanAmbush, bool weakCaravanAmbush)
            {
                Parms = parms;
                DebugTest = debugTest;
                CaravanAmbush = caravanAmbush;
                WeakCaravanAmbush = weakCaravanAmbush;
            }
        }
    }
}
