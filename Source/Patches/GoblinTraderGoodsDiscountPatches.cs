using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace MUGB.Patches
{
    internal static class GoblinTraderGoodsDiscountUtility
    {
        private const float PlayerBuyPriceFactor = 0.8f;
        private const string GoblinTraderPrefix = "MUGB_Caravan_Goblin";

        public static bool TryGetBuyPriceFactor(Tradeable tradeable, TradeAction action, out float factor)
        {
            factor = 1f;
            Thing thing = tradeable?.AnyThing;
            if (action != TradeAction.PlayerBuys
                || thing == null
                || thing is Pawn
                || thing.def == ThingDefOf.Silver)
            {
                return false;
            }

            string traderDefName = TradeSession.trader?.TraderKind?.defName;
            if (traderDefName?.StartsWith(GoblinTraderPrefix, StringComparison.Ordinal) != true)
            {
                return false;
            }

            factor = PlayerBuyPriceFactor;
            return true;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Tradeable_GetPriceFor_GoblinGoodsDiscountPatch
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref float __result)
        {
            if (GoblinTraderGoodsDiscountUtility.TryGetBuyPriceFactor(__instance, action, out float factor))
            {
                __result *= factor;
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceTooltip))]
    public static class Tradeable_GetPriceTooltip_GoblinGoodsDiscountPatch
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref string __result)
        {
            if (GoblinTraderGoodsDiscountUtility.TryGetBuyPriceFactor(__instance, action, out float factor))
            {
                __result += "\n  x "
                    + factor.ToString("F2")
                    + " ("
                    + "MUGB_GoblinTraderGoodsDiscount".Translate()
                    + ")";
            }
        }
    }
}
