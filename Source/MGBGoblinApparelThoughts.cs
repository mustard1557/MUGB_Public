using RimWorld;
using Verse;

namespace MUGB
{
    public class ThoughtWorker_WearingGoblinApparel : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (pawn == null
                || GoblinUtility.IsGoblin(pawn)
                || MUGBMod.Settings?.enableNonGoblinGoblinGearMoodPenalty == false)
            {
                return ThoughtState.Inactive;
            }

            if (HasTrait(pawn, "Cannibal") || HasTrait(pawn, "Bloodlust"))
            {
                return ThoughtState.Inactive;
            }

            int count = 0;
            if (pawn.apparel?.WornApparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    if (apparel?.def?.apparel?.tags?.Contains("MUGB_GoblinApparel") == true)
                    {
                        count++;
                    }
                }
            }

            if (pawn.equipment?.Primary?.def?.weaponTags?.Contains("MUGB_GoblinWeapon") == true)
            {
                count++;
            }

            if (count <= 0)
            {
                return ThoughtState.Inactive;
            }

            bool hasPheromoneAdaptation = MUGBDefOf.MUGB_GoblinPheromonePreference != null
                && pawn.health?.hediffSet?.HasHediff(MUGBDefOf.MUGB_GoblinPheromonePreference) == true;
            bool hasGoblinIdeology = HasGoblinIdeology(pawn);
            bool psychopath = HasTrait(pawn, "Psychopath");

            if (hasPheromoneAdaptation && (hasGoblinIdeology || psychopath))
            {
                return ThoughtState.Inactive;
            }

            if (hasPheromoneAdaptation)
            {
                return count <= 1
                    ? ThoughtState.Inactive
                    : ThoughtState.ActiveAtStage(count >= 3 ? 3 : 4);
            }

            if (hasGoblinIdeology)
            {
                return count <= 1 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(4);
            }

            if (psychopath)
            {
                return ThoughtState.ActiveAtStage(3);
            }

            return ThoughtState.ActiveAtStage(count >= 3 ? 2 : count - 1);
        }

        private static bool HasTrait(Pawn pawn, string traitDefName)
        {
            if (pawn?.story?.traits?.allTraits == null)
            {
                return false;
            }

            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                if (trait?.def?.defName == traitDefName)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasGoblinIdeology(Pawn pawn)
        {
            Ideo ideo = pawn?.Ideo;
            if (ideo == null)
            {
                return false;
            }

            return MUGBDefOf.MUGB_ChildrenOfBlinia != null && ideo.HasMeme(MUGBDefOf.MUGB_ChildrenOfBlinia)
                || MUGBDefOf.MUGB_GoblinSupremacy != null && ideo.HasMeme(MUGBDefOf.MUGB_GoblinSupremacy);
        }
    }
}
