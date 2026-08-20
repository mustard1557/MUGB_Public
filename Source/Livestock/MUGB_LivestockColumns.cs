using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Livestock
{
    // 한국어 의도: 인간가축 탭의 컬럼들입니다. 설계지침 6.6.
    //
    // 바닐라 컬럼 워커를 하나도 상속하거나 패치하지 않습니다. 동물 전용 컬럼(훈련·유대·착유)은
    // pawn.training 등을 읽는데 휴머노이드는 그것이 null이라 NRE가 납니다. 여기서는 전부
    // 직접 그리므로 그 경로가 원천적으로 없습니다.

    // 종족 / 제노타입. 설계지침 8.2의 행 키와 같은 기준으로 표기합니다.
    public class PawnColumnWorker_LivestockXenotype : PawnColumnWorker_Text
    {
        protected override string GetTextFor(Pawn pawn)
        {
            return MUGB_LivestockUtility.KindLabelOf(pawn);
        }
    }

    public class PawnColumnWorker_LivestockHealth : PawnColumnWorker_Text
    {
        protected override string GetTextFor(Pawn pawn)
        {
            if (pawn?.health?.summaryHealth == null)
            {
                return string.Empty;
            }

            return pawn.health.summaryHealth.SummaryHealthPercent.ToStringPercent("F0");
        }

        public override int Compare(Pawn a, Pawn b)
        {
            float av = a?.health?.summaryHealth?.SummaryHealthPercent ?? 0f;
            float bv = b?.health?.summaryHealth?.SummaryHealthPercent ?? 0f;
            return av.CompareTo(bv);
        }
    }

    // 죄수 전용. 저항이 남아 있으면 저항, 노예화 진행 중이면 의지를 보여줍니다.
    public class PawnColumnWorker_LivestockResistance : PawnColumnWorker_Text
    {
        protected override string GetTextFor(Pawn pawn)
        {
            Pawn_GuestTracker guest = pawn?.guest;
            if (guest == null || !pawn.IsPrisonerOfColony)
            {
                return string.Empty;
            }

            return guest.resistance.ToString("F1") + " / " + guest.will.ToString("F1");
        }

        public override int Compare(Pawn a, Pawn b)
        {
            float av = a?.guest?.resistance ?? 0f;
            float bv = b?.guest?.resistance ?? 0f;
            return av.CompareTo(bv);
        }
    }

    // 도축 지정 여부와 그 출처. 자동으로 걸린 것을 손으로 풀면 다시 걸리므로, 출처가
    // 보여야 "규율을 고치거나 보호를 걸어야겠네"로 이어집니다(설계지침 6.6).
    public class PawnColumnWorker_LivestockSource : PawnColumnWorker_Text
    {
        protected override string GetTextFor(Pawn pawn)
        {
            if (MUGB_LivestockUtility.IsDesignated(pawn))
            {
                return MUGB_LivestockAutoRules.Current?.WasAutoMarked(pawn) == true
                    ? "MUGB_LivestockSourceAuto".Translate().ToString()
                    : "MUGB_LivestockSourceManual".Translate().ToString();
            }

            if (MUGB_LivestockUtility.HasMeatLivestockMode(pawn))
            {
                return "MUGB_LivestockSourceTreatment".Translate();
            }

            return string.Empty;
        }
    }

    // 체크박스 컬럼은 바닐라 PawnColumnWorker_Designator를 그대로 상속합니다.
    // 바닐라 가축탭의 '도살' 컬럼(PawnColumnWorker_Slaughter)이 쓰는 바로 그 기반입니다.
    //
    // 직접 그리던 것을 갈아엎은 이유:
    //   - def에 paintable을 켜면 **드래그로 여러 줄을 한 번에 칠할 수 있습니다.** 바닐라
    //     도살 컬럼이 그렇게 동작하고, 직접 그리면 그 기능이 없습니다.
    //   - 헤더 클릭으로 전체 선택/해제도 기반 클래스가 해 줍니다.
    //   - 지정 추가/제거도 기반 클래스가 처리하므로 우리가 손댈 것이 줄어듭니다.
    public class PawnColumnWorker_LivestockDesignate : PawnColumnWorker_Designator
    {
        protected override DesignationDef DesignationType => MUGB_LivestockDefOf.MUGB_SlaughterHumanlike;

        protected override string GetTip(Pawn pawn) => "MUGB_ColumnDesignateTip".Translate();

        // 처우로 들어온 죄수에게는 체크박스를 아예 두지 않습니다. 여기서 끄면 처우는 그대로라
        // 다시 지정되어 "껐는데 다시 켜진다"가 됩니다. 그쪽은 처우 컬럼에서 바꿔야 하고,
        // '출처' 컬럼이 처우로 걸렸음을 알려 줍니다.
        protected override bool HasCheckbox(Pawn pawn)
        {
            return MUGB_LivestockUtility.CanEverDesignate(pawn)
                && MUGB_LivestockUtility.PreceptAllowsButchering()
                && !MUGB_LivestockUtility.HasMeatLivestockMode(pawn);
        }

        protected override void Notify_DesignationAdded(Pawn pawn)
        {
            base.Notify_DesignationAdded(pawn);
            MUGB_LivestockGizmos.WarnIfNoUsableStation(pawn);
        }
    }

    public class PawnColumnWorker_LivestockProtect : PawnColumnWorker_Designator
    {
        protected override DesignationDef DesignationType => MUGB_LivestockDefOf.MUGB_ProtectFromSlaughter;

        protected override string GetTip(Pawn pawn) => "MUGB_ColumnProtectTip".Translate();

        protected override bool HasCheckbox(Pawn pawn) => MUGB_LivestockUtility.IsValidTarget(pawn);
    }

    // 죄수 처우 드롭다운. Pawn_GuestTracker.interactionMode를 직접 읽고 쓰므로 바닐라 감방
    // UI와 자동으로 동기화됩니다. 따로 상태를 들지 않는 이유가 그것입니다(설계지침 6.8).
    public class PawnColumnWorker_LivestockInteractionMode : PawnColumnWorker
    {
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (pawn?.guest == null || !pawn.IsPrisonerOfColony)
            {
                return;
            }

            Rect button = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, Mathf.Min(rect.height - 4f, 28f));
            PrisonerInteractionModeDef current = pawn.guest.ExclusiveInteractionMode;

            if (Widgets.ButtonText(button, current?.LabelCap ?? "-"))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (PrisonerInteractionModeDef mode in DefDatabase<PrisonerInteractionModeDef>.AllDefsListForReading)
                {
                    if (!MUGB_LivestockUtility.ModeSelectableFor(mode, pawn))
                    {
                        continue;
                    }

                    PrisonerInteractionModeDef local = mode;
                    options.Add(new FloatMenuOption(local.LabelCap, delegate
                    {
                        pawn.guest.SetExclusiveInteraction(local);
                    }));
                }

                if (options.Count > 0)
                {
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            if (current != null && !current.description.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, current.description);
            }
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 120);

        public override int Compare(Pawn a, Pawn b)
        {
            int ao = a?.guest?.ExclusiveInteractionMode?.listOrder ?? -1;
            int bo = b?.guest?.ExclusiveInteractionMode?.listOrder ?? -1;
            return ao.CompareTo(bo);
        }
    }
}
