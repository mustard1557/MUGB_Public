using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.ExposeData))]
    public static class Thing_ExposeData_GoblinStuffMigrationPatch
    {
        private static readonly FieldInfo StuffIntField = AccessTools.Field(typeof(Thing), "stuffInt");

        public static void Prefix(Thing __instance)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars
                || __instance == null
                || __instance.Stuff != null
                || StuffIntField == null)
            {
                return;
            }

            ThingDef def = __instance.def;
            if (def == null || !def.MadeFromStuff)
            {
                return;
            }

            ThingDef fallbackStuff = GenStuff.DefaultStuffFor(def);
            if (fallbackStuff == null && def == MUGBDefOf.MUGB_Apparel_HumanHideMantle)
            {
                fallbackStuff = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_Plain")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Leather_Lightleather")
                    ?? DefDatabase<ThingDef>.AllDefsListForReading.Find(candidate => candidate.IsStuff && candidate.stuffProps?.categories?.Contains(StuffCategoryDefOf.Leathery) == true);
            }

            if (fallbackStuff != null)
            {
                StuffIntField.SetValue(__instance, fallbackStuff);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.ExposeData))]
    public static class Thing_ExposeData_GoblinMissingStuffWarningPatch
    {
        private static readonly MethodInfo LogErrorMethod = AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
        private static readonly MethodInfo RedirectMethod = AccessTools.Method(typeof(Thing_ExposeData_GoblinMissingStuffWarningPatch), nameof(MaybeLogMissingStuffError));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(LogErrorMethod))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, RedirectMethod);
                    continue;
                }

                yield return instruction;
            }
        }

        public static void MaybeLogMissingStuffError(string message, Thing thing)
        {
            if (ShouldSuppressMissingStuffWarning(message, thing))
            {
                return;
            }

            Log.Error(message);
        }

        private static bool ShouldSuppressMissingStuffWarning(string message, Thing thing)
        {
            if (message.NullOrEmpty()
                || thing?.def == null
                || !thing.def.MadeFromStuff)
            {
                return false;
            }

            if (!message.Contains("is made from stuff but has no stuff set. Setting default stuff."))
            {
                return false;
            }

            string defName = thing.def.defName;
            if (defName.NullOrEmpty())
            {
                return false;
            }

            return defName.StartsWith("MUGB_", System.StringComparison.Ordinal);
        }
    }
}
