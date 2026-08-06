using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace MUGB.Patches
{
    // HAR builds static body-addon render nodes only when this method permits them.
    // Blocking node creation here avoids per-frame filtering while preserving the
    // underlying genes and any non-visual effects they may provide.
    [StaticConstructorOnStartup]
    public static class GoblinHarBodyAddonCompatPatch
    {
        static GoblinHarBodyAddonCompatPatch()
        {
            try
            {
                Type bodyAddonType = AccessTools.TypeByName("AlienRace.AlienPartGenerator+BodyAddon");
                if (bodyAddonType == null)
                {
                    return;
                }

                MethodInfo target = AccessTools.Method(
                    bodyAddonType,
                    "CanDrawAddonStatic",
                    new[] { typeof(Pawn) });
                if (target == null)
                {
                    Log.Warning("[MUGB] HAR is loaded but BodyAddon.CanDrawAddonStatic(Pawn) was not found. HAR body addons may appear on full goblins.");
                    return;
                }

                Harmony harmony = new Harmony("mustard1557.mugb.goblin.harbodyaddons");
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(GoblinHarBodyAddonCompatPatch), nameof(CanDrawAddonStaticPrefix)));
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to apply the HAR body-addon compatibility patch: " + e);
            }
        }

        private static bool CanDrawAddonStaticPrefix(Pawn pawn, ref bool __result)
        {
            if (!GoblinUtility.HasGoblinCoreMarker(pawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
