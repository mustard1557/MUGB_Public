using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.PostOpen))]
    public static class MGBGoblinScenarioIdeoRecommendationPatch
    {
        private static readonly HashSet<Page_ConfigureStartingPawns> AcknowledgedPages = new HashSet<Page_ConfigureStartingPawns>();

        public static void Postfix(Page_ConfigureStartingPawns __instance)
        {
            if (__instance == null
                || AcknowledgedPages.Contains(__instance)
                || !ModsConfig.IdeologyActive
                || !IsGoblinStartScenario()
                || MUGBDefOf.MUGB_ChildrenOfBlinia == null
                || Faction.OfPlayer?.ideos?.PrimaryIdeo?.HasMeme(MUGBDefOf.MUGB_ChildrenOfBlinia) == true)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                "MUGB_GoblinStartRecommendedMemeWarning".Translate(),
                "MUGB_GoblinStartReturnToIdeo".Translate(),
                () => ReturnToIdeologyPage(__instance),
                "MUGB_GoblinStartContinueWithoutMeme".Translate(),
                () => AcknowledgedPages.Add(__instance),
                "MUGB_GoblinStartRecommendedMemeTitle".Translate()));
        }

        private static bool IsGoblinStartScenario()
        {
            return Find.Scenario?.AllParts?.Any(part => part is ScenPart_MUGB_GoblinStartMarker) == true;
        }

        private static void ReturnToIdeologyPage(Page_ConfigureStartingPawns page)
        {
            if (page?.prev == null)
            {
                return;
            }

            Page previous = page.prev;
            page.Close();
            Find.WindowStack.Add(previous);
        }
    }
}
