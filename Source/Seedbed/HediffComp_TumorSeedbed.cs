using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 영구 모판 헤디프에 붙는 comp입니다.
    //
    // 두 가지를 합니다.
    //   1) 자동 종부 on/off 상태를 폰별로 들고 있습니다.
    //   2) 다른 임신 시스템(바닐라 / RJW / 생리모드)이 이 폰에 임신을 걸면
    //      그것을 걷어내고 종양 잉태로 바꿉니다.
    //
    // 2번을 남의 모드를 패치하지 않고 여기서 처리하는 이유:
    //   - 세 시스템의 착상 경로를 각각 패치하면 세 배로 깨지고, 특히 생리모드는 배란 주기
    //     밖 착상을 막기 때문에 우리 100% 착상과 정면충돌합니다.
    //   - 반면 "임신 헤디프가 붙었으면 바꾼다"는 결과만 보므로, 세 시스템은 물론 아직
    //     없는 임신 모드까지 같은 코드로 처리됩니다. 남의 어셈블리를 참조하는 지점이
    //     하나도 없어서 림잡을 켜고 꺼도 세이브가 갈라지지 않습니다.
    //   - 덤으로 이것이 "사랑나누기로도 종양이 나온다"를 그대로 구현합니다. 바닐라 로빈으로
    //     임신이 걸리면 그 자리에서 종양 잉태로 전환됩니다.
    //
    // 틱 비용:
    //   CompPostTickInterval은 이미 배치 호출이고, 여기서 다시 1시간(2500틱)에 한 번만
    //   실제 검사를 합니다. 나머지 호출은 정수 비교 하나로 빠져나갑니다.
    //   검사 자체도 폰의 헤디프 목록을 한 번 훑는 것뿐이고, 대상은 모판 폰뿐입니다.
    public class HediffCompProperties_TumorSeedbed : HediffCompProperties
    {
        public HediffCompProperties_TumorSeedbed()
        {
            compClass = typeof(HediffComp_TumorSeedbed);
        }
    }

    public class HediffComp_TumorSeedbed : HediffComp
    {
        private const int CheckIntervalTicks = 2500;

        // 기본값이 true인 이유: 이 수술의 목적 자체가 생산이므로, 수술을 해 두고
        // 스위치를 따로 켜야 한다면 한 단계가 그냥 번거롭기만 합니다.
        // 멈추고 싶으면 인간가축 탭에서 끕니다.
        private bool autoActivate = true;

        private int nextCheckTick = -1;

        // 누적 산출량. 인간가축 탭이 "이 모판이 지금까지 얼마나 뽑았는지"를 보여주는 데 씁니다.
        private int totalTumors;

        public bool AutoActivate
        {
            get => autoActivate;
            set => autoActivate = value;
        }

        public int TotalTumors => totalTumors;

        public void RecordDelivery(int count)
        {
            totalTumors += count;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref autoActivate, "MUGB_seedbedAutoActivate", true);
            Scribe_Values.Look(ref nextCheckTick, "MUGB_seedbedNextCheckTick", -1);
            Scribe_Values.Look(ref totalTumors, "MUGB_seedbedTotalTumors", 0);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextCheckTick)
            {
                return;
            }

            nextCheckTick = now + CheckIntervalTicks;

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            ConvertForeignPregnancy(pawn);
        }

        // 외부 임신을 종양 잉태로 갈아끼웁니다.
        //
        // 이미 종양 잉태 중이면 외부 임신은 그냥 제거만 합니다. 중복 임신을 허용하면
        // 생리모드의 다태 임신 경로가 우리 잉태 위에 정상 아기를 얹어버립니다.
        private static void ConvertForeignPregnancy(Pawn pawn)
        {
            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return;
            }

            Hediff foreign = null;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff?.def != null && MUGB_SeedbedUtility.ForeignPregnancyDefs.Contains(hediff.def))
                {
                    foreign = hediff;
                    break;
                }
            }

            if (foreign == null)
            {
                return;
            }

            pawn.health.RemoveHediff(foreign);
            MUGB_SeedbedUtility.TryStartGestation(pawn);
        }

        public override string CompTipStringExtra
        {
            get
            {
                return autoActivate
                    ? "MUGB_SeedbedAutoActivateOn".Translate()
                    : "MUGB_SeedbedAutoActivateOff".Translate();
            }
        }
    }
}
