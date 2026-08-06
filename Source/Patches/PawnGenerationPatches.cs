using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GetBodyTypeFor))]
    public static class PawnGenerator_GetBodyTypeFor_HumanFemaleDistributionPatch
    {
        public static void Postfix(Pawn pawn, ref BodyTypeDef __result)
        {
            if (pawn?.def != ThingDefOf.Human
                || pawn.gender != Gender.Female
                || pawn.DevelopmentalStage.Juvenile()
                || HasBodyTypeGene(pawn))
            {
                return;
            }

            if (MUGBMod.Settings?.americanBeautyStandard == true)
            {
                __result = BodyTypeDefOf.Fat;
                return;
            }

            if (MUGBMod.Settings?.adjustFemaleBodyTypeChances != true)
            {
                return;
            }

            // 여성 60% / 마른 30% / 거구 5% / 비만 5%.
            // 바닐라는 백스토리가 정한 체형을 그대로 쓰는데, 그러면 거구·비만이 자주 나옵니다.
            // 13~17세도 여기 포함됩니다. 청소년의 developmentalStage는 Adult라서
            // 위의 Juvenile 가드에 걸리지 않습니다(0~13세만 걸러집니다).
            float roll = Rand.Value;
            __result = roll < 0.60f ? BodyTypeDefOf.Female
                : roll < 0.90f ? BodyTypeDefOf.Thin
                : roll < 0.95f ? BodyTypeDefOf.Hulk
                : BodyTypeDefOf.Fat;
        }

        private static bool HasBodyTypeGene(Pawn pawn)
        {
            List<Gene> genes = pawn.genes?.GenesListForReading;
            if (genes == null)
            {
                return false;
            }

            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i]?.def?.bodyType.HasValue == true)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    public static class PawnGenerator_GeneratePawn_Patch
    {
        public static void Prefix(ref PawnGenerationRequest request)
        {
            if (MUGBTravelerRelationGenerationContext.Active)
            {
                request.CanGeneratePawnRelations = false;
                request.ColonistRelationChanceFactor = 0f;
            }

            XenotypeDef pawnKindXenotype = ForcedXenotypeForMugbPawnKind(request.KindDef);
            if (pawnKindXenotype != null)
            {
                request.ForcedXenotype = pawnKindXenotype;
            }

            if (IsMugbGoblinKind(request.KindDef))
            {
                // 한국어 의도: 고블린 상단 경비/상인/노예 생성 중 바닐라 가족·배우자 관계 생성이 이름 처리에서 터지는 일을 막는다.
                // 고블린 전투/상단 PawnKind는 가족관계가 게임플레이 핵심이 아니므로 관계 생성을 꺼서 상단 생성 안정성을 우선한다.
                request.CanGeneratePawnRelations = false;
            }

            ApplyPlayerGoblinFactionXenotype(ref request);

            // 한국어 의도: 이 표시는 "다음에 생성되는 아기 1명"에만 유효한 1회용입니다.
            // 아래 성별 처리에서 조기 반환되더라도 반드시 먼저 읽어서 소비해야, 표시가 남아
            // 뒤이어 생성되는 무관한 폰에 잘못 적용되지 않습니다.
            bool fullGoblinNewborn = GoblinBirthUtility.ConsumeExpectingFullGoblinNewborn();

            // 첫째 아기는 바닐라 출산 코드가 어미 PawnKind로 생성하므로, 어미가 HAR 외계 종족이면
            // 아기도 그 종족으로 태어납니다. 순수 고블린으로 확정된 아기만 인간 뼈대로 되돌립니다.
            // 둘째 이후와 사전 생성 아기는 MUGB가 직접 만들면서 같은 규칙을 적용합니다.
            if (fullGoblinNewborn)
            {
                PawnKindDef humanKind = GoblinBirthUtility.HumanRaceKindFor(request.KindDef);
                if (humanKind != null && humanKind != request.KindDef)
                {
                    request.KindDef = humanKind;
                }
            }

            if (request.FixedGender.HasValue)
            {
                return;
            }

            if (fullGoblinNewborn
                || GoblinUtility.IsGoblinXenotype(request.ForcedXenotype)
                || FactionOnlyProducesGoblins(request.Faction))
            {
                request.FixedGender = Gender.Male;
            }
        }

        // 한국어 의도: 플레이어 고블린 팩션에 합류하는 폰을 고블린으로 만듭니다.
        //
        // 팩션 def에 xenotypeSet을 넣는 방법은 쓸 수 없습니다. 바닐라는 플레이어 팩션이
        // xenotypeSet을 갖지 않는다고 명시하고 있고, 실제로 넣으면 폰 생성이 실패해
        // 새 게임 시작이 깨집니다. 그래서 생성 요청 단계에서 제노타입을 직접 지정합니다.
        //
        // 시나리오가 이미 제노타입을 정한 경우(시작 폰)에는 건드리지 않습니다.
        private static void ApplyPlayerGoblinFactionXenotype(ref PawnGenerationRequest request)
        {
            // MUGB joiners/refugees use explicit goblin PawnKinds or their own conversion patches.
            // Restrict this generic fallback to scenario candidates so player-owned human
            // slaves and prisoners created by other systems retain their intended xenotype.
            if (request.Context != PawnGenerationContext.PlayerStarter)
            {
                return;
            }

            // 한국어 의도: Baseliner는 "지정 안 함"에 가까운 기본값입니다. 시작 폰 후보 중 고르지 않은
            // 쪽("남겨짐")이 Baseliner로 지정돼 오기 때문에, 이것까지 덮어야 후보 전원이 고블린이 됩니다.
            // 시나리오가 실제 제노타입(MUGB_Goblin 등)을 지정한 폰은 그대로 둡니다.
            bool xenotypeAlreadyChosen = request.ForcedXenotype != null
                && request.ForcedXenotype != XenotypeDefOf.Baseliner;
            if (xenotypeAlreadyChosen
                || request.ForcedCustomXenotype != null
                || request.KindDef?.race != ThingDefOf.Human)
            {
                return;
            }

            // 시작 폰 후보는 요청에 팩션이 실리지 않을 수 있습니다.
            Faction faction = request.Faction ?? Find.FactionManager?.OfPlayer;
            if (faction?.def == null || faction.def.defName != PlayerGoblinFactionDefName)
            {
                return;
            }

            // 띤 9 : 홉 1 비율입니다. 폰마다 독립적으로 굴립니다.
            request.ForcedXenotype = Rand.Chance(0.1f) ? MUGBDefOf.MUGB_Hobgoblin : MUGBDefOf.MUGB_Goblin;
        }

        private const string PlayerGoblinFactionDefName = "MUGB_PlayerGoblinFaction";

        // 한국어 의도: 팩션이 고블린 제노타입만 뽑는 경우에도 성별을 미리 남성으로 고정합니다.
        //
        // 제노타입이 request.ForcedXenotype이 아니라 팩션의 xenotypeSet에서 정해지는 경로가 있습니다.
        // 플레이어 고블린 팩션에 합류하는 폰이 그렇습니다. 이때 성별을 정해두지 않으면 여성 고블린이
        // 생성되고, 이 모드는 고블린을 남성으로만 다루므로 IsGoblin이 거짓이 되어 인간처럼 렌더됩니다.
        // 성별은 신체부위가 붙기 전에 정해져야 하므로 생성 요청 단계에서 처리합니다.
        //
        // 고블린이 아닌 제노타입이 하나라도 섞여 있으면 건드리지 않습니다. 그런 팩션은 인간도 뽑기 때문입니다.
        private static bool FactionOnlyProducesGoblins(Faction faction)
        {
            XenotypeSet set = faction?.def?.xenotypeSet;
            if (set == null || set.Count == 0)
            {
                return false;
            }

            bool sawGoblin = false;
            for (int i = 0; i < set.Count; i++)
            {
                XenotypeChance chance = set[i];
                if (chance == null || chance.chance <= 0f)
                {
                    continue;
                }

                if (!GoblinUtility.IsGoblinXenotype(chance.xenotype))
                {
                    return false;
                }

                sawGoblin = true;
            }

            return sawGoblin;
        }


        public static void Postfix(Pawn __result)
        {
            if (__result == null)
            {
                return;
            }

            // 한국어 의도: Prefix가 이미 request.ForcedXenotype으로 고블린 제노타입을 지정하므로
            // 정상 생성된 폰을 여기서 다시 덮어쓸 필요가 없다.
            // 예전에는 Postfix가 제노타입을 다시 뽑아(Prefix는 Rand.Chance, Postfix는 Rand.ChanceSeeded로
            // 서로 독립적인 주사위) 결과가 갈리면 SetXenotype으로 두 번째 체형 유전자를 얹었다.
            // 그 결과 GoblinCore와 HobgoblinFrame이 한 폰에 공존했고, 같은 exclusionTags 충돌에서
            // 코어가 이겨 "제노타입은 홉고블린인데 몸은 띤(0.8배)"인 폰이 생겼다.
            // 이제는 폰이 고블린 계열 제노타입을 아예 받지 못한 경우(개발자/콜로니스트 생성 경로에서
            // XML xenotypeSet을 우회해 인간으로 나오는 케이스)에만 안전망으로 지정한다.
            XenotypeDef pawnKindXenotype = ForcedXenotypeForMugbPawnKind(__result.kindDef, __result.thingIDNumber ^ 0x4D554742);
            if (pawnKindXenotype != null && !GoblinUtility.IsGoblinXenotype(__result.genes?.Xenotype))
            {
                __result.genes?.SetXenotype(pawnKindXenotype);
            }

            if (GoblinUtility.IsGoblin(__result)
                && __result.genes.GetGene(MUGBDefOf.MUGB_Gene_CrossEyed) == null
                && Rand.ChanceSeeded(0.25f, __result.thingIDNumber ^ 0x4D554742))
            {
                __result.genes.AddGene(MUGBDefOf.MUGB_Gene_CrossEyed, xenogene: false);
            }

            GoblinUtility.TryGiveGoblinFertileMutation(__result);
            GoblinAgeUtility.NormalizeGeneratedGoblinAge(__result);
            GoblinAgeUtility.RemovePrematureAgeHediffs(__result);
            GoblinUtility.EnforceGoblinStoryGraphics(__result);
            GoblinPersonalNameUtility.TryApplyKoreanGoblinName(__result, enforceGeneratedFormat: true);
            GoblinHunterPawnKindUtility.TryFinalizeGeneratedHunterBackstory(__result);
            GoblinPawnKindBackstoryUtility.TryFinalizeGeneratedGoblinBackstory(__result);
            GoblinPawnKindGearUtility.TryFinalizeGeneratedGoblinGear(__result);
        }

        private static XenotypeDef ForcedXenotypeForMugbPawnKind(PawnKindDef kindDef, int? seed = null)
        {
            if (kindDef == MUGBDefOf.MUGB_GoblinBareBrawler)
            {
                return MUGBDefOf.MUGB_Goblin;
            }

            if (kindDef == MUGBDefOf.MUGB_HobgoblinBareBrawler)
            {
                return MUGBDefOf.MUGB_Hobgoblin;
            }

            string defName = kindDef?.defName;
            if (!defName.NullOrEmpty() && defName.StartsWith("MUGB_GoblinKind_"))
            {
                // 한국어 의도: 새 고블린 전투 PawnKind가 개발자 생성/콜로니스트 생성 경로에서 XML xenotypeSet을 우회해
                // 인간으로 나오는 일을 막는다. 일반병은 띤고블린 중심, 지휘/정예 계열은 홉고블린 비중을 높인다.
                float hobgoblinChance = IsEliteGoblinKind(defName) ? 0.70f : 0.15f;
                bool hobgoblin = seed.HasValue
                    ? Rand.ChanceSeeded(hobgoblinChance, seed.Value)
                    : Rand.Chance(hobgoblinChance);
                return hobgoblin ? MUGBDefOf.MUGB_Hobgoblin : MUGBDefOf.MUGB_Goblin;
            }

            return null;
        }

        private static bool IsMugbGoblinKind(PawnKindDef kindDef)
        {
            string defName = kindDef?.defName;
            return !defName.NullOrEmpty() && defName.StartsWith("MUGB_GoblinKind_");
        }

        private static bool IsEliteGoblinKind(string defName)
        {
            return defName.Contains("Chief")
                || defName.Contains("Elite")
                || defName.Contains("SquadLeader")
                || defName.Contains("Handcannoneer")
                || defName.Contains("Shaman")
                || defName.Contains("HeavyBomber")
                || defName == "MUGB_GoblinKind_CultistBomber";
        }
    }

    public static class GoblinPersonalNameUtility
    {
        private static readonly string[] KoreanNickAdjectives =
        {
            "거대한", "위엄있는", "공포스러운", "호방한", "호색한", "푸른", "최후의", "최초의",
            "비명지르는", "용맹한", "기합찬", "기열찐빠의", "공포에질린", "위축된", "조이는",
            "뭉개는", "짓누르는", "피에젖은", "갈망하는", "허접한", "굶주린", "미세한", "축축한",
            "축처진", "찌릉내나는", "역겨운", "너덜너덜한", "음탕한", "음란한", "난잡한", "딱딱한",
            "부드러운", "털난", "털이수북한", "똥뭍은", "닭장냄새나는", "싱그러운", "산뜻한",
            "핑크빛", "탐하는", "묵은", "발정난", "천박한", "오돌토돌한", "우람한", "날뛰는",
            "속사의", "갈라진", "신성한", "저주받은", "불멸의", "영원한", "전설의", "무적의",
            "위대한", "존엄한", "비대한", "야윈", "뒤틀린", "짓뭉개진", "곪은", "미끌미끌한",
            "물컹한", "삐걱대는", "쉰내나는", "쿰쿰한", "매콤한", "얼얼한", "뜨끈한", "서늘한",
            "번들거리는", "흐물대는", "불타는", "얼어붙은", "녹아내리는", "부글거리는", "끓어오르는",
            "침묵하는", "울부짖는", "속삭이는", "킬킬대는", "게걸스러운", "걸신들린", "부풀어오른",
            "쪼그라붙은", "쳐진", "늘어진", "삐죽한", "뾰족한", "둥글넙적한"
        };

        private static readonly string[] KoreanNickNouns =
        {
            "사냥꾼", "인간백정", "도축업자", "돌격병", "신부님", "주교", "대장장이", "포주",
            "두개골수집가", "손가락수집가", "사랑꾼", "명사수", "난봉꾼", "목동", "인간목장주",
            "약탈자", "강탈자", "습격자", "눈알수집가", "집행자", "범죄자", "좀도둑", "노상강도",
            "난동꾼", "장난꾸러기", "남창", "기병대", "땜장이", "대서인", "서기관", "세리",
            "뚜쟁이", "거간꾼", "밀렵꾼", "도굴꾼", "넝마주이", "인신매매업자", "종놈", "마부",
            "마름", "광대", "곡예사", "약장수", "문신사", "박제사", "해부학자", "방부처리사",
            "무덤지기", "종치기", "파수꾼", "청소부", "오물수거인", "뒷간지기", "우물지기",
            "소몰이꾼", "돼지치기", "개백정", "겨드랑이", "코딱지", "사타구니", "뒷구멍", "눈곱",
            "콧구멍", "귓구멍", "배꼽", "목젖", "무릎딱지", "손톱때", "발냄새", "침샘", "뾰루지",
            "종기", "가래", "콧물", "땀구멍", "겨드랑이털", "발톱", "배불뚝이", "다리털", "전염병",
            "귀지", "항문털", "탈장", "치질", "치루", "창자루", "똥자루", "비료포대", "화장실",
            "궁뎅이", "궁둥이", "매독", "대머리", "여드름", "정강이", "복사뼈", "팔꿈치", "손등",
            "발등", "정수리", "뒤통수", "관자놀이", "새끼발가락", "발뒤꿈치", "사마귀", "무좀",
            "습진", "두드러기", "딸꾹질", "방귀", "트림", "이명", "백내장", "사시", "언청이",
            "곱슬머리", "잔털", "비듬", "부스럼", "고름", "진물", "딱지", "굳은살", "티눈",
            "눈꼽", "콧털", "귀털", "새치", "흰머리", "이빨자국", "잇몸"
        };

        private static readonly string[] KoreanNamePrefixes =
        {
            "바", "하르", "그로", "말", "도른", "케", "바르", "소", "하", "그림",
            "누르", "아이", "도르", "후투", "망구", "부얀", "오도", "자르", "투먼", "나이만"
        };

        private static readonly string[] KoreanNameSuffixes =
        {
            "두스", "락스", "문", "하르트", "소른", "그란", "문드", "락", "스타인", "돈",
            "하치", "부카", "타이", "게이", "무렌", "부루", "시린", "하라", "자이", "투르"
        };

        // 한국어 의도: 고블린은 13세로 태어나 16세에 유년기 백스토리를 받습니다.
        // 갓 태어난 개체까지 정식 이름을 붙이면 바닐라 이름짓기 창과 겹치고 어색하므로,
        // 16세 전에는 "고블린 아기" 같은 임시 이름만 달고 있다가 성숙할 때 정식 이름을 받습니다.
        public static bool IsTooYoungForRealName(Pawn pawn)
        {
            return pawn?.ageTracker != null
                && pawn.ageTracker.AgeBiologicalYearsFloat < GoblinAgeUtility.RomanceMinAgeYears;
        }

        public static void ApplyTemporaryGoblinBabyName(Pawn pawn)
        {
            if (pawn?.Name == null || !GoblinUtility.IsGoblin(pawn) || !MUGB.MGBFactionInjectionComponent.IsKoreanActive())
            {
                return;
            }

            string label = GoblinUtility.IsHobgoblin(pawn) ? "홉고블린 아기" : "고블린 아기";
            if (pawn.Name is NameTriple existing && existing.First == label)
            {
                return;
            }

            pawn.Name = new NameTriple(label, label, string.Empty);
        }

        public static void TryApplyKoreanGoblinName(Pawn pawn, bool enforceGeneratedFormat = false)
        {
            if (pawn == null
                || pawn.Name == null
                || !GoblinUtility.IsGoblin(pawn)
                || !MUGB.MGBFactionInjectionComponent.IsKoreanActive())
            {
                return;
            }

            if (IsTooYoungForRealName(pawn))
            {
                ApplyTemporaryGoblinBabyName(pawn);
                return;
            }

            string currentName = pawn.Name.ToStringFull;
            bool holdingTemporaryName = currentName != null && (currentName.Contains("고블린 아기") || currentName.Contains("홉고블린 아기"));
            if (!holdingTemporaryName && IsExpectedKoreanGoblinName(pawn.Name))
            {
                return;
            }

            if (!holdingTemporaryName && TryNormalizeKoreanGoblinDisplayName(pawn))
            {
                return;
            }

            // Preserve names deliberately assigned to an existing player pawn. Freshly generated pawns use
            // enforceGeneratedFormat=true, so Korean single names from another generator cannot leak through.
            if (!enforceGeneratedFormat
                && pawn.Faction == Faction.OfPlayerSilentFail
                && !currentName.NullOrEmpty())
            {
                return;
            }

            Rand.PushState(pawn.thingIDNumber ^ 0x6E616D65);
            try
            {
                // Nick을 비우면 바닐라가 First와 Last 중 하나를 해시로 골라 짧은 이름으로 쓴다.
                // First/Nick=A+B, Last=C로 두어 맵에서는 A+B, 전체 이름에서는 A+B C가 보이게 한다.
                string firstName = KoreanNickAdjectives.RandomElement() + " " + KoreanNickNouns.RandomElement();
                string lineName = GenerateLineName();
                pawn.Name = new NameTriple(firstName, firstName, lineName);
            }
            finally
            {
                Rand.PopState();
            }
        }

        public static bool TryNormalizeKoreanGoblinDisplayName(Pawn pawn)
        {
            if (pawn?.Name is NameTriple name
                && GoblinUtility.IsGoblin(pawn)
                && MUGB.MGBFactionInjectionComponent.IsKoreanActive()
                && !name.NickSet
                && HasExpectedKoreanGoblinNameParts(name.First, name.Last))
            {
                pawn.Name = new NameTriple(name.First, name.First, name.Last);
                return true;
            }
            return false;
        }

        private static bool IsExpectedKoreanGoblinName(Name name)
        {
            return name is NameTriple triple
                && triple.NickSet
                && triple.Nick == triple.First
                && HasExpectedKoreanGoblinNameParts(triple.First, triple.Last);
        }

        private static bool HasExpectedKoreanGoblinNameParts(string firstName, string lineName)
        {
            if (firstName.NullOrEmpty() || lineName.NullOrEmpty())
            {
                return false;
            }

            int separator = firstName.IndexOf(' ');
            if (separator <= 0 || separator >= firstName.Length - 1)
            {
                return false;
            }

            string adjective = firstName.Substring(0, separator);
            string noun = firstName.Substring(separator + 1);
            if (!KoreanNickAdjectives.Contains(adjective) || !KoreanNickNouns.Contains(noun))
            {
                return false;
            }

            for (int i = 0; i < KoreanNamePrefixes.Length; i++)
            {
                string prefix = KoreanNamePrefixes[i];
                if (!lineName.StartsWith(prefix))
                {
                    continue;
                }

                string suffix = lineName.Substring(prefix.Length);
                if (KoreanNameSuffixes.Contains(suffix))
                {
                    return true;
                }
            }

            return false;
        }

        public static void InheritGoblinLineName(Pawn child, Pawn father)
        {
            if (child == null || father == null || !GoblinUtility.IsGoblin(child) || !GoblinUtility.IsGoblin(father) || !MUGB.MGBFactionInjectionComponent.IsKoreanActive())
            {
                return;
            }

            string lineName = GetOrCreateLineName(father);
            if (lineName.NullOrEmpty())
            {
                return;
            }

            if (child.Name is NameTriple childName)
            {
                child.Name = new NameTriple(childName.First, childName.Nick, lineName);
            }
        }

        private static string GetOrCreateLineName(Pawn pawn)
        {
            if (pawn?.Name is NameTriple name && !name.Last.NullOrEmpty())
            {
                return name.Last;
            }

            Rand.PushState((pawn?.thingIDNumber ?? 0) ^ 0x6C696E65);
            try
            {
                string lineName = GenerateLineName();
                if (pawn?.Name is NameTriple existing)
                {
                    pawn.Name = new NameTriple(existing.First, existing.Nick, lineName);
                }
                return lineName;
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static string GenerateLineName()
        {
            return KoreanNamePrefixes.RandomElement() + KoreanNameSuffixes.RandomElement();
        }

    }


    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Pawn_SpawnSetup_GoblinDisplayNamePatch
    {
        public static void Postfix(Pawn __instance)
        {
            GoblinPersonalNameUtility.TryApplyKoreanGoblinName(__instance);
            GoblinHunterPawnKindUtility.TryRepairHunterCombatBackstory(__instance);
            GoblinPawnKindBackstoryUtility.TryRepairGoblinBackstory(__instance);
            if (__instance?.IsSlaveOfColony == true && GoblinUtility.IsGoblin(__instance))
            {
                // Repairs old saves and births whose slave status was assigned after needs initialized.
                __instance.needs?.AddOrRemoveNeedsAsAppropriate();
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.SetXenotype))]
    public static class PawnGeneTracker_SetXenotype_Patch
    {
        public static void Postfix(Pawn ___pawn)
        {
            GoblinUtility.TryGiveGoblinFertileMutation(___pawn);
            GoblinUtility.EnforceGoblinStoryGraphics(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.SetXenotypeDirect))]
    public static class PawnGeneTracker_SetXenotypeDirect_Patch
    {
        public static void Postfix(Pawn ___pawn)
        {
            GoblinUtility.TryGiveGoblinFertileMutation(___pawn);
            GoblinUtility.EnforceGoblinStoryGraphics(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "Notify_GenesChanged")]
    public static class PawnGeneTracker_NotifyGenesChanged_Patch
    {
        public static void Postfix(Pawn ___pawn)
        {
            // 바닐라가 유전자 피부/몸/머리 처리를 끝낸 뒤 MUGB 전용 BodyTypeDef와 HeadTypeDef를 한 번만 보정합니다.
            GoblinUtility.TryGiveGoblinFertileMutation(___pawn);
            GoblinUtility.EnforceGoblinStoryGraphics(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), "RecalculateLifeStageIndex")]
    public static class PawnAgeTracker_RecalculateLifeStageIndex_Patch
    {
        public static void Postfix(Pawn ___pawn, ref int ___cachedLifeStageIndex, ref bool ___lifeStageChange)
        {
            if (GoblinAgeUtility.TryGetGoblinLifeStageIndex(___pawn, out int desiredIndex) && ___cachedLifeStageIndex != desiredIndex)
            {
                ___cachedLifeStageIndex = desiredIndex;
                ___lifeStageChange = true;
                ___pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            GoblinUtility.EnforceGoblinStoryGraphics(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), "get_AdultMinAge")]
    public static class PawnAgeTracker_AdultMinAge_GoblinPatch
    {
        public static void Postfix(Pawn ___pawn, ref float __result)
        {
            if (GoblinUtility.IsGoblin(___pawn))
            {
                __result = GoblinAgeUtility.AdultAgeYears;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), "get_Adult")]
    public static class PawnAgeTracker_Adult_GoblinPatch
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (GoblinUtility.IsGoblin(___pawn))
            {
                __result = ___pawn.ageTracker.AgeBiologicalYearsFloat >= GoblinAgeUtility.AdultAgeYears;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), "get_BiologicalTicksPerTick")]
    public static class PawnAgeTracker_BiologicalTicksPerTick_GoblinFastGrowthPatch
    {
        public static void Postfix(Pawn ___pawn, ref float __result)
        {
            if (___pawn?.ageTracker == null)
            {
                return;
            }

            float age = ___pawn.ageTracker.AgeBiologicalYearsFloat;
            if (GoblinUtility.IsGoblin(___pawn) && age < GoblinAgeUtility.AdultAgeYears)
            {
                __result = age < GoblinAgeUtility.TeenAgeYears
                    ? GoblinAgeUtility.ChildGrowthRate
                    : age < GoblinAgeUtility.RomanceMinAgeYears
                        ? GoblinAgeUtility.TeenGrowthRate
                        : GoblinAgeUtility.LateTeenGrowthRate;
                return;
            }

            if (GoblinUtility.HasHalfGoblinAncestry(___pawn)
                && age < HalfGoblinAgeUtility.AdultAgeYearsFor(___pawn))
            {
                __result = age < HalfGoblinAgeUtility.TeenAgeYearsFor(___pawn) ? 4f : 3f;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTickInterval))]
    public static class PawnAgeTracker_AgeTick_GoblinMaturationPatch
    {
        public static void Prefix(Pawn ___pawn, out long __state)
        {
            __state = ___pawn?.ageTracker?.AgeBiologicalTicks ?? 0L;
            if (___pawn?.ageTracker == null
                || !GoblinAgeUtility.SkipChildStage
                || !GoblinUtility.IsGoblin(___pawn))
            {
                return;
            }

            long teenTicks = GoblinAgeUtility.TicksForYears(GoblinAgeUtility.TeenAgeYears);
            if (__state >= teenTicks)
            {
                return;
            }

            ___pawn.ageTracker.AgeBiologicalTicks = teenTicks;
            if (___pawn.ageTracker.AgeChronologicalTicks < teenTicks)
            {
                ___pawn.ageTracker.AgeChronologicalTicks = teenTicks;
            }
            GoblinRapidMaturationUtility.TryApplyJuvenileMaturation(___pawn);
            __state = teenTicks;
        }

        public static void Postfix(Pawn ___pawn, int delta, long __state)
        {
            if (!GoblinUtility.IsGoblin(___pawn) || ___pawn?.ageTracker == null)
            {
                return;
            }

            long ageAfter = ___pawn.ageTracker.AgeBiologicalTicks;
            if (GenTicks.IsTickIntervalDelta(___pawn.thingIDNumber, 250, delta)
                && ___pawn.ageTracker.AgeChronologicalTicks < ageAfter)
            {
                ___pawn.ageTracker.AgeChronologicalTicks = ageAfter;
            }

            long teenTicks = GoblinAgeUtility.TicksForYears(GoblinAgeUtility.TeenAgeYears);
            long romanceTicks = GoblinAgeUtility.TicksForYears(GoblinAgeUtility.RomanceMinAgeYears);
            long adultTicks = GoblinAgeUtility.TicksForYears(GoblinAgeUtility.AdultAgeYears);
            if (__state < teenTicks && ageAfter >= teenTicks)
            {
                GoblinRapidMaturationUtility.TryApplyJuvenileMaturation(___pawn);
            }
            if (__state < romanceTicks && ageAfter >= romanceTicks)
            {
                GoblinRapidMaturationUtility.TryApplyTeenMaturation(___pawn);
            }
            if (__state < adultTicks && ageAfter >= adultTicks)
            {
                GoblinRapidMaturationUtility.TryApplyAdultMaturation(___pawn);
            }
        }
    }

    public static class GoblinAgeUtility
    {
        public const float BirthAgeYears = 3f;
        public const float TeenAgeYears = 13f;
        private const float DefaultChildStageDays = 3.5f;
        private const float ChildGrowthRateNumerator = 600f;
        private static float childGrowthRate = ChildGrowthRateNumerator / DefaultChildStageDays;
        private static bool skipChildStage;
        public const float TeenGrowthRate = 90f;
        public const float LateTeenGrowthRate = 30f;
        // 한국어 참고: 고블린 청소년 렌더 축소 구간입니다. 13~16세는 더 작게, 16~18세는 성인에 가깝게 그립니다.
        public const float JuvenileMinAgeYears = 13f;
        public const float JuvenileLateAgeYears = 16f;
        public const float RomanceMinAgeYears = 16f;
        public const float AdultAgeYears = 18f;
        public const float ElderAgeYears = 30f;
        public const float LifeExpectancyYears = 40f;
        private const long AgeTicksPerYear = 3600000L;
        public static float ChildGrowthRate => childGrowthRate;
        public static bool SkipChildStage => skipChildStage;

        public static float NormalizeChildStageDays(float days)
        {
            if (days < 0.5f)
            {
                return 0f;
            }
            if (days < 1.5f)
            {
                return 1f;
            }
            if (days < 2.75f)
            {
                return 2f;
            }
            return DefaultChildStageDays;
        }

        public static float NextChildStageDays(float currentDays)
        {
            float normalized = NormalizeChildStageDays(currentDays);
            if (normalized < 0.5f)
            {
                return 1f;
            }
            if (normalized < 1.5f)
            {
                return 2f;
            }
            if (normalized < 2.75f)
            {
                return DefaultChildStageDays;
            }
            return 0f;
        }

        public static void RefreshChildStageSettings()
        {
            float days = NormalizeChildStageDays(MUGBMod.Settings?.goblinChildStageDays ?? DefaultChildStageDays);
            skipChildStage = days <= 0f;
            childGrowthRate = skipChildStage ? 1f : ChildGrowthRateNumerator / days;
        }

        private static readonly HashSet<string> AgeHediffDefNames = new HashSet<string>
        {
            "BadBack",
            "Frail",
            "Cataract",
            "Blindness",
            "HearingLoss",
            "Dementia",
            "Alzheimers",
            "HeartArteryBlockage",
            "Carcinoma"
        };

        public static long TicksForYears(float years)
        {
            return (long)(years * AgeTicksPerYear);
        }

        public static void NormalizeGeneratedGoblinAge(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || pawn.ageTracker == null)
            {
                return;
            }

            // Newborn generation is finalized by GoblinBirthUtility after this postfix.
            // Combat PawnKinds may intentionally generate at ages 13-17; only adult goblins
            // enter the short-lived adult population curve below.
            if (pawn.ageTracker.AgeBiologicalYearsFloat < AdultAgeYears)
            {
                return;
            }

            // 한국어 의도: 고블린은 빠르게 성장하지만 기대수명이 짧습니다.
            // 생성 시 바닐라 인간 나이 커브가 섞여 고령 고블린이 흔하게 나오는 일을 막고, 18~30세 청장년 중심으로 보정합니다.
            int seed = pawn.thingIDNumber ^ 0x4D554742;
            float roll = Rand.ValueSeeded(seed);
            float newBiologicalYears;
            if (roll < 0.60f)
            {
                newBiologicalYears = Rand.RangeSeeded(AdultAgeYears, 24f, seed ^ 0x1111);
            }
            else if (roll < 0.90f)
            {
                newBiologicalYears = Rand.RangeSeeded(24f, ElderAgeYears, seed ^ 0x2222);
            }
            else if (roll < 0.98f)
            {
                newBiologicalYears = Rand.RangeSeeded(ElderAgeYears, 36f, seed ^ 0x3333);
            }
            else
            {
                newBiologicalYears = Rand.RangeSeeded(36f, LifeExpectancyYears - 0.25f, seed ^ 0x4444);
            }

            float extraChronologicalYears = Rand.RangeSeeded(0f, 4f, seed ^ 0x424C494E);
            float newChronologicalYears = newBiologicalYears + extraChronologicalYears;
            if (newChronologicalYears > LifeExpectancyYears - 0.25f)
            {
                newChronologicalYears = LifeExpectancyYears - 0.25f;
            }
            pawn.ageTracker.AgeBiologicalTicks = TicksForYears(newBiologicalYears);
            pawn.ageTracker.AgeChronologicalTicks = TicksForYears(newChronologicalYears);
        }

        public static void RemovePrematureAgeHediffs(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn)
                || pawn.ageTracker == null
                || pawn.health?.hediffSet?.hediffs == null
                || pawn.ageTracker.AgeBiologicalYearsFloat >= ElderAgeYears)
            {
                return;
            }

            // 한국어 의도: 고블린은 30세부터 노년권입니다.
            // PawnGenerator가 인간 나이 곡선으로 먼저 붙인 백내장/노쇠/동맥경화 같은 노화 헤디프는,
            // 고블린 나이를 4~20대로 보정한 뒤 반드시 제거합니다.
            List<Hediff> toRemove = pawn.health.hediffSet.hediffs
                .Where(hediff => hediff?.def != null && AgeHediffDefNames.Contains(hediff.def.defName))
                .ToList();

            for (int i = 0; i < toRemove.Count; i++)
            {
                pawn.health.RemoveHediff(toRemove[i]);
            }
        }

        public static bool TryGetGoblinLifeStageIndex(Pawn pawn, out int index)
        {
            index = -1;
            if (!GoblinUtility.IsGoblin(pawn) || pawn?.RaceProps?.lifeStageAges == null || pawn.RaceProps.lifeStageAges.Count == 0)
            {
                return false;
            }

            string preferredDefName;
            float age = pawn.ageTracker.AgeBiologicalYearsFloat;
            if (age < TeenAgeYears)
            {
                preferredDefName = "HumanlikeChild";
            }
            else if (age < AdultAgeYears)
            {
                preferredDefName = "HumanlikeTeenager";
            }
            else
            {
                preferredDefName = "HumanlikeAdult";
            }

            index = pawn.RaceProps.lifeStageAges.FindIndex(stage => stage?.def?.defName == preferredDefName);
            if (index >= 0)
            {
                return true;
            }

            DevelopmentalStage fallbackStage = age < AdultAgeYears ? DevelopmentalStage.Child : DevelopmentalStage.Adult;
            index = pawn.RaceProps.lifeStageAges.FindIndex(stage => stage?.def?.developmentalStage == fallbackStage);
            return index >= 0;
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.TryChildGrowthMoment))]
    public static class PawnAgeTracker_TryChildGrowthMoment_GoblinPatch
    {
        public static bool Prefix(
            Pawn ___pawn,
            int birthdayAge,
            ref int newPassionOptions,
            ref int newTraitOptions,
            ref int passionGainsCount)
        {
            if (!GoblinUtility.IsGoblin(___pawn) || birthdayAge > 13)
            {
                return true;
            }

            newPassionOptions = 0;
            newTraitOptions = 0;
            passionGainsCount = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(GrowthUtility), nameof(GrowthUtility.GrowthFlavorForTier))]
    public static class GrowthUtility_GrowthFlavorForTier_GoblinPatch
    {
        private static readonly string[] StoryKeys =
        {
            "MUGB_GoblinGrowthStory01",
            "MUGB_GoblinGrowthStory02",
            "MUGB_GoblinGrowthStory03",
            "MUGB_GoblinGrowthStory04",
            "MUGB_GoblinGrowthStory05",
            "MUGB_GoblinGrowthStory06",
            "MUGB_GoblinGrowthStory07",
            "MUGB_GoblinGrowthStory08",
            "MUGB_GoblinGrowthStory09",
            "MUGB_GoblinGrowthStory10",
            "MUGB_GoblinGrowthStory11",
            "MUGB_GoblinGrowthStory12",
            "MUGB_GoblinGrowthStory13",
            "MUGB_GoblinGrowthStory14",
            "MUGB_GoblinGrowthStory15",
            "MUGB_GoblinGrowthStory16"
        };

        public static void Postfix(Pawn pawn, ref string __result)
        {
            if (pawn == null
                || !GoblinUtility.IsGoblin(pawn)
                || pawn.ageTracker.AgeBiologicalYears != GoblinAgeUtility.TeenAgeYears)
            {
                return;
            }

            string key = StoryKeys[Rand.Range(0, StoryKeys.Length)];
            __result = key.Translate(pawn.Named("PAWN"));
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new[]
    {
        typeof(Letter),
        typeof(string),
        typeof(int),
        typeof(bool)
    })]
    public static class LetterStack_ReceiveLetter_GoblinGrowthMomentPatch
    {
        public static bool Prefix(Letter let)
        {
            return !IsSuppressedGoblinGrowthMoment(let as ChoiceLetter_GrowthMoment);
        }

        internal static bool IsSuppressedGoblinGrowthMoment(ChoiceLetter_GrowthMoment letter)
        {
            Pawn pawn = letter?.pawn;
            return pawn != null
                && GoblinUtility.IsGoblin(pawn)
                && pawn.ageTracker.AgeBiologicalYears < GoblinAgeUtility.TeenAgeYears;
        }
    }

    [HarmonyPatch(typeof(ChoiceLetter_GrowthMoment), nameof(ChoiceLetter_GrowthMoment.CanShowInLetterStack), MethodType.Getter)]
    public static class ChoiceLetter_GrowthMoment_CanShowInLetterStack_GoblinPatch
    {
        public static void Postfix(ChoiceLetter_GrowthMoment __instance, ref bool __result)
        {
            if (__result && LetterStack_ReceiveLetter_GoblinGrowthMomentPatch.IsSuppressedGoblinGrowthMoment(__instance))
            {
                __result = false;
            }
        }
    }

    public static class GoblinHunterPawnKindUtility
    {
        private static readonly string[] HunterChildhoods =
        {
            "MUGB_Backstory_HumanChild_SavedSoul",
            "MUGB_Backstory_HumanChild_HalfGoblin",
            "MUGB_Backstory_HumanChild_GoblinSPlaything"
        };

        private static readonly string[] HunterAdults =
        {
            "MUGB_Backstory_Adult_GoblinHunter",
            "MUGB_Backstory_Adult_GoblinRaidSurvivor",
            "MUGB_Backstory_Adult_GoblinExterminator",
            "MUGB_Backstory_HumanAdult_GoblinPunitiveSoldier",
            "MUGB_Backstory_HumanAdult_GoblinSubjugator",
            "MUGB_Backstory_HumanAdult_GoblinHunter2"
        };

        private static readonly string[] LeaderAdults =
        {
            "MUGB_Backstory_HumanAdult_GoblinSubjugationCaptain",
            "MUGB_Backstory_HumanAdult_GoblinSlayer",
            "MUGB_Backstory_Adult_GoblinExterminator"
        };

        public static void TryFinalizeGeneratedHunterBackstory(Pawn pawn)
        {
            string kindName = pawn?.kindDef?.defName;
            if (kindName.NullOrEmpty() || !kindName.StartsWith("MUGB_HumanKind_Goblin") || pawn.story == null)
            {
                return;
            }

            Rand.PushState(pawn.thingIDNumber ^ 0x48554E54);
            try
            {
                // Korean source intent: 고블린 사냥꾼단 PawnKind는 종족은 비고블린으로 유지하되,
                // 고블린에게 가족을 잃었거나 토벌대에 들어간 백스토리 분위기를 확실히 준다.
                BackstoryDef childhood = PickBackstory(HunterChildhoods);
                if (childhood != null)
                {
                    pawn.story.Childhood = childhood;
                }

                BackstoryDef adulthood = PickBackstory(kindName.Contains("Boss") || kindName.Contains("Captain") ? LeaderAdults : HunterAdults);
                if (adulthood != null)
                {
                    pawn.story.Adulthood = adulthood;
                }

                ReinforceHunterSkills(pawn, kindName);
                TryRepairHunterCombatBackstory(pawn);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static BackstoryDef PickBackstory(string[] defNames)
        {
            List<BackstoryDef> candidates = new List<BackstoryDef>();
            for (int i = 0; i < defNames.Length; i++)
            {
                BackstoryDef backstory = DefDatabase<BackstoryDef>.GetNamedSilentFail(defNames[i]);
                if (backstory != null && (backstory.workDisables & WorkTags.Violent) == 0)
                {
                    candidates.Add(backstory);
                }
            }

            return candidates.TryRandomElement(out BackstoryDef result) ? result : null;
        }

        public static void TryRepairHunterCombatBackstory(Pawn pawn)
        {
            string kindName = pawn?.kindDef?.defName;
            if (kindName.NullOrEmpty()
                || !kindName.StartsWith("MUGB_HumanKind_Goblin")
                || pawn.story == null
                || !pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return;
            }

            bool changed = false;
            if ((pawn.story.Childhood?.workDisables & WorkTags.Violent) != 0)
            {
                BackstoryDef childhood = PickBackstory(HunterChildhoods);
                if (childhood != null)
                {
                    pawn.story.Childhood = childhood;
                    changed = true;
                }
            }

            string[] adultPool = kindName.Contains("Boss") || kindName.Contains("Captain") ? LeaderAdults : HunterAdults;
            if ((pawn.story.Adulthood?.workDisables & WorkTags.Violent) != 0)
            {
                BackstoryDef adulthood = PickBackstory(adultPool);
                if (adulthood != null)
                {
                    pawn.story.Adulthood = adulthood;
                    changed = true;
                }
            }

            if (changed)
            {
                pawn.Notify_DisabledWorkTypesChanged();
            }
        }

        private static void ReinforceHunterSkills(Pawn pawn, string kindName)
        {
            if (pawn.skills == null)
            {
                return;
            }

            if (kindName.Contains("Boss") || kindName.Contains("Captain"))
            {
                EnsureSkillAtLeast(pawn, "Shooting", 9);
                EnsureSkillAtLeast(pawn, "Melee", 7);
                EnsureSkillAtLeast(pawn, "Social", 6);
                return;
            }

            if (kindName.Contains("Infantry") || kindName.Contains("Captor"))
            {
                EnsureSkillAtLeast(pawn, "Melee", 5);
            }

            EnsureSkillAtLeast(pawn, "Shooting", kindName.Contains("Crossbowman") ? 5 : 6);
        }

        private static void EnsureSkillAtLeast(Pawn pawn, string skillDefName, int level)
        {
            SkillDef skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            SkillRecord skill = skillDef == null ? null : pawn.skills.GetSkill(skillDef);
            if (skill != null && skill.Level < level)
            {
                skill.Level = level;
            }
        }
    }

    public static class GoblinPawnKindBackstoryUtility
    {
        public const string GrowingGoblinChildhoodDefName = "MUGB_Backstory_Child_GrowingGoblin";

        private static readonly string[] DefaultChildhood =
        {
            "MUGB_Backstory_Child_TribeBorn",
            "MUGB_Backstory_Child_PitBorn",
            "MUGB_Backstory_Child_YoungRaider",
            "MUGB_Backstory_Child_CauldronSideRunt",
            "MUGB_Backstory_Child_SlaughterhouseAssistant"
        };

        private static readonly string[] SlaveChildhood =
        {
            "MUGB_Backstory_Child_ChildSlave",
            "MUGB_Backstory_Child_PitBorn",
            "MUGB_Backstory_Child_ArrowBait"
        };

        private static readonly string[] MiningChildhood =
        {
            "MUGB_Backstory_Child_QuarryPup",
            "MUGB_Backstory_Child_PitBorn",
            "MUGB_Backstory_Child_ChildSlave"
        };

        private static readonly string[] ShamanChildhood =
        {
            "MUGB_Backstory_Child_ShamansAide",
            "MUGB_Backstory_Child_CauldronSideRunt",
            "MUGB_Backstory_Child_SlaughterhouseAssistant"
        };

        private static readonly string[] MeleeAdults =
        {
            "MUGB_Backstory_Adult_GoblinLightInfantry",
            "MUGB_Backstory_Adult_GoblinMeatShield",
            "MUGB_Backstory_Adult_StabStabGoblin",
            "MUGB_Backstory_Adult_LaughsWhileRunning"
        };

        private static readonly string[] SwordAdults =
        {
            "MUGB_Backstory_Adult_GoblinSwordsman",
            "MUGB_Backstory_Adult_GoblinLightInfantry",
            "MUGB_Backstory_Adult_StabStabGoblin"
        };

        private static readonly string[] SpearAdults =
        {
            "MUGB_Backstory_Adult_GoblinSpearman",
            "MUGB_Backstory_Adult_StabStabGoblin",
            "MUGB_Backstory_Adult_RaidingVanguard"
        };

        private static readonly string[] RangedAdults =
        {
            "MUGB_Backstory_Adult_CrossbowShooter",
            "MUGB_Backstory_Adult_HumanHunter",
            "MUGB_Backstory_Adult_SmellsBloodFirst"
        };

        private static readonly string[] GunnerAdults =
        {
            "MUGB_Backstory_Adult_GoblinMusketeer",
            "MUGB_Backstory_Adult_FirearmTinkerer",
            "MUGB_Backstory_Adult_GoblinPowderMiner"
        };

        private static readonly string[] HeavyAdults =
        {
            "MUGB_Backstory_Adult_GoblinHeavyInfantry",
            "MUGB_Backstory_Adult_RaidingVanguard",
            "MUGB_Backstory_Adult_GoblinSwordsman"
        };

        private static readonly string[] LeaderAdults =
        {
            "MUGB_Backstory_Adult_RaidSquadLeader",
            "MUGB_Backstory_Adult_GoblinHeavyInfantry",
            "MUGB_Backstory_Adult_GoblinSwordsman"
        };

        private static readonly string[] ShamanAdults =
        {
            "MUGB_Backstory_Adult_SkinMaskShaman",
            "MUGB_Backstory_Adult_HumanMimicker",
            "MUGB_Backstory_Adult_BeaconPilgrim"
        };

        private static readonly string[] CultistAdults =
        {
            "MUGB_Backstory_Adult_HumanMimicker",
            "MUGB_Backstory_Adult_BeaconPilgrim",
            "MUGB_Backstory_Adult_SkinMaskShaman",
            "MUGB_Backstory_Adult_StabStabGoblin"
        };

        private static readonly string[] CaptorAdults =
        {
            "MUGB_Backstory_Adult_GoblinKidnapper",
            "MUGB_Backstory_Adult_HumanHunter",
            "MUGB_Backstory_Adult_SmellsBloodFirst"
        };

        private static readonly string[] SlaveAdults =
        {
            "MUGB_Backstory_Adult_GoblinSlaveSoldier",
            "MUGB_Backstory_Adult_TunnelConstructionSlave",
            "MUGB_Backstory_Adult_GoblinMeatShield"
        };

        private static readonly string[] TunnelAdults =
        {
            "MUGB_Backstory_Adult_TunnelConstructionSlave",
            "MUGB_Backstory_Adult_GoblinPowderMiner",
            "MUGB_Backstory_Adult_GoblinHeavyInfantry"
        };

        public static void TryFinalizeGeneratedGoblinBackstory(Pawn pawn)
        {
            if (pawn?.story == null
                || !GoblinUtility.IsGoblin(pawn)
                || pawn.DevelopmentalStage.Newborn()
                || pawn.DevelopmentalStage.Baby())
            {
                return;
            }

            string kindName = pawn.kindDef?.defName;
            if (kindName.NullOrEmpty() || !kindName.StartsWith("MUGB_GoblinKind_"))
            {
                kindName = "MUGB_GoblinKind_Basic";
            }

            Rand.PushState(pawn.thingIDNumber ^ 0x6261636B);
            try
            {
                if (pawn.ageTracker?.AgeBiologicalYearsFloat < GoblinAgeUtility.RomanceMinAgeYears)
                {
                    AssignGrowingGoblinChildhood(pawn);
                    pawn.Notify_DisabledWorkTypesChanged();
                    return;
                }

                // Korean source intent: 고블린 PawnKind가 바닐라/랜덤 백스토리만 들고 나와 병종성이 흐려지는 일을 막는다.
                // 예: 창병 PawnKind는 "고블린 창병" 계열 백스토리 확률이 높아야 한다.
                BackstoryDef childhood = PickBackstory(ChildhoodPoolFor(kindName), kindName, childhood: true, pawn);
                if (childhood != null)
                {
                    pawn.story.Childhood = childhood;
                }

                if (pawn.ageTracker?.AgeBiologicalYearsFloat >= GoblinAgeUtility.AdultAgeYears)
                {
                    BackstoryDef adulthood = PickBackstory(AdultPoolFor(kindName), kindName, childhood: false, pawn);
                    if (adulthood != null)
                    {
                        pawn.story.Adulthood = adulthood;
                    }
                }
                else
                {
                    pawn.story.Adulthood = null;
                }

                ReinforceRoleSkills(pawn, kindName);
                pawn.Notify_DisabledWorkTypesChanged();
            }
            finally
            {
                Rand.PopState();
            }
        }

        public static void AssignGrowingGoblinChildhood(Pawn pawn)
        {
            if (pawn?.story == null)
            {
                return;
            }

            BackstoryDef placeholder = DefDatabase<BackstoryDef>.GetNamedSilentFail(GrowingGoblinChildhoodDefName);
            if (placeholder != null)
            {
                pawn.story.Childhood = placeholder;
            }
            pawn.story.Adulthood = null;
        }

        public static void AssignMatureGoblinChildhood(Pawn pawn)
        {
            if (pawn?.story == null)
            {
                return;
            }

            Rand.PushState(pawn.thingIDNumber ^ 0x4348494C);
            try
            {
                BackstoryDef childhood = PickBackstory(DefaultChildhood, "MUGB_GoblinKind_Basic", childhood: true, pawn);
                if (childhood != null && childhood.defName != GrowingGoblinChildhoodDefName)
                {
                    pawn.story.Childhood = childhood;
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        public static void AssignMatureGoblinAdulthood(Pawn pawn)
        {
            if (pawn?.story == null)
            {
                return;
            }

            Rand.PushState(pawn.thingIDNumber ^ 0x4144554C);
            try
            {
                BackstoryDef adulthood = PickBackstory(MeleeAdults, "MUGB_GoblinKind_Basic", childhood: false, pawn);
                if (adulthood != null)
                {
                    pawn.story.Adulthood = adulthood;
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static string[] ChildhoodPoolFor(string kindName)
        {
            if (kindName.Contains("Slave") || kindName.Contains("BomberNaked"))
            {
                return SlaveChildhood;
            }

            if (kindName.Contains("Tunnel"))
            {
                return MiningChildhood;
            }

            if (kindName.Contains("Shaman") || kindName.Contains("Cultist"))
            {
                return ShamanChildhood;
            }

            return DefaultChildhood;
        }

        private static string[] AdultPoolFor(string kindName)
        {
            if (kindName.Contains("Slave") || kindName.Contains("BomberNaked"))
            {
                return SlaveAdults;
            }

            if (kindName.Contains("Tunnel"))
            {
                return TunnelAdults;
            }

            if (kindName.Contains("Shaman"))
            {
                return ShamanAdults;
            }

            if (kindName.Contains("Cultist"))
            {
                return CultistAdults;
            }

            if (kindName.Contains("Captor"))
            {
                return CaptorAdults;
            }

            if (kindName.Contains("SquadLeader") || kindName.Contains("Chief") || kindName.Contains("CaptorLeader"))
            {
                return LeaderAdults;
            }

            if (kindName.Contains("Spear"))
            {
                return SpearAdults;
            }

            if (kindName.Contains("Gunner") || kindName.Contains("Handcannoneer"))
            {
                return GunnerAdults;
            }

            if (kindName.Contains("Ranged") || kindName.Contains("Marksman") || kindName.Contains("Archer"))
            {
                return RangedAdults;
            }

            if (kindName.Contains("Elite") || kindName.Contains("Shock") || kindName.Contains("Heavy"))
            {
                return HeavyAdults;
            }

            if (kindName.Contains("HighSoldier") || kindName.Contains("Machete"))
            {
                return SwordAdults;
            }

            return MeleeAdults;
        }

        public static void TryRepairGoblinBackstory(Pawn pawn)
        {
            if (pawn?.story == null || !GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            float age = pawn.ageTracker?.AgeBiologicalYearsFloat ?? GoblinAgeUtility.AdultAgeYears;
            string kindName = pawn.kindDef?.defName;
            if (kindName.NullOrEmpty() || !kindName.StartsWith("MUGB_GoblinKind_"))
            {
                kindName = "MUGB_GoblinKind_Basic";
            }

            bool changed = false;
            Rand.PushState(pawn.thingIDNumber ^ 0x52455041);
            try
            {
                if (age < GoblinAgeUtility.RomanceMinAgeYears)
                {
                    if (pawn.story.Childhood?.defName != GrowingGoblinChildhoodDefName || pawn.story.Adulthood != null)
                    {
                        AssignGrowingGoblinChildhood(pawn);
                        changed = true;
                    }
                }
                else
                {
                    if (!IsValidGoblinBackstory(pawn.story.Childhood, childhood: true, pawn))
                    {
                        BackstoryDef childhood = PickBackstory(ChildhoodPoolFor(kindName), kindName, childhood: true, pawn);
                        if (childhood != null)
                        {
                            pawn.story.Childhood = childhood;
                            changed = true;
                        }
                    }

                    if (age < GoblinAgeUtility.AdultAgeYears)
                    {
                        if (pawn.story.Adulthood != null)
                        {
                            pawn.story.Adulthood = null;
                            changed = true;
                        }
                    }
                    else if (!IsValidGoblinBackstory(pawn.story.Adulthood, childhood: false, pawn))
                    {
                        BackstoryDef adulthood = PickBackstory(AdultPoolFor(kindName), kindName, childhood: false, pawn);
                        if (adulthood != null)
                        {
                            pawn.story.Adulthood = adulthood;
                            changed = true;
                        }
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }

            if (changed)
            {
                pawn.Notify_DisabledWorkTypesChanged();
            }
        }

        private static BackstoryDef PickBackstory(string[] defNames, string kindName, bool childhood, Pawn pawn)
        {
            List<BackstoryDef> candidates = new List<BackstoryDef>();
            for (int i = 0; i < defNames.Length; i++)
            {
                BackstoryDef backstory = DefDatabase<BackstoryDef>.GetNamedSilentFail(defNames[i]);
                if (IsValidGoblinBackstory(backstory, childhood, pawn))
                {
                    candidates.Add(backstory);
                }
            }

            AddExpandedBackstoryCandidates(candidates, kindName, childhood, pawn);
            return candidates.TryRandomElement(out BackstoryDef result) ? result : null;
        }

        private static void AddExpandedBackstoryCandidates(List<BackstoryDef> candidates, string kindName, bool childhood, Pawn pawn)
        {
            string requiredPrefix = childhood ? "MUGB_Backstory_Child_" : "MUGB_Backstory_Adult_";
            List<BackstoryDef> allBackstories = DefDatabase<BackstoryDef>.AllDefsListForReading;
            for (int i = 0; i < allBackstories.Count; i++)
            {
                BackstoryDef backstory = allBackstories[i];
                string defName = backstory?.defName;
                if (defName.NullOrEmpty()
                    || defName == GrowingGoblinChildhoodDefName
                    || !defName.StartsWith(requiredPrefix)
                    || candidates.Contains(backstory))
                {
                    continue;
                }

                if (IsValidGoblinBackstory(backstory, childhood, pawn)
                    && ExpandedBackstoryMatchesKind(defName, kindName, childhood))
                {
                    candidates.Add(backstory);
                }
            }
        }

        private static bool IsValidGoblinBackstory(BackstoryDef backstory, bool childhood, Pawn pawn)
        {
            string prefix = childhood ? "MUGB_Backstory_Child_" : "MUGB_Backstory_Adult_";
            if (backstory == null || backstory.defName.NullOrEmpty() || !backstory.defName.StartsWith(prefix))
            {
                return false;
            }

            WorkTags required = pawn?.kindDef?.requiredWorkTags ?? WorkTags.None;
            return (backstory.workDisables & required) == WorkTags.None;
        }

        private static bool ExpandedBackstoryMatchesKind(string defName, string kindName, bool childhood)
        {
            if (childhood)
            {
                if (kindName.Contains("Slave") || kindName.Contains("BomberNaked"))
                {
                    return ContainsAny(defName, "Slave", "Pit", "Arrow", "PunchingBag", "ScheduledForThePot", "TaggedMeat", "WastePit");
                }

                if (kindName.Contains("Tunnel"))
                {
                    return ContainsAny(defName, "Quarry", "Crevice", "Cave", "Sewer", "Stone", "Damp");
                }

                if (kindName.Contains("Shaman") || kindName.Contains("Cultist"))
                {
                    return ContainsAny(defName, "Shaman", "Mask", "Beacon", "Ceremony", "Pilgrim", "Fireside", "Ash", "Sacrifice");
                }

                return !ContainsAny(defName, "HumanChild");
            }

            if (kindName.Contains("Slave") || kindName.Contains("BomberNaked"))
            {
                return ContainsAny(defName, "Slave", "MeatShield", "Tunnel", "Bomb", "Pit", "Fodder");
            }

            if (kindName.Contains("Tunnel"))
            {
                return ContainsAny(defName, "Tunnel", "Shaft", "Miner", "Mining", "Digger", "Quarry", "Cave");
            }

            if (kindName.Contains("Shaman"))
            {
                return ContainsAny(defName, "Shaman", "Mimicker", "Beacon", "Ceremony", "Pilgrim", "Priest", "Prophet", "Mask");
            }

            if (kindName.Contains("Cultist"))
            {
                return ContainsAny(defName, "Mimicker", "Beacon", "Shaman", "Skin", "Ceremony", "Pilgrim", "Bride", "Sacrifice");
            }

            if (kindName.Contains("Captor"))
            {
                return ContainsAny(defName, "Kidnap", "Hunter", "Bride", "Captor", "Livestock", "Keeper", "SlaveMarket");
            }

            if (kindName.Contains("SquadLeader") || kindName.Contains("Chief"))
            {
                return ContainsAny(defName, "Leader", "Chief", "Captain", "Caesar", "Commander", "Manager");
            }

            if (kindName.Contains("Spear"))
            {
                return ContainsAny(defName, "Spear", "Vanguard", "Phalanx", "Pike");
            }

            if (kindName.Contains("Gunner") || kindName.Contains("Handcannoneer"))
            {
                return ContainsAny(defName, "Musketeer", "Firearm", "Powder", "Gunner", "Bomb", "Handcannon");
            }

            if (kindName.Contains("Ranged") || kindName.Contains("Marksman") || kindName.Contains("Archer"))
            {
                return ContainsAny(defName, "Crossbow", "Hunter", "Bow", "Shooter", "Marksman", "Sling", "Thrower");
            }

            if (kindName.Contains("Elite") || kindName.Contains("Shock") || kindName.Contains("Heavy"))
            {
                return ContainsAny(defName, "Heavy", "Vanguard", "Shock", "Swordsman", "Shield", "Machete");
            }

            if (kindName.Contains("HighSoldier") || kindName.Contains("Machete"))
            {
                return ContainsAny(defName, "Swordsman", "Blade", "Cleaver", "Machete", "Stab");
            }

            return ContainsAny(defName, "Infantry", "Raider", "Stab", "Running", "MeatShield", "Swordsman");
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReinforceRoleSkills(Pawn pawn, string kindName)
        {
            if (pawn.skills == null)
            {
                return;
            }

            // Korean source intent: 생성 후 백스토리 교체는 바닐라 생성 중 skillGains 적용 시점을 이미 지난다.
            // 그래서 병종이 말이 되도록 최소 스킬값만 보정한다. 과도한 보너스가 아니라 "창병이 창 못 쓰는" 상황 방지용이다.
            if (kindName.Contains("SquadLeader") || kindName.Contains("Chief"))
            {
                bool rangedLeader = kindName.Contains("Ranged")
                    || kindName.Contains("Archer")
                    || kindName.Contains("Gunner")
                    || kindName.Contains("Gunpowder")
                    || kindName.Contains("Handcannoneer");
                EnsureSkillAtLeast(pawn, rangedLeader ? "Shooting" : "Melee", 6);
                EnsureSkillAtLeast(pawn, "Social", 4);
                return;
            }

            if (kindName.Contains("Shaman"))
            {
                EnsureSkillAtLeast(pawn, "Intellectual", 5);
                EnsureSkillAtLeast(pawn, "Social", 4);
                return;
            }

            if (kindName.Contains("Spear") || kindName.Contains("Elite") || kindName.Contains("Shock") || kindName.Contains("Heavy"))
            {
                EnsureSkillAtLeast(pawn, "Melee", 5);
            }
            else if (kindName.Contains("Ranged") || kindName.Contains("Marksman") || kindName.Contains("Archer"))
            {
                EnsureSkillAtLeast(pawn, "Shooting", 5);
            }
            else if (kindName.Contains("Gunner") || kindName.Contains("Handcannoneer"))
            {
                EnsureSkillAtLeast(pawn, "Shooting", 5);
                EnsureSkillAtLeast(pawn, "Crafting", 2);
            }
            else if (kindName.Contains("Captor"))
            {
                EnsureSkillAtLeast(pawn, "Shooting", 3);
                EnsureSkillAtLeast(pawn, "Melee", 2);
            }
            else if (kindName.Contains("Tunnel"))
            {
                EnsureSkillAtLeast(pawn, "Mining", 4);
                EnsureSkillAtLeast(pawn, "Construction", 3);
            }
            else if (!kindName.Contains("Slave") && !kindName.Contains("Beggar"))
            {
                EnsureSkillAtLeast(pawn, "Melee", 3);
            }
        }

        private static void EnsureSkillAtLeast(Pawn pawn, string skillDefName, int level)
        {
            SkillDef skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
            SkillRecord skill = skillDef == null ? null : pawn.skills.GetSkill(skillDef);
            if (skill != null && skill.Level < level)
            {
                skill.Level = level;
            }
        }
    }

    public static class GoblinPawnKindGearUtility
    {
        private static readonly string[] BasicClothing =
        {
            "MUGB_Apparel_GoblinLoincloth",
            "MUGB_Apparel_GoblinBodyWrap"
        };

        private static readonly string[] ColdWeatherCloaks =
        {
            "MUGB_Apparel_HumanHideMantle",
            "MUGB_Apparel_HumanHideCapeB"
        };

        private static readonly string[] PrimitiveArmor =
        {
            "MUGB_Apparel_WoodArmorA",
            "MUGB_Apparel_WoodArmorB"
        };

        private static readonly string[] PrimitiveHelmet =
        {
            "MUGB_Apparel_BoneMaskA",
            "MUGB_Apparel_BoneMaskB",
            "MUGB_Apparel_BoneMaskC"
        };

        private static readonly string[] MedievalArmor =
        {
            "MUGB_Apparel_GoblinChainArmor",
            "MUGB_Apparel_GoblinChestplate",
            "MUGB_Apparel_GoblinScaleArmor",
            "MUGB_Apparel_GoblinTorsoArmorA"
        };

        private static readonly string[] MedievalHelmet =
        {
            "MUGB_Apparel_BirdHelmetA",
            "MUGB_Apparel_BirdHelmetB",
            "MUGB_Apparel_KettleHelmetA",
            "MUGB_Apparel_KettleHelmetB",
            "MUGB_Apparel_KettleHelmetC"
        };

        private static readonly string[] EliteArmor =
        {
            "MUGB_Apparel_GoblinPlateArmorA",
            "MUGB_Apparel_GoblinPlateArmorB",
            "MUGB_Apparel_GoblinTorsoArmorB"
        };

        private static readonly string[] EliteHelmet =
        {
            "MUGB_Apparel_HammerHelmet",
            "MUGB_Apparel_WarHelmetA",
            "MUGB_Apparel_WarHelmetB"
        };

        private const string CultistSkinMask = "MUGB_Apparel_CultSkinMask";

        private static readonly string[] CultistAlternateHeadgear =
        {
            "Apparel_Headwrap",
            "Apparel_WarMask",
            "Apparel_WarVeil"
        };

        private static readonly string[] TunnelVanguardHelmet =
        {
            "MUGB_Apparel_BirdHelmetA",
            "MUGB_Apparel_BirdHelmetB",
            "MUGB_Apparel_CrudeHelmetA",
            "MUGB_Apparel_CrudeHelmetB",
            "MUGB_Apparel_FrogHelmet",
            "MUGB_Apparel_HammerHelmet",
            "MUGB_Apparel_KettleHelmetA",
            "MUGB_Apparel_KettleHelmetB",
            "MUGB_Apparel_KettleHelmetC"
        };

        private static readonly string[] TunnelVanguardWeapons =
        {
            "MUGB_GoblinCleaver",
            "MUGB_GoblinCurvedBlade",
            "MUGB_GoblinMachete"
        };

        private static readonly string[] TunnelLightShields =
        {
            "MUGB_Apparel_RoughBoardShield",
            "MUGB_Apparel_BoardShield",
            "MUGB_Apparel_RoundShield"
        };

        private static readonly string[] ShockWeapons =
        {
            "MUGB_GoblinMace",
            "MUGB_GoblinFlail",
            "MUGB_GoblinBoomstick"
        };

        private static readonly string[] PrimitiveRangedWeapons =
        {
            "MUGB_GoblinStaffSling",
            "MUGB_GoblinWarbow"
        };

        private static readonly string[] CrossbowWeapons =
        {
            "MUGB_GoblinCrossbow",
            "MUGB_GoblinRepeatingCrossbow"
        };

        private static readonly string[] CaptureWeapons =
        {
            "MUGB_GoblinStinkbomb",
            "MUGB_GoblinChainSnare"
        };

        private const string SpecialCaptureWeapon = "MUGB_GoblinBlowdart";
        private const float SpecialCaptureWeaponChance = 0.10f;

        private static readonly string[] CultistBomberPacks =
        {
            "MUGB_Apparel_GoblinWarBanner",
            "MUGB_Apparel_GoblinPheromonePack"
        };

        private static readonly string[] TunnelSlavePickaxes =
        {
            "DankPyon_MeleeWeapon_Pickaxe",
            "DankPyon_MeleeWeapon_MilitaryPick"
        };

        private static readonly HashSet<string> AllowedLegOnlyUnderlayers = new HashSet<string>
        {
            "MUGB_Apparel_GoblinLoincloth"
        };

        public static void TryFinalizeGeneratedGoblinGear(Pawn pawn)
        {
            string kindName = pawn?.kindDef?.defName;
            if (kindName.NullOrEmpty()
                || !kindName.StartsWith("MUGB_GoblinKind_")
                || pawn.DevelopmentalStage.Newborn()
                || pawn.DevelopmentalStage.Baby())
            {
                return;
            }

            Rand.PushState(pawn.thingIDNumber ^ 0x67656172);
            try
            {
                RemoveGeneratedTrousers(pawn);
                bool replaceParka = RemoveGeneratedParka(pawn);
                if (IsCultistCombatant(pawn, kindName))
                {
                    NormalizeCultistHeadgear(pawn);
                }
                if (!UsesNaturalMixedMedievalGear(kindName))
                {
                    EnsureBasicClothing(pawn, kindName);
                    EnsureSlaveMarkers(pawn, kindName);
                    EnsureArmorAndHelmet(pawn, kindName);
                    EnsureSpecialUtilityApparel(pawn, kindName);
                    TryGiveGoblinVanillaHeadgear(pawn, kindName);
                }
                if (replaceParka)
                {
                    WearOneOf(pawn, ColdWeatherCloaks);
                }
                EnsureRequiredWeapon(pawn, kindName);
                TryGiveGoblinCombatDrug(pawn, kindName);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static bool RemoveGeneratedParka(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return false;
            }

            bool removed = false;
            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def?.defName != "Apparel_Parka")
                {
                    continue;
                }

                pawn.apparel.Remove(apparel);
                apparel.Destroy(DestroyMode.Vanish);
                removed = true;
            }
            return removed;
        }

        private static void RemoveGeneratedTrousers(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return;
            }

            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                Apparel apparel = wornApparel[i];
                if (!IsGeneratedTrouser(apparel?.def))
                {
                    continue;
                }

                pawn.apparel.Remove(apparel);
                apparel.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool IsGeneratedTrouser(ThingDef def)
        {
            if (def?.apparel == null || AllowedLegOnlyUnderlayers.Contains(def.defName))
            {
                return false;
            }

            List<ApparelLayerDef> layers = def.apparel.layers;
            List<BodyPartGroupDef> groups = def.apparel.bodyPartGroups;
            if (layers == null || groups == null
                || !layers.Any(layer => layer?.defName == "OnSkin")
                || !groups.Any(group => group?.defName == "Legs"))
            {
                return false;
            }

            // Goblin wraps and surcoats cover the torso as well. Leg-only OnSkin apparel is the
            // stable vanilla/mod-compatible signature for generated pants and trousers.
            return !groups.Any(group => group?.defName == "Torso");
        }

        private static bool UsesNaturalMixedMedievalGear(string kindName)
        {
            return kindName == "MUGB_GoblinKind_MedievalFootman"
                || kindName == "MUGB_GoblinKind_MedievalAdvancedFootman";
        }

        private static void EnsureBasicClothing(Pawn pawn, string kindName)
        {
            if (kindName == "MUGB_GoblinKind_TunnelVanguard")
            {
                // Korean source intent: 땅굴선봉대는 훈도시만 두르고 등짐/방패/근접무기로 날뛰는 선봉 컨셉이다.
                WearApparel(pawn, "MUGB_Apparel_GoblinLoincloth");
                return;
            }

            if (kindName.Contains("PrimitiveCaptor"))
            {
                EnsureAnyApparelFrom(pawn, PrimitiveArmor);
                EnsureAnyApparelFrom(pawn, PrimitiveHelmet);
                return;
            }

            if (kindName.Contains("Slave") || kindName.Contains("Beggar"))
            {
                WearApparel(pawn, "MUGB_Apparel_GoblinLoincloth");
                return;
            }

            if (!HasAnyApparel(pawn, BasicClothing))
            {
                WearOneOf(pawn, BasicClothing);
            }
        }

        private static void EnsureSlaveMarkers(Pawn pawn, string kindName)
        {
            if (!kindName.Contains("Slave"))
            {
                return;
            }

            // Korean source intent: 이름에 Slave가 붙은 고블린 PawnKind는 노예목줄을 신분 표시로 보장한다.
            WearApparel(pawn, "Apparel_Collar");
            if (kindName == "MUGB_GoblinKind_SlaveBomber"
                || kindName == "MUGB_GoblinKind_SlaveBoomstickSapper")
            {
                // Korean source intent: 노예자폭병은 훈도시 + 노예목줄 + 안대 + 붐스틱으로 헐벗은 고기방패 느낌을 낸다.
                WearApparel(pawn, "Apparel_Blindfold");
            }
        }

        private static void EnsureArmorAndHelmet(Pawn pawn, string kindName)
        {
            bool cultistCombatant = IsCultistCombatant(pawn, kindName);
            if (kindName.Contains("Slave") || kindName.Contains("Beggar") || kindName.Contains("BomberNaked"))
            {
                return;
            }

            if (kindName == "MUGB_GoblinKind_TunnelVanguard")
            {
                // Korean source intent: 땅굴선봉대는 훈도시+투구+경방패+페르몬등짐 조합이다.
                // 뼈투구, War A/B, Spike은 제외하고 일반 고블린 투구만 쓴다.
                if (!cultistCombatant)
                {
                    EnsureAnyApparelFrom(pawn, TunnelVanguardHelmet);
                }
                return;
            }

            if (kindName == "MUGB_GoblinKind_CultistShaman")
            {
                EnsureAnyApparelFrom(pawn, ColdWeatherCloaks);
                return;
            }

            if (kindName.Contains("SquadLeader"))
            {
                bool eliteLeader = kindName.Contains("Late") || kindName.Contains("Cultist");
                EnsureAnyApparelFrom(pawn, eliteLeader ? EliteArmor : MedievalArmor);
                if (!cultistCombatant)
                {
                    EnsureAnyApparelFrom(pawn, eliteLeader ? EliteHelmet : MedievalHelmet);
                }
                return;
            }

            if (kindName.Contains("Elite") || kindName.Contains("ShockElite") || kindName.Contains("Handcannoneer") || kindName.Contains("HeavyBomber"))
            {
                EnsureAnyApparelFrom(pawn, EliteArmor);
                if (!cultistCombatant)
                {
                    EnsureAnyApparelFrom(pawn, EliteHelmet);
                }
                return;
            }

            if (kindName.Contains("Medieval") || kindName.Contains("Cultist") || kindName.Contains("Captor"))
            {
                EnsureAnyApparelFrom(pawn, MedievalArmor);
                if (!cultistCombatant)
                {
                    EnsureAnyApparelFrom(pawn, MedievalHelmet);
                }
                return;
            }

            if (kindName.Contains("Primitive"))
            {
                EnsureAnyApparelFrom(pawn, PrimitiveArmor);
                if (!cultistCombatant)
                {
                    EnsureAnyApparelFrom(pawn, PrimitiveHelmet);
                }
            }
        }

        private static bool IsCultistCombatant(Pawn pawn, string kindName)
        {
            return kindName.Contains("Cultist")
                || (pawn.Faction?.def == MUGBDefOf.MUGB_GoblinCultists && pawn.kindDef?.isFighter != false);
        }

        private static void NormalizeCultistHeadgear(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return;
            }

            bool hasSkinMask = false;
            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def?.apparel?.layers?.Contains(ApparelLayerDefOf.Overhead) != true)
                {
                    continue;
                }

                if (apparel.def.defName == CultistSkinMask)
                {
                    hasSkinMask = true;
                    continue;
                }

                pawn.apparel.Remove(apparel);
                apparel.Destroy(DestroyMode.Vanish);
            }

            // apparelRequired로 인피가면이 지정된 특수 광신도는 그 의도를 그대로 유지한다.
            if (hasSkinMask)
            {
                return;
            }

            if (Rand.Chance(0.90f))
            {
                WearApparel(pawn, CultistSkinMask);
                return;
            }

            List<string> availableAlternates = CultistAlternateHeadgear
                .Where(defName => DefDatabase<ThingDef>.GetNamedSilentFail(defName)?.apparel != null)
                .ToList();
            if (availableAlternates.TryRandomElement(out string selectedHeadgear))
            {
                WearApparel(pawn, selectedHeadgear);
            }
        }

        private static void EnsureSpecialUtilityApparel(Pawn pawn, string kindName)
        {
            if (kindName == "MUGB_GoblinKind_TunnelVanguard")
            {
                WearApparel(pawn, "MUGB_Apparel_GoblinPheromonePack");
                EnsureAnyApparelFrom(pawn, TunnelLightShields);
            }

            if (kindName == "MUGB_GoblinKind_CultistBomber" && !HasAnyApparel(pawn, CultistBomberPacks))
            {
                // Korean source intent: 광신도 자폭병은 그냥 폭탄병이 아니라 의식용 표식이 있어야 한다.
                // 결속깃대 또는 페르몬등짐 중 하나를 보장해 광신도 분대 느낌을 만든다.
                WearOneOf(pawn, CultistBomberPacks);
            }
        }

        private static void TryGiveGoblinVanillaHeadgear(Pawn pawn, string kindName)
        {
            if (pawn.apparel?.WornApparel == null || HasOverheadApparel(pawn))
            {
                return;
            }

            bool slave = kindName == "MUGB_GoblinKind_Slave"
                || kindName == "MUGB_GoblinKind_TunnelSlave";

            // Keep optional vanilla headgear uncommon so it does not replace goblin helmets.
            float headwrapChance = slave ? 0.10f : 0.02f;
            float tailcapChance = slave ? 0.02f : 0.02f;
            float flophatChance = slave ? 0.03f : 0.02f;
            float blindfoldChance = slave ? 0.02f : 0f;
            float roll = Rand.Value;
            if (roll < headwrapChance)
            {
                WearApparel(pawn, "Apparel_Headwrap");
            }
            else if (roll < headwrapChance + tailcapChance)
            {
                WearApparel(pawn, "Apparel_Tailcap");
            }
            else if (roll < headwrapChance + tailcapChance + flophatChance)
            {
                WearApparel(pawn, "Apparel_Flophat");
            }
            else if (roll < headwrapChance + tailcapChance + flophatChance + blindfoldChance)
            {
                WearApparel(pawn, "Apparel_Blindfold");
            }
        }

        private static bool HasOverheadApparel(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return false;
            }

            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def?.apparel?.layers?.Contains(ApparelLayerDefOf.Overhead) == true)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureRequiredWeapon(Pawn pawn, string kindName)
        {
            if (pawn.equipment == null)
            {
                return;
            }

            if (kindName == "MUGB_GoblinKind_BoomstickShockEliteSapper"
                || kindName == "MUGB_GoblinKind_SlaveBoomstickSapper")
            {
                if (pawn.equipment.Primary != null)
                {
                    pawn.equipment.DestroyEquipment(pawn.equipment.Primary);
                }
                EquipWeapon(pawn, "MUGB_GoblinBoomstick");
                return;
            }

            if (kindName == "MUGB_GoblinKind_TunnelSlave")
            {
                List<ThingDef> pickaxes = TunnelSlavePickaxes
                    .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                    .Where(def => def != null)
                    .ToList();
                if (pickaxes.Count == 0)
                {
                    ThingDef fallback = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_BreachAxe");
                    if (fallback != null)
                    {
                        pickaxes.Add(fallback);
                    }
                }

                if (pickaxes.Count == 0)
                {
                    return;
                }

                ThingDef pickaxe = pickaxes.Count == 1 || !Rand.Chance(0.15f)
                    ? pickaxes[0]
                    : pickaxes[1];
                if (pawn.equipment.Primary != null)
                {
                    pawn.equipment.DestroyEquipment(pawn.equipment.Primary);
                }
                EquipWeapon(pawn, pickaxe.defName);
                return;
            }

            if (kindName == "MUGB_GoblinKind_Sapper")
            {
                bool gunpowderFaction = pawn.Faction?.def?.defName == "MUGB_GoblinCivilMedieval"
                    || pawn.Faction?.def?.defName == "MUGB_GoblinSavageMedieval"
                    || pawn.Faction?.def?.defName == "MUGB_GoblinCultists";
                float roll = Rand.Value;
                string weaponDefName;
                if (gunpowderFaction)
                {
                    weaponDefName = roll < 0.30f ? "DankPyon_MeleeWeapon_Pickaxe"
                        : roll < 0.50f ? "DankPyon_MeleeWeapon_MilitaryPick"
                        : roll < 0.70f ? "MeleeWeapon_BreachAxe"
                        : "MUGB_GoblinHandcannon";
                }
                else
                {
                    weaponDefName = roll < 0.60f ? "DankPyon_MeleeWeapon_Pickaxe"
                        : roll < 0.80f ? "DankPyon_MeleeWeapon_MilitaryPick"
                        : "MeleeWeapon_BreachAxe";
                }

                if (DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName) == null)
                {
                    weaponDefName = "MeleeWeapon_BreachAxe";
                }
                if (pawn.equipment.Primary != null)
                {
                    pawn.equipment.DestroyEquipment(pawn.equipment.Primary);
                }
                EquipWeapon(pawn, weaponDefName);
                return;
            }

            if (kindName.Contains("Captor"))
            {
                if (pawn.equipment.Primary != null)
                {
                    pawn.equipment.DestroyEquipment(pawn.equipment.Primary);
                }
                if (Rand.Chance(SpecialCaptureWeaponChance))
                {
                    EquipWeapon(pawn, SpecialCaptureWeapon);
                }
                else
                {
                    EquipOneOf(pawn, CaptureWeapons);
                }
                return;
            }

            if (pawn.equipment.Primary != null)
            {
                return;
            }

            if (kindName.Contains("SlaveBomber") || kindName.Contains("HeavyBomber") || kindName.Contains("CultistBomber"))
            {
                EquipWeapon(pawn, "MUGB_GoblinBoomstick");
            }
            else if (kindName == "MUGB_GoblinKind_TunnelVanguard")
            {
                EquipOneOf(pawn, TunnelVanguardWeapons);
            }
            else if (kindName.Contains("ShockElite"))
            {
                EquipOneOf(pawn, ShockWeapons);
            }
            else if (kindName.Contains("PrimitiveSpear"))
            {
                EquipWeapon(pawn, "MUGB_GoblinBoneSpear");
            }
            else if (kindName.Contains("Spear"))
            {
                EquipWeapon(pawn, kindName.Contains("Leader") ? "MUGB_GoblinBannerSpear" : "MUGB_GoblinSpear");
            }
            else if (kindName.Contains("PrimitiveRanged"))
            {
                EquipOneOf(pawn, PrimitiveRangedWeapons);
            }
            else if (kindName.Contains("EliteMarksman"))
            {
                EquipOneOf(pawn, CrossbowWeapons);
            }
            else if (kindName.Contains("MedievalRanged") || kindName.Contains("CultistRanged"))
            {
                EquipOneOf(pawn, CrossbowWeapons);
            }
            else if (kindName.Contains("PrimitiveGunner") || kindName.Contains("Gunner"))
            {
                EquipWeapon(pawn, "MUGB_GoblinArquebus");
            }
        }

        private static void TryGiveGoblinCombatDrug(Pawn pawn, string kindName)
        {
            if (pawn.inventory?.innerContainer == null || kindName.Contains("Slave") || kindName.Contains("Beggar"))
            {
                return;
            }

            if (HasAnyDrugInInventory(pawn))
            {
                return;
            }

            float chance = kindName.Contains("Elite") || kindName.Contains("Shock") || kindName.Contains("Cultist") ? 0.35f : 0.12f;
            if (!Rand.Chance(chance))
            {
                return;
            }

            ThingDef drug = Rand.Chance(0.75f) ? MUGBDefOf.MUGB_GloopJuice : MUGBDefOf.MUGB_SpermJuice;
            if (drug == null)
            {
                return;
            }

            Thing thing = ThingMaker.MakeThing(drug);
            thing.stackCount = Rand.Chance(0.2f) ? 2 : 1;
            pawn.inventory.innerContainer.TryAdd(thing);
        }

        private static bool HasAnyDrugInInventory(Pawn pawn)
        {
            ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
            if (inventory == null)
            {
                return false;
            }

            for (int i = 0; i < inventory.Count; i++)
            {
                Thing thing = inventory[i];
                if (thing?.def?.GetCompProperties<CompProperties_Drug>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureAnyApparelFrom(Pawn pawn, string[] defNames)
        {
            if (!HasAnyApparel(pawn, defNames))
            {
                WearOneOf(pawn, defNames);
            }
        }

        private static bool HasAnyApparel(Pawn pawn, string[] defNames)
        {
            if (pawn.apparel?.WornApparel == null)
            {
                return false;
            }

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                string wornDefName = pawn.apparel.WornApparel[i]?.def?.defName;
                for (int j = 0; j < defNames.Length; j++)
                {
                    if (wornDefName == defNames[j])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void WearOneOf(Pawn pawn, string[] defNames)
        {
            if (defNames.TryRandomElement(out string defName))
            {
                WearApparel(pawn, defName);
            }
        }

        private static void WearApparel(Pawn pawn, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def?.apparel == null || pawn.apparel == null || HasAnyApparel(pawn, new[] { defName }))
            {
                return;
            }

            if (!pawn.apparel.CanWearWithoutDroppingAnything(def))
            {
                return;
            }

            ThingDef stuff = GenStuff.RandomStuffFor(def);
            Apparel apparel = ThingMaker.MakeThing(def, stuff) as Apparel;
            if (apparel == null)
            {
                return;
            }

            apparel.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
            pawn.apparel.Wear(apparel, dropReplacedApparel: false, locked: false);
        }

        private static void EquipOneOf(Pawn pawn, string[] defNames)
        {
            if (defNames.TryRandomElement(out string defName))
            {
                EquipWeapon(pawn, defName);
            }
        }

        private static void EquipWeapon(Pawn pawn, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || pawn.equipment == null || pawn.equipment.Primary != null)
            {
                return;
            }

            ThingDef stuff = GenStuff.RandomStuffFor(def);
            ThingWithComps weapon = ThingMaker.MakeThing(def, stuff) as ThingWithComps;
            if (weapon == null)
            {
                return;
            }

            weapon.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
            pawn.equipment.AddEquipment(weapon);
        }
    }

    public static class HalfGoblinAgeUtility
    {
        private const float FallbackTeenAgeYears = 13f;
        private const float FallbackAdultAgeYears = 18f;

        public static float TeenAgeYearsFor(Pawn pawn)
        {
            LifeStageAge stage = pawn?.RaceProps?.lifeStageAges?
                .FirstOrDefault(candidate => candidate?.def?.defName == "HumanlikeTeenager");
            return stage?.minAge ?? FallbackTeenAgeYears;
        }

        public static float AdultAgeYearsFor(Pawn pawn)
        {
            LifeStageAge stage = pawn?.RaceProps?.lifeStageAges?
                .FirstOrDefault(candidate => candidate?.def?.defName == "HumanlikeAdult");
            if (stage == null)
            {
                stage = pawn?.RaceProps?.lifeStageAges?
                    .FirstOrDefault(candidate => candidate?.def?.defName?.IndexOf("Adult", System.StringComparison.OrdinalIgnoreCase) >= 0
                        && candidate.def.defName.IndexOf("Teen", System.StringComparison.OrdinalIgnoreCase) < 0);
            }
            return stage?.minAge ?? FallbackAdultAgeYears;
        }
    }

    public class GoblinRapidMaturationComponent : GameComponent
    {
        private HashSet<int> juvenileMaturedPawnIds = new HashSet<int>();
        private HashSet<int> teenMaturedPawnIds = new HashSet<int>();
        private HashSet<int> maturedPawnIds = new HashSet<int>();
        private HashSet<int> captiveBornGoblinPawnIds = new HashSet<int>();

        public GoblinRapidMaturationComponent(Game game)
        {
        }

        public bool HasJuvenileMatured(Pawn pawn)
        {
            return pawn != null && juvenileMaturedPawnIds.Contains(pawn.thingIDNumber);
        }

        public bool HasTeenMatured(Pawn pawn)
        {
            return pawn != null && teenMaturedPawnIds.Contains(pawn.thingIDNumber);
        }

        public bool HasMatured(Pawn pawn)
        {
            return pawn != null && maturedPawnIds.Contains(pawn.thingIDNumber);
        }

        public void MarkJuvenileMatured(Pawn pawn)
        {
            if (pawn != null)
            {
                juvenileMaturedPawnIds.Add(pawn.thingIDNumber);
            }
        }

        public void MarkTeenMatured(Pawn pawn)
        {
            if (pawn != null)
            {
                teenMaturedPawnIds.Add(pawn.thingIDNumber);
            }
        }

        public void MarkMatured(Pawn pawn)
        {
            if (pawn != null)
            {
                maturedPawnIds.Add(pawn.thingIDNumber);
            }
        }

        public void MarkCaptiveBornGoblin(Pawn pawn)
        {
            if (pawn != null)
            {
                captiveBornGoblinPawnIds.Add(pawn.thingIDNumber);
            }
        }

        public bool ConsumeCaptiveBornGoblin(Pawn pawn)
        {
            return pawn != null && captiveBornGoblinPawnIds.Remove(pawn.thingIDNumber);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref juvenileMaturedPawnIds, "mugbJuvenileMaturedPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref teenMaturedPawnIds, "mugbTeenMaturedPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref maturedPawnIds, "mugbRapidMaturedPawnIds", LookMode.Value);
            // Keep the original save key used by the short-lived slave-marriage-only implementation.
            Scribe_Collections.Look(ref captiveBornGoblinPawnIds, "mugbSlaveMarriageChildPawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (juvenileMaturedPawnIds == null)
                {
                    juvenileMaturedPawnIds = new HashSet<int>();
                }

                if (teenMaturedPawnIds == null)
                {
                    teenMaturedPawnIds = new HashSet<int>();
                }

                if (maturedPawnIds == null)
                {
                    maturedPawnIds = new HashSet<int>();
                }

                if (captiveBornGoblinPawnIds == null)
                {
                    captiveBornGoblinPawnIds = new HashSet<int>();
                }
            }
        }
    }

    public static class GoblinRapidMaturationUtility
    {
        private static readonly SkillDef[] JuvenileCombatSkillPool =
        {
            SkillDefOf.Melee,
            SkillDefOf.Shooting
        };

        private static readonly SkillDef[] ThinGoblinSkillPool =
        {
            SkillDefOf.Melee,
            SkillDefOf.Shooting,
            SkillDefOf.Crafting,
            SkillDefOf.Mining,
            SkillDefOf.Cooking,
            SkillDefOf.Animals
        };

        private static readonly SkillDef[] HobgoblinSkillPool =
        {
            SkillDefOf.Melee,
            SkillDefOf.Shooting,
            SkillDefOf.Crafting,
            SkillDefOf.Mining,
            SkillDefOf.Cooking,
            SkillDefOf.Animals,
            SkillDefOf.Social,
            SkillDefOf.Intellectual
        };

        public static void TryApplyJuvenileMaturation(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || pawn.skills == null || pawn.ageTracker == null
                || pawn.ageTracker.AgeBiologicalYearsFloat < GoblinAgeUtility.TeenAgeYears)
            {
                return;
            }

            GoblinRapidMaturationComponent component = Current.Game?.GetComponent<GoblinRapidMaturationComponent>();
            if (component == null || component.HasJuvenileMatured(pawn))
            {
                return;
            }

            List<SkillDef> candidates = JuvenileCombatSkillPool
                .Where(skill =>
                {
                    SkillRecord record = pawn.skills.GetSkill(skill);
                    return record != null && !record.TotallyDisabled;
                })
                .ToList();

            if (candidates.TryRandomElement(out SkillDef selectedSkill))
            {
                SkillRecord record = pawn.skills.GetSkill(selectedSkill);
                record.Level = System.Math.Min(20, record.Level + 1);
            }

            component.MarkJuvenileMatured(pawn);
            GoblinSlaveChildStatusUtility.TryIssueStatusChoice(pawn, component);
        }

        public static void TryApplyTeenMaturation(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || pawn.skills == null || pawn.ageTracker == null)
            {
                return;
            }

            if (pawn.ageTracker.AgeBiologicalYearsFloat < GoblinAgeUtility.RomanceMinAgeYears
                || pawn.ageTracker.AgeBiologicalYearsFloat >= GoblinAgeUtility.AdultAgeYears)
            {
                return;
            }

            GoblinRapidMaturationComponent component = Current.Game?.GetComponent<GoblinRapidMaturationComponent>();
            if (component == null || component.HasTeenMatured(pawn))
            {
                return;
            }

            GoblinPawnKindBackstoryUtility.AssignMatureGoblinChildhood(pawn);
            // 임시 이름을 달고 있던 개체가 여기서 정식 고블린 이름을 받습니다.
            GoblinPersonalNameUtility.TryApplyKoreanGoblinName(pawn);
            component.MarkTeenMatured(pawn);
            bool hobgoblin = pawn.genes?.Xenotype == MUGBDefOf.MUGB_Hobgoblin || pawn.genes?.GetGene(MUGBDefOf.MUGB_Gene_HobgoblinFrame) != null;
            SkillDef boostedSkill = BoostTeenInstinctSkill(pawn, hobgoblin);
            if (pawn.Faction == Faction.OfPlayer)
            {
                string boostedSkillLabel = boostedSkill?.label ?? "survival";
                string text = "MUGB_TeenMatured".Translate(pawn.LabelShortCap, boostedSkillLabel);
                Messages.Message(text, pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }

        public static void TryApplyAdultMaturation(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || pawn.skills == null || pawn.ageTracker == null)
            {
                return;
            }

            if (pawn.ageTracker.AgeBiologicalYearsFloat < GoblinAgeUtility.AdultAgeYears)
            {
                return;
            }

            GoblinRapidMaturationComponent component = Current.Game?.GetComponent<GoblinRapidMaturationComponent>();
            if (component == null || component.HasMatured(pawn))
            {
                return;
            }

            GoblinPawnKindBackstoryUtility.AssignMatureGoblinAdulthood(pawn);
            component.MarkMatured(pawn);
            bool hobgoblin = pawn.genes?.Xenotype == MUGBDefOf.MUGB_Hobgoblin || pawn.genes?.GetGene(MUGBDefOf.MUGB_Gene_HobgoblinFrame) != null;
            SkillDef boostedSkill = BoostInstinctSkill(pawn, hobgoblin);
            SkillDef passionSkill = TryGrantInstinctPassion(pawn, hobgoblin);
            if (pawn.Faction == Faction.OfPlayer)
            {
                string boostedSkillLabel = boostedSkill?.label ?? "survival";
                string text = passionSkill != null
                    ? "MUGB_RapidMaturedWithPassion".Translate(pawn.LabelShortCap, boostedSkillLabel, passionSkill.label)
                    : "MUGB_RapidMatured".Translate(pawn.LabelShortCap, boostedSkillLabel);
                Messages.Message(text, pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }

        private static SkillDef BoostTeenInstinctSkill(Pawn pawn, bool hobgoblin)
        {
            return BoostSkills(
                pawn,
                hobgoblin ? HobgoblinSkillPool : ThinGoblinSkillPool,
                new IntRange(2, 3),
                hobgoblin ? new IntRange(4, 6) : new IntRange(2, 4));
        }

        private static SkillDef BoostInstinctSkill(Pawn pawn, bool hobgoblin)
        {
            return BoostSkills(
                pawn,
                hobgoblin ? HobgoblinSkillPool : ThinGoblinSkillPool,
                new IntRange(2, 3),
                hobgoblin ? new IntRange(6, 8) : new IntRange(3, 4));
        }

        private static SkillDef TryGrantInstinctPassion(Pawn pawn, bool hobgoblin)
        {
            float majorChance = hobgoblin ? 0.18f : 0.03f;
            float minorChance = hobgoblin ? 0.65f : 0.30f;
            Passion targetPassion;
            if (Rand.Chance(majorChance))
            {
                targetPassion = Passion.Major;
            }
            else if (Rand.Chance(minorChance))
            {
                targetPassion = Passion.Minor;
            }
            else
            {
                return null;
            }

            SkillDef skill = PickUsableSkill(pawn, hobgoblin ? HobgoblinSkillPool : ThinGoblinSkillPool);
            if (skill == null)
            {
                return null;
            }

            SkillRecord record = pawn.skills.GetSkill(skill);
            if (targetPassion == Passion.Major || record.passion == Passion.None)
            {
                record.passion = targetPassion;
            }

            return skill;
        }

        // 한국어 의도: 성숙할 때 본능적으로 익히는 분야를 2~3가지로 넓힙니다.
        // 한 가지만 오르면 성장 체감이 약해서, 중복 없이 여러 개를 골라 올립니다.
        private static List<SkillDef> PickUsableSkills(Pawn pawn, SkillDef[] pool, int count)
        {
            List<SkillDef> usable = new List<SkillDef>();
            for (int i = 0; i < pool.Length; i++)
            {
                SkillDef skill = pool[i];
                SkillRecord record = pawn.skills.GetSkill(skill);
                if (record != null && !record.TotallyDisabled)
                {
                    usable.Add(skill);
                }
            }

            List<SkillDef> picked = new List<SkillDef>();
            for (int i = 0; i < count && usable.Count > 0; i++)
            {
                SkillDef skill = usable.RandomElement();
                usable.Remove(skill);
                picked.Add(skill);
            }

            return picked;
        }

        private static SkillDef BoostSkills(Pawn pawn, SkillDef[] pool, IntRange countRange, IntRange levelRange)
        {
            List<SkillDef> skills = PickUsableSkills(pawn, pool, countRange.RandomInRange);
            SkillDef primary = null;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord record = pawn.skills.GetSkill(skills[i]);
                int targetLevel = levelRange.RandomInRange;
                if (record.Level < targetLevel)
                {
                    record.Level = targetLevel;
                }

                if (primary == null)
                {
                    primary = skills[i];
                }
            }

            return primary;
        }

        private static SkillDef PickUsableSkill(Pawn pawn, SkillDef[] pool)
        {
            List<SkillDef> usable = new List<SkillDef>();
            for (int i = 0; i < pool.Length; i++)
            {
                SkillDef skill = pool[i];
                SkillRecord record = pawn.skills.GetSkill(skill);
                if (record != null && !record.TotallyDisabled)
                {
                    usable.Add(skill);
                }
            }

            return usable.TryRandomElement(out SkillDef result) ? result : null;
        }
    }
}
