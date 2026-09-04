using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 종양 모판 시스템의 수술 워커 2종입니다. 설계지침 3장.
    //
    // 대상 자격은 AvailableOnNow가 아니라 GetPartsToApplyOn에서 거릅니다.
    // 조건이 맞지 않으면 부위를 하나도 내놓지 않으므로 수술 목록에 아예 뜨지 않고,
    // 기존 MUGB_ExtractGoblinReproductiveSac이 쓰는 방식과 같습니다.

    public class Recipe_InduceTumorSeedbed : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.health?.hediffSet == null)
            {
                yield break;
            }

            // 성별은 RecipeDef.genderPrerequisite가 이미 거르지만, 다른 모드가 성별을
            // 바꾸는 경우가 있어 여기서도 한 번 봅니다.
            if (pawn.gender != Gender.Female)
            {
                yield break;
            }

            // 되돌릴 수 없는 수술이라 두 번 걸리지 않게 합니다.
            if (MUGB_SeedbedUtility.IsSeedbed(pawn))
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
            if (pawn?.health == null || MUGB_SeedbedDefOf.MUGB_TumorSeedbed == null)
            {
                return;
            }

            if (MUGB_SeedbedUtility.IsSeedbed(pawn))
            {
                return;
            }

            pawn.health.AddHediff(MUGB_SeedbedDefOf.MUGB_TumorSeedbed, part);
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGB_SeedbedDefOf.MUGB_TumorSeedbedInstalled);

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -30);
            }
        }
    }

    public class Recipe_InjectTumorActivator : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.health?.hediffSet == null)
            {
                yield break;
            }

            // 모판이 아니면 의미가 없고, 이미 잉태 중이면 덮어씌울 것이 없습니다.
            if (!MUGB_SeedbedUtility.IsSeedbed(pawn) || MUGB_SeedbedUtility.IsGestating(pawn))
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
            if (pawn?.health == null)
            {
                return;
            }

            if (!MUGB_SeedbedUtility.TryStartGestation(pawn))
            {
                return;
            }

            MUGB_SeedbedUtility.ApplyInjectionMood(pawn);
            ApplySurgeonDrain(billDoer);

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -10);
            }
        }

        // 시술자에게 누적되는 피로입니다. 차단이 아니라 누적이라 급할 때 몰아서 할 수 있고,
        // 대신 그 대가가 눈에 보입니다. severityPerDay -6이라 1회분(0.25)이 1시간에 사라집니다.
        private static void ApplySurgeonDrain(Pawn surgeon)
        {
            HediffDef def = MUGB_SeedbedDefOf.MUGB_ActivatorDrain;
            if (surgeon == null || surgeon.health == null || def == null)
            {
                return;
            }

            Hediff existing = surgeon.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing == null)
            {
                surgeon.health.AddHediff(def);
                return;
            }

            existing.Severity = UnityEngine.Mathf.Min(existing.Severity + def.initialSeverity, def.maxSeverity);
        }
    }
}
