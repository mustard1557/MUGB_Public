using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    internal static class GoblinRitualEligibilityUtility
    {
        private static readonly HashSet<string> AllowedSlaveMarriagePrecepts = new HashSet<string>
        {
            "MUGB_SlaveMarriage_Acceptable",
            "MUGB_SlaveMarriage_Preferred",
            "MUGB_SlaveMarriage_Important"
        };

        private static bool removingInvalidRitual;

        internal static bool AllowsSlaveMarriageRitual(Ideo ideo)
        {
            return ideo?.PreceptsListForReading.Any(precept =>
                precept?.def != null && AllowedSlaveMarriagePrecepts.Contains(precept.def.defName)) == true;
        }

        internal static void RemoveInvalidRitualsFromAllIdeos()
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager == null)
            {
                return;
            }

            foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading)
            {
                RemoveInvalidRituals(ideo);
            }
        }

        internal static void RemoveInvalidRituals(Ideo ideo)
        {
            if (removingInvalidRitual || ideo == null)
            {
                return;
            }

            removingInvalidRitual = true;
            try
            {
                bool hasGoblinMeme = ideo.memes != null
                    && (ideo.memes.Contains(MUGBDefOf.MUGB_ChildrenOfBlinia)
                        || ideo.memes.Contains(MUGBDefOf.MUGB_GoblinSupremacy));
                RemoveRitualIfInvalid(ideo, "MUGB_GoblinBeaconSignal", hasGoblinMeme);
                RemoveRitualIfInvalid(ideo, "MUGB_SlaveMarriageCeremony", AllowsSlaveMarriageRitual(ideo));
            }
            finally
            {
                removingInvalidRitual = false;
            }
        }

        private static void RemoveRitualIfInvalid(Ideo ideo, string ritualDefName, bool valid)
        {
            if (valid)
            {
                return;
            }

            List<Precept> invalidRituals = ideo.PreceptsListForReading
                .Where(precept => precept?.def?.defName == ritualDefName)
                .ToList();
            foreach (Precept ritual in invalidRituals)
            {
                ideo.RemovePrecept(ritual, true);
            }
        }

        internal static bool IsSlaveMarriageSetting(Precept precept)
        {
            return precept?.def?.issue?.defName == "MUGB_SlaveMarriage"
                || precept?.def?.defName == "MUGB_SlaveMarriageCeremony";
        }
    }

    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.CanAdd))]
    internal static class IdeoFoundation_CanAdd_GoblinRitualEligibilityPatch
    {
        private static void Postfix(PreceptDef precept, Ideo ___ideo, ref AcceptanceReport __result)
        {
            if (__result.Accepted
                && precept?.defName == "MUGB_SlaveMarriageCeremony"
                && !GoblinRitualEligibilityUtility.AllowsSlaveMarriageRitual(___ideo))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Ideo), nameof(Ideo.AddPrecept))]
    internal static class Ideo_AddPrecept_GoblinRitualEligibilityPatch
    {
        private static void Postfix(Ideo __instance, Precept precept)
        {
            if (GoblinRitualEligibilityUtility.IsSlaveMarriageSetting(precept))
            {
                GoblinRitualEligibilityUtility.RemoveInvalidRituals(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Ideo), nameof(Ideo.RemovePrecept))]
    internal static class Ideo_RemovePrecept_GoblinRitualEligibilityPatch
    {
        private static void Postfix(Ideo __instance, Precept precept, bool replacing)
        {
            if (!replacing && GoblinRitualEligibilityUtility.IsSlaveMarriageSetting(precept))
            {
                GoblinRitualEligibilityUtility.RemoveInvalidRituals(__instance);
            }
        }
    }
}
