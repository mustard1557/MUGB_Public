using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested), typeof(Pawn), typeof(float))]
    public static class Thing_Ingested_GoblinFoodReaction_Patch
    {
        private static readonly Dictionary<ThingDef, FoodIngredientFlags> DirectFoodCache = new Dictionary<ThingDef, FoodIngredientFlags>();

        public static void Postfix(Thing __instance, Pawn ingester)
        {
            if (ingester?.needs?.mood?.thoughts?.memories == null || __instance?.def == null)
            {
                return;
            }


            if (__instance.def == MUGBDefOf.MUGB_GoblinPepperCheese)
            {
                ApplyPepperCheeseEffects(ingester);
            }

            FoodIngredientFlags flags = GetFoodFlags(__instance);
            if (!GoblinUtility.IsGoblin(ingester))
            {
                float conditioningScore = PheromoneConditioningScore(__instance.def, flags);
                if (GoblinPheromonePreferenceUtility.HasPreference(ingester))
                {
                    if ((flags & (FoodIngredientFlags.HumanlikeMeat | FoodIngredientFlags.GoblinMeat)) != 0 || conditioningScore > 0f)
                    {
                        GoblinPheromonePreferenceUtility.RemoveHumanlikeAndGoblinFoodPenalties(ingester);
                    }

                    if (conditioningScore > 0f && MUGBDefOf.MUGB_AteGoblinFoodPreferred != null)
                    {
                        GoblinPheromonePreferenceUtility.NotifyPreferenceGoblinFoodEaten(ingester);
                        ingester.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_AteGoblinFoodPreferred);
                    }
                    return;
                }

                if (conditioningScore > 0f)
                {
                    GoblinPheromonePreferenceUtility.TryGainConditioning(ingester, conditioningScore);
                }

                if ((flags & FoodIngredientFlags.GoblinMeat) != 0)
                {
                    ThoughtDef thought = GoblinMeatThoughtFor(ingester, __instance.def == MUGBDefOf.Meat_Goblin);
                    if (thought != null)
                    {
                        ingester.needs.mood.thoughts.memories.TryGainMemory(thought);
                    }
                }

                ThoughtDef goblinDishThought = NonGoblinGoblinDishThoughtFor(__instance.def);
                if (goblinDishThought != null)
                {
                    ingester.needs.mood.thoughts.memories.TryGainMemory(goblinDishThought);
                }
                return;
            }

            ApplyGoblinFoodBuff(__instance.def, ingester);
            ApplyGoblinFoodThought(__instance.def, ingester);

            if ((flags & FoodIngredientFlags.Vegetable) != 0)
            {
                ingester.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_AteVegetableIngredient);
            }

            if ((flags & FoodIngredientFlags.InsectMeat) != 0)
            {
                RemoveVanillaInsectMeatThoughts(ingester);
            }

            if ((flags & (FoodIngredientFlags.HumanlikeMeat | FoodIngredientFlags.GoblinMeat)) != 0)
            {
                RemoveVanillaHumanlikeMeatThoughts(ingester);
                if (MUGBDefOf.MUGB_AteProperFlesh != null)
                {
                    ingester.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_AteProperFlesh);
                }
            }
        }

        private static FoodIngredientFlags GetFoodFlags(Thing food)
        {
            FoodIngredientFlags flags = FoodFlagsForDef(food.def);
            CompIngredients compIngredients = food.TryGetComp<CompIngredients>();
            if (compIngredients?.ingredients == null)
            {
                return flags;
            }

            for (int i = 0; i < compIngredients.ingredients.Count; i++)
            {
                flags |= FoodFlagsForDef(compIngredients.ingredients[i]);
            }
            return flags;
        }

        private static FoodIngredientFlags FoodFlagsForDef(ThingDef def)
        {
            if (def == null)
            {
                return FoodIngredientFlags.None;
            }

            if (DirectFoodCache.TryGetValue(def, out FoodIngredientFlags cached))
            {
                return cached;
            }

            FoodIngredientFlags flags = FoodIngredientFlags.None;
            IngestibleProperties ingestible = def.ingestible;
            if (ingestible != null)
            {
                FoodTypeFlags foodType = ingestible.foodType;
                bool fungus = HasFoodType(foodType, FoodTypeFlags.Fungus);
                bool plantFood = HasFoodType(foodType, FoodTypeFlags.VegetableOrFruit)
                    || HasFoodType(foodType, FoodTypeFlags.Plant)
                    || HasFoodType(foodType, FoodTypeFlags.Seed)
                    || HasFoodType(foodType, FoodTypeFlags.Tree);

                if (plantFood && !fungus)
                {
                    flags |= FoodIngredientFlags.Vegetable;
                }

                if (IsInsectMeatThought(ingestible.specialThoughtDirect) || IsInsectMeatThought(ingestible.specialThoughtAsIngredient))
                {
                    flags |= FoodIngredientFlags.InsectMeat;
                }

                if (IsHumanlikeMeatThought(ingestible.specialThoughtDirect) || IsHumanlikeMeatThought(ingestible.specialThoughtAsIngredient))
                {
                    flags |= FoodIngredientFlags.HumanlikeMeat;
                }
            }

            if (def == ThingDefOf.Meat_Human || def == MUGBDefOf.MUGB_Bone || def.defName == "Meat_Human" || def.defName == "MUGB_Bone"
                || def.defName == "MUGB_Hchunk" || def.defName == "MUGB_Hgut"
                || def.defName == "MUGB_HBBQ" || def.defName == "MUGB_legHBBQ" || def.defName == "MUGB_armHBBQ" || def.defName == "MUGB_excHBBQ")
            {
                flags |= FoodIngredientFlags.HumanlikeMeat;
            }

            if (MUGBHumanlikeFoodUtility.IsHumanlikeMeatDef(def))
            {
                flags |= FoodIngredientFlags.HumanlikeMeat;
            }

            // Processed foods from other mods may discard their source meat and only encode
            // the humanlike origin in the finished def. Keep mood reactions in sync with the
            // flesh-craving classifier used when the food is actually ingested.
            if (GoblinFleshFoodUtility.FleshFoodKindFor(def) == GoblinFleshFoodUtility.FleshFoodKind.Humanlike)
            {
                flags |= FoodIngredientFlags.HumanlikeMeat;
            }

            if (def == MUGBDefOf.Meat_Goblin || def.defName == "Meat_Goblin" || def.defName == "MUGB_Gchunk" || def.defName == "MUGB_Ggut"
                || def.defName == "MUGB_GBBQ" || def.defName == "MUGB_legGBBQ" || def.defName == "MUGB_armGBBQ" || def.defName == "MUGB_excGBBQ")
            {
                flags |= FoodIngredientFlags.GoblinMeat;
            }

            DirectFoodCache[def] = flags;
            return flags;
        }

        private static bool HasFoodType(FoodTypeFlags value, FoodTypeFlags flag)
        {
            return (value & flag) != 0;
        }

        private static bool IsInsectMeatThought(ThoughtDef thought)
        {
            return thought != null
                && (thought.defName == "AteInsectMeatDirect" || thought.defName == "AteInsectMeatAsIngredient");
        }

        private static bool IsHumanlikeMeatThought(ThoughtDef thought)
        {
            return thought != null
                && (thought.defName == "AteHumanlikeMeatDirect"
                    || thought.defName == "AteHumanlikeMeatDirectCannibal"
                    || thought.defName == "AteHumanlikeMeatAsIngredient"
                    || thought.defName == "AteHumanlikeMeatAsIngredientCannibal");
        }

        private static void ApplyGoblinFoodBuff(ThingDef foodDef, Pawn ingester)
        {
            HediffDef hediff = BuffFor(foodDef);
            if (hediff == null)
            {
                return;
            }

            Hediff existing = ingester.health?.hediffSet?.GetFirstHediffOfDef(hediff);
            if (existing != null)
            {
                ingester.health.RemoveHediff(existing);
            }
            ingester.health?.AddHediff(hediff);
        }

        private static void ApplyGoblinFoodThought(ThingDef foodDef, Pawn ingester)
        {
            ThoughtDef thought = null;
            switch (foodDef?.defName)
            {
                case "MUGB_heart":
                    thought = MUGBDefOf.MUGB_AteHeartGoblin;
                    break;
                case "MUGB_brain":
                    thought = MUGBDefOf.MUGB_AteBrainGoblin;
                    break;
            }

            if (thought != null)
            {
                ingester.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
            }
        }

        private static ThoughtDef GoblinMeatThoughtFor(Pawn ingester, bool direct)
        {
            TraitDef cannibalTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Cannibal");
            bool cannibal = cannibalTrait != null && ingester?.story?.traits?.HasTrait(cannibalTrait) == true;
            if (cannibal)
            {
                return direct ? MUGBDefOf.MUGB_AteGoblinMeatDirectCannibal : MUGBDefOf.MUGB_AteGoblinMeatAsIngredientCannibal;
            }
            return direct ? MUGBDefOf.MUGB_AteGoblinMeatDirect : MUGBDefOf.MUGB_AteGoblinMeatAsIngredient;
        }

        private static ThoughtDef NonGoblinGoblinDishThoughtFor(ThingDef foodDef)
        {
            switch (foodDef?.defName)
            {
                case "MUGB_gutsausage":
                    return MUGBDefOf.MUGB_AteGutSausageNonGoblin;
                case "MUGB_gutstew":
                    return MUGBDefOf.MUGB_AteGutStewNonGoblin;
                default:
                    return null;
            }
        }

        private static float PheromoneConditioningScore(ThingDef foodDef, FoodIngredientFlags flags)
        {
            switch (foodDef?.defName)
            {
                case "MUGB_GoblinPepperCheese":
                    return 6f;
                case "MUGB_gutstew":
                case "MUGB_Brainstew":
                case "MUGB_excGBBQ":
                    return 2f;
                case "MUGB_GBBQ":
                case "MUGB_legGBBQ":
                case "MUGB_armGBBQ":
                    return 1.5f;
                case "Meat_Goblin":
                case "MUGB_Gchunk":
                case "MUGB_Ggut":
                    return 1f;
                default:
                    return (flags & FoodIngredientFlags.GoblinMeat) != 0 ? 1f : 0f;
            }
        }

        private static void ApplyPepperCheeseEffects(Pawn ingester)
        {
            if (MUGBDefOf.MUGB_GoblinPepperCheeseFertility != null && ingester.health != null)
            {
                Hediff existing = ingester.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinPepperCheeseFertility);
                if (existing != null)
                {
                    ingester.health.RemoveHediff(existing);
                }
                ingester.health.AddHediff(MUGBDefOf.MUGB_GoblinPepperCheeseFertility);
            }

            ThoughtDef thought = GoblinUtility.IsGoblin(ingester)
                ? MUGBDefOf.MUGB_AteGoblinPepperCheese
                : MUGBDefOf.MUGB_AteGoblinPepperCheeseNonGoblin;
            if (thought != null)
            {
                ingester.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }

        private static HediffDef BuffFor(ThingDef foodDef)
        {
            switch (foodDef?.defName)
            {
                case "MUGB_heart":
                    return MUGBDefOf.MUGB_HeartRush;
                case "MUGB_brain":
                    return MUGBDefOf.MUGB_BrainFocus;
                case "MUGB_Brainstew":
                    return MUGBDefOf.MUGB_BrainStewInsight;
                case "MUGB_legHBBQ":
                case "MUGB_legGBBQ":
                    return MUGBDefOf.MUGB_LegBBQStride;
                case "MUGB_armHBBQ":
                case "MUGB_armGBBQ":
                    return MUGBDefOf.MUGB_ArmBBQGrip;
                case "MUGB_excHBBQ":
                case "MUGB_excGBBQ":
                    return MUGBDefOf.MUGB_LavishBBQFeast;
                default:
                    return null;
            }
        }

        private static void RemoveVanillaInsectMeatThoughts(Pawn pawn)
        {
            ThoughtDef direct = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteInsectMeatDirect");
            ThoughtDef ingredient = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteInsectMeatAsIngredient");
            if (direct != null)
            {
                pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(direct);
            }
            if (ingredient != null)
            {
                pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(ingredient);
            }
        }

        private static void RemoveVanillaHumanlikeMeatThoughts(Pawn pawn)
        {
            RemoveMemory(pawn, "AteHumanlikeMeatDirect");
            RemoveMemory(pawn, "AteHumanlikeMeatDirectCannibal");
            RemoveMemory(pawn, "AteHumanlikeMeatAsIngredient");
            RemoveMemory(pawn, "AteHumanlikeMeatAsIngredientCannibal");
        }

        private static void RemoveMemory(Pawn pawn, string defName)
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (thought != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(thought);
            }
        }

        private enum FoodIngredientFlags
        {
            None = 0,
            Vegetable = 1,
            InsectMeat = 2,
            GoblinMeat = 4,
            HumanlikeMeat = 8
        }
    }

    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class GenRecipe_MakeRecipeProducts_GutStewIngredient_Patch
    {
        public static void Postfix(RecipeDef recipeDef, ref IEnumerable<Thing> __result)
        {
            if (__result == null)
            {
                return;
            }

            List<Thing> products = __result.ToList();
            for (int i = 0; i < products.Count; i++)
            {
                GoblinFoodIngredientUtility.NormalizeGutStewIngredients(products[i]);
            }
            __result = products;
        }
    }

    public static class GoblinFoodIngredientUtility
    {
        public static void NormalizeGutStewIngredients(Thing food)
        {
            if (food?.def?.defName != "MUGB_gutstew")
            {
                return;
            }

            CompIngredients comp = food.TryGetComp<CompIngredients>();
            if (comp == null)
            {
                return;
            }

            comp.ingredients?.Clear();
            ThingDef gut = MUGBDefOf.MUGB_Hgut ?? DefDatabase<ThingDef>.GetNamedSilentFail("MUGB_Hgut");
            if (gut != null)
            {
                comp.RegisterIngredient(gut);
            }
            comp.RegisterIngredient(ThingDefOf.Meat_Human);
        }
    }
}
