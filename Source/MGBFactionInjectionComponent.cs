using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class MGBFactionInjectionComponent : GameComponent
    {
        private static readonly FieldInfo ActiveLanguageField =
            AccessTools.Field(typeof(LanguageDatabase), "activeLanguage");

        private static readonly string[] KoreanGoblinNameCores =
        {
            "뼈", "무쇠", "강철", "합금", "도가니", "화로", "조형자", "완전자", "계시", "속죄",
            "잿바람", "그을음", "잉걸불", "재", "검댕", "진흙", "늪", "이끼", "곰팡이", "독안개",
            "슬래그", "피", "울음", "비명", "사슬", "올가미", "덫", "역병", "닭장", "아쎄이",
            "전우애", "인육", "내장", "골수", "갈비뼈", "척추뼈", "눈알", "두개골", "겨드랑이",
            "가랑이", "항문", "사타구니", "뱃살", "콧구멍", "발가락", "겨털", "배꼽", "엉덩이",
            "정강이", "볼살", "목젖", "땀", "침", "콧물", "방귀", "발냄새"
        };

        private static readonly string[] KoreanGoblinNameAdjectives =
        {
            "퀴퀴한", "시큼한", "악취나는", "썩어가는", "곰팡내나는", "구린", "비린", "쩐내나는",
            "검댕묻은", "누런", "얼룩진", "딱지앉은", "곪은", "물집잡힌", "쓸린", "부푼",
            "고름덮인", "침흘리는", "콧물범벅", "질질새는", "끈적한", "축축한", "쭈글쭈글한",
            "물컹한", "축늘어진", "짓이긴", "사마귀난", "기백있는", "열성적인", "오줌내나는",
            "밤꽃냄새나는"
        };

        private static readonly string[] KoreanPrimitiveSuffixes =
        {
            "무리", "떼", "소굴", "굴", "족", "일족", "소부락", "둥지", "사랑단"
        };

        private static readonly string[] KoreanMedievalSuffixes =
        {
            "결사", "동맹", "연합체", "회", "단", "조합", "계", "학회", "전우회", "사랑단"
        };

        private static readonly string[] KoreanSavageSuffixes =
        {
            "도당", "패거리", "무리떼", "광란단", "파벌", "잔당", "사랑단"
        };

        private static readonly string[] KoreanCultistSuffixes =
        {
            "교단", "성회", "의식단", "신앙회", "순례단", "계시단", "사랑단"
        };

        private bool relationsInitialized;
        private bool nonGoblinRelationsInitialized;
        private bool playerStartedAsGoblins;
        private int nextBeggarTravelerTick;
        private int nextKimDeokPalCaravanTick;
        private int nextTemporaryBeggarCleanupTick;

        public MGBFactionInjectionComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref relationsInitialized, "MUGB_relationsInitialized", false);
            Scribe_Values.Look(ref nonGoblinRelationsInitialized, "MUGB_nonGoblinRelationsInitialized", false);
            Scribe_Values.Look(ref playerStartedAsGoblins, "MUGB_playerStartedAsGoblins", false);
            Scribe_Values.Look(ref nextBeggarTravelerTick, "MUGB_nextBeggarTravelerTick", 0);
            Scribe_Values.Look(ref nextKimDeokPalCaravanTick, "MUGB_nextKimDeokPalCaravanTick", 0);
            Scribe_Values.Look(ref nextTemporaryBeggarCleanupTick, "MUGB_nextTemporaryBeggarCleanupTick", 0);
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (!relationsInitialized)
            {
                playerStartedAsGoblins = DetectGoblinStartFromPlayerPawns();
            }
            EnsureGoblinFaction(playerStartedAsGoblins, initializeNonGoblinRelations: !nonGoblinRelationsInitialized);
            relationsInitialized = true;
            nonGoblinRelationsInitialized = true;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            playerStartedAsGoblins = DetectGoblinStartScenario();
            EnsureGoblinFaction(playerStartedAsGoblins, initializeNonGoblinRelations: true);
            relationsInitialized = true;
            nonGoblinRelationsInitialized = true;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            // 임시 거지 세력은 퀘스트 소유가 아니므로, 통과 후에만 하루 단위로 정리한다.
            if (currentTick >= nextTemporaryBeggarCleanupTick)
            {
                nextTemporaryBeggarCleanupTick = currentTick + GenDate.TicksPerDay;
                MUGBBeggarFactionUtility.CleanupUnusedTemporaryFactions();
            }

            // 거지 통행은 스토리텔러를 가리지 않는다. 초반 인육 공급원이 사실상 이것뿐이라
            // 김덕팔 전용으로 두면 다른 스토리텔러에서는 갈망을 채울 방법이 없다.
            TickBeggarTravelers(currentTick);

            if (!GoblinRaidPerformanceUtility.IsKimDeokPalStorytellerActive)
            {
                // 상단 일정은 김덕팔 고유 연출이므로 다른 스토리텔러에는 남기지 않는다.
                nextKimDeokPalCaravanTick = 0;
                return;
            }

            TickKimDeokPalAmbientEvents(currentTick);
        }

        private static void EnsureGoblinFaction(bool goblinStart, bool initializeNonGoblinRelations)
        {
            if (Find.World == null || Find.FactionManager == null)
            {
                return;
            }

            RepairExistingGoblinFactions(goblinStart, initializeNonGoblinRelations);

            foreach (FactionDef factionDef in GoblinFactionDefs())
            {
                if (factionDef == null || factionDef.hidden)
                {
                    continue;
                }

                Faction existingGoblinFaction = Find.FactionManager.FirstFactionOfDef(factionDef);
                if (existingGoblinFaction != null)
                {
                    EnsureGoblinFactionIconUsesNeutralTint(existingGoblinFaction);
                    TryApplyLocalizedGoblinFactionName(existingGoblinFaction, replaceExistingEnglish: true);
                    ApplyPlayerRelation(existingGoblinFaction, goblinStart);
                    EnsureGoblinIdeology(existingGoblinFaction);
                    EnsureLeader(existingGoblinFaction);
                    continue;
                }

                Faction goblinFaction = MakeGoblinFaction(factionDef);
                foreach (Faction other in Find.FactionManager.AllFactionsListForReading.ToList())
                {
                    goblinFaction.TryMakeInitialRelationsWith(other);
                }

                Find.FactionManager.Add(goblinFaction);
                EnsureGoblinFactionIconUsesNeutralTint(goblinFaction);
                TryApplyLocalizedGoblinFactionName(goblinFaction, replaceExistingEnglish: true);
                ApplyPlayerRelation(goblinFaction, goblinStart);
                EnsureGoblinIdeology(goblinFaction);
                EnsureLeader(goblinFaction);

                Log.Message("[MUGB] Added goblin faction " + factionDef.defName + " to this world without creating settlements.");
            }
        }

        private static void RepairExistingGoblinFactions(bool goblinStart, bool initializeNonGoblinRelations)
        {
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading.ToList())
            {
                if (!IsGoblinFaction(faction))
                {
                    if (initializeNonGoblinRelations)
                    {
                        ApplyPlayerRelation(faction, goblinStart);
                    }
                    continue;
                }

                EnsureGoblinFactionIconUsesNeutralTint(faction);
                TryApplyLocalizedGoblinFactionName(faction, replaceExistingEnglish: true);
                ApplyPlayerRelation(faction, goblinStart);
                EnsureGoblinIdeology(faction);
                EnsureLeader(faction);
            }
        }

        internal static bool IsGoblinFaction(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            return GoblinFactionDefs().Contains(faction.def);
        }

        private static IEnumerable<FactionDef> GoblinFactionDefs()
        {
            if (MUGBDefOf.MUGB_GoblinTribe != null)
            {
                yield return MUGBDefOf.MUGB_GoblinTribe;
            }

            if (MUGBDefOf.MUGB_GoblinCivilTribe != null)
            {
                yield return MUGBDefOf.MUGB_GoblinCivilTribe;
            }

            if (MUGBDefOf.MUGB_GoblinCivilMedieval != null)
            {
                yield return MUGBDefOf.MUGB_GoblinCivilMedieval;
            }

            if (MUGBDefOf.MUGB_GoblinSavageMedieval != null)
            {
                yield return MUGBDefOf.MUGB_GoblinSavageMedieval;
            }

            if (MUGBDefOf.MUGB_GoblinCultists != null)
            {
                yield return MUGBDefOf.MUGB_GoblinCultists;
            }

            if (MUGBDefOf.MUGB_GoblinHunters != null)
            {
                yield return MUGBDefOf.MUGB_GoblinHunters;
            }
        }

        private static Faction MakeGoblinFaction(FactionDef factionDef)
        {
            Faction faction = new Faction
            {
                def = factionDef,
                loadID = Find.UniqueIDsManager.GetNextFactionID(),
                colorFromSpectrum = -999f,
                hidden = false,
                defeated = false,
                temporary = false
            };

            EnsureGoblinFactionIconUsesNeutralTint(faction);
            faction.Name = GenerateFactionName(factionDef);

            if (factionDef.humanlikeFaction)
            {
                faction.ideos = new FactionIdeosTracker(faction);
                faction.ideos.ChooseOrGenerateIdeo(
                    FactionIdeosTracker.IdeoGenerationParmsForFaction_BackCompatibility(
                        factionDef,
                        !ModsConfig.IdeologyActive));
            }

            return faction;
        }

        // 한국어 의도: 어느 세력에도 속하지 않은 거지 무리가 3~5명씩 맵을 통과한다.
        // 이건 초반 구제용 공급원이지 상시 인육 자판기가 아니다. 8일차에 작업장 퀘스트가 열리고
        // 기지가 커지면 습격 시체까지 들어오므로, 시간이 갈수록 간격을 뚜렷하게 벌린다.
        private static readonly SimpleCurve BeggarIntervalDaysByDaysPassed = new SimpleCurve
        {
            new CurvePoint(0f, 4f),
            new CurvePoint(15f, 4f),
            new CurvePoint(30f, 8f),
            new CurvePoint(60f, 14f),
            new CurvePoint(120f, 20f)
        };

        private static int NextBeggarDelayTicks()
        {
            float days = BeggarIntervalDaysByDaysPassed.Evaluate(GenDate.DaysPassed) * Rand.Range(0.8f, 1.2f);
            int frequencyPercent = Mathf.Clamp(MUGBMod.Settings?.passingGroupFrequencyPercent ?? 110, 0, 200);
            days /= Mathf.Max(0.1f, frequencyPercent / 100f);
            if (GoblinRaidPerformanceUtility.IsKimDeokPalStorytellerActive)
            {
                // 김덕팔은 청중이 지루해지는 꼴을 못 본다. 통행 사건을 조금 더 자주 끼워 넣는다.
                days *= 0.85f;
            }
            return Mathf.RoundToInt(days * GenDate.TicksPerDay);
        }

        private void TickBeggarTravelers(int currentTick)
        {
            if ((MUGBMod.Settings?.passingGroupFrequencyPercent ?? 110) <= 0)
            {
                nextBeggarTravelerTick = 0;
                return;
            }

            if (nextBeggarTravelerTick <= 0)
            {
                nextBeggarTravelerTick = currentTick + NextBeggarDelayTicks();
                return;
            }

            if (currentTick < nextBeggarTravelerTick)
            {
                return;
            }

            nextBeggarTravelerTick = currentTick + NextBeggarDelayTicks();
            Map map = Find.Maps?.Where(candidate => candidate.IsPlayerHome).RandomElementWithFallback();
            if (map == null || !IncidentWorker_BeggarTravelerGroup.TryFire(map))
            {
                nextBeggarTravelerTick = currentTick + GenDate.TicksPerDay;
            }
        }

        private void TickKimDeokPalAmbientEvents(int currentTick)
        {
            if (nextKimDeokPalCaravanTick <= 0)
            {
                nextKimDeokPalCaravanTick = currentTick + Rand.RangeInclusive(5, 7) * GenDate.TicksPerDay;
            }

            if (currentTick >= nextKimDeokPalCaravanTick)
            {
                // 한국어 의도: 김덕팔은 분기(15일)마다 적어도 두 번 정도 상단을 보게 하는 이야기꾼이다.
                nextKimDeokPalCaravanTick = currentTick + Rand.RangeInclusive(5, 7) * GenDate.TicksPerDay;
                if (!TryFireKimDeokPalTraderCaravan())
                {
                    nextKimDeokPalCaravanTick = currentTick + GenDate.TicksPerDay;
                }
            }
        }

        private static bool TryFireKimDeokPalTraderCaravan()
        {
            Map map = Find.Maps?.Where(candidate => candidate.IsPlayerHome).RandomElementWithFallback();
            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail("TraderCaravanArrival");
            if (map == null || incident?.Worker == null)
            {
                return false;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
            parms.target = map;
            return incident.Worker.CanFireNow(parms) && incident.Worker.TryExecute(parms);
        }

        private static string GenerateFactionName(FactionDef factionDef)
        {
            if (!factionDef.fixedName.NullOrEmpty())
            {
                return factionDef.fixedName;
            }

            if (TryGenerateKoreanGoblinFactionName(factionDef, out string koreanName))
            {
                return koreanName;
            }

            try
            {
                if (factionDef.factionNameMaker != null)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        string generatedName = NameGenerator.GenerateName(
                            factionDef.factionNameMaker,
                            Find.FactionManager.AllFactionsVisible.Select(faction => faction.Name));

                        if (!generatedName.NullOrEmpty() && generatedName.Length <= 20)
                        {
                            return generatedName;
                        }
                    }

                    string fallbackGeneratedName = NameGenerator.GenerateName(
                        factionDef.factionNameMaker,
                        Find.FactionManager.AllFactionsVisible.Select(faction => faction.Name));

                    if (!fallbackGeneratedName.NullOrEmpty())
                    {
                        return fallbackGeneratedName;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[MUGB] Failed to generate goblin faction name. Falling back to faction label.\n" + ex);
            }

            return factionDef.LabelCap;
        }

        internal static void TryApplyLocalizedGoblinFactionName(Faction faction, bool replaceExistingEnglish = false)
        {
            if (!IsGoblinFaction(faction) || !TryGenerateKoreanGoblinFactionName(faction.def, out string koreanName))
            {
                return;
            }

            if (faction.Name.NullOrEmpty()
                || replaceExistingEnglish && ContainsLatinLetters(faction.Name))
            {
                faction.Name = koreanName;
            }
        }

        internal static bool TryGenerateKoreanGoblinFactionName(FactionDef factionDef, out string name)
        {
            name = null;
            if (!IsKoreanActive() || factionDef == null || !TryGetKoreanGoblinNameSettings(factionDef, out string[] suffixes, out float adjectiveChance))
            {
                return false;
            }

            // RulePackDef는 영어 원본을 보존하고, 한국어 환경에서는 전용 한국어 이름 은행으로 직접 조합한다.
            // 구조는 [형용사(확률)] + [코어단어] + [접미사]다.
            for (int i = 0; i < 10; i++)
            {
                string core = KoreanGoblinNameCores.RandomElement();
                string suffix = suffixes.RandomElement();
                string generated = Rand.Chance(adjectiveChance)
                    ? KoreanGoblinNameAdjectives.RandomElement() + " " + core + suffix
                    : core + suffix;

                if (Find.FactionManager == null || !Find.FactionManager.AllFactionsVisible.Any(faction => faction.Name == generated))
                {
                    name = generated;
                    return true;
                }
            }

            name = KoreanGoblinNameCores.RandomElement() + suffixes.RandomElement();
            return true;
        }

        private static bool TryGetKoreanGoblinNameSettings(FactionDef factionDef, out string[] suffixes, out float adjectiveChance)
        {
            switch (factionDef.defName)
            {
                case "MUGB_GoblinCivilTribe":
                    suffixes = KoreanPrimitiveSuffixes;
                    adjectiveChance = 0.70f;
                    return true;
                case "MUGB_GoblinTribe":
                    suffixes = KoreanSavageSuffixes;
                    adjectiveChance = 0.70f;
                    return true;
                case "MUGB_GoblinCivilMedieval":
                    suffixes = KoreanMedievalSuffixes;
                    adjectiveChance = 0.50f;
                    return true;
                case "MUGB_GoblinSavageMedieval":
                    suffixes = KoreanSavageSuffixes;
                    adjectiveChance = 0.50f;
                    return true;
                case "MUGB_GoblinCultists":
                    suffixes = KoreanCultistSuffixes;
                    adjectiveChance = 0.30f;
                    return true;
                default:
                    suffixes = null;
                    adjectiveChance = 0f;
                    return false;
            }
        }

        internal static bool IsKoreanActive()
        {
            try
            {
                LoadedLanguage language = ActiveLanguageField?.GetValue(null) as LoadedLanguage;
                if (language == null)
                {
                    return false;
                }

                if (string.Equals(language.LegacyFolderName, "Korean", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!language.FriendlyNameNative.NullOrEmpty() && language.FriendlyNameNative.Contains("한국어"))
                {
                    return true;
                }

                if (!language.FriendlyNameEnglish.NullOrEmpty() && language.FriendlyNameEnglish.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal static void EnsureGoblinFactionIconUsesNeutralTint(Faction faction)
        {
            if (faction?.def == null || !IsGoblinFaction(faction))
            {
                return;
            }

            // 월드맵 아이콘 PNG 자체에 색이 있으므로 빨강/초록 같은 세력 색 틴트를 추가하지 않는다.
            // FactionDef의 colorSpectrum은 흰색만 두고, 기존 세이브에 남은 이전 틴트도 흰색 중립값으로 되돌린다.
            faction.colorFromSpectrum = 0.5f;
        }

        private static bool ContainsLatinLetters(string text)
        {
            if (text.NullOrEmpty())
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyPlayerRelation(Faction faction, bool goblinStart)
        {
            Faction player = Faction.OfPlayer;
            if (faction == null || faction.def == null || faction == player)
            {
                return;
            }

            if (IsCivilGoblinFaction(faction.def) && goblinStart)
            {
                EnsureLowNeutralToPlayer(faction, -35);
                return;
            }

            if (goblinStart
                && faction.def.humanlikeFaction
                && !IsGoblinFaction(faction)
                && faction.def != MUGBDefOf.MUGB_NeutralBeggarBand)
            {
                EnsureHostileToPlayer(faction);
                return;
            }

            if (IsGoblinFaction(faction) || faction.def == MUGBDefOf.MUGB_GoblinHunters)
            {
                EnsureHostileToPlayer(faction);
            }
        }

        private static bool IsCivilGoblinFaction(FactionDef factionDef)
        {
            return factionDef == MUGBDefOf.MUGB_GoblinCivilTribe
                || factionDef == MUGBDefOf.MUGB_GoblinCivilMedieval;
        }

        private static bool IsSavageGoblinFaction(FactionDef factionDef)
        {
            return factionDef == MUGBDefOf.MUGB_GoblinTribe
                || factionDef == MUGBDefOf.MUGB_GoblinSavageMedieval;
        }

        private static bool IsCultistGoblinFaction(FactionDef factionDef)
        {
            return factionDef == MUGBDefOf.MUGB_GoblinCultists;
        }

        private static void EnsureGoblinIdeology(Faction faction)
        {
            if (!ModsConfig.IdeologyActive || faction?.def == null || faction.def == MUGBDefOf.MUGB_GoblinHunters)
            {
                return;
            }

            if (!IsGoblinFaction(faction))
            {
                return;
            }

            Ideo ideo = faction.ideos?.PrimaryIdeo;
            if (ideo == null)
            {
                return;
            }

            EnsureCommonGoblinPrecepts(ideo, faction.def);

            if (IsCultistGoblinFaction(faction.def))
            {
                SetIssuePrecept(ideo, faction.def, "MUGB_OrganEating_Important");
                SetIssuePrecept(ideo, faction.def, "MUGB_SlaveMarriage_Important");
                SetIssuePrecept(ideo, faction.def, "Slavery_Honorable");
                SetIssuePrecept(ideo, faction.def, "Cannibalism_RequiredStrong");
                SetIssuePrecept(ideo, faction.def, "OrganUse_Acceptable");
                return;
            }

            if (IsSavageGoblinFaction(faction.def))
            {
                SetIssuePrecept(ideo, faction.def, "MUGB_OrganEating_Important");
                SetIssuePrecept(ideo, faction.def, "MUGB_SlaveMarriage_Preferred");
                SetIssuePrecept(ideo, faction.def, "Slavery_Honorable");
                SetIssuePrecept(ideo, faction.def, "Cannibalism_Preferred");
                SetIssuePrecept(ideo, faction.def, "OrganUse_Acceptable");
                return;
            }

            if (IsCivilGoblinFaction(faction.def))
            {
                SetIssuePrecept(ideo, faction.def, "MUGB_OrganEating_Preferred");
                SetIssuePrecept(ideo, faction.def, "MUGB_SlaveMarriage_Acceptable");
                SetIssuePrecept(ideo, faction.def, "Slavery_Acceptable");
                SetIssuePrecept(ideo, faction.def, "Cannibalism_Preferred");
                SetIssuePrecept(ideo, faction.def, "OrganUse_Acceptable");
            }
        }

        private static void EnsureCommonGoblinPrecepts(Ideo ideo, FactionDef factionDef)
        {
            // 한국어 의도: 고블린 세력 이념이 인간 기준의 어색한 금기보다 고블린 로어에 맞는 기본 규율을 갖도록 강제합니다.
            // 버섯/곤충고기는 바닐라에 "신경쓰지 않음" 단계가 없어 MUGB 전용 무효 precept를 사용합니다.
            SetIssuePrecept(ideo, factionDef, "Execution_DontCare");
            SetIssuePrecept(ideo, factionDef, "Blinding_Horrible");
            SetIssuePrecept(ideo, factionDef, "Corpses_DontCare");
            SetIssuePrecept(ideo, factionDef, "MUGB_InsectMeatEating_DontCare");
            SetIssuePrecept(ideo, factionDef, "MUGB_FungusEating_DontCare");
            SetIssuePrecept(ideo, factionDef, "Lovin_FreeApproved");
            SetIssuePrecept(ideo, factionDef, "SpouseCount_Male_Unlimited");
            SetIssuePrecept(ideo, factionDef, "SpouseCount_Female_Unlimited");
        }

        private static void SetIssuePrecept(Ideo ideo, FactionDef factionDef, string preceptDefName)
        {
            PreceptDef targetDef = DefDatabase<PreceptDef>.GetNamedSilentFail(preceptDefName);
            if (ideo == null || targetDef?.issue == null)
            {
                return;
            }

            if (ideo.HasPrecept(targetDef))
            {
                return;
            }

            List<Precept> existing = ideo.PreceptsListForReading
                .Where(precept => precept?.def?.issue == targetDef.issue)
                .ToList();

            foreach (Precept precept in existing)
            {
                ideo.RemovePrecept(precept, true);
            }

            Precept newPrecept = PreceptMaker.MakePrecept(targetDef);
            if (newPrecept == null)
            {
                return;
            }

            ideo.AddPrecept(newPrecept, true, factionDef, null);
            ideo.RecachePrecepts();
        }

        private static void EnsureLowNeutralToPlayer(Faction faction, int targetGoodwill)
        {
            Faction player = Faction.OfPlayer;
            if (player == null || faction == null || faction.def.permanentEnemy)
            {
                return;
            }

            if (faction.HostileTo(player))
            {
                faction.SetRelationDirect(player, FactionRelationKind.Neutral, canSendHostilityLetter: false);
            }

            int current = faction.GoodwillWith(player);
            if (current != targetGoodwill)
            {
                faction.TryAffectGoodwillWith(player, targetGoodwill - current, canSendMessage: false, canSendHostilityLetter: false);
            }
        }

        private static void EnsureHostileToPlayer(Faction faction)
        {
            Faction player = Faction.OfPlayer;
            if (player == null || faction == null || faction.HostileTo(player))
            {
                return;
            }

            bool changedGoodwill = player.TryAffectGoodwillWith(
                faction,
                player.GoodwillToMakeHostile(faction),
                canSendMessage: false,
                canSendHostilityLetter: false);

            if (!changedGoodwill && !faction.HostileTo(player))
            {
                faction.SetRelationDirect(player, FactionRelationKind.Hostile, canSendHostilityLetter: false);
            }
        }

        private static bool DetectGoblinStartScenario()
        {
            try
            {
                return Find.Scenario?.AllParts?.Any(part => part is ScenPart_MUGB_GoblinStartMarker) == true;
            }
            catch (Exception ex)
            {
                Log.Warning("[MUGB] Failed to detect goblin start scenario. Falling back to player pawn scan.\n" + ex);
                return DetectGoblinStartFromPlayerPawns();
            }
        }

        private static bool DetectGoblinStartFromPlayerPawns()
        {
            try
            {
                List<Pawn> pawns = new List<Pawn>();
                foreach (Map map in Find.Maps)
                {
                    pawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction == Faction.OfPlayer));
                }

                foreach (Caravan caravan in Find.WorldObjects.Caravans)
                {
                    pawns.AddRange(caravan.PawnsListForReading.Where(pawn => pawn.Faction == Faction.OfPlayer));
                }

                pawns = pawns
                    .Where(pawn => pawn != null && !pawn.Dead && !pawn.InCryptosleep)
                    .Distinct()
                    .ToList();
                return pawns.Count > 0 && pawns.Count(pawn => GoblinUtility.IsGoblin(pawn)) >= pawns.Count;
            }
            catch (Exception ex)
            {
                Log.Warning("[MUGB] Failed to detect goblin start from player pawns.\n" + ex);
                return false;
            }
        }

        private static void EnsureLeader(Faction faction)
        {
            if (faction == null || faction.leader != null || faction.def == null || !faction.def.humanlikeFaction)
            {
                return;
            }

            if (!TryGenerateLeader(faction) && !TryGenerateFallbackLeader(faction))
            {
                Log.Warning("[MUGB] Failed to repair a goblin faction leader for " + faction.Name + ".");
            }
        }

        private static bool TryGenerateLeader(Faction faction)
        {
            try
            {
                faction.TryGenerateNewLeader();
                return faction.leader != null;
            }
            catch (Exception ex)
            {
                Log.Warning("[MUGB] Failed to generate a goblin faction leader.\n" + ex);
                return false;
            }
        }

        private static bool TryGenerateFallbackLeader(Faction faction)
        {
            try
            {
                PawnKindDef kindDef = faction.def.fixedLeaderKinds?.FirstOrDefault()
                    ?? faction.def.basicMemberKind
                    ?? MUGBDefOf.MUGB_HobgoblinBareBrawler
                    ?? MUGBDefOf.MUGB_GoblinBareBrawler
                    ?? PawnKindDefOf.Colonist;

                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 0f,
                    forceNoIdeo: !ModsConfig.IdeologyActive,
                    developmentalStages: DevelopmentalStage.Adult);

                Pawn leader = PawnGenerator.GeneratePawn(request);
                if (leader == null)
                {
                    return false;
                }

                if (leader.Faction != faction)
                {
                    leader.SetFaction(faction);
                }

                faction.leader = leader;
                Find.WorldPawns.PassToWorld(leader, PawnDiscardDecideMode.KeepForever);
                Log.Message("[MUGB] Repaired missing goblin faction leader for " + faction.Name + ".");
                return faction.leader != null;
            }
            catch (Exception ex)
            {
                Log.Warning("[MUGB] Fallback goblin faction leader generation failed.\n" + ex);
                return false;
            }
        }
    }

    public class ScenPart_MUGB_GoblinStartMarker : ScenPart
    {
        public override string Summary(Scenario scen)
        {
            return "MUGB goblin start marker";
        }
    }

    [HarmonyPatch(typeof(FactionGenerator), "NewGeneratedFaction", typeof(PlanetLayer), typeof(FactionGeneratorParms))]
    public static class FactionGenerator_NewGeneratedFaction_MUGBGoblinLocalizationPatch
    {
        public static void Postfix(ref Faction __result)
        {
            if (__result == null)
            {
                return;
            }

            // Korean source intent: 바닐라 월드 생성은 FactionDef.factionNameMaker를 직접 써서 DefInjected 번역을 거치지 않는다.
            // 한국어 환경에서는 생성 직후 고블린 세력명을 전용 한국어 이름풀로 다시 뽑는다.
            MGBFactionInjectionComponent.TryApplyLocalizedGoblinFactionName(__result, replaceExistingEnglish: true);
            MGBFactionInjectionComponent.EnsureGoblinFactionIconUsesNeutralTint(__result);
        }
    }

    /// <summary>
    /// 한국어 의도: 세력 관계 목록을 Faction.RelationWith를 거치지 않고 직접 다룹니다.
    ///
    /// Rim War가 Faction.RelationWith에 프리픽스를 걸어 두는데, 관계가 아직 없으면 자기 쪽
    /// 헬퍼를 부르고 그 헬퍼는 관계를 목록에 넣기 전에 CheckKindThresholds를 돌립니다.
    /// CheckKindThresholds -> GoodwillWith -> RelationWith 로 되돌아와도 관계는 여전히
    /// 없으므로 같은 경로가 무한 반복되어 스택 오버플로로 게임이 죽습니다.
    ///
    /// 그래서 새로 만든 세력을 다루기 전에 여기서 관계를 먼저 채워 둡니다. 조회 자체를
    /// 하지 않으므로 그 함정이 열리지 않고, Rim War가 없어도 동작이 달라지지 않습니다.
    /// (관계가 이미 있으면 아무것도 바꾸지 않습니다.)
    /// </summary>
    public static class MUGBFactionRelationSafety
    {
        private static readonly FieldInfo RelationsField = AccessTools.Field(typeof(Faction), "relations");

        public static bool Available => RelationsField != null;

        /// <summary>
        /// 양방향 관계를 만들거나 갱신합니다.
        /// 호감도는 관계 종류와 같은 방향으로 넣어, 나중에 CheckKindThresholds가 돌아도 뒤집히지 않게 합니다.
        /// </summary>
        public static void SetPair(Faction a, Faction b, FactionRelationKind kind, int goodwill)
        {
            WriteSide(a, b, kind, goodwill);
            WriteSide(b, a, kind, goodwill);
        }

        /// <summary>
        /// 구형 세이브처럼 한쪽 관계가 빠진 경우에만 보충합니다. 이미 존재하는 적대/중립 상태는 덮어쓰지 않습니다.
        /// </summary>
        public static void EnsurePair(Faction a, Faction b, FactionRelationKind defaultKind, int defaultGoodwill)
        {
            if (a == null || b == null)
            {
                return;
            }

            FactionRelation relationA = FindRelation(a, b);
            FactionRelation relationB = FindRelation(b, a);
            FactionRelation existing = relationA ?? relationB;
            FactionRelationKind kind = existing?.kind ?? defaultKind;
            int goodwill = existing?.baseGoodwill ?? defaultGoodwill;

            if (relationA == null)
            {
                WriteSide(a, b, kind, goodwill);
            }
            if (relationB == null)
            {
                WriteSide(b, a, kind, goodwill);
            }
        }

        private static FactionRelation FindRelation(Faction from, Faction to)
        {
            if (!(RelationsField.GetValue(from) is List<FactionRelation> relations))
            {
                return null;
            }

            for (int i = 0; i < relations.Count; i++)
            {
                if (relations[i]?.other == to)
                {
                    return relations[i];
                }
            }

            return null;
        }

        private static void WriteSide(Faction from, Faction to, FactionRelationKind kind, int goodwill)
        {
            if (from == null || to == null || !(RelationsField.GetValue(from) is List<FactionRelation> relations))
            {
                return;
            }

            FactionRelation relation = FindRelation(from, to);
            if (relation == null)
            {
                relation = new FactionRelation { other = to };
                relations.Add(relation);
            }

            relation.kind = kind;
            relation.baseGoodwill = goodwill;
        }
    }
}
