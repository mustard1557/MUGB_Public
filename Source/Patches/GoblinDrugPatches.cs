using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    public class HediffCompProperties_AddHediffOnRemoved : HediffCompProperties
    {
        public HediffDef hediffDef;
        public bool setRestToZero;

        public HediffCompProperties_AddHediffOnRemoved()
        {
            compClass = typeof(HediffComp_AddHediffOnRemoved);
        }
    }

    public class HediffComp_AddHediffOnRemoved : HediffComp
    {
        public HediffCompProperties_AddHediffOnRemoved Props => (HediffCompProperties_AddHediffOnRemoved)props;

        public override void CompPostPostRemoved()
        {
            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            if (Props.hediffDef != null && pawn.health?.hediffSet?.GetFirstHediffOfDef(Props.hediffDef) == null)
            {
                Hediff hediff = HediffMaker.MakeHediff(Props.hediffDef, pawn);
                hediff.Severity = 1f;
                pawn.health.AddHediff(hediff);
            }

            if (Props.setRestToZero && pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevel = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested), typeof(Pawn), typeof(float))]
    public static class Thing_Ingested_GoblinDrug_Patch
    {
        public static void Postfix(Thing __instance, Pawn ingester)
        {
            if (__instance?.def != MUGBDefOf.MUGB_Smartyoil || ingester == null)
            {
                return;
            }

            if (GoblinUtility.IsGoblin(ingester))
            {
                ApplySmartyoil(ingester);
            }
            else
            {
                ApplyNonGoblinSmartyOilThought(ingester);
            }
        }

        private static void ApplySmartyoil(Pawn pawn)
        {
            if (MUGBDefOf.MUGB_SmartyoilSynapticReconnection == null)
            {
                return;
            }

            Hediff hediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_SmartyoilSynapticReconnection);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(MUGBDefOf.MUGB_SmartyoilSynapticReconnection, pawn);
                hediff.Severity = 1f;
                pawn.health.AddHediff(hediff);
                return;
            }

            hediff.Severity = Math.Min(hediff.Severity + 1f, hediff.def.maxSeverity);
        }

        private static void ApplyNonGoblinSmartyOilThought(Pawn pawn)
        {
            if (pawn.needs?.mood?.thoughts?.memories == null)
            {
                return;
            }

            if (GoblinPheromonePreferenceUtility.HasPreference(pawn))
            {
                GoblinPheromonePreferenceUtility.RemoveHumanlikeAndGoblinFoodPenalties(pawn);
                if (MUGBDefOf.MUGB_AteGoblinFoodPreferred != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_AteGoblinFoodPreferred);
                }
                return;
            }

            TraitDef cannibalTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Cannibal");
            bool cannibal = cannibalTrait != null && pawn.story?.traits?.HasTrait(cannibalTrait) == true;
            ThoughtDef thought = cannibal ? MUGBDefOf.MUGB_AteGoblinMeatAsIngredientCannibal : MUGBDefOf.MUGB_AteGoblinMeatAsIngredient;
            if (thought != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }
    }
}
