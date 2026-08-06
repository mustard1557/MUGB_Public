using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace MUGB
{
    public class Recipe_ExtractVisceraFromCorpse : RecipeWorker
    {
        private const float ButcherSpotFactor = 0.025f;
        private const float ButcherTableFactor = 0.04f;
        private const int ButcherSpotMaxGuts = 2;
        private const int ButcherTableMaxGuts = 3;

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);

            Corpse corpse = ingredients?.OfType<Corpse>().FirstOrDefault();
            Pawn innerPawn = corpse?.InnerPawn;
            if (billDoer?.Map == null || innerPawn?.RaceProps?.Humanlike != true)
            {
                return;
            }

            ThingDef gutDef = GoblinUtility.IsGoblin(innerPawn) ? MUGBDefOf.MUGB_Ggut : MUGBDefOf.MUGB_Hgut;
            if (gutDef == null)
            {
                return;
            }

            Thing billGiver = billDoer.CurJob?.GetTarget(TargetIndex.A).Thing;
            bool butcherSpot = billGiver?.def?.defName == "ButcherSpot";
            int maxGuts = butcherSpot ? ButcherSpotMaxGuts : ButcherTableMaxGuts;
            float factor = butcherSpot ? ButcherSpotFactor : ButcherTableFactor;
            StatDef efficiencyStat = DefDatabase<StatDef>.GetNamedSilentFail("ButcheryFleshEfficiency");
            float efficiency = efficiencyStat != null ? billDoer.GetStatValue(efficiencyStat) : 1f;
            int amount = GenMath.RoundRandom(innerPawn.GetStatValue(StatDefOf.MeatAmount) * efficiency * factor);
            amount = Math.Min(maxGuts, Math.Max(1, amount));

            Thing guts = ThingMaker.MakeThing(gutDef);
            guts.stackCount = amount;
            IntVec3 dropCell = billGiver?.Position ?? billDoer.Position;
            GenPlace.TryPlaceThing(guts, dropCell, billDoer.Map, ThingPlaceMode.Near);
        }
    }
}
