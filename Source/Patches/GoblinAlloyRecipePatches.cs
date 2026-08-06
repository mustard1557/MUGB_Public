using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class GenRecipe_MakeRecipeProducts_GoblinAlloy_Patch
    {
        private static readonly HashSet<string> AlloyRecipeDefNames = new HashSet<string>
        {
            "MUGB_MakeGoblinAlloy",
            "MUGB_MakeGoblinAlloyBulk50",
            "MUGB_MakeGoblinAlloyBulk100"
        };

        public static bool Prefix(
            RecipeDef recipeDef,
            List<Thing> ingredients,
            ref IEnumerable<Thing> __result)
        {
            if (recipeDef == null || !AlloyRecipeDefNames.Contains(recipeDef.defName))
            {
                return true;
            }

            ThingDef alloyDef = AlloyDefForIngredients(recipeDef, ingredients);
            int count = recipeDef.products?.FirstOrDefault()?.count ?? 1;
            Thing alloy = ThingMaker.MakeThing(alloyDef);
            alloy.stackCount = count;
            __result = new[] { alloy };
            return false;
        }

        private static ThingDef AlloyDefForIngredients(RecipeDef recipeDef, List<Thing> ingredients)
        {
            float steelScore = MUGBResourceUtility.ArmorScore(ThingDefOf.Steel);
            float weightedScore = 0f;
            int metalCount = 0;
            int fixedSteelToSkip = FixedSteelCount(recipeDef);

            foreach (Thing ingredient in ingredients ?? Enumerable.Empty<Thing>())
            {
                ThingDef def = ingredient?.def;
                if (!MUGBResourceUtility.IsValidAlloyInputMetal(def))
                {
                    continue;
                }

                int count = ingredient.stackCount;
                if (def == ThingDefOf.Steel && fixedSteelToSkip > 0)
                {
                    int skipped = Math.Min(count, fixedSteelToSkip);
                    count -= skipped;
                    fixedSteelToSkip -= skipped;
                }

                if (count <= 0)
                {
                    continue;
                }

                weightedScore += MUGBResourceUtility.ArmorScore(def) * count;
                metalCount += count;
            }

            float ratio = steelScore > 0f && metalCount > 0 ? weightedScore / metalCount / steelScore : 1f;
            if (ratio < MUGBResourceUtility.CrudeTierMaxSteelRatio)
            {
                return MUGBDefOf.MUGB_CrudeGoblinAlloy;
            }

            if (ratio >= MUGBResourceUtility.FineTierMinSteelRatio)
            {
                return MUGBDefOf.MUGB_FineGoblinAlloy;
            }

            return MUGBDefOf.MUGB_GoblinAlloy;
        }

        private static int FixedSteelCount(RecipeDef recipeDef)
        {
            return recipeDef.defName switch
            {
                "MUGB_MakeGoblinAlloyBulk100" => 20,
                "MUGB_MakeGoblinAlloyBulk50" => 10,
                _ => 1
            };
        }
    }
}
