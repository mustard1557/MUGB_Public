using RimWorld;
using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 잉태 중 무드의 단계를 고릅니다.
    //
    // 바닐라 ThoughtWorker_Hediff를 그대로 쓰지 않은 이유:
    //   그쪽은 자기 헤디프의 단계만 보고 그것을 사상 단계로 그대로 씁니다. 그래서
    //   "모판화 4단계면 반전", "피학성애자면 반전" 같은 분기를 만들 수 없습니다.
    //
    // 사상을 두 개로 나누는 대신 한 사상에 단계를 10개 두고 여기서 오프셋을 겁니다.
    //   0~4 : 평상시 (잉태 초기 -> 밀어내는 중)
    //   5~9 : 반전   (같은 순서, 부호만 반대)
    //
    // 사상을 둘로 쪼개면 한쪽이 붙어 있는 동안 다른 쪽이 사라지는 전환 순간에
    // 무드가 튀고, 번역 파일도 두 벌이 됩니다. 한 사상 안에서 단계만 옮기면
    // 전환이 그냥 단계 변경이라 그런 틈이 없습니다.
    //
    // 반전 조건은 셋 중 하나입니다.
    //   - 모판화 4단계          (본편)
    //   - RJW 무너짐 최종 단계  (설치되어 있을 때만, def 조회로만 판별)
    //   - 피학성애자 특성
    //
    // 피학성애자를 넣은 이유: 바닐라 통증 사상이 이미 그 특성으로 무효화됩니다
    // (Core/Defs/ThoughtDefs/Thoughts_Situation_Special.xml의 Pain).
    // 통증은 즐기는데 그 통증의 원인만 괴로워하는 것은 앞뒤가 맞지 않습니다.
    public class ThoughtWorker_TumorGestation : ThoughtWorker
    {
        // 평상시 단계 수. 반전 단계는 이만큼 뒤에 같은 순서로 놓입니다.
        private const int ReversedStageOffset = 5;

        private static bool masochistResolved;
        private static TraitDef masochistTrait;

        private static TraitDef MasochistTrait
        {
            get
            {
                if (!masochistResolved)
                {
                    masochistResolved = true;
                    masochistTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Masochist");
                }

                return masochistTrait;
            }
        }

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Hediff gestation = p?.health?.hediffSet?.GetFirstHediffOfDef(MUGB_SeedbedDefOf.MUGB_TumorGestation);
            if (gestation == null)
            {
                return ThoughtState.Inactive;
            }

            int stage = gestation.CurStageIndex;
            if (IsReversed(p))
            {
                stage += ReversedStageOffset;
            }

            return ThoughtState.ActiveAtStage(stage);
        }

        private static bool IsReversed(Pawn pawn)
        {
            if (MUGB_SeedbedUtility.MoodIsReversed(pawn))
            {
                return true;
            }

            TraitDef masochist = MasochistTrait;
            return masochist != null && pawn.story?.traits?.HasTrait(masochist) == true;
        }
    }
}
