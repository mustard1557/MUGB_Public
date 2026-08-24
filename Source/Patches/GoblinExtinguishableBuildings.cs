using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    internal static class GoblinExtinguishableBuildingUtility
    {
        private static readonly HashSet<string> ExtinguishableBuildingDefs = new HashSet<string>
        {
            "MUGB_campfire",
            "MUGB_fireplace",
            "MUGB_cookstation",
            "MUGB_smithbench",
            "MUGB_bigpot"
        };

        public static bool IsExtinguishableBuilding(Thing thing)
        {
            return thing?.def?.defName != null
                && ExtinguishableBuildingDefs.Contains(thing.def.defName);
        }

        public static bool IsExtinguishableWorkTable(Building_WorkTable workTable)
        {
            return IsExtinguishableBuilding(workTable);
        }

        public static bool IsSwitchedOn(ThingWithComps thing)
        {
            return thing?.GetComp<CompFlickable>()?.SwitchIsOn != false;
        }

        public static void InterruptActiveBill(ThingWithComps billGiver)
        {
            Map map = billGiver?.Map;
            if (map == null)
            {
                return;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.jobs?.curDriver is JobDriver_DoBill
                    && pawn.CurJob?.GetTarget(JobDriver_DoBill.BillGiverInd).Thing == billGiver)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
                }
            }
        }
    }

    public class CompHeatPusherGoblinFlickable : CompHeatPusherPowered
    {
        private CompFlickable flickableComp;

        public override bool ShouldPushHeatNow => base.ShouldPushHeatNow
            && (flickableComp?.SwitchIsOn ?? true);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickableComp = parent.GetComp<CompFlickable>();
        }
    }

    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.CurrentlyUsableForBills))]
    public static class Building_WorkTable_CurrentlyUsableForBills_GoblinExtinguishablePatch
    {
        public static void Postfix(Building_WorkTable __instance, ref bool __result)
        {
            if (__result
                && GoblinExtinguishableBuildingUtility.IsExtinguishableWorkTable(__instance)
                && !GoblinExtinguishableBuildingUtility.IsSwitchedOn(__instance))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(CompFlickable), nameof(CompFlickable.DoFlick))]
    public static class CompFlickable_DoFlick_GoblinExtinguishablePatch
    {
        public static void Postfix(CompFlickable __instance)
        {
            ThingWithComps parent = __instance?.parent;
            if (parent != null
                && !__instance.SwitchIsOn
                && GoblinExtinguishableBuildingUtility.IsExtinguishableBuilding(parent))
            {
                GoblinExtinguishableBuildingUtility.InterruptActiveBill(parent);
            }
        }
    }
}
