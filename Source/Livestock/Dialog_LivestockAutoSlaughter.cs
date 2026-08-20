using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Livestock
{
    // 한국어 의도: 자동 솎아내기 창입니다. 설계지침 8.1.
    //
    // 바닐라 가축탭의 '자동 도살 관리' 창을 그대로 미러링합니다. 열 구성이 같습니다.
    //
    //   종족 | 총 | 수컷 | 암컷 | 어린 수컷 | 어린 암컷 | 임신 도축
    //
    // 각 칸은 '현재 / 상한' 두 줄입니다. 바닐라도 현재 인원을 같이 보여줍니다 — 상한만
    // 있으면 지금 몇 명인지 몰라서 숫자를 못 정합니다.
    //
    // 빈 칸 = 상한 없음. 바닐라의 무한 표시와 같은 의미입니다.
    //
    // 행은 '현재 맵에 실제로 있는 종족·제노타입 조합'만 만듭니다(설계지침 8.2). 제노타입
    // 모드를 여럿 켠 사람은 안 그러면 행이 수십 개가 됩니다.
    public class Dialog_LivestockAutoSlaughter : Window
    {
        private Vector2 scroll;

        private const float RowHeight = 52f;
        private const float LabelWidth = 170f;
        private const float ColWidth = 78f;
        private const float PregnantWidth = 90f;
        private const float OrderWidth = 150f;

        public override Vector2 InitialSize => new Vector2(940f, 620f);

        public Dialog_LivestockAutoSlaughter()
        {
            // 게임을 멈추지 않습니다. 창을 띄워둔 채로 정착지가 계속 돌아가야 합니다.
            forcePause = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;

            // 원하는 위치로 끌어다 둘 수 있게 합니다.
            draggable = true;
            resizeable = true;

            doCloseX = true;
            doCloseButton = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            MUGB_LivestockAutoRules rules = MUGB_LivestockAutoRules.Current;
            if (rules == null)
            {
                return;
            }

            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "MUGB_AutoSlaughterTitle".Translate());
            Text.Font = GameFont.Small;
            y += 38f;

            bool before = rules.enabled;
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, 300f, 28f), "MUGB_AutoSlaughterEnable".Translate(), ref rules.enabled);
            if (before != rules.enabled)
            {
                if (rules.enabled)
                {
                    rules.Notify_LivestockChanged();
                }
                else
                {
                    // 끄면 자동으로 찍힌 지정이 같이 풀립니다. 손으로 찍은 것과 '고기용
                    // 가축' 처우로 들어온 것은 그대로 남습니다.
                    rules.ClearAutoDesignations();
                }
            }

            y += 34f;

            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "MUGB_AutoSlaughterHint".Translate());
            GUI.color = Color.white;
            y += 28f;

            Rect tableRect = new Rect(inRect.x, y, inRect.width, inRect.height - y - 40f);
            DrawTable(tableRect, rules);
        }

        private void DrawTable(Rect rect, MUGB_LivestockAutoRules rules)
        {
            List<MUGB_LivestockGroup> groups = MUGB_LivestockAutoRules.GroupsOn(Find.CurrentMap);

            float headerHeight = 30f;
            DrawHeader(new Rect(rect.x, rect.y, rect.width, headerHeight));

            Rect body = new Rect(rect.x, rect.y + headerHeight, rect.width, rect.height - headerHeight);
            Widgets.DrawMenuSection(body);
            Rect inner = body.ContractedBy(4f);

            if (groups.Count == 0)
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(inner, "MUGB_AutoSlaughterNoKinds".Translate());
                GUI.color = Color.white;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, inner.width - 20f, groups.Count * RowHeight + 4f);
            Widgets.BeginScrollView(inner, ref scroll, viewRect);

            float y = 0f;
            for (int i = 0; i < groups.Count; i++)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(rowRect);
                }

                DrawRow(rowRect, groups[i], rules);
                y += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private static void DrawHeader(Rect rect)
        {
            Text.Anchor = TextAnchor.LowerCenter;
            Text.Font = GameFont.Tiny;

            float x = rect.x + LabelWidth + 4f;
            DrawHeaderCell(ref x, rect, "MUGB_AutoColTotal", ColWidth);
            DrawHeaderCell(ref x, rect, "MUGB_AutoColMales", ColWidth);
            DrawHeaderCell(ref x, rect, "MUGB_AutoColFemales", ColWidth);
            DrawHeaderCell(ref x, rect, "MUGB_AutoColMalesYoung", ColWidth);
            DrawHeaderCell(ref x, rect, "MUGB_AutoColFemalesYoung", ColWidth);
            DrawHeaderCell(ref x, rect, "MUGB_AutoColPregnant", PregnantWidth);
            DrawHeaderCell(ref x, rect, "MUGB_AutoColOrder", OrderWidth);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawHeaderCell(ref float x, Rect rect, string key, float width)
        {
            Widgets.Label(new Rect(x, rect.y, width, rect.height - 2f), key.Translate());
            x += width + 4f;
        }

        private void DrawRow(Rect rect, MUGB_LivestockGroup group, MUGB_LivestockAutoRules rules)
        {
            MUGB_LivestockRule rule = rules.RuleFor(group.key, createIfMissing: true);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, LabelWidth - 8f, rect.height), group.label);
            Text.Anchor = TextAnchor.UpperLeft;

            float x = rect.x + LabelWidth + 4f;

            DrawLimitCell(ref x, rect, group.all.Count, ref rule.maxTotal, ref rule.uiMaxTotal, rules);
            DrawLimitCell(ref x, rect, group.adultMales.Count, ref rule.maxMales, ref rule.uiMaxMales, rules);
            DrawLimitCell(ref x, rect, group.adultFemales.Count, ref rule.maxFemales, ref rule.uiMaxFemales, rules);
            DrawLimitCell(ref x, rect, group.youngMales.Count, ref rule.maxMalesYoung, ref rule.uiMaxMalesYoung, rules);
            DrawLimitCell(ref x, rect, group.youngFemales.Count, ref rule.maxFemalesYoung, ref rule.uiMaxFemalesYoung, rules);

            // 임신 도축 허용. 바닐라도 행 단위 체크박스입니다.
            Rect pregRect = new Rect(x, rect.y, PregnantWidth, rect.height);
            DrawCurrent(new Rect(pregRect.x, pregRect.y + 4f, pregRect.width, 16f), group.pregnant);
            bool allow = rule.allowSlaughterPregnant;
            Widgets.Checkbox(new Vector2(pregRect.x + (PregnantWidth - 24f) / 2f, pregRect.y + 24f), ref allow, 24f);
            if (allow != rule.allowSlaughterPregnant)
            {
                rule.allowSlaughterPregnant = allow;
                rules.Notify_LivestockChanged();
            }
            TooltipHandler.TipRegion(pregRect, "MUGB_AutoColPregnantTip".Translate());
            x += PregnantWidth + 4f;

            // 정렬 기준 — 바닐라에는 없는 우리 것. 누구부터 솎을지가 이 시스템의 핵심이라
            // 종족별로 다르게 둘 수 있어야 합니다.
            Rect orderRect = new Rect(x, rect.y + 11f, OrderWidth, rect.height - 22f);
            if (Widgets.ButtonText(orderRect, OrderLabel(rule.order)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (MUGB_CullOrder value in System.Enum.GetValues(typeof(MUGB_CullOrder)))
                {
                    MUGB_CullOrder local = value;
                    options.Add(new FloatMenuOption(OrderLabel(local), delegate
                    {
                        rule.order = local;
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(orderRect, "MUGB_AutoColOrderTip".Translate());
        }

        // 한 칸에 '현재 인원'과 '상한 입력'을 위아래로 둡니다.
        private void DrawLimitCell(ref float x, Rect rect, int current, ref int limit, ref string buffer, MUGB_LivestockAutoRules rules)
        {
            Rect cell = new Rect(x, rect.y, ColWidth, rect.height);

            DrawCurrent(new Rect(cell.x, cell.y + 4f, cell.width, 16f), current);

            Rect fieldRect = new Rect(cell.x + 10f, cell.y + 22f, cell.width - 20f, 24f);
            if (buffer == null)
            {
                buffer = limit < 0 ? string.Empty : limit.ToString();
            }

            string edited = Widgets.TextField(fieldRect, buffer);
            if (edited != buffer)
            {
                buffer = edited;
                int parsed = int.TryParse(edited, out int value) && value >= 0 ? value : -1;
                if (parsed != limit)
                {
                    limit = parsed;
                    rules.Notify_LivestockChanged();
                }
            }

            if (limit < 0)
            {
                // 빈 칸이 '상한 없음'이라는 걸 알려줍니다.
                TooltipHandler.TipRegion(cell, "MUGB_AutoSlaughterNoCapTip".Translate());
            }

            x += ColWidth + 4f;
        }

        private static void DrawCurrent(Rect rect, int current)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(rect, current.ToString());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static string OrderLabel(MUGB_CullOrder order)
        {
            switch (order)
            {
                case MUGB_CullOrder.Oldest:
                    return "MUGB_CullOrderOldest".Translate();
                case MUGB_CullOrder.RecentlyAdult:
                    return "MUGB_CullOrderRecentlyAdult".Translate();
                default:
                    return "MUGB_CullOrderLowestSkill".Translate();
            }
        }
    }
}
