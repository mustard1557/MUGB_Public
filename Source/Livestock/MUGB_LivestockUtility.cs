using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Livestock
{
    // 한국어 의도: 인간가축 파이프라인의 공통 판정을 한곳에 모읍니다.
    //
    // 설계지침 9.1에 따라 대상 조건은 종족을 가리지 않습니다.
    //   RaceProps.Humanlike && (IsSlaveOfColony || IsPrisonerOfColony)
    // HAR 종족도 Humanlike이므로 별도 분기 없이 자동으로 포함됩니다. 종족별 분기를 넣는
    // 순간 오히려 깨지는 구조라 일부러 넣지 않았습니다.
    //
    // 성능 원칙: 이 파일에는 Tick 훅이 없습니다. 전부 호출 시점 계산이며, 매 프레임 불릴
    // 수 있는 것(규율 판정)만 틱 스탬프로 캐시합니다. 여기에 주기 검사를 추가하지 마세요.
    public static class MUGB_LivestockUtility
    {
        public static bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.RaceProps?.Humanlike != true)
            {
                return false;
            }

            return pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony;
        }

        // 설계지침 8.2: 고기가 안 나오는 종족(HAR 기계·무기물 계열)은 대상에서 뺍니다.
        public static bool YieldsMeat(Pawn pawn)
        {
            return pawn?.RaceProps?.IsFlesh == true && pawn.RaceProps.corpseDef != null;
        }

        public static bool CanEverDesignate(Pawn pawn)
        {
            return IsValidTarget(pawn) && YieldsMeat(pawn);
        }

        // 고블린과 하프고블린은 인간가축 도축에 아무 감정이 없습니다. 하프고블린은 외형만
        // 비고블린인 고블린이라는 설정이므로 같이 묶습니다(유저 확인).
        public static bool IsGoblinKind(Pawn pawn)
        {
            return pawn != null
                && (GoblinUtility.IsGoblin(pawn) || GoblinUtility.HasHalfGoblinAncestry(pawn));
        }

        // ── 규율 게이트 ──────────────────────────────────────────────
        //
        // 설계지침 3.1: 금지 / 허용 / 권장 3단 중 '허용' 이상이어야 기능이 열립니다.
        //
        // 이 판정은 기즈모와 탭 버튼에서 불리므로 매 프레임 돕니다. 규율 목록 순회가 초당
        // 60번 도는 것을 막으려고 캐시하되, 스탬프는 '게임 틱'이 아니라 '프레임'입니다.
        //
        // 틱 기준으로 하면 일시정지 중에 캐시가 영영 만료되지 않습니다. 이데올로기 편집은
        // 보통 일시정지 상태로 하므로, 규율을 넣고 나와도 탭이 안 나타나는 버그가 됩니다.
        // (실제로 그 버그를 냈습니다. 프레임은 일시정지 중에도 흐릅니다.)
        private const int PreceptCacheIntervalFrames = 30;
        private static int preceptCacheStamp = -99999;
        private static bool preceptCacheValue;

        public static bool PreceptAllowsButchering()
        {
            if (!ModsConfig.IdeologyActive)
            {
                return false;
            }

            int now = UnityEngine.Time.frameCount;
            if (now - preceptCacheStamp < PreceptCacheIntervalFrames && now >= preceptCacheStamp)
            {
                return preceptCacheValue;
            }

            preceptCacheStamp = now;
            preceptCacheValue = IdeoAllowsButchering(Faction.OfPlayer?.ideos?.PrimaryIdeo);
            return preceptCacheValue;
        }

        // 규율이 없어도 탭과 기즈모는 보여주고, 대신 이유를 표시합니다.
        // 전부 숨겨 버리면 플레이어가 "왜 아무것도 안 되지"를 진단할 방법이 없습니다.
        public static bool AnyLivestockOnMap(Map map)
        {
            return map != null && map.mapPawns.SlavesAndPrisonersOfColonySpawnedCount > 0;
        }

        public static bool IdeoAllowsButchering(Ideo ideo)
        {
            List<Precept> precepts = ideo?.PreceptsListForReading;
            if (precepts == null)
            {
                return false;
            }

            for (int i = 0; i < precepts.Count; i++)
            {
                PreceptDef def = precepts[i]?.def;
                if (def == null)
                {
                    continue;
                }

                if (def == MUGB_LivestockDefOf.MUGB_LivestockSlaves_Acceptable
                    || def == MUGB_LivestockDefOf.MUGB_LivestockSlaves_Encouraged)
                {
                    return true;
                }
            }

            return false;
        }

        // '허용함'은 단순 허가가 아니라 "도축을 통해 속죄가 이루어진다"는 의무이므로 긍정
        // 무드를 줍니다. ThoughtDef에는 requiredPrecepts 필드가 없어(nullifyingPrecepts만
        // 존재) 이 확인은 코드에서 합니다.
        public static bool IdeoRequiresButchering(Ideo ideo)
        {
            List<Precept> precepts = ideo?.PreceptsListForReading;
            if (precepts == null)
            {
                return false;
            }

            for (int i = 0; i < precepts.Count; i++)
            {
                if (precepts[i]?.def == MUGB_LivestockDefOf.MUGB_LivestockSlaves_Encouraged)
                {
                    return true;
                }
            }

            return false;
        }

        // ── 지정(Designation) ────────────────────────────────────────
        //
        // DesignationManager는 thingDesignations 딕셔너리를 들고 있어서 DesignationOn은
        // 사실상 상수 시간입니다. 매 프레임 불려도 부담이 없습니다.
        public static bool IsDesignated(Pawn pawn)
        {
            return DesignationOn(pawn) != null;
        }

        // MapHeld를 쓰는 것이 중요합니다. 운반이 시작되면 대상은 디스폰되어 Map이 null이
        // 되지만, 지정 항목은 맵의 목록에 그대로 남아 있습니다. Map으로 조회하면 업는 순간
        // "지정이 사라졌다"고 오판해서 잡이 즉시 취소됩니다.
        public static Designation DesignationOn(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            if (map == null)
            {
                return null;
            }

            return map.designationManager.DesignationOn(pawn, MUGB_LivestockDefOf.MUGB_SlaughterHumanlike);
        }

        public static void SetDesignated(Pawn pawn, bool designated)
        {
            Map map = pawn?.MapHeld;
            if (map == null)
            {
                return;
            }

            Designation existing = map.designationManager.DesignationOn(pawn, MUGB_LivestockDefOf.MUGB_SlaughterHumanlike);
            if (designated)
            {
                if (existing == null && CanEverDesignate(pawn))
                {
                    map.designationManager.AddDesignation(new Designation(pawn, MUGB_LivestockDefOf.MUGB_SlaughterHumanlike));
                }
                return;
            }

            if (existing != null)
            {
                map.designationManager.RemoveDesignation(existing);
            }
        }

        // 설계지침 5.4: 대상이 조건을 잃으면 지정을 정리합니다. 빠뜨리면 유령 지정이 남아
        // 잡이 계속 실패합니다.
        public static void CleanupDesignationIfInvalid(Pawn pawn)
        {
            if (pawn == null || CanEverDesignate(pawn))
            {
                return;
            }

            SetDesignated(pawn, false);
            SetProtected(pawn, false);
        }

        // ── 보호 표시 ────────────────────────────────────────────────
        //
        // 자동 규칙(P4)에서 영구 제외할 개체입니다. 별도 GameComponent 대신 Designation을
        // 쓰므로 저장 스키마가 늘지 않고, 사망·신분 변경 정리 경로도 그대로 재사용됩니다.
        public static bool IsProtected(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            return map != null
                && map.designationManager.DesignationOn(pawn, MUGB_LivestockDefOf.MUGB_ProtectFromSlaughter) != null;
        }

        public static void SetProtected(Pawn pawn, bool value)
        {
            Map map = pawn?.MapHeld;
            if (map == null)
            {
                return;
            }

            Designation existing = map.designationManager.DesignationOn(pawn, MUGB_LivestockDefOf.MUGB_ProtectFromSlaughter);
            if (value)
            {
                if (existing == null && IsValidTarget(pawn))
                {
                    map.designationManager.AddDesignation(new Designation(pawn, MUGB_LivestockDefOf.MUGB_ProtectFromSlaughter));
                }
                return;
            }

            if (existing != null)
            {
                map.designationManager.RemoveDesignation(existing);
            }
        }

        // ── '고기용 가축' 처우 (P2) ──────────────────────────────────
        //
        // 처우는 Designation과 별개의 '입구'입니다. 둘을 동기화하지 않고 판정만 합치는
        // 이유는, 처우 변경이 필드 직접 대입이라 훅을 걸 지점이 없기 때문입니다. 주기
        // 검사로 동기화하면 틱을 먹으므로, 읽을 때 합쳐서 보는 편이 정확하고 공짜입니다.
        public static bool HasMeatLivestockMode(Pawn pawn)
        {
            // interactionMode 필드는 private입니다. 공개 API인 IsInteractionEnabled를
            // 씁니다. 배타/비배타 처우를 모두 올바르게 다뤄줍니다.
            return MUGB_LivestockDefOf.MUGB_MeatLivestock != null
                && pawn?.guest != null
                && pawn.IsPrisonerOfColony
                && pawn.guest.IsInteractionEnabled(MUGB_LivestockDefOf.MUGB_MeatLivestock);
        }

        // 도축 대상인가 — 수동 지정이든 처우든.
        public static bool IsMarkedForSlaughter(Pawn pawn)
        {
            return IsDesignated(pawn) || HasMeatLivestockMode(pawn);
        }

        // 처우 목록에 이 항목을 띄울지. 바닐라가 개별 bool(hideIfNotRecruitable 등)로
        // 처리하는 부분이라 범용 필드가 없어 여기서 봅니다.
        public static bool ModeSelectableFor(PrisonerInteractionModeDef mode, Pawn prisoner)
        {
            if (mode == null || prisoner?.guest == null)
            {
                return false;
            }

            // 비배타 처우(흡혈·헤모겐 농장)는 라디오 방식 드롭다운에 섞으면 안 됩니다.
            // 그쪽은 감방 탭에서 토글로 다루는 항목입니다.
            if (mode.isNonExclusiveInteraction)
            {
                return false;
            }

            // 규율이 없으면 '고기용 가축'을 고를 수 없게 합니다. 골라봐야 아무도 안 오므로
            // 목록에 띄우면 오해만 삽니다.
            if (mode == MUGB_LivestockDefOf.MUGB_MeatLivestock && !PreceptAllowsButchering())
            {
                return false;
            }

            return true;
        }

        // ── 표시용 ───────────────────────────────────────────────────
        //
        // 설계지침 8.2의 행 키와 같은 기준입니다. HAR 종족은 ThingDef가 갈리고, 제노타입은
        // 같은 Human ThingDef라 XenotypeDef로 한 단계 더 쪼갭니다. 유전자를 섞어
        // UniqueXenotype이 켜진 폰은 개별 이름마다 나누면 끝이 없으므로 하나로 묶습니다.
        // 자동 규칙(P4)의 행 키입니다. 표시용 KindLabelOf와 같은 기준이되, 번역과 무관하게
        // 안정적이어야 하므로 defName을 씁니다. 세이브에 문자열로 들어갑니다.
        public static string KindKeyOf(Pawn pawn)
        {
            if (pawn?.def == null)
            {
                return string.Empty;
            }

            string xeno = "Baseliner";
            if (pawn.genes != null)
            {
                if (pawn.genes.UniqueXenotype)
                {
                    xeno = "Hybrid";
                }
                else if (pawn.genes.Xenotype != null)
                {
                    xeno = pawn.genes.Xenotype.defName;
                }
            }

            return pawn.def.defName + "/" + xeno;
        }

        public static bool IsAdult(Pawn pawn)
        {
            return pawn?.DevelopmentalStage.Adult() == true;
        }

        public static bool IsPregnant(Pawn pawn)
        {
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return false;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                // 타입으로 봅니다. 바닐라 PregnantHuman은 물론 그것을 상속한 모드 임신
                // 헤디프도 함께 잡힙니다. defName 하드코딩보다 넓게 걸립니다.
                if (hediffs[i] is Hediff_Pregnant)
                {
                    return true;
                }
            }

            return false;
        }

        // 능력 낮은 순 정렬용 점수. 스킬 합이 낮을수록 먼저 솎입니다.
        public static float SkillScoreOf(Pawn pawn)
        {
            List<SkillRecord> skills = pawn?.skills?.skills;
            if (skills == null)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord skill = skills[i];
                if (skill != null && !skill.TotallyDisabled)
                {
                    total += skill.Level;
                }
            }

            return total;
        }

        public static string KindLabelOf(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            if (pawn.genes != null)
            {
                if (pawn.genes.UniqueXenotype)
                {
                    return "MUGB_KindHybridXenotype".Translate();
                }

                XenotypeDef xenotype = pawn.genes.Xenotype;
                if (xenotype != null && xenotype != XenotypeDefOf.Baseliner)
                {
                    return xenotype.LabelCap;
                }
            }

            return pawn.def?.LabelCap ?? string.Empty;
        }

        // ── 도축 작업대 탐색 ─────────────────────────────────────────
        //
        // 설계지침 v1.2: "도축 레시피가 있는 건물"이 아니라 "이 대상의 시체를 실제로 받아줄
        // 계획서가 걸린 작업대"만 목적지로 삼습니다.
        //
        // 이유: 계획서가 없거나 인간형이 꺼진 도축대로 끌고 가면 시체가 그냥 방치됩니다.
        // 계획서까지 확인하면 도착하는 순간 이미 도축 대기열에 올라가므로 체감이 "바로 도축"이
        // 되고, 유효한 목적지가 없으면 애초에 끌고 가지 않습니다.
        //
        // RaceProps.corpseDef로 검사하므로 종족별로 정확합니다. 랫킨(HAR) 노예를 랫킨 시체가
        // 꺼진 도축대로 끌고 가는 일이 없습니다.
        //
        // 계획서는 플레이어가 수시로 바꾸므로 이 판정은 캐시하지 않습니다. 낡은 캐시는
        // "켰는데 안 데려간다"는 버그처럼 보입니다. 대신 호출 지점을 좁게 유지합니다.
        public static bool StationAcceptsCorpseOf(Thing station, Pawn victim)
        {
            ThingDef corpseDef = victim?.RaceProps?.corpseDef;
            if (corpseDef == null || !(station is IBillGiver billGiver))
            {
                return false;
            }

            BillStack bills = billGiver.BillStack;
            if (bills == null || bills.Count == 0)
            {
                return false;
            }

            List<Bill> billList = bills.Bills;
            for (int i = 0; i < billList.Count; i++)
            {
                Bill bill = billList[i];
                if (bill == null || bill.suspended || bill.deleted || bill.recipe == null)
                {
                    continue;
                }

                // 'Butchery 산출물이 있는 레시피'로 좁히지 않습니다. 물어야 할 것은
                // "이 작업대가 시체를 처리해 주느냐"이지 "도축 레시피냐"가 아닙니다.
                //
                // MUGB의 내장 추출·뼈 추출은 시체를 재료로 먹지만 specialProducts에
                // Butchery가 없습니다. 좁게 잡으면 그런 계획서만 걸린 고블린 조리대를
                // 놓치게 됩니다.
                if (bill.recipe.fixedIngredientFilter?.Allows(corpseDef) == false)
                {
                    continue;
                }

                if (bill.ingredientFilter?.Allows(corpseDef) == true)
                {
                    return true;
                }
            }

            return false;
        }

        public static Thing FindButcherStationFor(Pawn worker, Pawn victim)
        {
            Map map = victim?.Map;
            if (worker == null || map == null || map != worker.Map)
            {
                return null;
            }

            // GenClosest는 가까운 것부터 보고 첫 유효 결과에서 멈춥니다. 작업대를 전부 훑지
            // 않으므로 계획서 검사가 붙어도 비용이 크게 늘지 않습니다.
            return GenClosest.ClosestThingReachable(
                victim.Position,
                map,
                ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver),
                PathEndMode.Touch,
                TraverseParms.For(worker),
                9999f,
                station => !station.IsForbidden(worker)
                    && StationAcceptsCorpseOf(station, victim)
                    && worker.CanReach(station, PathEndMode.Touch, Danger.Deadly));
        }

        // 기즈모 툴팁이 아니라 '지정하는 순간'에만 부릅니다. 매 프레임 도는 것을 피하기 위한
        // 배치입니다(설계지침 6.9).
        public static bool AnyStationAcceptsCorpseOf(Pawn victim)
        {
            Map map = victim?.Map;
            if (map == null)
            {
                return false;
            }

            List<Thing> candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            if (candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (StationAcceptsCorpseOf(candidates[i], victim))
                {
                    return true;
                }
            }

            return false;
        }

        // ── 도축 반응 사상 ───────────────────────────────────────────
        //
        // 설계지침 4.3 / 3.4. 규율 3단이 서로 다른 무드를 주도록 갈라집니다. 무드가 같으면
        // '상관없음'과 '허용함'이 사실상 같은 규율이 되어버립니다.
        //
        //   도축당하는 쪽(노예·죄수) → 금지함·무입장: WitnessedLivestockButchery (-2, 4일)
        //                              상관없음·허용함: WitnessedLivestockResigned (-1, 2일)
        //     동료가 도축당하는 걸 본 공포. 사기를 떨어뜨리므로 반란 위험을 '높입니다'.
        //     도축을 공짜로 쓰지 못하게 하는 비용입니다. 눈으로 본 경우에만.
        //
        //   '허용함' 정착민 → MUGB_LivestockSlaughterAtonement (+2, 2중첩)
        //     속죄가 이루어졌다는 만족. 규율 확인은 ThoughtDef에 requiredPrecepts가 없어
        //     코드에서 합니다.
        //
        //   그 밖의 정착민 → MUGB_KnowLivestockSlaughter (-5, 1중첩)
        //     '금지함'이거나 이 이슈에 입장이 없는 경우입니다. '상관없음'은 XML
        //     nullifyingPrecepts가 걸러 무드가 없습니다.
        //     바닐라 ButcheredHumanlikeCorpse(-6)는 '시체를 해체했다'라 종류가 다르고,
        //     그쪽은 실제 도축 작업이 별도로 겁니다. 둘은 합산됩니다.
        //
        // 도축이 실제로 일어날 때 한 번만 돕니다. 상시 비용이 없습니다.
        private const float WitnessRadius = 24f;

        public static void ApplySlaughterThoughts(Pawn butcher, Pawn victim)
        {
            Map map = butcher?.Map;
            if (map == null)
            {
                return;
            }

            ThoughtDef witnessed = MUGB_LivestockDefOf.MUGB_WitnessedLivestockButchery;
            ThoughtDef resigned = MUGB_LivestockDefOf.MUGB_WitnessedLivestockResigned;
            ThoughtDef known = MUGB_LivestockDefOf.MUGB_KnowLivestockSlaughter;
            ThoughtDef atonement = MUGB_LivestockDefOf.MUGB_LivestockSlaughterAtonement;
            IntVec3 origin = butcher.Position;
            float radiusSquared = WitnessRadius * WitnessRadius;

            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn observer = spawned[i];
                if (observer == null || observer == victim || observer.Dead)
                {
                    continue;
                }

                if (observer.RaceProps?.Humanlike != true || observer.needs?.mood?.thoughts?.memories == null)
                {
                    continue;
                }

                if (IsValidTarget(observer))
                {
                    // 가축 신분: 눈으로 본 경우에만. 고블린·하프고블린은 면제.
                    if (observer == butcher || IsGoblinKind(observer) || !observer.Awake())
                    {
                        continue;
                    }

                    // 자기 이데올로기가 도축을 인정하면 공포가 아니라 체념입니다.
                    // ThoughtDef에 requiredPrecepts가 없어 여기서 가릅니다.
                    ThoughtDef forWitness = IdeoAllowsButchering(observer.Ideo) ? resigned : witnessed;
                    if (forWitness == null)
                    {
                        continue;
                    }

                    if ((float)observer.Position.DistanceToSquared(origin) > radiusSquared)
                    {
                        continue;
                    }

                    if (!GenSight.LineOfSight(observer.Position, origin, map, true))
                    {
                        continue;
                    }

                    observer.needs.mood.thoughts.memories.TryGainMemory(forWitness);
                    continue;
                }

                // 정착민: 소식을 들은 것이므로 시야와 무관합니다. 바닐라
                // KnowButcheredHumanlikeCorpse와 같은 취급입니다.
                if (!observer.IsFreeNonSlaveColonist)
                {
                    continue;
                }

                // '허용함'이면 속죄가 이루어진 것이므로 긍정. 규율을 가진 이상 고블린 여부와
                // 무관하게 적용합니다.
                if (atonement != null && IdeoRequiresButchering(observer.Ideo))
                {
                    observer.needs.mood.thoughts.memories.TryGainMemory(atonement);
                    continue;
                }

                // 나머지는 부정. '상관없음'은 XML nullifyingPrecepts가 걸러냅니다.
                // 고블린·하프고블린은 규율이 없어도 면제입니다.
                if (known != null && !IsGoblinKind(observer))
                {
                    observer.needs.mood.thoughts.memories.TryGainMemory(known);
                }
            }
        }
    }
}
