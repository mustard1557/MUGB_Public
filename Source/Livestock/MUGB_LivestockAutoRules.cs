using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MUGB.Livestock
{
    // 솎아낼 순서. 구현은 정렬 키 하나만 바뀌므로 선택지를 여러 개 둬도 비용이 같습니다.
    public enum MUGB_CullOrder
    {
        LowestSkill,   // 능력 낮은 순 — 원래 의도("쓸만한 놈만 남기기")에 부합
        Oldest,        // 노예 생활 오래된 순 — 가축 회전
        RecentlyAdult  // 최근 성인이 된 순 — 평가할 시간을 안 주므로 기본값은 아님
    }

    // 종족·제노타입 하나에 대한 상한입니다.
    //
    // 바닐라 AutoSlaughterConfig와 같은 구성으로 맞췄습니다. 플레이어가 가축탭 자동도살에서
    // 이미 아는 그림이라, 새로 배울 것이 없어야 합니다.
    //   maxTotal / maxMales / maxFemales / maxMalesYoung / maxFemalesYoung  (-1 = 무제한)
    //   allowSlaughterPregnant                                             (바닐라와 동일)
    // 여기에 우리 것 하나만 더합니다: 정렬 기준(order).
    //
    // 키를 Def 참조가 아니라 문자열로 저장하는 이유(설계지침 8.2): HAR 종족이나 제노타입
    // 모드를 나중에 빼면 Def 참조는 로드 에러가 납니다. 문자열이면 못 찾는 행을 조용히
    // 버리고 넘어갑니다.
    public class MUGB_LivestockRule : IExposable
    {
        public string kindKey;

        public int maxTotal = -1;
        public int maxMales = -1;
        public int maxFemales = -1;
        public int maxMalesYoung = -1;
        public int maxFemalesYoung = -1;

        public bool allowSlaughterPregnant;

        public MUGB_CullOrder order = MUGB_CullOrder.LowestSkill;

        // 바닐라도 UI 입력 버퍼를 config에 들고 있습니다. 같은 방식입니다.
        [System.NonSerialized] public string uiMaxTotal;
        [System.NonSerialized] public string uiMaxMales;
        [System.NonSerialized] public string uiMaxFemales;
        [System.NonSerialized] public string uiMaxMalesYoung;
        [System.NonSerialized] public string uiMaxFemalesYoung;

        public bool AnyLimitSet =>
            maxTotal >= 0 || maxMales >= 0 || maxFemales >= 0 || maxMalesYoung >= 0 || maxFemalesYoung >= 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kindKey, "kindKey");
            Scribe_Values.Look(ref maxTotal, "maxTotal", -1);
            Scribe_Values.Look(ref maxMales, "maxMales", -1);
            Scribe_Values.Look(ref maxFemales, "maxFemales", -1);
            Scribe_Values.Look(ref maxMalesYoung, "maxMalesYoung", -1);
            Scribe_Values.Look(ref maxFemalesYoung, "maxFemalesYoung", -1);
            Scribe_Values.Look(ref allowSlaughterPregnant, "allowSlaughterPregnant", false);
            Scribe_Values.Look(ref order, "order", MUGB_CullOrder.LowestSkill);
        }
    }

    // 한 종족·제노타입의 현재 인원 집계입니다. 창에도 쓰고 평가에도 씁니다.
    public class MUGB_LivestockGroup
    {
        public string key;
        public string label;
        public readonly List<Pawn> all = new List<Pawn>();
        public readonly List<Pawn> adultMales = new List<Pawn>();
        public readonly List<Pawn> adultFemales = new List<Pawn>();
        public readonly List<Pawn> youngMales = new List<Pawn>();
        public readonly List<Pawn> youngFemales = new List<Pawn>();
        public int pregnant;

        public void Add(Pawn pawn)
        {
            all.Add(pawn);

            bool adult = MUGB_LivestockUtility.IsAdult(pawn);
            bool male = pawn.gender == Gender.Male;

            if (adult)
            {
                (male ? adultMales : adultFemales).Add(pawn);
            }
            else
            {
                (male ? youngMales : youngFemales).Add(pawn);
            }

            if (MUGB_LivestockUtility.IsPregnant(pawn))
            {
                pregnant++;
            }
        }
    }

    // 한국어 의도: 자동 솎아내기 규칙 저장소이자 평가기입니다. 설계지침 8장.
    //
    // ── 성능 (유저 요구: 바닐라 수준 이상) ───────────────────────────
    //
    // 주기 폴링을 쓰지 않습니다. 평가는 '노예 수가 변하는 순간'에만 돕니다.
    //   출생 / 노예화 / 방면 / 정착민 승격 / 판매  → Pawn_GuestTracker.SetGuestStatus 패치
    //   사망                                        → Pawn.Kill 패치
    //   플레이어가 규칙 변경                        → 다이얼로그
    //
    // 이 사건들은 하루 몇 번 안 일어나므로 평상시 계산량은 0입니다.
    //
    // GameComponentTick에 안전망이 하나 있지만, 하는 일은 정수 나머지 비교 하나입니다
    // (하루에 한 번만 실제 평가). 바닐라 GameComponent들이 쓰는 것과 같은 방식입니다.
    public class MUGB_LivestockAutoRules : GameComponent
    {
        private const int DailyTicks = 60000;

        public bool enabled;

        private List<MUGB_LivestockRule> rules = new List<MUGB_LivestockRule>();

        // 자동 규칙이 찍은 대상만 따로 기억합니다.
        //
        // 자동 솎아내기를 끄면 자동으로 찍힌 지정은 같이 풀려야 합니다. 손으로 찍은 것까지
        // 지우면 안 되므로 출처를 구분해야 하고, 그래서 이 목록이 필요합니다.
        // '출처' 컬럼이 수동/자동/처우를 구분해 보여주는 근거이기도 합니다.
        private HashSet<int> autoMarked = new HashSet<int>();

        public MUGB_LivestockAutoRules(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "MUGB_livestockAutoEnabled", false);
            Scribe_Collections.Look(ref rules, "MUGB_livestockRules", LookMode.Deep);
            Scribe_Collections.Look(ref autoMarked, "MUGB_livestockAutoMarked", LookMode.Value);

            if (rules == null)
            {
                rules = new List<MUGB_LivestockRule>();
            }

            if (autoMarked == null)
            {
                autoMarked = new HashSet<int>();
            }
        }

        public static MUGB_LivestockAutoRules Current => Verse.Current.Game?.GetComponent<MUGB_LivestockAutoRules>();

        public MUGB_LivestockRule RuleFor(string kindKey, bool createIfMissing = false)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i]?.kindKey == kindKey)
                {
                    return rules[i];
                }
            }

            if (!createIfMissing || kindKey.NullOrEmpty())
            {
                return null;
            }

            MUGB_LivestockRule created = new MUGB_LivestockRule { kindKey = kindKey };
            rules.Add(created);
            return created;
        }

        // ── 집계 ─────────────────────────────────────────────────────
        // 창을 열 때와 평가할 때만 돕니다. 틱과 무관합니다.
        public static List<MUGB_LivestockGroup> GroupsOn(Map map)
        {
            List<MUGB_LivestockGroup> groups = new List<MUGB_LivestockGroup>();
            if (map == null)
            {
                return groups;
            }

            Dictionary<string, MUGB_LivestockGroup> byKey = new Dictionary<string, MUGB_LivestockGroup>();
            List<Pawn> slaves = map.mapPawns.SlavesOfColonySpawned;
            for (int i = 0; i < slaves.Count; i++)
            {
                Pawn slave = slaves[i];
                if (!MUGB_LivestockUtility.CanEverDesignate(slave))
                {
                    continue;
                }

                string key = MUGB_LivestockUtility.KindKeyOf(slave);
                if (!byKey.TryGetValue(key, out MUGB_LivestockGroup group))
                {
                    group = new MUGB_LivestockGroup
                    {
                        key = key,
                        label = MUGB_LivestockUtility.KindLabelOf(slave)
                    };
                    byKey[key] = group;
                    groups.Add(group);
                }

                group.Add(slave);
            }

            groups.Sort((a, b) => b.all.Count.CompareTo(a.all.Count));
            return groups;
        }

        // ── 평가 트리거 ──────────────────────────────────────────────
        public void Notify_LivestockChanged()
        {
            if (!enabled)
            {
                return;
            }

            Map map = Find.CurrentMap;
            if (map != null)
            {
                Evaluate(map);
            }
        }

        public bool WasAutoMarked(Pawn pawn) => pawn != null && autoMarked.Contains(pawn.thingIDNumber);

        public void Forget(Pawn pawn)
        {
            if (pawn != null)
            {
                autoMarked.Remove(pawn.thingIDNumber);
            }
        }

        // 자동 솎아내기를 끌 때 자동으로 찍힌 지정만 되돌립니다. 손으로 찍은 것과 '고기용
        // 가축' 처우로 들어온 것은 건드리지 않습니다.
        public void ClearAutoDesignations()
        {
            if (autoMarked.Count == 0)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Pawn> pawns = maps[m].mapPawns.AllPawnsSpawned.ToList();
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (autoMarked.Contains(pawn.thingIDNumber))
                    {
                        MUGB_LivestockUtility.SetDesignated(pawn, false);
                    }
                }
            }

            autoMarked.Clear();
        }

        // 안전망. 이벤트를 놓쳤을 때만 의미가 있습니다. 매 틱 하는 일은 나머지 비교 한 번뿐.
        public override void GameComponentTick()
        {
            if (!enabled || Find.TickManager.TicksGame % DailyTicks != 0)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Evaluate(maps[i]);
            }
        }

        // ── 평가 본체 ────────────────────────────────────────────────
        //
        // 바닐라 자동도살과 같은 방식입니다. 다섯 상한을 각각 따로 보고, 해당 분류에서만
        // 초과분을 솎습니다. 예를 들어 '어린 수컷 5명까지'는 어린 수컷만 건드립니다.
        public void Evaluate(Map map)
        {
            if (map == null || !enabled || !MUGB_LivestockUtility.PreceptAllowsButchering())
            {
                return;
            }

            List<MUGB_LivestockGroup> groups = GroupsOn(map);

            for (int i = 0; i < groups.Count; i++)
            {
                MUGB_LivestockGroup group = groups[i];
                MUGB_LivestockRule rule = RuleFor(group.key);
                if (rule == null || !rule.AnyLimitSet)
                {
                    continue;
                }

                // 분류별 상한 먼저. 좁은 조건이 먼저 걸려야 의도대로 움직입니다.
                MarkExcess(group.youngMales, rule.maxMalesYoung, rule);
                MarkExcess(group.youngFemales, rule.maxFemalesYoung, rule);
                MarkExcess(group.adultMales, rule.maxMales, rule);
                MarkExcess(group.adultFemales, rule.maxFemales, rule);

                // 총원 상한은 마지막에, 전체를 대상으로.
                MarkExcess(group.all, rule.maxTotal, rule);
            }
        }

        // 한 분류에서 상한 초과분을 지정합니다.
        //
        // 바닐라 자동도살처럼 초과분을 한 번에 전부 찍습니다. 예전에는 '한 번에 N명'이라는
        // 제한을 뒀는데, 바닐라에 없는 개념이라 무슨 뜻인지 알기 어려웠습니다. 실수를
        // 되돌리는 수단은 자동 솎아내기를 끄는 것(자동 지정이 함께 풀림)과 '지정됨' 뷰로
        // 충분합니다.
        private void MarkExcess(List<Pawn> pool, int cap, MUGB_LivestockRule rule)
        {
            if (cap < 0 || pool.Count <= cap)
            {
                return;
            }

            // 이미 지정된 인원은 초과분에서 뺍니다. 안 그러면 처리되기 전에 계속 더 찍혀서
            // 한꺼번에 몰살합니다.
            int alreadyMarked = pool.Count(MUGB_LivestockUtility.IsMarkedForSlaughter);
            int need = pool.Count - cap - alreadyMarked;
            if (need <= 0)
            {
                return;
            }

            List<Pawn> candidates = pool.Where(p => IsCullable(p, rule)).ToList();
            SortCandidates(candidates, rule.order);

            int take = UnityEngine.Mathf.Min(need, candidates.Count);
            for (int i = 0; i < take; i++)
            {
                MUGB_LivestockUtility.SetDesignated(candidates[i], true);
                autoMarked.Add(candidates[i].thingIDNumber);
            }
        }

        private static bool IsCullable(Pawn pawn, MUGB_LivestockRule rule)
        {
            if (pawn == null || MUGB_LivestockUtility.IsMarkedForSlaughter(pawn))
            {
                return false;
            }

            // 보호 표시는 규칙으로 뒤집을 수 없습니다. 플레이어가 그 개체를 콕 집어 남기라고
            // 한 것이므로, 행 설정보다 우선합니다.
            if (MUGB_LivestockUtility.IsProtected(pawn))
            {
                return false;
            }

            if (!rule.allowSlaughterPregnant && MUGB_LivestockUtility.IsPregnant(pawn))
            {
                return false;
            }

            return true;
        }

        private static void SortCandidates(List<Pawn> candidates, MUGB_CullOrder order)
        {
            switch (order)
            {
                case MUGB_CullOrder.Oldest:
                    candidates.Sort((a, b) => b.ageTracker.AgeBiologicalTicks.CompareTo(a.ageTracker.AgeBiologicalTicks));
                    break;

                case MUGB_CullOrder.RecentlyAdult:
                    candidates.Sort((a, b) => a.ageTracker.AgeBiologicalTicks.CompareTo(b.ageTracker.AgeBiologicalTicks));
                    break;

                default:
                    candidates.Sort((a, b) => MUGB_LivestockUtility.SkillScoreOf(a).CompareTo(MUGB_LivestockUtility.SkillScoreOf(b)));
                    break;
            }
        }
    }
}
