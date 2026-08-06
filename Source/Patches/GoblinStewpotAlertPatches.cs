using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    [HarmonyPatch]
    public static class GoblinStewpotNoHopperAlertPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Alert_PasteDispenserNeedsHopper), "BadDispensers");
        }

        public static void Postfix(ref List<Thing> __result)
        {
            if (__result == null)
            {
                return;
            }

            __result.RemoveAll(thing => thing is Building_GoblinStewpot || thing?.def == MUGBDefOf.MUGB_bigpot);
        }
    }
}
