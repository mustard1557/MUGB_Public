using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 종양 모판 시스템의 공용 판정과 출산 처리입니다. 설계지침 4·5·8장.
    public static class MUGB_SeedbedUtility
    {
        // 출산당 종양 개수. 설계지침 5장.
        public const int MinTumorsPerBirth = 3;
        public const int MaxTumorsPerBirth = 4;

        // 모판화 최종 단계. 이 단계에서 무드가 반전됩니다.
        public const float FinalDegenerationSeverity = 4f;

        // ── 외부 임신 시스템 판별 ────────────────────────────────────
        //
        // 바닐라 / RJW / RJW 생리모드를 각각 패치하지 않고, "임신 헤디프로 보이는 것"을
        // def 이름과 클래스로 한 번만 걸러서 캐시합니다.
        //
        // 이렇게 한 이유:
        //   세 시스템을 각각 패치하면 세 배로 깨집니다. 특히 생리모드는 배란 주기 밖에서
        //   착상 자체를 막기 때문에 100% 착상과 정면충돌하고, RJW는 버전마다 클래스 경로가
        //   바뀝니다. 리플렉션으로 남의 타입을 잡으러 가면 그쪽이 업데이트될 때마다 터집니다.
        //
        //   반면 "임신 헤디프가 붙었는지"만 보면 세 시스템은 물론 아직 존재하지도 않는
        //   임신 모드까지 같은 코드로 처리됩니다. 우리 쪽에서 남의 어셈블리를 참조하는
        //   지점이 하나도 없으므로 림잡이 없어도, 나중에 켜도 그대로 돌아갑니다.
        private static HashSet<HediffDef> foreignPregnancyDefs;

        public static HashSet<HediffDef> ForeignPregnancyDefs
        {
            get
            {
                if (foreignPregnancyDefs != null)
                {
                    return foreignPregnancyDefs;
                }

                foreignPregnancyDefs = new HashSet<HediffDef>();
                foreach (HediffDef def in DefDatabase<HediffDef>.AllDefsListForReading)
                {
                    if (def == null || def == MUGB_SeedbedDefOf.MUGB_TumorGestation)
                    {
                        continue;
                    }

                    bool looksLikePregnancy =
                        (def.hediffClass != null && typeof(Hediff_Pregnant).IsAssignableFrom(def.hediffClass))
                        || def.defName.IndexOf("Pregnan", System.StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksLikePregnancy)
                    {
                        foreignPregnancyDefs.Add(def);
                    }
                }

                return foreignPregnancyDefs;
            }
        }

        // RJW의 무너짐(FeelingBroken)입니다. 없으면 null이고 그래도 아무 문제 없습니다.
        // 어셈블리를 참조하지 않고 def 이름만 한 번 찾아둡니다.
        private static bool feelingBrokenResolved;
        private static HediffDef feelingBrokenDef;

        public static HediffDef FeelingBrokenDef
        {
            get
            {
                if (!feelingBrokenResolved)
                {
                    feelingBrokenResolved = true;
                    feelingBrokenDef = DefDatabase<HediffDef>.GetNamedSilentFail("FeelingBroken");
                }

                return feelingBrokenDef;
            }
        }

        // ── 상태 판정 ────────────────────────────────────────────────

        public static bool IsSeedbed(Pawn pawn)
        {
            return pawn?.health?.hediffSet != null
                && MUGB_SeedbedDefOf.MUGB_TumorSeedbed != null
                && pawn.health.hediffSet.HasHediff(MUGB_SeedbedDefOf.MUGB_TumorSeedbed);
        }

        public static bool IsGestating(Pawn pawn)
        {
            return pawn?.health?.hediffSet != null
                && MUGB_SeedbedDefOf.MUGB_TumorGestation != null
                && pawn.health.hediffSet.HasHediff(MUGB_SeedbedDefOf.MUGB_TumorGestation);
        }

        public static float DegenerationSeverity(Pawn pawn)
        {
            Hediff hediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(MUGB_SeedbedDefOf.MUGB_SeedbedDegeneration);
            return hediff?.Severity ?? 0f;
        }

        // 무드 반전 조건. 둘 중 하나만 맞으면 됩니다.
        //   1) 모판화 4단계 - 본편만으로 도달합니다.
        //   2) RJW 무너짐 최종 단계 - 4단계 헤디프라 최종은 CurStageIndex >= 3입니다.
        //      그 단계에서 RJW가 마조히스트를 붙이고 부정적 기억을 지우지만, 우리 사상은
        //      MUGB 것이라 RJW가 건드리지 않습니다. 그래서 여기서 직접 봐야 합니다.
        public static bool MoodIsReversed(Pawn pawn)
        {
            if (DegenerationSeverity(pawn) >= FinalDegenerationSeverity)
            {
                return true;
            }

            HediffDef brokenDef = FeelingBrokenDef;
            if (brokenDef == null || pawn?.health?.hediffSet == null)
            {
                return false;
            }

            Hediff broken = pawn.health.hediffSet.GetFirstHediffOfDef(brokenDef);
            return broken != null && broken.CurStageIndex >= 3;
        }

        // ── 출산 ─────────────────────────────────────────────────────

        // 종양 잉태가 끝났을 때 호출됩니다. 이 안에서 전부 끝나므로 바닐라 출산 경로,
        // RJW 출산 경로, 생리모드 경로를 하나도 타지 않습니다.
        public static void DeliverTumors(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return;
            }

            int count = Rand.RangeInclusive(MinTumorsPerBirth, MaxTumorsPerBirth);
            SpawnTumors(pawn, count);
            SeedbedCompOn(pawn)?.RecordDelivery(count);

            AdvanceDegeneration(pawn);
            RefreshLactation(pawn);
            ApplyBirthMood(pawn, count);

            // 기존 MUGB_GoblinBirthStrain은 일부러 부여하지 않습니다.
            // 그쪽 마지막 단계가 Consciousness setMax 0이라 모판화 4단계(-0.35)와 겹치면
            // 즉사급이 됩니다. 종양 출산은 별도 경로이므로 명시적으로 제외합니다.

            Messages.Message(
                "MUGB_MessageTumorsDelivered".Translate(pawn.LabelShortCap, count),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        private static void SpawnTumors(Pawn pawn, int count)
        {
            if (MUGB_SeedbedDefOf.MUGB_FleshTumor == null)
            {
                return;
            }

            // stackLimit이 1이라 개수만큼 개별 Thing으로 놓습니다.
            for (int i = 0; i < count; i++)
            {
                Thing tumor = ThingMaker.MakeThing(MUGB_SeedbedDefOf.MUGB_FleshTumor);
                GenPlace.TryPlaceThing(tumor, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }

            // 기존 MUGB 코드와 같은 방식으로 이름 조회를 씁니다. ThingDefOf에 이 필드가 없어서
            // 직접 참조하면 다른 DLC 구성에서 컴파일/로드가 갈릴 수 있습니다.
            ThingDef amnioticFluid = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_AmnioticFluid");
            if (amnioticFluid != null)
            {
                FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, amnioticFluid, pawn.LabelIndefinite());
            }
        }

        private static void AdvanceDegeneration(Pawn pawn)
        {
            HediffDef def = MUGB_SeedbedDefOf.MUGB_SeedbedDegeneration;
            if (def == null || pawn?.health == null)
            {
                return;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing == null)
            {
                pawn.health.AddHediff(def);
                return;
            }

            existing.Severity = Mathf.Min(existing.Severity + 1f, def.maxSeverity);
        }

        // 수유 유지. 바닐라 Lactating은 severityPerDay -0.1이라 10일에 걸쳐 사라지는데,
        // 출산 주기가 1.5일이므로 출산마다 1로 되돌리면 영구 유지됩니다.
        //
        // 수유가 임신 확률을 깎는 문제는 없습니다. MUGB가 이미 고블린에 대해 그 패널티를
        // 상쇄하고 있고(Patches/GoblinBirthPatches.cs), 무엇보다 우리는 임신 헤디프를
        // 직접 부여하므로 확률 계산 자체를 타지 않습니다.
        private static void RefreshLactation(Pawn pawn)
        {
            if (pawn?.health == null || HediffDefOf.Lactating == null)
            {
                return;
            }

            Hediff lactating = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating);
            if (lactating == null)
            {
                pawn.health.AddHediff(HediffDefOf.Lactating);
                return;
            }

            lactating.Severity = 1f;
        }

        // 무드 반전의 핵심입니다. 4단계(또는 무너짐 최종)에서는 출산 슬픔을 아예 주지 않고
        // 고양감만 붙습니다.
        //
        // ThoughtDef.nullifyingHediffs로는 이 분기를 만들 수 없습니다. 그쪽은 헤디프의
        // 유무만 보고 단계는 못 봅니다. 그래서 부여 시점에 코드가 판단합니다.
        private static void ApplyBirthMood(Pawn pawn, int tumorCount)
        {
            MemoryThoughtHandler memories = pawn?.needs?.mood?.thoughts?.memories;
            if (memories == null)
            {
                return;
            }

            if (!MoodIsReversed(pawn))
            {
                memories.TryGainMemory(MUGB_SeedbedDefOf.MUGB_GaveBirthToTumor);
                return;
            }

            // 종양 개수만큼 스택. ThoughtDef의 stackLimit이 4라 4개까지 그대로 들어갑니다.
            for (int i = 0; i < tumorCount; i++)
            {
                memories.TryGainMemory(MUGB_SeedbedDefOf.MUGB_SeedbedEuphoria);
            }
        }

        // 주입 시술을 받은 폰의 무드. 반전 조건이 맞으면 디버프 대신 버프가 붙습니다.
        public static void ApplyInjectionMood(Pawn pawn)
        {
            MemoryThoughtHandler memories = pawn?.needs?.mood?.thoughts?.memories;
            if (memories == null)
            {
                return;
            }

            memories.TryGainMemory(MoodIsReversed(pawn)
                ? MUGB_SeedbedDefOf.MUGB_ActivatorAccepted
                : MUGB_SeedbedDefOf.MUGB_InjectedWithActivator);
        }

        // ── 잉태 시작 ────────────────────────────────────────────────

        // 시술, 사랑나누기 전환, 그 밖의 모든 경로가 여기로 모입니다.
        public static bool TryStartGestation(Pawn pawn)
        {
            if (!IsSeedbed(pawn) || IsGestating(pawn) || MUGB_SeedbedDefOf.MUGB_TumorGestation == null)
            {
                return false;
            }

            pawn.health.AddHediff(MUGB_SeedbedDefOf.MUGB_TumorGestation);
            return true;
        }

        // ── 자동 종부 ────────────────────────────────────────────────

        // 출산 직후에 다음 시술을 예약합니다. 폴링이 아니라 이벤트 구동이므로
        // 이 함수 밖에서는 아무것도 돌지 않습니다.
        public static void TryQueueActivatorBill(Pawn pawn)
        {
            RecipeDef recipe = MUGB_SeedbedDefOf.MUGB_InjectTumorActivator;
            if (recipe == null || pawn?.health?.surgeryBills == null)
            {
                return;
            }

            if (!IsSeedbed(pawn) || IsGestating(pawn))
            {
                return;
            }

            if (HasActivatorBill(pawn))
            {
                return;
            }

            pawn.health.surgeryBills.AddBill(new Bill_Medical(recipe, null));
        }

        public static bool HasActivatorBill(Pawn pawn)
        {
            return HasBillOf(pawn, MUGB_SeedbedDefOf.MUGB_InjectTumorActivator);
        }

        // ── 모판화 후보와 그 수술 계획서 ──────────────────────────────

        // 아직 모판이 아니지만 시술 대상이 될 수 있는 폰입니다.
        // 콜로니스트는 제외합니다 - 이 탭은 가축 관리용이고, 실수로 콜로니스트에게
        // 되돌릴 수 없는 수술을 거는 것을 UI 차원에서 막습니다.
        // 정말 걸고 싶으면 바닐라 건강 탭에서 직접 걸 수 있습니다.
        public static bool IsSeedbedCandidate(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true
                && pawn.gender == Gender.Female
                && !pawn.Dead
                && pawn.DevelopmentalStage.Adult()
                && !IsSeedbed(pawn)
                && (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony);
        }

        // 모판화 시술은 연구 선행이 있습니다. 연구 전에 계획서만 쌓이면
        // "걸었는데 아무도 안 온다"가 되므로 체크박스 자체를 내지 않습니다.
        public static bool SeedbedSurgeryResearched()
        {
            ResearchProjectDef research = MUGB_SeedbedDefOf.MUGB_InduceTumorSeedbed?.researchPrerequisite;
            return research == null || research.IsFinished;
        }

        public static bool HasSeedbedSurgeryBill(Pawn pawn)
        {
            return HasBillOf(pawn, MUGB_SeedbedDefOf.MUGB_InduceTumorSeedbed);
        }

        public static void SetSeedbedSurgeryBill(Pawn pawn, bool wanted)
        {
            RecipeDef recipe = MUGB_SeedbedDefOf.MUGB_InduceTumorSeedbed;
            BillStack stack = pawn?.health?.surgeryBills;
            if (recipe == null || stack == null)
            {
                return;
            }

            if (wanted)
            {
                if (!HasSeedbedSurgeryBill(pawn))
                {
                    stack.AddBill(new Bill_Medical(recipe, null));
                }

                return;
            }

            for (int i = stack.Bills.Count - 1; i >= 0; i--)
            {
                if (stack.Bills[i]?.recipe == recipe)
                {
                    stack.Delete(stack.Bills[i]);
                }
            }
        }

        private static bool HasBillOf(Pawn pawn, RecipeDef recipe)
        {
            List<Bill> bills = pawn?.health?.surgeryBills?.Bills;
            if (bills == null || recipe == null)
            {
                return false;
            }

            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i]?.recipe == recipe)
                {
                    return true;
                }
            }

            return false;
        }

        public static HediffComp_TumorSeedbed SeedbedCompOn(Pawn pawn)
        {
            return pawn?.health?.hediffSet
                ?.GetFirstHediffOfDef(MUGB_SeedbedDefOf.MUGB_TumorSeedbed)
                ?.TryGetComp<HediffComp_TumorSeedbed>();
        }

        // 맵 위의 모판들. 인간가축 탭이 열려 있을 때만 호출됩니다.
        public static IEnumerable<Pawn> SeedbedsOn(Map map)
        {
            if (map == null)
            {
                return Enumerable.Empty<Pawn>();
            }

            return map.mapPawns.AllPawnsSpawned.Where(p => p?.RaceProps?.Humanlike == true && IsSeedbed(p));
        }

        public static IEnumerable<Pawn> CandidatesOn(Map map)
        {
            if (map == null)
            {
                return Enumerable.Empty<Pawn>();
            }

            return map.mapPawns.AllPawnsSpawned.Where(IsSeedbedCandidate);
        }

        // 표의 기본 순서입니다. 위쪽이 지금 돌아가는 생산 라인, 아래쪽이 후보 풀이 되도록
        // 모판을 먼저 놓고, 모판 안에서는 단계가 높은 순으로 둡니다.
        // 유저가 컬럼 헤더를 누르면 그 정렬이 이 순서를 덮습니다.
        public static IEnumerable<Pawn> SeedbedRows(Map map, bool includeCandidates)
        {
            IEnumerable<Pawn> rows = SeedbedsOn(map)
                .OrderByDescending(DegenerationSeverity)
                .ThenBy(p => p.LabelShortCap.ToString());

            if (!includeCandidates)
            {
                return rows;
            }

            return rows.Concat(CandidatesOn(map)
                .OrderByDescending(HasSeedbedSurgeryBill)
                .ThenBy(p => p.LabelShortCap.ToString()));
        }
    }
}
