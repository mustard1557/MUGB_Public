using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    public static class MUGBGoblinIdeologyUtility
    {
        public static bool HasGoblinCoreMeme(Ideo ideo)
        {
            return ideo != null
                && ((MUGBDefOf.MUGB_ChildrenOfBlinia != null && ideo.HasMeme(MUGBDefOf.MUGB_ChildrenOfBlinia))
                    || (MUGBDefOf.MUGB_GoblinSupremacy != null && ideo.HasMeme(MUGBDefOf.MUGB_GoblinSupremacy)));
        }
    }

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
                || MUGBGoblinIdeologyUtility.HasGoblinCoreMeme(Faction.OfPlayer?.ideos?.PrimaryIdeo))
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
