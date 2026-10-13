using System.Collections.Generic;
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
    //   CompPostTickInterval은 이미 배치 호출이고, 여기서 다시 30분(1250틱)에 한 번만
    //   실제 검사를 합니다. 나머지 호출은 정수 비교 하나로 빠져나갑니다.
    //   검사 자체도 폰의 헤디프 목록 한 번 + 기록값 두 번 + 기억 목록 한 번이 전부이고,
    //   대상은 모판 폰뿐입니다.
    public class HediffCompProperties_TumorSeedbed : HediffCompProperties
    {
        public HediffCompProperties_TumorSeedbed()
        {
            compClass = typeof(HediffComp_TumorSeedbed);
        }
    }

    public class HediffComp_TumorSeedbed : HediffComp
    {
        // 30분(게임 내)마다 확인합니다. 성행위 직후 반응이 눈에 보여야 하는데
        // 1시간은 "안 되는 건가?"로 읽힐 만큼 깁니다. 검사 자체가 기록값 비교 몇 번이라
        // 주기를 절반으로 줄여도 비용은 여전히 무시할 수준이고, 대상도 모판 폰뿐입니다.
        private const int CheckIntervalTicks = 1250;

        // 기본값이 true인 이유: 이 수술의 목적 자체가 생산이므로, 수술을 해 두고
        // 스위치를 따로 켜야 한다면 한 단계가 그냥 번거롭기만 합니다.
        // 멈추고 싶으면 인간가축 탭에서 끕니다.
        private bool autoActivate = true;

        private int nextCheckTick = -1;

        // 누적 산출량. 인간가축 탭이 "이 모판이 지금까지 얼마나 뽑았는지"를 보여주는 데 씁니다.
        private int totalTumors;

        // 출산 횟수입니다. 종양 개수(totalTumors)와 다릅니다.
        private int births;

        // 성행위 신호의 직전 값입니다. 이 값이 올라가면 성행위가 있었다는 뜻입니다.
        // -1은 아직 기준값을 안 잡았다는 표시이고, 그때는 발동시키지 않고 기준만 잡습니다
        // (수술 직후나 세이브 로드 직후에 과거 기록으로 잘못 발동하는 것을 막습니다).
        private float lastCreampied = -1f;
        private float lastSexCount = -1f;

        public bool AutoActivate
        {
            get => autoActivate;
            set => autoActivate = value;
        }

        public int TotalTumors => totalTumors;

        public int BirthCount => births;

        public void RecordDelivery(int count)
        {
            totalTumors += count;
            births++;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref autoActivate, "MUGB_seedbedAutoActivate", true);
            Scribe_Values.Look(ref nextCheckTick, "MUGB_seedbedNextCheckTick", -1);
            Scribe_Values.Look(ref totalTumors, "MUGB_seedbedTotalTumors", 0);
            Scribe_Values.Look(ref births, "MUGB_seedbedBirths", 0);
            Scribe_Values.Look(ref lastCreampied, "MUGB_seedbedLastCreampied", -1f);
            Scribe_Values.Look(ref lastSexCount, "MUGB_seedbedLastSexCount", -1f);
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
            CheckSexSignals(pawn);
        }

        // 성행위가 있었으면 종양 잉태를 시작합니다.
        //
        // 임신이 성사되기를 기다리지 않는 것이 핵심입니다. 모판의 난소는 배란 주기도
        // 수정 확률도 타지 않는다는 것이 이 시스템의 전제이므로, 정자가 들어온 사실
        // 하나로 충분합니다. 나이가 많아 임신률이 0이어도 종양은 생깁니다
        // (산출량은 MUGB_SeedbedUtility.TumorCountRangeFor가 따로 줄입니다).
        private void CheckSexSignals(Pawn pawn)
        {
            float creampied = MUGB_SeedbedUtility.CreampiedRecordOf(pawn);
            float sexCount = MUGB_SeedbedUtility.SexCountRecordOf(pawn);

            // 생리모드가 있으면 질내사정 기록만 봅니다. CountOfSex는 구강·항문 성교까지
            // 세므로, 둘을 함께 보면 정확한 신호가 있는데도 부정확한 쪽 때문에
            // 정자 없이 종양이 생깁니다. 폴백은 생리모드가 없을 때만 씁니다.
            bool useCreampie = MUGB_SeedbedUtility.HasCreampieRecord;

            bool baselineMissing = useCreampie ? lastCreampied < 0f : lastSexCount < 0f;
            bool recordRose = useCreampie
                ? creampied > lastCreampied
                : sexCount > lastSexCount;

            bool signalled =
                !baselineMissing
                && (recordRose || MUGB_SeedbedUtility.HadRecentVanillaLovin(pawn, CheckIntervalTicks));

            lastCreampied = creampied;
            lastSexCount = sexCount;

            if (baselineMissing)
            {
                // 기준값만 잡고 이번에는 넘어갑니다.
                return;
            }

            if (signalled)
            {
                MUGB_SeedbedUtility.TryStartGestation(pawn);
            }
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

            // 한 번에 전부 걷습니다. 예전에는 첫 번째 하나만 지우고 빠져나가서,
            // 임신 헤디프가 둘 이상 붙는 경우 시간당 하나씩만 정리되었습니다.
            // 뒤에서부터 도는 이유는 목록에서 지우면서 순회하기 때문입니다.
            List<Hediff> foreign = null;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffs[i];
                if (hediff?.def != null && MUGB_SeedbedUtility.ForeignPregnancyDefs.Contains(hediff.def))
                {
                    (foreign ?? (foreign = new List<Hediff>())).Add(hediff);
                }
            }

            if (foreign == null)
            {
                return;
            }

            for (int i = 0; i < foreign.Count; i++)
            {
                pawn.health.RemoveHediff(foreign[i]);
            }

            MUGB_SeedbedUtility.TryStartGestation(pawn);
        }

        // 건강 탭의 헤디프 이름 옆에 남은 수명을 붙입니다. 예고 없이 죽으면 유저가
        // 버그로 읽으므로, 언제 끝나는지가 항상 보여야 합니다.
        public override string CompLabelInBracketsExtra
        {
            get
            {
                int limit = MUGB_SeedbedUtility.BirthLimit;
                return limit > 0 ? births + " / " + limit : births.ToString();
            }
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
