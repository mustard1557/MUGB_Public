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

        // 마지막으로 외형 갱신을 알린 시점입니다.
        private int nextRedrawTick = -1;

        // 외형을 갱신시키는 주기(게임 내 1시간).
        //
        // Sized Apparel은 Pawn_HealthTracker.Notify_HediffChanged를 후킹해서 그때만
        // 배 크기를 다시 계산합니다(SizedApparelMain.cs의 PawnHealthTrackerPatch).
        // 그런데 이 헤디프의 severity는 HediffComp_SeverityPerDay로 조용히 차오르기만 하고,
        // 그 알림은 단계가 바뀌는 순간(0.88 / 0.96)에만 발생합니다. 그래서 잉태 60%가
        // 되어도 SA는 처음 계산해둔 0에 가까운 배를 계속 그리고 있었습니다.
        //
        // RJW 임신이 잘 보이는 이유는 SA가 RJW 전용 갱신 패치를 따로 갖고 있기 때문이고
        // (Mods/Patch-RimJobWorld.cs), 우리 헤디프에는 그런 것이 없습니다.
        //
        // 그래서 우리가 직접 알립니다. Notify_HediffChanged는 바닐라 메서드라
        // SA를 참조하지 않고, SA가 없으면 그냥 아무도 안 듣습니다.
        // 1.5일 주기에 36번이라 비용은 무시할 수준입니다.
        private const int RedrawIntervalTicks = 2500;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref delivered, "MUGB_tumorDelivered", false);
            Scribe_Values.Look(ref nextRedrawTick, "MUGB_tumorNextRedrawTick", -1);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            NotifyAppearanceIfDue();

            if (delivered || parent.Severity < 1f)
            {
                return;
            }

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
            {
                return;
            }

            // 재진입 방지 겸 제거 신호입니다. 아래 CompShouldRemove가 이 값을 봅니다.
            delivered = true;

            MUGB_SeedbedUtility.DeliverTumors(pawn);
        }

        // 배가 자라는 것이 보이도록 주기적으로 외형 갱신을 알립니다.
        private void NotifyAppearanceIfDue()
        {
            if (delivered)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (now < nextRedrawTick)
            {
                return;
            }

            nextRedrawTick = now + RedrawIntervalTicks;

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
            {
                return;
            }

            pawn.health.Notify_HediffChanged(parent);
        }

        // 헤디프를 스스로 pawn.health.RemoveHediff(parent)로 지우지 않습니다.
        //
        // 이 comp는 Pawn_HealthTracker가 헤디프 목록을 순회하는 도중에 호출됩니다.
        // 그 안에서 목록을 건드리면 순회가 어긋나거나 예외가 납니다. 바닐라가
        // HediffComp_Disappears 등에서 쓰는 방식이 이것이고, 체력 처리가 순회를
        // 끝낸 뒤에 안전하게 제거해 줍니다.
        public override bool CompShouldRemove => delivered;

        public override string CompLabelInBracketsExtra
        {
            get { return parent.Severity.ToStringPercent("F0"); }
        }
    }
}
