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

                // Korean intent: 부품이 필요한 고블린 무기는 바닐라 산업 부품 또는
                // 미디블 오버홀 기본 부품 중 한 종류를 골라 같은 수량만큼 사용할 수 있습니다.
                filter.SetAllow(industrialComponent, true);
                filter.SetAllow(medievalComponent, true);
            }
        }
    }
}
