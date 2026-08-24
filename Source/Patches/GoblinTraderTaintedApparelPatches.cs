using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.PlayerSellableNow))]
    public static class TradeUtility_PlayerSellableNow_GoblinTaintedApparelPatch
    {
        private const string GoblinTraderPrefix = "MUGB_Caravan_Goblin";

        public static void Postfix(Thing t, ITrader trader, ref bool __result)
        {
            if (__result || t == null || trader?.TraderKind?.defName == null
                || !trader.TraderKind.defName.StartsWith(GoblinTraderPrefix))
            {
                return;
            }

            Thing inner = t.GetInnerIfMinified();
            if (!(inner is Apparel apparel) || !apparel.WornByCorpse
                || !trader.TraderKind.WillTrade(apparel.def))
            {
                return;
            }

            // The vanilla method rejects tainted apparel before reaching its remaining checks.
            // Re-run those checks unchanged so goblin traders only ignore the tainted restriction.
            __result = TradeUtility.EverPlayerSellable(apparel.def)
                && !apparel.IsNotFresh()
                && !CompBiocodable.IsBiocoded(apparel);
        }
    }
}
