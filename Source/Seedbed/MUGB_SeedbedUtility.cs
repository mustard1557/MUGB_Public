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
        //
        // 임신 능력(Fertility)에 따라 산출이 갈립니다. 모판화가 난소를 폭주시키긴 하지만
        // 배출할 난자 자체가 줄어든 몸에서 같은 양이 나오는 것은 어색하고, 무엇보다
        // 나이를 아무 상관 없게 두면 아무 여자폰이나 넣으면 되어 후보를 고르는 판단이
        // 사라집니다. 젊은 폰을 모판으로 쓸 이유를 남겨두는 쪽이 낫습니다.
        //
        //   Fertility  > 0.3   종양 3~4개   (가임기)
        //   Fertility  > 0.1   종양 2~3개   (쇠퇴기)
        //   그 이하            종양 1~2개   (폐경 이후 포함)
        //
        // 하한이 1인 것이 중요합니다. 0이 되면 주기는 돌았는데 아무것도 안 나오므로
        // 유저가 고장으로 읽습니다. "폐경한 모판도 쓸 수는 있는데 효율이 반토막"이라는
        // 읽히는 규칙으로 만듭니다.
        public const int MinTumorsPerBirth = 3;
        public const int MaxTumorsPerBirth = 4;

        private const float FertileThreshold = 0.3f;
        private const float DecliningThreshold = 0.1f;

        // 나이·유전자·모드가 만든 임신 능력을 바닐라 Fertility 스탯 하나로 받습니다.
        //
        // 나이를 직접 읽지 않는 이유: RJW·생리모드·유전자가 전부 이 스탯을 건드리므로,
        // 스탯을 보면 어떤 임신 모드를 쓰든 그쪽 수치를 자동으로 따라갑니다. 아무 모드가
        // 없어도 바닐라 Biotech의 나이 곡선이 그대로 반영됩니다. 나이를 직접 계산하면
        // 그 모드들과 따로 놀게 되고, 생리모드의 폐경 헤디프를 이름으로 잡으러 가야 해서
        // 지금까지 피해온 결합이 도로 생깁니다.
        //
        // 틱 비용은 없습니다. 출산 처리에서 1.5일에 한 번 읽을 뿐이고,
        // GetStatValue는 캐시된 조회입니다.
        public static void TumorCountRangeFor(Pawn pawn, out int min, out int max)
        {
            float fertility = pawn?.GetStatValue(StatDefOf.Fertility) ?? 1f;

            if (fertility > FertileThreshold)
            {
                min = MinTumorsPerBirth;
                max = MaxTumorsPerBirth;
            }
            else if (fertility > DecliningThreshold)
            {
                min = 2;
                max = 3;
            }
            else
            {
                min = 1;
                max = 2;
            }
        }

        // 모판화 최종 단계. 이 단계에서 무드가 반전됩니다.
        public const float FinalDegenerationSeverity = 4f;

        // ── 모판 수명 ────────────────────────────────────────────────
        //
        // 수명이 없으면 수술 한 번으로 고기가 영원히 나옵니다. 유지비가 의사 노동뿐이라
        // 라인이 한 번 서면 노예를 더 잡아올 이유가 사라지고, 고기 공장이 콜로니 운영과
        // 분리됩니다. 수명이 있으면 여성 죄수·노예를 꾸준히 확보해야 하는 소모성
        // 파이프라인이 되고, 모판화 설명("여기 끝까지 간 뒤에 돌아온 사람은 아직 없다")도
        // 사실이 됩니다.
        //
        // 값은 종양 개수가 아니라 출산 횟수입니다. 한 번 출산에 종양이 3~4개 나오므로
        // 기본값 10회면 총 30~40개, 고기로 1800~2400입니다.
        // 종양 개수로 세면 임신 능력이 낮은 폰이 훨씬 오래 사는 이상한 결과가 됩니다.
        //
        // 0은 수명 없음입니다. 설정 버튼이 이 목록을 순환합니다.
        public static readonly int[] BirthLimitChoices = { 0, 30, 20, 10, 5, 3, 1 };

        public static int NormalizeBirthLimit(int value)
        {
            for (int i = 0; i < BirthLimitChoices.Length; i++)
            {
                if (BirthLimitChoices[i] == value)
                {
                    return value;
                }
            }

            // 예전 세이브나 손으로 고친 설정값이 목록에 없으면 기본값으로 되돌립니다.
            return 10;
        }

        public static int NextBirthLimit(int current)
        {
            for (int i = 0; i < BirthLimitChoices.Length; i++)
            {
                if (BirthLimitChoices[i] == current)
                {
                    return BirthLimitChoices[(i + 1) % BirthLimitChoices.Length];
                }
            }

            return BirthLimitChoices[0];
        }

        // 0이면 수명 없음입니다.
        public static int BirthLimit => MUGBMod.Settings?.seedbedBirthLimit ?? 10;

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

                    if (looksLikePregnancy && !IsPregnancyNameFalsePositive(def.defName))
                    {
                        foreignPregnancyDefs.Add(def);
                    }
                }

                return foreignPregnancyDefs;
            }
        }

        // 이름에 Pregnan이 들어가지만 임신 자체가 아닌 헤디프들입니다.
        //
        // 특히 바닐라 PregnancyLabor / PregnancyLaborPushing이 문제입니다. 이것들은
        // 임신이 아니라 진통 단계인데, 걸러내지 않으면 진통 중인 폰이 모판이 되는 순간
        // 진통만 뜯겨나가고 바닐라 출산 흐름이 중간에 끊깁니다.
        //
        // 이름 기반 판별의 한계는 인정하고 씁니다. 대안은 남의 모드 타입을 리플렉션으로
        // 잡는 것인데 그쪽이 훨씬 잘 깨집니다. 오탐이 하나 더 발견되면 이 목록에 추가하면 됩니다.
        private static bool IsPregnancyNameFalsePositive(string defName)
        {
            return defName.IndexOf("Labor", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Ended", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Terminated", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Attitude", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Mood", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Unwanted", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── 성행위 신호 ──────────────────────────────────────────────
        //
        // 종양 잉태의 트리거는 "임신이 성사됐는가"가 아니라 "성행위가 있었는가"입니다.
        //
        // 처음에는 임신 헤디프가 붙는 것을 기다렸는데, 그건 설계와 정반대였습니다.
        // 설계지침 4장은 "착상 확률 100%, 배란 주기·수정 확률 롤을 전부 건너뛴다"인데,
        // 임신 헤디프를 기다리면 그 롤을 전부 통과해야만 종양이 생깁니다. 모판은
        // 나이가 많거나 배란기가 아닌 경우가 흔해서, 질내사정을 해도 아무 일도
        // 일어나지 않았습니다. 폭주한 난소라는 설정 자체가 무의미해집니다.
        //
        // 남의 어셈블리를 참조하지 않는다는 원칙은 유지합니다. 세 경로 모두
        // 바닐라 자료구조만 읽어서 판별합니다.
        //
        //   생리모드 : RecordDef "AmountofCreampied" - 자궁에 정액이 들어간 양.
        //              질내사정에만 오르므로 가장 정확한 신호입니다.
        //              (HediffComp_Menstruation.CumIn -> records.AddTo)
        //   RJW      : RecordDef "CountOfSex" - 생리모드가 없을 때의 폴백입니다.
        //              (AfterSexUtility -> records.Increment)
        //   바닐라   : ThoughtDef "GotSomeLovin" 기억의 나이. 사랑을 나눔이 끝나면
        //              붙으므로, 방금 붙은 것이면 직전에 성행위가 있었다는 뜻입니다.
        //
        // 바닐라 쪽을 JobDriver_Lovin 패치로 하지 않은 이유: 그 클래스가 완료 시점에
        // 실행하는 것은 MakeNewToils 안의 람다(b__12_1 같은 것)뿐이라, 패치하면
        // 게임 빌드가 바뀔 때마다 이름이 흔들립니다. 기억을 읽는 쪽이 훨씬 안정적입니다.
        private static bool sexSignalDefsResolved;
        private static RecordDef creampiedRecord;
        private static RecordDef sexCountRecord;
        private static ThoughtDef lovinThought;

        private static void ResolveSexSignalDefs()
        {
            if (sexSignalDefsResolved)
            {
                return;
            }

            sexSignalDefsResolved = true;
            creampiedRecord = DefDatabase<RecordDef>.GetNamedSilentFail("AmountofCreampied");
            sexCountRecord = DefDatabase<RecordDef>.GetNamedSilentFail("CountOfSex");
            lovinThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("GotSomeLovin");
        }

        // 생리모드가 켜져 있는지를 이 기록의 존재로 판별합니다.
        public static bool HasCreampieRecord
        {
            get
            {
                ResolveSexSignalDefs();
                return creampiedRecord != null;
            }
        }

        public static float CreampiedRecordOf(Pawn pawn)
        {
            ResolveSexSignalDefs();
            return creampiedRecord == null || pawn?.records == null
                ? 0f
                : pawn.records.GetValue(creampiedRecord);
        }

        public static float SexCountRecordOf(Pawn pawn)
        {
            ResolveSexSignalDefs();
            return sexCountRecord == null || pawn?.records == null
                ? 0f
                : pawn.records.GetValue(sexCountRecord);
        }

        // 최근 maxAgeTicks 안에 사랑을 나눔이 끝났는지.
        public static bool HadRecentVanillaLovin(Pawn pawn, int maxAgeTicks)
        {
            ResolveSexSignalDefs();
            if (lovinThought == null)
            {
                return false;
            }

            List<Thought_Memory> memories = pawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return false;
            }

            for (int i = 0; i < memories.Count; i++)
            {
                Thought_Memory memory = memories[i];
                if (memory != null && memory.def == lovinThought && memory.age <= maxAgeTicks)
                {
                    return true;
                }
            }

            return false;
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

        // 종양 잉태가 끝났을 때 호출됩니다. 산출물과 메시지만 여기서 처리하고, 폰의
        // 건강 상태를 바꾸는 후처리는 잉태 헤디프가 제거된 뒤 FinishTumorDelivery에서
        // 처리합니다. 바닐라 출산 경로, RJW 출산 경로, 생리모드 경로는 타지 않습니다.
        public static bool DeliverTumors(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            TumorCountRangeFor(pawn, out int minTumors, out int maxTumors);
            int count = Rand.RangeInclusive(minTumors, maxTumors);
            SpawnTumors(pawn, count);
            SeedbedCompOn(pawn)?.RecordDelivery(count);

            Messages.Message(
                "MUGB_MessageTumorsDelivered".Translate(pawn.LabelShortCap, count),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);

            return true;
        }

        // 잉태 헤디프가 완전히 제거된 뒤 한 번만 실행됩니다. 밀어내기 단계와 새 모판화
        // 단계가 동시에 의식을 낮추는 순간을 없애며, 이벤트 콜백이라 주기 검사가 없습니다.
        public static void FinishTumorDelivery(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.health == null)
            {
                return;
            }

            AdvanceDegeneration(pawn);
            RefreshLactation(pawn);
            ApplyBirthMood(pawn);

            // 기존 MUGB_GoblinBirthStrain은 일부러 부여하지 않습니다.
            // 그쪽 마지막 단계는 Consciousness setMax 0이므로 종양 출산과 섞지 않습니다.

            HediffComp_TumorSeedbed seedbed = SeedbedCompOn(pawn);

            // 수명이 다했으면 여기서 끝냅니다.
            //
            // 종양을 이미 낳은 뒤이므로 마지막 산출이 버려지지 않고, 시체도 갓 죽은
            // 상태라 바로 도축할 수 있습니다. 잉태 헤디프가 제거된 뒤에 불리는 함수라
            // 헤디프 목록을 순회하는 도중에 폰을 죽이는 일도 없습니다.
            //
            // 설정을 낮춰서 이미 한계를 넘긴 모판이 생겼을 수 있으므로 >= 로 비교합니다.
            if (seedbed != null && BirthLimit > 0 && seedbed.BirthCount >= BirthLimit)
            {
                Messages.Message(
                    "MUGB_MessageSeedbedExpired".Translate(pawn.LabelShortCap),
                    pawn,
                    MessageTypeDefOf.NegativeEvent,
                    false);

                pawn.Kill(null);
                return;
            }

            // 자동 종부. 잉태 헤디프가 이미 제거됐으므로 일반 자격 판정을 그대로 씁니다.
            if (seedbed != null && seedbed.AutoActivate)
            {
                TryQueueActivatorBill(pawn);
            }
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
        private static void ApplyBirthMood(Pawn pawn)
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

            // 출산 1회당 한 번만 붙입니다. 종양 개수만큼 쌓던 것을 되돌린 이유는
            // ThoughtDef 쪽 주석에 적어두었습니다 - 잉태 사상이 이미 상시 보상을
            // 주고 있어서 여기까지 개수로 부풀리면 4단계 모판이 무적이 됩니다.
            memories.TryGainMemory(MUGB_SeedbedDefOf.MUGB_SeedbedEuphoria);
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
        public static void TryQueueActivatorBill(Pawn pawn, bool ignoreCurrentGestation = false)
        {
            RecipeDef recipe = MUGB_SeedbedDefOf.MUGB_InjectTumorActivator;
            if (recipe == null || pawn?.health?.surgeryBills == null)
            {
                return;
            }

            if (!IsSeedbed(pawn) || (!ignoreCurrentGestation && IsGestating(pawn)))
            {
                return;
            }

            TryAddSurgeryBill(pawn, recipe, ignoreCurrentGestation);
        }

        // 수술 계획서는 반드시 대상 부위를 함께 지정해야 합니다.
        //
        // new Bill_Medical(recipe, null)만으로 넣으면 Part가 null인 채로 들어가고,
        // 부위를 대상으로 하는 레시피(appliedOnFixedBodyParts가 있으면 기본값이 그렇습니다)에서는
        // 그 계획서가 유효하지 않아 곧바로 정리되어 사라집니다. UI에서는 "체크했는데 즉시
        // 풀린다"로 보입니다. 재료 부족이나 구속 상태와는 무관합니다 - 재료가 없는 계획서는
        // 경고만 달고 그대로 남습니다.
        //
        // 부위는 레시피 워커에게 물어봅니다. 그러면 자격 판정(이미 모판인지, 잉태 중인지)이
        // 계획서 생성과 같은 코드 한 곳에서만 정의되고, 자격이 없으면 부위가 비어 돌아오므로
        // 애초에 계획서가 만들어지지 않습니다.
        private static bool TryAddSurgeryBill(Pawn pawn, RecipeDef recipe, bool forceTorso = false)
        {
            if (pawn?.health?.surgeryBills == null || recipe?.Worker == null)
            {
                return false;
            }

            if (HasBillOf(pawn, recipe))
            {
                return true;
            }

            // 출산 직후 예약처럼 워커가 아직 자격 없다고 답하는 순간이 있습니다
            // (잉태 헤디프 제거가 이 틱 뒤로 밀려 있어서). 그때만 몸통을 직접 찾습니다.
            BodyPartRecord part = forceTorso
                ? TorsoOf(pawn)
                : recipe.Worker.GetPartsToApplyOn(pawn, recipe).FirstOrDefault();
            if (part == null)
            {
                return false;
            }

            Bill_Medical bill = new Bill_Medical(recipe, null);
            bill.Part = part;
            pawn.health.surgeryBills.AddBill(bill);
            return true;
        }

        public static bool HasActivatorBill(Pawn pawn)
        {
            return HasBillOf(pawn, MUGB_SeedbedDefOf.MUGB_InjectTumorActivator);
        }

        // 자동을 껐을 때 이미 걸려 있던 주입 계획서까지 걷어냅니다.
        //
        // 이것이 없으면 껐는데도 시술자가 걸어와서 주입을 하고 갑니다. 유저 입장에서는
        // 스위치를 껐는데 아무 일도 안 일어난 것처럼 보이므로 고장으로 읽힙니다.
        // "다음 주기부터 안 건다"와 "지금 예약된 것도 취소한다"는 다른 얘기인데,
        // 체크박스 하나에 두 뜻이 다 담겨 있다고 유저가 읽는 쪽이 자연스럽습니다.
        public static void CancelActivatorBill(Pawn pawn)
        {
            RecipeDef recipe = MUGB_SeedbedDefOf.MUGB_InjectTumorActivator;
            BillStack stack = pawn?.health?.surgeryBills;
            if (recipe == null || stack == null)
            {
                return;
            }

            // 지우면서 순회하므로 뒤에서부터 돕니다.
            for (int i = stack.Bills.Count - 1; i >= 0; i--)
            {
                if (stack.Bills[i]?.recipe == recipe)
                {
                    stack.Delete(stack.Bills[i]);
                }
            }
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
                TryAddSurgeryBill(pawn, recipe);
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

        private static BodyPartRecord TorsoOf(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Torso");
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
