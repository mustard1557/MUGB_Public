using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class MUGBThingSetMaker_GoblinRaidLoot : ThingSetMaker
    {
        public ThingSetMaker baseLootMaker;

        public float totalValueFactor = 1.1f;

        public int minimumGold = 10;

        public int minimumFineAlloy = 20;

        protected override bool CanGenerateSub(ThingSetMakerParams parms)
        {
            return parms.totalMarketValueRange.HasValue
                && parms.totalMarketValueRange.Value.max > 0f
                && MUGBDefOf.MUGB_FineGoblinAlloy != null;
        }

        protected override void Generate(ThingSetMakerParams parms, List<Thing> outThings)
        {
            float originalBudget = parms.totalMarketValueRange?.RandomInRange ?? 0f;
            if (originalBudget <= 0f)
            {
                return;
            }

            int goldCount = Mathf.Clamp(
                minimumGold + Mathf.FloorToInt(Mathf.Max(0f, originalBudget - 200f) / 160f),
                minimumGold,
                30);
            int alloyCount = Mathf.Clamp(
                minimumFineAlloy + Mathf.FloorToInt(Mathf.Max(0f, originalBudget - 200f) / 45f),
                minimumFineAlloy,
                60);

            AddStacks(ThingDefOf.Gold, goldCount, outThings);
            AddStacks(MUGBDefOf.MUGB_FineGoblinAlloy, alloyCount, outThings);

            float guaranteedValue =
                ThingDefOf.Gold.BaseMarketValue * goldCount
                + MUGBDefOf.MUGB_FineGoblinAlloy.BaseMarketValue * alloyCount;
            float remainingBudget = Mathf.Max(0f, originalBudget * totalValueFactor - guaranteedValue);
            if (remainingBudget <= 0f || baseLootMaker == null)
            {
                return;
            }

            ThingSetMakerParams remainderParms = parms;
            remainderParms.totalMarketValueRange = new FloatRange(remainingBudget, remainingBudget);
            outThings.AddRange(baseLootMaker.Generate(remainderParms));
        }

        private static void AddStacks(ThingDef def, int count, List<Thing> outThings)
        {
            while (count > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = Mathf.Min(count, def.stackLimit);
                count -= thing.stackCount;
                outThings.Add(thing);
            }
        }

        protected override IEnumerable<ThingDef> AllGeneratableThingsDebugSub(ThingSetMakerParams parms)
        {
            yield return ThingDefOf.Gold;
            if (MUGBDefOf.MUGB_FineGoblinAlloy != null)
            {
                yield return MUGBDefOf.MUGB_FineGoblinAlloy;
            }

            if (baseLootMaker == null)
            {
                yield break;
            }

            foreach (ThingDef def in baseLootMaker.AllGeneratableThingsDebug(parms))
            {
                yield return def;
            }
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            baseLootMaker?.ResolveReferences();
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (baseLootMaker == null)
            {
                yield return "baseLootMaker is null.";
            }
            else
            {
                foreach (string error in baseLootMaker.ConfigErrors())
                {
                    yield return error;
                }
            }

            if (totalValueFactor < 1f)
            {
                yield return "totalValueFactor must be at least 1.";
            }
        }
    }
}
