using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MUGB.Patches
{
    internal static class GoblinCampfireRecipeInheritance
    {
        private static readonly string[] GoblinCampfireDefNames =
        {
            "MUGB_campfire",
            "MUGB_fireplace"
        };

        public static void Apply()
        {
            ThingDef vanillaCampfire = DefDatabase<ThingDef>.GetNamedSilentFail("Campfire");
            if (vanillaCampfire == null)
            {
                return;
            }

            List<ThingDef> targets = new List<ThingDef>(GoblinCampfireDefNames.Length);
            for (int i = 0; i < GoblinCampfireDefNames.Length; i++)
            {
                ThingDef target = DefDatabase<ThingDef>.GetNamedSilentFail(GoblinCampfireDefNames[i]);
                if (target != null)
                {
                    targets.Add(target);
                    CopyRecipes(vanillaCampfire, target);
                }
            }

            if (targets.Count == 0)
            {
                return;
            }

            List<WorkGiverDef> workGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < workGivers.Count; i++)
            {
                List<ThingDef> billGivers = workGivers[i].fixedBillGiverDefs;
                if (billGivers == null || !billGivers.Contains(vanillaCampfire))
                {
                    continue;
                }

                for (int j = 0; j < targets.Count; j++)
                {
                    if (!billGivers.Contains(targets[j]))
                    {
                        billGivers.Add(targets[j]);
                    }
                }
            }
        }

        private static void CopyRecipes(ThingDef source, ThingDef target)
        {
            List<RecipeDef> sourceRecipes = source.AllRecipes;
            target.recipes ??= new List<RecipeDef>();
            List<RecipeDef> resolvedRecipes = target.AllRecipes;

            for (int i = 0; i < sourceRecipes.Count; i++)
            {
                RecipeDef recipe = sourceRecipes[i];
                if (!target.recipes.Contains(recipe))
                {
                    target.recipes.Add(recipe);
                }
                if (!resolvedRecipes.Contains(recipe))
                {
                    resolvedRecipes.Add(recipe);
                }
            }
        }
    }
}
