using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    public class Recipe_ExtractBrain : Recipe_Surgery
    {
        private const float SuccessChance = 0.5f;

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true)
            {
                yield break;
            }

            BodyPartRecord brain = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part.def.defName == "Brain");
            if (brain != null)
            {
                yield return brain;
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            bool success = Rand.Chance(SuccessChance);
            if (success)
            {
                SpawnProductNearPawn(pawn, MUGBDefOf.MUGB_brain, 1);
                SpawnProductNearPawn(pawn, ThingDefOf.Skull, 1);
            }

            BodyPartRecord head = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(record => record.def == BodyPartDefOf.Head);
            if (head != null)
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 99999f, 999f, -1f, billDoer, head));
            }
            else if (!pawn.Dead)
            {
                pawn.Kill(null);
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -70);
            }
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
    }
}
