using HarmonyLib;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    // Feeds the shared wave morale pool. Vanilla's own per-squad flee trigger is left alone:
    // a squad that gets crushed on its own still runs, and the pool routs the rest of the wave.
    [HarmonyPatch(typeof(Lord), "AddPawnInternal")]
    internal static class MUGB_TunnelMoralePawnAddedPatch
    {
        private static void Prefix(Lord __instance, out int __state)
        {
            __state = __instance.numPawnsEverGained;
        }

        private static void Postfix(Lord __instance, int __state)
        {
            int gained = __instance.numPawnsEverGained - __state;
            if (gained > 0)
                __instance.Map?.GetComponent<MUGB_GoblinTunnelClusterManager>()?
                    .NotifySquadChanged(__instance, gained, 0);
        }
    }

    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
    internal static class MUGB_TunnelMoralePawnLostPatch
    {
        private static void Prefix(Lord __instance, out int __state)
        {
            __state = __instance.numPawnsLostViolently;
        }

        private static void Postfix(Lord __instance, int __state)
        {
            // Use vanilla's counter: killed, incapacitated or captured; never ordinary departures.
            int lost = __instance.numPawnsLostViolently - __state;
            if (lost > 0)
                __instance.Map?.GetComponent<MUGB_GoblinTunnelClusterManager>()?
                    .NotifySquadChanged(__instance, 0, lost);
        }
    }
}
