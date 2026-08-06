using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB
{
    public static class GoblinSlaveWorkUtility
    {
        public static bool IsAllowedSlaveWorkType(WorkTypeDef workType)
        {
            return workType == WorkTypeDefOf.Research || workType == WorkTypeDefOf.Warden;
        }

        public static bool SlaveMayDoWardenMode(Pawn warden, Pawn prisoner)
        {
            if (warden?.IsSlaveOfColony != true || prisoner?.guest == null)
            {
                return true;
            }

            if (ReferenceEquals(warden, prisoner))
            {
                return false;
            }

            return prisoner.guest.ExclusiveInteractionMode != PrisonerInteractionModeDefOf.AttemptRecruit;
        }
    }

    [HarmonyPatch]
    public static class WorkGiver_Warden_ShouldTakeCareOfSlave_GoblinSlaveWorkPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WorkGiver_Warden), "ShouldTakeCareOfSlave");
        }

        public static bool Prefix(Pawn warden, Thing slave, ref bool __result)
        {
            if (!ReferenceEquals(warden, slave))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Pawn_GetDisabledWorkTypes_GoblinSlaveWorkPatch
    {
        public static void Postfix(Pawn __instance, ref List<WorkTypeDef> __result)
        {
            if (__instance?.IsSlaveOfColony != true || __result == null)
            {
                return;
            }

            __result.RemoveAll(GoblinSlaveWorkUtility.IsAllowedSlaveWorkType);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Warden_Chat), nameof(WorkGiver_Warden_Chat.HasJobOnThing))]
    public static class WorkGiver_Warden_Chat_HasJobOnThing_GoblinSlaveWorkPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (t is Pawn prisoner && !GoblinSlaveWorkUtility.SlaveMayDoWardenMode(pawn, prisoner))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Warden_Chat), nameof(WorkGiver_Warden_Chat.JobOnThing))]
    public static class WorkGiver_Warden_Chat_JobOnThing_GoblinSlaveWorkPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref Job __result)
        {
            if (t is Pawn prisoner && !GoblinSlaveWorkUtility.SlaveMayDoWardenMode(pawn, prisoner))
            {
                __result = null;
                return false;
            }

            return true;
        }
    }
}
