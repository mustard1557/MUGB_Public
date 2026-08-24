using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace MUGB.Patches
{
    internal static class GoblinTraderSlaveDiscountUtility
    {
        private const float GoblinTraderBuyPriceFactor = 0.9f;
        private const float LivestockTraderBuyPriceFactor = 0.85f;
        private const string GoblinTraderPrefix = "MUGB_Caravan_Goblin";
        private const string GoblinLivestockTrader = "MUGB_Caravan_GoblinLivestock";

        public static bool TryGetBuyPriceFactor(Tradeable tradeable, TradeAction action, out float factor)
        {
            factor = 1f;
            if (action != TradeAction.PlayerBuys
                || !(tradeable?.AnyThing is Pawn pawn)
                || !pawn.IsSlave)
            {
                return false;
            }

            string traderDefName = TradeSession.trader?.TraderKind?.defName;
            if (traderDefName?.StartsWith(GoblinTraderPrefix, StringComparison.Ordinal) != true)
            {
                return false;
            }

            factor = traderDefName == GoblinLivestockTrader
                ? LivestockTraderBuyPriceFactor
                : GoblinTraderBuyPriceFactor;
            return true;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Tradeable_GetPriceFor_GoblinSlaveDiscountPatch
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref float __result)
        {
            if (GoblinTraderSlaveDiscountUtility.TryGetBuyPriceFactor(__instance, action, out float factor))
            {
                __result *= factor;
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceTooltip))]
    public static class Tradeable_GetPriceTooltip_GoblinSlaveDiscountPatch
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref string __result)
        {
            if (GoblinTraderSlaveDiscountUtility.TryGetBuyPriceFactor(__instance, action, out float factor))
            {
                __result += "\n  x "
                    + factor.ToString("F2")
                    + " ("
                    + "MUGB_GoblinTraderSlaveDiscount".Translate()
                    + ")";
            }
        }
    }
}
