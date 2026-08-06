using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    public class Recipe_ExtractHeart : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true)
            {
                yield break;
            }

            BodyPartRecord heart = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part.def.defName == "Heart");
            if (heart != null)
            {
                yield return heart;
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null)
            {
                return;
            }

            SpawnProductNearPawn(pawn, MUGBDefOf.MUGB_heart, 1);

            if (!pawn.Dead && part != null)
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 99999f, 999f, -1f, billDoer, part));
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
