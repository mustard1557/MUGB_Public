using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Livestock
{
    // 한국어 의도: 인간가축 탭입니다. 설계지침 6장.
    //
    // MainTabWindow_PawnTable을 상속하지 않고 MainTabWindow에서 직접 PawnTable을 관리합니다.
    // 상위 클래스는 표 하나를 캐시하는 구조라 뷰 전환과 맞지 않고, 뷰마다 컬럼이 다르므로
    // 표를 세 개 들고 있는 편이 단순합니다. 바닐라 클래스는 여전히 하나도 건드리지 않습니다.
    public class MainTabWindow_HumanLivestock : MainTabWindow
    {
        private enum View
        {
            Prisoners,
            Slaves,
            Marked
        }

        // 기본 뷰는 죄수입니다. 습격 직후 이 탭을 여는 것이 가장 잦고, 그때 필요한 것은
        // 처우 일괄 지정이기 때문입니다(설계지침 6.4).
        private View view = View.Prisoners;

        private PawnTable prisonerTable;
        private PawnTable slaveTable;
        private PawnTable markedTable;

        private const float TopRowHeight = 32f;
        private const float SummaryHeight = 24f;

        public override Vector2 RequestedTabSize => new Vector2(1010f, 640f);

        protected override float Margin => 6f;

        // 탭을 열어둔 채로 게임이 계속 돌아가야 합니다.
        public override bool IsDebug => false;

        public MainTabWindow_HumanLivestock()
        {
            forcePause = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            SetDirty();
        }

        private void SetDirty()
        {
            prisonerTable?.SetDirty();
            slaveTable?.SetDirty();
            markedTable?.SetDirty();
        }

        private PawnTable CurrentTable
        {
            get
            {
                switch (view)
                {
                    case View.Slaves:
                        return slaveTable ?? (slaveTable = MakeTable(MUGB_LivestockDefOf.MUGB_HumanLivestock_Slaves, SlavePawns));
                    case View.Marked:
                        return markedTable ?? (markedTable = MakeTable(MUGB_LivestockDefOf.MUGB_HumanLivestock_Marked, MarkedPawns));
                    default:
                        return prisonerTable ?? (prisonerTable = MakeTable(MUGB_LivestockDefOf.MUGB_HumanLivestock_Prisoners, PrisonerPawns));
                }
            }
        }

        private PawnTable MakeTable(PawnTableDef def, System.Func<IEnumerable<Pawn>> source)
        {
            return new PawnTable(def, source, (int)(RequestedTabSize.x - 20f), (int)(RequestedTabSize.y - 100f));
        }

        // ── 행 목록 ──────────────────────────────────────────────────
        // 창이 열려 있을 때만 계산됩니다. 틱과 무관합니다(설계지침 6.9).
        private static Map Map => Find.CurrentMap;

        private static IEnumerable<Pawn> PrisonerPawns()
        {
            Map map = Map;
            if (map == null)
            {
                return Enumerable.Empty<Pawn>();
            }

            return map.mapPawns.PrisonersOfColonySpawned.Where(p => p?.RaceProps?.Humanlike == true);
        }

        private static IEnumerable<Pawn> SlavePawns()
        {
            Map map = Map;
            if (map == null)
            {
                return Enumerable.Empty<Pawn>();
            }

            return map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                .Where(p => p?.RaceProps?.Humanlike == true && p.IsSlaveOfColony)
                // 노예 뷰의 정렬은 "다음에 누가 죽는지"가 위에서부터 읽히도록 둡니다.
                // 표 자체가 도축 대기열이 됩니다(설계지침 6.7).
                .OrderByDescending(MUGB_LivestockUtility.IsMarkedForSlaughter)
                .ThenBy(p => p.skills?.AverageOfRelevantSkillsFor(WorkTypeDefOf.Hunting) ?? 0f);
        }

        private static IEnumerable<Pawn> MarkedPawns()
        {
            Map map = Map;
            if (map == null)
            {
                return Enumerable.Empty<Pawn>();
            }

            return map.mapPawns.AllPawnsSpawned
                .Where(p => MUGB_LivestockUtility.IsValidTarget(p) && MUGB_LivestockUtility.IsMarkedForSlaughter(p));
        }

        // ── 그리기 ───────────────────────────────────────────────────
        public override void DoWindowContents(Rect rect)
        {
            // 요약 줄: 바닐라는 자동도축 설정이 팝업에 숨어 있어 현황을 알 수 없습니다.
            // 표 위 한 줄로 노출하는 것이 체감 개선이 가장 큽니다(설계지침 6.6).
            Rect summaryRect = new Rect(rect.x, rect.y, rect.width, SummaryHeight);
            DrawSummary(summaryRect);

            Rect topRow = new Rect(rect.x, rect.y + SummaryHeight + 2f, rect.width, TopRowHeight);
            DrawTopRow(topRow);

            float tableTop = topRow.yMax + 6f;
            CurrentTable.PawnTableOnGUI(new Vector2(rect.x, tableTop));
        }

        private void DrawSummary(Rect rect)
        {
            Text.Font = GameFont.Small;

            // 규율이 없으면 이 기능 전체가 잠깁니다. 그 사실을 여기서 가장 먼저 알립니다.
            // 전에는 탭을 통째로 숨겨서 원인을 알 방법이 없었습니다.
            if (!MUGB_LivestockUtility.PreceptAllowsButchering())
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(rect, "MUGB_TabNoPrecept".Translate());
                GUI.color = Color.white;
                return;
            }

            GUI.color = new Color(0.8f, 0.8f, 0.8f);

            int slaves = SlavePawns().Count();
            int prisoners = PrisonerPawns().Count();
            int marked = MarkedPawns().Count();

            string text = "MUGB_TabSummary".Translate(slaves, prisoners, marked);

            // 지정은 했는데 받아줄 도축대가 없는 상황을 여기서 알립니다. 지정 시점 메시지는
            // 한 번뿐이라, 나중에 계획서를 꺼버린 경우를 이 줄이 잡아줍니다(설계지침 6.9).
            if (marked > 0 && !MarkedPawns().Any(p => MUGB_LivestockUtility.AnyStationAcceptsCorpseOf(p)))
            {
                GUI.color = ColorLibrary.Orange;
                text = text + "   " + "MUGB_TabSummaryNoStation".Translate();
            }

            Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawTopRow(Rect rect)
        {
            float x = rect.x;
            x = DrawViewButton(rect, x, View.Prisoners, "MUGB_TabViewPrisoners");
            x = DrawViewButton(rect, x, View.Slaves, "MUGB_TabViewSlaves");
            DrawViewButton(rect, x, View.Marked, "MUGB_TabViewMarked");

            // [자동 도축]은 노예 뷰에서만 띄웁니다. 죄수는 자동 상한 규칙 대상이 아니고
            // '고기용 가축' 처우로 들어옵니다(설계지침 7.3).
            float right = rect.xMax;
            if (view == View.Slaves)
            {
                Rect autoRect = new Rect(right - 150f, rect.y, 150f, rect.height - 4f);
                if (Widgets.ButtonText(autoRect, "MUGB_TabAutoSlaughter".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_LivestockAutoSlaughter());
                }

                right = autoRect.x - 6f;
            }

            // 죄수 뷰에서만 의미가 있습니다. 습격 직후 여럿에게 한 번에 처우를 먹이는 용도.
            if (view != View.Prisoners || MUGB_LivestockDefOf.MUGB_MeatLivestock == null)
            {
                return;
            }

            Rect bulkRect = new Rect(right - 180f, rect.y, 180f, rect.height - 4f);
            if (Widgets.ButtonText(bulkRect, "MUGB_TabBulkTreatment".Translate()))
            {
                OpenBulkTreatmentMenu();
            }
        }

        private float DrawViewButton(Rect row, float x, View target, string labelKey)
        {
            const float width = 110f;
            Rect buttonRect = new Rect(x, row.y, width, row.height - 4f);

            bool selected = view == target;
            if (selected)
            {
                Widgets.DrawHighlightSelected(buttonRect);
            }

            if (Widgets.ButtonText(buttonRect, labelKey.Translate()) && !selected)
            {
                view = target;
                SetDirty();
            }

            return x + width + 4f;
        }

        private void OpenBulkTreatmentMenu()
        {
            List<Pawn> targets = PrisonerPawns().ToList();
            if (targets.Count == 0)
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (PrisonerInteractionModeDef mode in DefDatabase<PrisonerInteractionModeDef>.AllDefsListForReading)
            {
                PrisonerInteractionModeDef local = mode;
                if (!targets.Any(p => MUGB_LivestockUtility.ModeSelectableFor(local, p)))
                {
                    continue;
                }

                options.Add(new FloatMenuOption(local.LabelCap, delegate
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        Pawn prisoner = targets[i];
                        if (prisoner?.guest != null && MUGB_LivestockUtility.ModeSelectableFor(local, prisoner))
                        {
                            prisoner.guest.SetExclusiveInteraction(local);
                        }
                    }

                    SetDirty();
                }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }

    // 규율이 없으면 탭 자체를 숨깁니다. 이 기능을 안 쓰는 플레이어에게는 탭바가 전혀
    // 늘어나지 않습니다(설계지침 6.2).
    public class MainButtonWorker_HumanLivestock : MainButtonWorker_ToggleTab
    {
        public override bool Visible
        {
            get
            {
                if (!ModsConfig.IdeologyActive)
                {
                    return false;
                }

                // 모드 설정으로 탭만 끌 수 있습니다. 꺼도 폰 기즈모와 죄수 처우는
                // 그대로 동작하므로 기능이 사라지는 것은 아닙니다.
                if (MUGBMod.Settings?.showHumanLivestockTab == false)
                {
                    return false;
                }

                // 규율 유무로 숨기지 않습니다. 규율이 없을 때 탭까지 사라지면 플레이어가
                // "왜 아무것도 안 되는지" 알 방법이 없습니다. 대신 탭 안에서 이유를
                // 알려주고, 가축이 아예 없는 정착지에서만 숨깁니다.
                return MUGB_LivestockUtility.AnyLivestockOnMap(Find.CurrentMap);
            }
        }
    }
}
