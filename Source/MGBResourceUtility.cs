using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MUGB
{
    public static class MUGBHumanlikeFoodUtility
    {
        private static List<ThingDef> stewpotIngredientDefs;
        private static HashSet<ThingDef> humanlikeMeatDefs;

        public static bool IsHumanlikeMeatDef(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            ThingDef sourceRace = def.ingestible?.sourceDef;
            if (sourceRace?.race != null)
            {
                return def.IsMeat && sourceRace.race.Humanlike;
            }

            EnsureHumanlikeMeatDefs();
            return humanlikeMeatDefs.Contains(def);
        }

        public static bool IsStewpotIngredientDef(ThingDef def)
        {
            return def != null
                && (def == MUGBDefOf.MUGB_Hgut
                    || def == MUGBDefOf.MUGB_Ggut
                    || def == MUGBDefOf.MUGB_Hchunk
                    || def == MUGBDefOf.MUGB_Gchunk
                    || def == MUGBDefOf.Meat_Goblin
                    || def == ThingDefOf.Meat_Human
                    || def.defName == "MUGB_Hgut"
                    || def.defName == "MUGB_Ggut"
                    || def.defName == "MUGB_Hchunk"
                    || def.defName == "MUGB_Gchunk"
                    || def.defName == "Meat_Goblin"
                    || def.defName == "Meat_Human"
                    || IsHumanlikeMeatDef(def));
        }

        public static IReadOnlyList<ThingDef> StewpotIngredientDefs
        {
            get
            {
                if (stewpotIngredientDefs == null)
                {
                    stewpotIngredientDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(IsStewpotIngredientDef)
                        .Distinct()
                        .ToList();
                }

                return stewpotIngredientDefs;
            }
        }

        private static void EnsureHumanlikeMeatDefs()
        {
            if (humanlikeMeatDefs != null)
            {
                return;
            }

            humanlikeMeatDefs = new HashSet<ThingDef>();
            foreach (ThingDef raceDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (raceDef?.category == ThingCategory.Pawn
                    && raceDef.race?.Humanlike == true
                    && raceDef.race.meatDef != null)
                {
                    humanlikeMeatDefs.Add(raceDef.race.meatDef);
                }
            }
        }
    }

    public class Graphic_GoblinAlloyStackCount : Graphic_StackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            return SubGraphicForGoblinAlloyCount(thing?.stackCount ?? 1);
        }

        public override Material MatSingleFor(Thing thing)
        {
            return SubGraphicFor(thing).MatSingleFor(thing);
        }

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            return SubGraphicFor(thing).MatAt(rot, thing);
        }

        private Graphic SubGraphicForGoblinAlloyCount(int stackCount)
        {
            if (subGraphics == null || subGraphics.Length == 0)
            {
                return this;
            }

            int index = stackCount <= 10 ? 0 : stackCount <= 40 ? 1 : 2;
            if (index >= subGraphics.Length)
            {
                index = subGraphics.Length - 1;
            }

            return subGraphics[index] ?? this;
        }
    }

    public class Graphic_GoblinPercentStackCount : Graphic_StackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            return SubGraphicForPercentCount(thing);
        }

        public override Material MatSingleFor(Thing thing)
        {
            return SubGraphicFor(thing).MatSingleFor(thing);
        }

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            return SubGraphicFor(thing).MatAt(rot, thing);
        }

        private Graphic SubGraphicForPercentCount(Thing thing)
        {
            if (subGraphics == null || subGraphics.Length == 0)
            {
                return this;
            }

            int stackCount = Mathf.Max(1, thing?.stackCount ?? 1);
            int stackLimit = Mathf.Max(1, thing?.def?.stackLimit ?? stackCount);
            float percent = Mathf.Clamp01(stackCount / (float)stackLimit);
            int index;

            if (subGraphics.Length == 1)
            {
                index = 0;
            }
            else if (subGraphics.Length == 2)
            {
                index = percent >= 0.5f ? 1 : 0;
            }
            else if (subGraphics.Length == 3)
            {
                index = percent > 0.6f ? 2 : percent > 0.3f ? 1 : 0;
            }
            else
            {
                index = Mathf.Clamp(Mathf.CeilToInt(percent * subGraphics.Length) - 1, 0, subGraphics.Length - 1);
            }

            return subGraphics[index] ?? this;
        }
    }

    public class Graphic_GoblinTwoStageStackCount : Graphic_GoblinPercentStackCount
    {
    }

    // Pepper cheese switches to its piled graphic at five pieces, independent of stack limit.
    public class Graphic_GoblinFiveStackCount : Graphic_StackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            if (subGraphics == null || subGraphics.Length == 0)
            {
                return this;
            }

            int index = (thing?.stackCount ?? 1) >= 5 ? 1 : 0;
            index = Mathf.Min(index, subGraphics.Length - 1);
            return subGraphics[index] ?? this;
        }

        public override Material MatSingleFor(Thing thing) => SubGraphicFor(thing).MatSingleFor(thing);

        public override Material MatAt(Rot4 rot, Thing thing = null) => SubGraphicFor(thing).MatAt(rot, thing);
    }

    // 한국어 의도: 박격포탄/대형볼트는 10개 미만일 때 a, 10개 이상일 때 b 텍스처를 사용한다.
    public class Graphic_GoblinTenStackCount : Graphic_StackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            if (subGraphics == null || subGraphics.Length == 0)
            {
                return this;
            }

            int index = (thing?.stackCount ?? 1) >= 10 ? 1 : 0;
            index = Mathf.Min(index, subGraphics.Length - 1);
            return subGraphics[index] ?? this;
        }

        public override Material MatSingleFor(Thing thing)
        {
            return SubGraphicFor(thing).MatSingleFor(thing);
        }

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            return SubGraphicFor(thing).MatAt(rot, thing);
        }
    }

    public class Graphic_GoblinThreeStageStackCount : Graphic_GoblinPercentStackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            if (subGraphics == null || subGraphics.Length == 0)
            {
                return this;
            }

            int stackCount = Mathf.Max(1, thing?.stackCount ?? 1);
            int stackLimit = Mathf.Max(1, thing?.def?.stackLimit ?? stackCount);
            float percent = Mathf.Clamp01(stackCount / (float)stackLimit);
            int desiredIndex = percent > 0.6f ? 2 : percent > 0.3f ? 1 : 0;
            int index = Mathf.Min(desiredIndex, subGraphics.Length - 1);
            return subGraphics[index] ?? this;
        }
    }

    public class Graphic_GoblinGutStackCount : Graphic_GoblinPercentStackCount
    {
        public override Graphic SubGraphicFor(Thing thing)
        {
            if (subGraphics == null || subGraphics.Length == 0)
            {
                return this;
            }

            int index = (thing?.stackCount ?? 1) >= 30 ? 1 : 0;
            if (index >= subGraphics.Length)
            {
                index = subGraphics.Length - 1;
            }

            return subGraphics[index] ?? this;
        }
    }

    [StaticConstructorOnStartup]
    public static class MUGBResourceUtility
    {
        public const float CrudeTierMaxSteelRatio = 0.90f;
        public const float FineTierMinSteelRatio = 1.25f;
        private static readonly string[] AlloyRecipeDefNames =
        {
            "MUGB_MakeGoblinAlloy",
            "MUGB_MakeGoblinAlloyBulk50",
            "MUGB_MakeGoblinAlloyBulk100"
        };

        static MUGBResourceUtility()
        {
            PatchGoblinAlloyRecipeFilters();
            PatchHumanlikeMeatRecipeFilters();
        }

        public static int HumanlikeBoneAmountFor(Pawn pawn, float efficiency, float yieldFactor)
        {
            if (pawn?.RaceProps?.Humanlike != true || !pawn.RaceProps.IsFlesh || yieldFactor <= 0f)
            {
                return 0;
            }

            int amount = GenMath.RoundRandom(pawn.GetStatValue(StatDefOf.MeatAmount) * efficiency * yieldFactor);
            return amount > 0 ? amount : 1;
        }

        private static void PatchGoblinAlloyRecipeFilters()
        {
            ThingDef steel = ThingDefOf.Steel;
            if (steel == null)
            {
                Log.Warning("[MUGB] Could not patch goblin alloy recipes because Steel is missing.");
                return;
            }

            List<ThingDef> allowedMetals = new List<ThingDef>();

            foreach (ThingDef metal in DefDatabase<ThingDef>.AllDefsListForReading.Where(IsValidAlloyInputMetal))
            {
                allowedMetals.Add(metal);
            }

            ApplyMetalFilter(AlloyRecipeDefNames, allowedMetals);
        }

        private static void PatchHumanlikeMeatRecipeFilters()
        {
            ThingDef vanillaHumanMeat = ThingDefOf.Meat_Human;
            if (vanillaHumanMeat == null)
            {
                return;
            }

            List<ThingDef> humanlikeMeats = MUGBHumanlikeFoodUtility.StewpotIngredientDefs
                .Where(MUGBHumanlikeFoodUtility.IsHumanlikeMeatDef)
                .ToList();
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe?.defName?.StartsWith("MUGB_") != true)
                {
                    continue;
                }

                bool acceptsHumanMeat = recipe.fixedIngredientFilter?.Allows(vanillaHumanMeat) == true
                    || recipe.defaultIngredientFilter?.Allows(vanillaHumanMeat) == true
                    || recipe.ingredients?.Any(ingredient => ingredient?.filter?.Allows(vanillaHumanMeat) == true) == true;
                if (!acceptsHumanMeat)
                {
                    continue;
                }

                AllowDefs(recipe.fixedIngredientFilter, humanlikeMeats);
                AllowDefs(recipe.defaultIngredientFilter, humanlikeMeats);
                if (recipe.ingredients == null)
                {
                    continue;
                }

                foreach (IngredientCount ingredient in recipe.ingredients)
                {
                    if (ingredient?.filter?.Allows(vanillaHumanMeat) == true)
                    {
                        AllowDefs(ingredient.filter, humanlikeMeats);
                    }
                }
            }
        }

        private static void AllowDefs(ThingFilter filter, IEnumerable<ThingDef> defs)
        {
            if (filter == null)
            {
                return;
            }

            foreach (ThingDef def in defs)
            {
                filter.SetAllow(def, true);
            }
        }

        public static bool IsValidAlloyInputMetal(ThingDef def)
        {
            return def != null
                && def.IsStuff
                && def.IsMetal
                && def.category == ThingCategory.Item
                && def.thingCategories != null
                && def.thingCategories.Contains(ThingCategoryDefOf.ResourcesRaw)
                && def != MUGBDefOf.MUGB_CrudeGoblinAlloy
                && def != MUGBDefOf.MUGB_GoblinAlloy
                && def != MUGBDefOf.MUGB_FineGoblinAlloy;
        }

        public static float ArmorScore(ThingDef def)
        {
            return def.GetStatValueAbstract(StatDefOf.StuffPower_Armor_Sharp) * 0.5f
                + def.GetStatValueAbstract(StatDefOf.StuffPower_Armor_Blunt) * 0.35f
                + def.GetStatValueAbstract(StatDefOf.StuffPower_Armor_Heat) * 0.15f;
        }

        private static void ApplyMetalFilter(IEnumerable<string> recipeDefNames, List<ThingDef> allowedMetals)
        {
            foreach (string recipeDefName in recipeDefNames)
            {
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName);
                IngredientCount metalIngredient = recipe?.ingredients?.LastOrDefault();
                if (metalIngredient?.filter == null)
                {
                    Log.Warning($"[MUGB] Could not patch alloy metal filter for recipe {recipeDefName}.");
                    continue;
                }

                foreach (ThingDef currentlyAllowed in metalIngredient.filter.AllowedThingDefs.ToList())
                {
                    metalIngredient.filter.SetAllow(currentlyAllowed, false);
                }

                foreach (ThingDef metal in allowedMetals)
                {
                    metalIngredient.filter.SetAllow(metal, true);
                }
            }
        }

    }
}
