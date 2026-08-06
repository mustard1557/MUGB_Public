using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    public class Recipe_NosePickLobotomy : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true || MUGBSurgeryUtility.HasNosePickedLobotomy(pawn))
            {
                yield break;
            }

            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts().Where(part => part?.def?.defName == "Brain"))
            {
                yield return part;
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null)
            {
                return;
            }

            bool violation = IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail);
            if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
            {
                if (!pawn.Dead && part != null)
                {
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 12f, 999f, -1f, billDoer, part));
                }
            }
            else if (!pawn.Dead && MUGBDefOf.MUGB_NosePickedLobotomy != null && !MUGBSurgeryUtility.HasNosePickedLobotomy(pawn))
            {
                pawn.health.AddHediff(MUGBDefOf.MUGB_NosePickedLobotomy, part);
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (violation)
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -70);
            }
        }
    }

    public static class MUGBSurgeryUtility
    {
        private static readonly HashSet<string> NosePickNullifiedThoughts = new HashSet<string>
        {
            "MUGB_AteVegetableIngredient",
            "MUGB_AteProperFlesh",
            "MUGB_AteHeartGoblin",
            "MUGB_AteBrainGoblin",
            "MUGB_AteGoblinMeatDirect",
            "MUGB_AteGoblinMeatDirectCannibal",
            "MUGB_AteGoblinMeatAsIngredient",
            "MUGB_AteGoblinMeatAsIngredientCannibal",
            "MUGB_AteGoblinFoodPreferred",
            "MUGB_FleshCravingLow",
            "MUGB_GoblinPregnancyBurden",
            "MUGB_GaveBirthToGoblinLitter",
            "MUGB_GoblinChildDiedRelief",
            "MUGB_ButcheredAlive",
            "MUGB_PerformedLiveButchery"
        };

        public static bool HasNosePickedLobotomy(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.HasHediff(MUGBDefOf.MUGB_NosePickedLobotomy) == true;
        }

        public static float ToxicPheromoneSensitivityFactor(Pawn pawn)
        {
            return HasNosePickedLobotomy(pawn) ? 0.25f : 1f;
        }

        public static float ToxicPheromoneEmissionFactor(Pawn pawn)
        {
            return HasNosePickedLobotomy(pawn) ? 0.5f : 1f;
        }

        public static bool ShouldNullifyMugbThought(Pawn pawn, ThoughtDef thought)
        {
            return HasNosePickedLobotomy(pawn)
                && thought != null
                && NosePickNullifiedThoughts.Contains(thought.defName);
        }
    }

    [HarmonyPatch(typeof(PrisonBreakUtility), nameof(PrisonBreakUtility.CanParticipateInPrisonBreak))]
    public static class PrisonBreakUtility_CanParticipateInPrisonBreak_NosePickPatch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && MUGBSurgeryUtility.HasNosePickedLobotomy(pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.CanParticipateInSlaveRebellion))]
    public static class SlaveRebellionUtility_CanParticipateInSlaveRebellion_NosePickPatch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && MUGBSurgeryUtility.HasNosePickedLobotomy(pawn))
            {
                __result = false;
            }
        }
    }
}
