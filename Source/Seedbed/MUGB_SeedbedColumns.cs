using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 인간가축 탭 '모판' 뷰의 컬럼들입니다.
    //
    // 이 뷰 한 곳에서 후보 물색과 라인 관리를 다 합니다. 행은 두 종류입니다.
    //   - 모판  : 이미 시술이 끝난 폰. 위쪽에 모입니다.
    //   - 후보  : 아직 아닌 여성 죄수/노예. 아래쪽에 모입니다.
    //
    // 체크박스를 종류별로 다른 컬럼에 두는 것이 이 표의 핵심입니다.
    //   후보 행은 '모판화' 칸에만 체크박스가 있고, 모판 행은 '자동' 칸에만 있습니다.
    //   즉 체크박스가 어느 칸에 있는지가 그 행이 무엇인지를 그대로 말해주므로,
    //   두 종류가 한 표에 섞여 있어도 눈으로 즉시 갈립니다. 한 칸이 두 가지 일을
    //   하도록 만들면 "이 체크가 지금 무슨 뜻이지"를 매번 생각해야 합니다.
    //
    // 나머지 칸은 두 종류 모두 의미 있는 값을 냅니다. 특히 '상태'는 후보에게도
    // 후보 / 수술 예약됨을 보여주므로, 빈 칸으로 남는 자리가 사실상 없습니다.

    // 공용 베이스. 모판이 아닌 행에서 '-'를 내는 처리를 한 곳에 모읍니다.
    public abstract class PawnColumnWorker_SeedbedText : PawnColumnWorker_Text
    {
        protected const string Blank = "-";

        protected static Hediff GestationOn(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(MUGB_SeedbedDefOf.MUGB_TumorGestation);
        }
    }

    // 후보 전용 체크박스. 켜면 모판화 수술 계획서를 걸고, 끄면 지웁니다.
    public class PawnColumnWorker_SeedbedConvert : PawnColumnWorker
    {
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (pawn == null)
            {
                return;
            }

            // 이미 모판이면 체크박스 대신 완료 표시를 둡니다. 빈 칸으로 두면
            // "왜 이 줄만 체크를 못 하지"로 읽히지만, 표시가 있으면 "끝난 줄"로 읽힙니다.
            if (MUGB_SeedbedUtility.IsSeedbed(pawn))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.55f, 0.75f, 0.55f);
                Widgets.Label(rect, "√");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(rect, "MUGB_SeedbedColumnConvertDone".Translate());
                return;
            }

            if (!MUGB_SeedbedUtility.IsSeedbedCandidate(pawn))
            {
                return;
            }

            // 연구 전에는 계획서만 쌓이고 아무도 오지 않습니다. 체크박스를 아예 내지 않고
            // 요약 줄이 이유를 설명합니다.
            if (!MUGB_SeedbedUtility.SeedbedSurgeryResearched())
            {
                return;
            }

            Rect box = new Rect(rect.x + (rect.width - 24f) / 2f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
            bool value = MUGB_SeedbedUtility.HasSeedbedSurgeryBill(pawn);
            bool old = value;
            Widgets.Checkbox(box.position, ref value, 24f);
            if (value != old)
            {
                MUGB_SeedbedUtility.SetSeedbedSurgeryBill(pawn, value);
            }

            TooltipHandler.TipRegion(rect, "MUGB_SeedbedColumnConvertTip".Translate());
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 46);

        public override int Compare(Pawn a, Pawn b)
        {
            return Rank(a).CompareTo(Rank(b));
        }

        private static int Rank(Pawn pawn)
        {
            if (MUGB_SeedbedUtility.IsSeedbed(pawn))
            {
                return 2;
            }

            return MUGB_SeedbedUtility.HasSeedbedSurgeryBill(pawn) ? 1 : 0;
        }
    }

    // 모판 전용 체크박스. 자동 종부 토글이고 사실상 이 폰의 가동 스위치입니다.
    public class PawnColumnWorker_SeedbedAuto : PawnColumnWorker
    {
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            HediffComp_TumorSeedbed comp = MUGB_SeedbedUtility.SeedbedCompOn(pawn);
            if (comp == null)
            {
                return;
            }

            Rect box = new Rect(rect.x + (rect.width - 24f) / 2f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
            bool value = comp.AutoActivate;
            Widgets.Checkbox(box.position, ref value, 24f);
            if (value != comp.AutoActivate)
            {
                comp.AutoActivate = value;

                // 켜면 즉시 예약하고, 끄면 이미 예약된 것까지 취소합니다.
                // 양쪽 다 즉시 반영해야 합니다. 끌 때 예약을 남겨두면 시술자가 그대로
                // 걸어와서 주입하고 가므로 "껐는데 왜 와?"가 됩니다.
                if (value)
                {
                    MUGB_SeedbedUtility.TryQueueActivatorBill(pawn);
                }
                else
                {
                    MUGB_SeedbedUtility.CancelActivatorBill(pawn);
                }
            }

            TooltipHandler.TipRegion(rect, "MUGB_SeedbedColumnAutoTip".Translate());
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 40);

        public override int Compare(Pawn a, Pawn b)
        {
            bool av = MUGB_SeedbedUtility.SeedbedCompOn(a)?.AutoActivate ?? false;
            bool bv = MUGB_SeedbedUtility.SeedbedCompOn(b)?.AutoActivate ?? false;
            return av.CompareTo(bv);
        }
    }

    // 모판화 단계. 최종 단계는 색으로 구분합니다 - 무드가 반전되어 관리가 끝난 폰이라
    // 목록에서 한눈에 골라낼 수 있어야 합니다.
    public class PawnColumnWorker_SeedbedStage : PawnColumnWorker
    {
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!MUGB_SeedbedUtility.IsSeedbed(pawn))
            {
                DrawCentered(rect, "-", new Color(0.5f, 0.5f, 0.5f));
                return;
            }

            int stage = Mathf.RoundToInt(MUGB_SeedbedUtility.DegenerationSeverity(pawn));
            bool final = stage >= (int)MUGB_SeedbedUtility.FinalDegenerationSeverity;

            DrawCentered(
                rect,
                stage + " / 4",
                final ? new Color(0.85f, 0.65f, 0.85f) : Color.white);

            TooltipHandler.TipRegion(rect, final
                ? "MUGB_SeedbedStageFinalTip".Translate()
                : "MUGB_SeedbedStageTip".Translate());
        }

        internal static void DrawCentered(Rect rect, string label, Color color)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = color;
            Widgets.Label(rect, label);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 52);

        public override int Compare(Pawn a, Pawn b)
        {
            return MUGB_SeedbedUtility.DegenerationSeverity(a)
                .CompareTo(MUGB_SeedbedUtility.DegenerationSeverity(b));
        }
    }

    // 현재 상태. 후보 행에도 값이 있어서 빈 칸이 생기지 않습니다.
    public class PawnColumnWorker_SeedbedStatus : PawnColumnWorker_SeedbedText
    {
        protected override string GetTextFor(Pawn pawn)
        {
            if (!MUGB_SeedbedUtility.IsSeedbed(pawn))
            {
                return MUGB_SeedbedUtility.HasSeedbedSurgeryBill(pawn)
                    ? "MUGB_SeedbedStatusSurgeryQueued".Translate()
                    : "MUGB_SeedbedStatusCandidate".Translate();
            }

            Hediff gestation = GestationOn(pawn);
            if (gestation != null)
            {
                // 인덱스는 MGB_TumorSeedbedHediffs.xml의 단계 순서와 짝입니다.
                // 0 초기 / 1 커지는 중 / 2 만삭 / 3 진통 / 4 밀어내는 중.
                // 그쪽 단계를 늘리거나 줄이면 여기도 같이 고쳐야 합니다.
                switch (gestation.CurStageIndex)
                {
                    case 4:
                        return "MUGB_SeedbedStatusPushing".Translate();
                    case 3:
                        return "MUGB_SeedbedStatusLabor".Translate();
                    case 2:
                        return "MUGB_SeedbedStatusFullTerm".Translate();
                    default:
                        return "MUGB_SeedbedStatusGestating".Translate();
                }
            }

            return MUGB_SeedbedUtility.HasActivatorBill(pawn)
                ? "MUGB_SeedbedStatusQueued".Translate()
                : "MUGB_SeedbedStatusIdle".Translate();
        }
    }

    // 출산까지 남은 시간. 진행도가 곧 남은 시간이므로 severity에서 바로 환산합니다.
    public class PawnColumnWorker_SeedbedDue : PawnColumnWorker_SeedbedText
    {
        // 잉태 1.5일 = 90000틱. 헤디프의 severityPerDay 0.667과 같은 값에서 나옵니다.
        private const float GestationTicks = 90000f;

        protected override string GetTextFor(Pawn pawn)
        {
            Hediff gestation = GestationOn(pawn);
            if (gestation == null)
            {
                return Blank;
            }

            int remaining = Mathf.Max(0, Mathf.RoundToInt((1f - gestation.Severity) * GestationTicks));
            return remaining.ToStringTicksToPeriod(false, false, true, true, false);
        }

        public override int Compare(Pawn a, Pawn b)
        {
            // 잉태 중이 아닌 행은 항상 아래로 보냅니다.
            float av = GestationOn(a)?.Severity ?? -1f;
            float bv = GestationOn(b)?.Severity ?? -1f;
            return bv.CompareTo(av);
        }
    }

    // 누적 산출량.
    public class PawnColumnWorker_SeedbedYield : PawnColumnWorker_SeedbedText
    {
        protected override string GetTextFor(Pawn pawn)
        {
            HediffComp_TumorSeedbed comp = MUGB_SeedbedUtility.SeedbedCompOn(pawn);
            return comp == null ? Blank : comp.TotalTumors.ToString();
        }

        public override int Compare(Pawn a, Pawn b)
        {
            int av = MUGB_SeedbedUtility.SeedbedCompOn(a)?.TotalTumors ?? -1;
            int bv = MUGB_SeedbedUtility.SeedbedCompOn(b)?.TotalTumors ?? -1;
            return av.CompareTo(bv);
        }
    }
}
