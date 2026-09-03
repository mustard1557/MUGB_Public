using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    internal static class GoblinRawTraderPriceUtility
    {
        private const float PlayerSellPriceFactor = 0.8f;
        private const string RawMaterialsTrader = "MUGB_Caravan_GoblinRawMaterials";

        public static bool TryGetSellPriceFactor(Tradeable tradeable, TradeAction action, out float factor)
        {
            factor = 1f;
            if (action != TradeAction.PlayerSells
                || TradeSession.trader?.TraderKind?.defName != RawMaterialsTrader)
            {
                return false;
            }

            ThingDef def = tradeable?.AnyThing?.GetInnerIfMinified()?.def;
            if (def == null || !def.IsWithinCategory(ThingCategoryDefOf.ResourcesRaw))
            {
                return false;
            }

            factor = PlayerSellPriceFactor;
            return true;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Tradeable_GetPriceFor_GoblinRawTraderPatch
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref float __result)
        {
            if (GoblinRawTraderPriceUtility.TryGetSellPriceFactor(__instance, action, out float factor))
            {
                __result *= factor;
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceTooltip))]
    public static class Tradeable_GetPriceTooltip_GoblinRawTraderPatch
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref string __result)
        {
            if (GoblinRawTraderPriceUtility.TryGetSellPriceFactor(__instance, action, out float factor))
            {
                __result += "\n  x "
                    + factor.ToString("F2")
                    + " ("
                    + "MUGB_GoblinRawTraderPurchaseDiscount".Translate()
                    + ")";
            }
        }
    }
}
