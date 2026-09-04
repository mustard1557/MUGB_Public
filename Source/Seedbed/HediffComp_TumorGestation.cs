using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 종양 잉태의 만삭 판정입니다.
    //
    // 진행 자체는 바닐라 HediffComp_SeverityPerDay(0.667/일 = 1.5일에 1.0)가 합니다.
    // 여기서 하는 일은 severity >= 1 비교 하나뿐이고, 그 다음 틱부터는 헤디프가 사라지므로
    // 더 돌지도 않습니다.
    //
    // 진통을 별도 헤디프로 나누지 않고 이 헤디프의 마지막 두 단계로 접은 것도 같은 이유입니다.
    // 주기가 1.5일이라 헤디프를 붙였다 떼는 일이 잦은데, 단계로 접으면 그 붙임/떼임이
    // 전부 사라지고 폰당 저장되는 헤디프도 하나로 유지됩니다.
    public class HediffCompProperties_TumorGestation : HediffCompProperties
    {
        public HediffCompProperties_TumorGestation()
        {
            compClass = typeof(HediffComp_TumorGestation);
        }
    }

    public class HediffComp_TumorGestation : HediffComp
    {
        private bool delivered;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref delivered, "MUGB_tumorDelivered", false);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (delivered || parent.Severity < 1f)
            {
                return;
            }

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
            {
                return;
            }

            // 재진입 방지. 출산 처리가 헤디프를 제거하기 전에 다음 틱이 들어올 여지를 막습니다.
            delivered = true;

            MUGB_SeedbedUtility.DeliverTumors(pawn);

            HediffComp_TumorSeedbed seedbed = MUGB_SeedbedUtility.SeedbedCompOn(pawn);
            pawn.health.RemoveHediff(parent);

            // 자동 종부. 폴링 없이 여기 한 곳에서만 다음 시술을 예약합니다.
            if (seedbed != null && seedbed.AutoActivate)
            {
                MUGB_SeedbedUtility.TryQueueActivatorBill(pawn);
            }
        }

        public override string CompLabelInBracketsExtra
        {
            get { return parent.Severity.ToStringPercent("F0"); }
        }
    }
}
