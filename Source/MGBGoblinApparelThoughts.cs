using RimWorld;
using Verse;

namespace MUGB
{
    public class ThoughtWorker_WearingGoblinApparel : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (pawn == null || GoblinUtility.IsGoblin(pawn))
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

            if (HasTrait(pawn, "Psychopath"))
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
    }
}
