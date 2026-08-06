using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    public class Recipe_ExtractFleshChunks : Recipe_Surgery
    {
        private const float SuccessChance = 0.7f;
        private const int ProductCount = 4;
        private const int FailedProductCount = 2;
        private const int LimbsToRemove = 2;

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true)
            {
                yield break;
            }

            List<BodyPartRecord> limbs = GetAvailableLimbs(pawn);
            if (limbs.Count < LimbsToRemove)
            {
                yield break;
            }

            BodyPartRecord torso = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Torso");
            if (torso != null)
            {
                yield return torso;
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null)
            {
                return;
            }

            bool violation = IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail);
            List<BodyPartRecord> limbs = GetAvailableLimbs(pawn);
            if (limbs.Count < LimbsToRemove)
            {
                return;
            }

            if (Rand.Chance(SuccessChance))
            {
                SpawnProductNearPawn(pawn, ProductFor(pawn), ProductCount);
                foreach (BodyPartRecord limb in ChooseLimbsToRemove(limbs))
                {
                    if (!pawn.Dead && pawn.health.hediffSet.GetNotMissingParts().Contains(limb))
                    {
                        pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 99999f, 999f, -1f, billDoer, limb));
                    }
                }

                ApplyLiveButcheryConsequences(pawn, billDoer);
            }
            else if (!pawn.Dead)
            {
                SpawnProductNearPawn(pawn, ProductFor(pawn), FailedProductCount);
                pawn.Kill(null);
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (violation)
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -50);
            }
        }

        private static List<BodyPartRecord> GetAvailableLimbs(Pawn pawn)
        {
            return pawn.health.hediffSet.GetNotMissingParts()
                .Where(IsExtractableLimb)
                .ToList();
        }

        private static bool IsExtractableLimb(BodyPartRecord part)
        {
            string defName = part?.def?.defName;
            return defName == "Arm" || defName == "Leg";
        }

        private static IEnumerable<BodyPartRecord> ChooseLimbsToRemove(List<BodyPartRecord> limbs)
        {
            List<BodyPartRecord> chosen = new List<BodyPartRecord>();
            foreach (BodyPartRecord limb in limbs.InRandomOrder())
            {
                chosen.Add(limb);
                if (chosen.Count >= LimbsToRemove)
                {
                    break;
                }
            }

            return chosen;
        }

        private static ThingDef ProductFor(Pawn pawn)
        {
            return GoblinUtility.IsGoblin(pawn) ? MUGBDefOf.MUGB_Gchunk : MUGBDefOf.MUGB_Hchunk;
        }

        private static void SpawnProductNearPawn(Pawn pawn, ThingDef def, int count)
        {
            if (pawn?.Map == null || def == null || count <= 0)
            {
                return;
            }

            Thing product = ThingMaker.MakeThing(def);
            product.stackCount = count;
            GenPlace.TryPlaceThing(product, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        }

        private static void ApplyLiveButcheryConsequences(Pawn pawn, Pawn billDoer)
        {
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            if (MUGBDefOf.MUGB_LiveButcheryAftermath != null && !pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_LiveButcheryAftermath))
            {
                pawn.health.AddHediff(MUGBDefOf.MUGB_LiveButcheryAftermath);
            }

            HealthUtility.AdjustSeverity(pawn, HediffDefOf.BloodLoss, 0.18f);

            if (GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_ButcheredAlive, billDoer);

            if (billDoer != null && billDoer != pawn)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_ButcheredAliveSocial, billDoer);

                if (!GoblinUtility.IsGoblin(billDoer))
                {
                    billDoer.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_PerformedLiveButchery, pawn);
                }
            }
        }
    }
}
