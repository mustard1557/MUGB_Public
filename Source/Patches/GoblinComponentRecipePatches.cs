using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(RecipeDefGenerator), nameof(RecipeDefGenerator.SetIngredients))]
    public static class RecipeDefGenerator_SetIngredients_GoblinComponentsPatch
    {
        public static void Postfix(RecipeDef r, ThingDef def)
        {
            if (r == null
                || def?.weaponTags == null
                || !def.weaponTags.Contains("MUGB_GoblinWeapon"))
            {
                return;
            }

            ThingDef industrialComponent = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial");
            ThingDef medievalComponent = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_ComponentBasic");
            if (industrialComponent == null || medievalComponent == null)
            {
                return;
            }

            for (int i = 0; i < r.ingredients.Count; i++)
            {
                ThingFilter filter = r.ingredients[i]?.filter;
                if (filter == null
                    || (!filter.Allows(industrialComponent) && !filter.Allows(medievalComponent)))
                {
                    continue;
                }

                // Keep this ingredient fixed. Allowing both component defs makes RimWorld
                // treat it as a bill-filtered variable ingredient and reject both at the bench.
                filter.SetAllow(industrialComponent, false);
                filter.SetAllow(medievalComponent, true);
            }
        }
    }
}
