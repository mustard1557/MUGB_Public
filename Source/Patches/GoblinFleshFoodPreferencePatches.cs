using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    /*
      [파일 개요]
      고블린이 "인육을 좋아하는 종족"답게 음식을 고르도록 만드는 패치 묶음입니다.

      배경:
      바닐라 FoodUtility.FoodOptimality는 "이 음식을 먹으면 어떤 생각이 뜰까"를 def에서
      미리 조회해서 점수에 반영합니다. 인육 관련 혐오 생각(AteHumanlikeMeatDirect -20,
      AteHumanlikeMeatAsIngredient -15)은 점수로 환산하면 -156 / -128이라, 거리 1칸이 1점인
      계산에서 사실상 "인육은 100칸 이상 손해"가 됩니다. 그래서 고블린은 인육 요리를 앞에
      두고도 일반 식사를 먹으러 갔습니다.

      MGB_GoblinFleshFoodThoughtNullify.xml이 그 혐오 생각 자체를 고블린 유전자 보유자에게만
      끄고, 이 파일은 그 위에 "얼마나 더 좋아하는가"와 "시체는 언제 후보가 되는가"를 얹습니다.

      설계 원칙:
      - 대상 판정은 MUGB_Gene_GoblinFleshCraving 유전자 하나로 통일합니다. XML 무효화 패치와
        정확히 같은 대상이라 둘이 어긋날 일이 없고, 띤고블린/홉고블린 양쪽 제노타입이
        이미 이 유전자를 갖고 있습니다.
      - 시체는 점수를 크게 올리지 않습니다. 창고에 식사가 있으면 시체는 후보에서 밀리도록
        의도적으로 낮게 둡니다(아래 CorpseBonus 주석 참고).
      - 남이 날라다 주는 경우(getter != eater)는 건드리지 않습니다. 인간 간병인이 고블린
        환자에게 시체를 배달하는 그림을 막기 위해서입니다.
    */
    internal static class GoblinFleshFoodPreference
    {
        // 점수 감각 기준: FoodOptimality는 300에서 시작해 거리 1칸당 1점씩 깎습니다.
        // 즉 여기 붙는 보너스 값은 그대로 "몇 칸 더 걸어갈 의향이 있는가"로 읽으면 됩니다.
        //
        // 조리된 인육/고블린 요리. 같은 등급끼리 붙으면 인육 쪽이 이기고, 한 등급 위의
        // 일반 요리(간단식 316 vs 고급식 356)는 못 이기는 선입니다.
        private const float HumanlikeDishBonus = 40f;
        private const float GoblinDishBonus = 45f;
        private const float GoblinOrganDishBonus = 50f;

        // 손질된 원물(인육 덩이, 창자, 심장 등). 생식 혐오(AteRawFood, -82점)를 넘기고
        // 일반 간단식(316) 바로 위에 서도록 잡았습니다. 300 - 82 + 100 = 318.
        private const float HumanlikeRawBonus = 100f;
        private const float GoblinRawBonus = 105f;
        private const float GoblinOrganRawBonus = 110f;

        // 인간형 시체. 기본 점수는 300 - 150(DesperateOnly) - 111(AteCorpse) = 39입니다.
        // 여기에 190을 더하면 229가 되어,
        //   동물 생고기(218) < 인간형 시체(229) < 일반 간단식(316) < 인육 원물(318)
        // 순서가 됩니다. 창고에 식사가 있으면 87칸 이상 손해라 시체 쪽으로 가지 않습니다.
        private const float CorpseBonus = 190f;

        // 시체 보너스가 붙기 시작하는 갈망 단계입니다.
        // 0 = 60% 이하, 1 = 35% 이하, 2 = 15% 이하.
        //
        // 1로 둔 이유: 갈망은 9.5일에 걸쳐 다 빠지기 때문에 0단계(60% 이하)는 인육을 며칠만
        // 안 먹어도 거의 상시로 걸립니다. 그 상태에서 시체가 계속 229점을 유지하면
        // "식사가 87칸보다 멀면 시체로 간다"가 일상이 되어 버립니다.
        // 1단계로 올리면 평소에는 시체가 39점짜리 최후 후보로 남고, 갈망이 실제로
        // 심해졌을 때만 후보로 올라옵니다.
        //
        // 이 값만 0으로 바꾸면 더 자주 뜯어먹고, 2로 바꾸면 훨씬 드물게 먹습니다.
        private const int CorpseCravingStage = 1;

        private const int NotFleshEater = -2;

        private static Pawn cachedPawn;
        private static int cachedTick = -1;
        private static int cachedStage = NotFleshEater;

        /// <summary>
        /// 갈망 단계를 폰+틱 단위로 캐싱해 돌려줍니다.
        /// -2: 고블린(인육 갈망 유전자 보유자)이 아님, -1: 갈망 정상, 0/1/2: 갈망 낮음.
        /// FoodOptimality가 탐색 한 번마다 후보 수만큼 불리기 때문에 캐시가 필요합니다.
        /// </summary>
        internal static int CravingStageOf(Pawn pawn)
        {
            if (pawn == null)
            {
                return NotFleshEater;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (pawn == cachedPawn && tick == cachedTick)
            {
                return cachedStage;
            }

            int stage = NotFleshEater;
            if (MUGBDefOf.MUGB_Gene_GoblinFleshCraving != null
                && pawn.genes?.HasActiveGene(MUGBDefOf.MUGB_Gene_GoblinFleshCraving) == true)
            {
                // 아이 고블린은 NeedDef의 developmentalStageFilter 때문에 갈망 need가 없습니다.
                // 유전자는 갖고 있으니 선호도는 그대로 받고, 갈망 단계만 정상(-1)으로 봅니다.
                Need_FleshCraving need = pawn.needs?.TryGetNeed<Need_FleshCraving>();
                stage = need?.CravingStage ?? -1;
            }

            cachedPawn = pawn;
            cachedTick = tick;
            cachedStage = stage;
            return stage;
        }

        internal static bool IsFleshEater(int stage)
        {
            return stage != NotFleshEater;
        }

        /// <summary>
        /// 우리 편 시체인지 판정합니다. 식민지 소속(정착민, 길들인 동물)은 먹지 않습니다.
        /// 죄수는 원래 세력을 그대로 갖고 있어서 여기 걸리지 않습니다.
        /// </summary>
        internal static bool IsProtectedCorpse(Corpse corpse)
        {
            Pawn inner = corpse?.InnerPawn;
            if (inner == null)
            {
                return false;
            }

            return inner.Faction != null && inner.Faction == Faction.OfPlayer;
        }

        internal static float BonusFor(GoblinFleshFoodUtility.FleshFoodKind kind, bool cooked)
        {
            switch (kind)
            {
                case GoblinFleshFoodUtility.FleshFoodKind.Humanlike:
                    return cooked ? HumanlikeDishBonus : HumanlikeRawBonus;
                case GoblinFleshFoodUtility.FleshFoodKind.Goblin:
                    return cooked ? GoblinDishBonus : GoblinRawBonus;
                case GoblinFleshFoodUtility.FleshFoodKind.GoblinOrgan:
                    return cooked ? GoblinOrganDishBonus : GoblinOrganRawBonus;
                default:
                    return 0f;
            }
        }

        internal static float CorpseBonusValue => CorpseBonus;

        /// <summary>갈망이 시체를 후보로 올릴 만큼 낮아졌는지.</summary>
        internal static bool CravingWantsCorpses(int stage)
        {
            return IsFleshEater(stage) && stage >= CorpseCravingStage;
        }
    }

    /// <summary>
    /// 고블린이 인육류 음식에 더 높은 점수를 주도록 합니다.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
    public static class FoodUtility_FoodOptimality_GoblinFleshPreferencePatch
    {
        public static void Postfix(Pawn eater, Thing foodSource, ThingDef foodDef, ref float __result)
        {
            // 바닐라가 이미 "절대 안 먹음"으로 못박은 경우(백골 시체 등)는 그대로 둡니다.
            if (__result < -9999f || foodSource == null || foodDef?.ingestible == null)
            {
                return;
            }

            int stage = GoblinFleshFoodPreference.CravingStageOf(eater);
            if (!GoblinFleshFoodPreference.IsFleshEater(stage))
            {
                return;
            }

            if (foodSource is Corpse corpse)
            {
                // 시체는 갈망이 이미 낮을 때만 후보로 끌어올립니다. 평소에는 39점짜리
                // 최하위 후보로 남아서, 다른 먹을 것이 전혀 없을 때만 선택됩니다.
                if (!GoblinFleshFoodPreference.CravingWantsCorpses(stage)
                    || corpse.InnerPawn?.RaceProps?.Humanlike != true)
                {
                    return;
                }

                if (GoblinFleshFoodPreference.IsProtectedCorpse(corpse))
                {
                    return;
                }

                __result += GoblinFleshFoodPreference.CorpseBonusValue;
                return;
            }

            GoblinFleshFoodUtility.FleshFoodKind kind = GoblinFleshFoodUtility.FleshFoodKindOf(foodSource, foodDef);
            if (kind == GoblinFleshFoodUtility.FleshFoodKind.None)
            {
                return;
            }

            bool cooked = (int)foodDef.ingestible.preferability >= (int)FoodPreferability.MealAwful;
            __result += GoblinFleshFoodPreference.BonusFor(kind, cooked);
        }
    }

    /// <summary>
    /// 고블린이 배고프고 갈망이 낮을 때 시체를 음식 후보에 포함시킵니다.
    ///
    /// 바닐라 JobGiver_GetFood는 인간형 폰에게 영양실조 심각도가 0.4를 넘어야 시체를
    /// 허용합니다(구울만 예외로 항상 허용 + minPrefOverride를 DesperateOnly로 지정).
    /// 고블린에게 그 구울과 같은 대우를 해 주는 패치입니다.
    ///
    /// allowCorpse / minPrefOverride 둘 다 메서드 파라미터라 Prefix에서 ref로 바꾸면 되고,
    /// IL을 건드리지 않아 다른 모드나 게임 업데이트와 부딪힐 여지가 적습니다.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor))]
    public static class FoodUtility_TryFindBestFoodSourceFor_GoblinCorpsePatch
    {
        public static void Prefix(Pawn getter, Pawn eater, ref bool allowCorpse, ref FoodPreferability minPrefOverride)
        {
            if (allowCorpse || getter == null || getter != eater)
            {
                return;
            }

            // 이미 다른 쪽에서 최소 선호도를 지정했다면 건드리지 않습니다.
            if (minPrefOverride != FoodPreferability.Undefined)
            {
                return;
            }

            int stage = GoblinFleshFoodPreference.CravingStageOf(eater);
            if (!GoblinFleshFoodPreference.IsFleshEater(stage))
            {
                return;
            }

            Need_Food food = eater.needs?.food;
            if (food == null || (int)food.CurCategory < (int)HungerCategory.Hungry)
            {
                // 배고프지도 않은데 최소 선호도를 낮추면 멀쩡한 고블린이 건초나 시체를
                // 후보에 올리게 됩니다. 최소한 "배고픔" 단계는 되어야 합니다.
                return;
            }

            // 갈망이 아직 견딜 만하면 아사 직전에만 열어 줍니다.
            if (!GoblinFleshFoodPreference.CravingWantsCorpses(stage) && !food.Starving)
            {
                return;
            }

            allowCorpse = true;
            minPrefOverride = FoodPreferability.DesperateOnly;
        }
    }

    /// <summary>
    /// 고블린이 식민지 소속 시체(동료 고블린, 길들인 동물)를 먹지 않도록 막습니다.
    /// 매장하려고 옮기는 중인 동료를 뜯어먹는 그림을 방지합니다.
    /// 고블린이 아닌 폰의 바닐라 동작은 그대로 둡니다.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat),
        new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class FoodUtility_WillEat_GoblinColonyCorpsePatch
    {
        public static void Postfix(Pawn p, Thing food, ref bool __result)
        {
            if (!__result || !(food is Corpse corpse))
            {
                return;
            }

            if (!GoblinFleshFoodPreference.IsProtectedCorpse(corpse))
            {
                return;
            }

            if (!GoblinFleshFoodPreference.IsFleshEater(GoblinFleshFoodPreference.CravingStageOf(p)))
            {
                return;
            }

            __result = false;
        }
    }
}
