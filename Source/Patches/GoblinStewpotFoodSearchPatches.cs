using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB
{
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap))]
    public static class FoodUtility_BestFoodSourceOnMap_GoblinStewpotPatch
    {
        public static void Postfix(
            Pawn getter,
            Pawn eater,
            bool desperate,
            ref ThingDef foodDef,
            FoodPreferability maxPref,
            bool allowDispenserFull,
            bool allowForbidden,
            bool ignoreReservations,
            FoodPreferability minPrefOverride,
            float? minNutrition,
            bool allowVenerated,
            ref Thing __result)
        {
            if (__result != null || !allowDispenserFull || getter?.Map == null || eater == null || MUGBDefOf.MUGB_bigpot == null)
            {
                return;
            }

            if (getter.RaceProps?.ToolUser != true || !getter.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return;
            }

            Thing stewpot = FindClosestUsableStewpot(getter, eater, desperate, maxPref, allowForbidden, ignoreReservations, minPrefOverride, minNutrition, allowVenerated);

            if (stewpot is Building_GoblinStewpot pot)
            {
                __result = pot;
                foodDef = pot.DispensableDef;
            }
        }

        private static Thing FindClosestUsableStewpot(
            Pawn getter,
            Pawn eater,
            bool desperate,
            FoodPreferability maxPref,
            bool allowForbidden,
            bool ignoreReservations,
            FoodPreferability minPrefOverride,
            float? minNutrition,
            bool allowVenerated)
        {
            List<Thing> stewpots = getter.Map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_bigpot);
            if (stewpots.NullOrEmpty())
            {
                return null;
            }

            Thing best = null;
            float bestDistanceSquared = float.MaxValue;
            for (int i = 0; i < stewpots.Count; i++)
            {
                Thing stewpot = stewpots[i];
                if (!IsUsableStewpot(stewpot, getter, eater, desperate, maxPref, allowForbidden, ignoreReservations, minPrefOverride, minNutrition, allowVenerated))
                {
                    continue;
                }

                float distanceSquared = (stewpot.Position - getter.Position).LengthHorizontalSquared;
                if (distanceSquared < bestDistanceSquared)
                {
                    best = stewpot;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return best;
        }

        private static bool IsUsableStewpot(
            Thing thing,
            Pawn getter,
            Pawn eater,
            bool desperate,
            FoodPreferability maxPref,
            bool allowForbidden,
            bool ignoreReservations,
            FoodPreferability minPrefOverride,
            float? minNutrition,
            bool allowVenerated)
        {
            if (!(thing is Building_GoblinStewpot pot) || pot.Destroyed || pot.Fogged() || !pot.HasEnoughFeedstockInHoppers())
            {
                return false;
            }

            if (pot.Faction != getter.Faction && pot.Faction != getter.HostFaction)
            {
                return false;
            }

            if (!allowForbidden && pot.IsForbidden(getter))
            {
                return false;
            }

            if (!ignoreReservations && !getter.CanReserve(pot))
            {
                return false;
            }

            CompPowerTrader power = pot.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                return false;
            }

            ThingDef stewDef = pot.DispensableDef;
            if (stewDef?.ingestible == null)
            {
                return false;
            }

            FoodPreferability minPref = MinimumPreferability(eater, desperate, minPrefOverride);
            FoodPreferability preferability = stewDef.ingestible.preferability;
            if (preferability < minPref || preferability > maxPref)
            {
                return false;
            }

            if (minNutrition.HasValue && stewDef.GetStatValueAbstract(StatDefOf.Nutrition) < minNutrition.Value)
            {
                return false;
            }

            if (!eater.WillEat(stewDef, getter, careIfNotAcceptableForTitle: true, allowVenerated: allowVenerated))
            {
                return false;
            }

            return getter.CanReach(pot, PathEndMode.Touch, Danger.Some);
        }

        private static FoodPreferability MinimumPreferability(Pawn eater, bool desperate, FoodPreferability minPrefOverride)
        {
            if (minPrefOverride != FoodPreferability.Undefined)
            {
                return minPrefOverride;
            }

            if (eater.RaceProps?.Humanlike != true)
            {
                return FoodPreferability.NeverForNutrition;
            }

            if (desperate)
            {
                return FoodPreferability.DesperateOnly;
            }

            return eater.needs?.food != null && eater.needs.food.CurCategory >= HungerCategory.Hungry
                ? FoodPreferability.RawBad
                : FoodPreferability.MealAwful;
        }
    }
}
