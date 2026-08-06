using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class Need_FleshCraving : Need
    {
        private const int TickInterval = 150;
        private const float GoblinDaysToEmpty = 9.5f;
        private const float HobgoblinDaysToEmpty = 6f;
        private const float StableCellDecayFactor = 0.75f;
        private const float UnstableCellDecayFactor = 1.25f;

        public Need_FleshCraving(Pawn pawn) : base(pawn)
        {
            threshPercents = new List<float> { 0.15f, 0.35f, 0.6f };
        }

        public override int GUIChangeArrow => -1;

        public override void SetInitialLevel()
        {
            CurLevel = MaxLevel;
        }

        public override void NeedInterval()
        {
            if (IsFrozen)
            {
                return;
            }

            CurLevel -= FallPerInterval();
            UpdateWeaknessHediff();
        }

        public void Satisfy(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            CurLevel = Mathf.Min(MaxLevel, CurLevel + amount);
            UpdateWeaknessHediff();
        }

        public int CravingStage
        {
            get
            {
                if (CurLevelPercentage <= 0.15f)
                {
                    return 2;
                }
                if (CurLevelPercentage <= 0.35f)
                {
                    return 1;
                }
                if (CurLevelPercentage <= 0.6f)
                {
                    return 0;
                }
                return -1;
            }
        }

        private float FallPerInterval()
        {
            float daysToEmpty = GoblinUtility.IsHobgoblin(pawn) ? HobgoblinDaysToEmpty : GoblinDaysToEmpty;
            float factor = 1f;
            if (pawn.genes?.HasActiveGene(MUGBDefOf.MUGB_Gene_GoblinStableCellMetabolism) == true)
            {
                factor *= StableCellDecayFactor;
            }
            if (pawn.genes?.HasActiveGene(MUGBDefOf.MUGB_Gene_GoblinUnstableCellMetabolism) == true)
            {
                factor *= UnstableCellDecayFactor;
            }

            return TickInterval / (GenDate.TicksPerDay * daysToEmpty) * factor;
        }

        private void UpdateWeaknessHediff()
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_FleshCravingWeakness);
            if (CurLevelPercentage > 0.6f)
            {
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
                return;
            }

            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(MUGBDefOf.MUGB_FleshCravingWeakness, pawn);
                pawn.health.AddHediff(hediff);
            }

            hediff.Severity = Mathf.Clamp01(1f - CurLevelPercentage);
        }
    }

    public class ThoughtWorker_FleshCravingLow : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (MUGBSurgeryUtility.HasNosePickedLobotomy(p))
            {
                return ThoughtState.Inactive;
            }

            Need_FleshCraving need = p?.needs?.TryGetNeed<Need_FleshCraving>();
            if (need == null)
            {
                return ThoughtState.Inactive;
            }

            int stage = need.CravingStage;
            return stage >= 0 ? ThoughtState.ActiveAtStage(stage) : ThoughtState.Inactive;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested), typeof(Pawn), typeof(float))]
    public static class Thing_Ingested_FleshCraving_Patch
    {
        public static void Postfix(Thing __instance, Pawn ingester, float nutritionWanted)
        {
            Need_FleshCraving need = ingester?.needs?.TryGetNeed<Need_FleshCraving>();
            if (need == null || __instance?.def == null)
            {
                return;
            }

            float satisfaction = GoblinFleshFoodUtility.FleshCravingSatisfaction(__instance, nutritionWanted);
            if (satisfaction > 0f)
            {
                need.Satisfy(satisfaction);
            }
        }
    }

    public static class GoblinFleshFoodUtility
    {
        // 한국어 의도: def -> 등급 판정은 문자열 비교가 여러 번 들어갑니다. 먹을 때 한 번만
        // 부르던 시절에는 문제가 없었지만, 음식 선호도 계산(FoodOptimality)은 탐색 한 번마다
        // 맵 위 음식 후보 전부에 대해 불리기 때문에 def 단위로 캐싱해 둡니다.
        private static readonly Dictionary<ThingDef, FleshFoodKind> FleshFoodKindCache = new Dictionary<ThingDef, FleshFoodKind>();

        public static float FleshCravingSatisfaction(Thing food, float nutritionWanted)
        {
            float fixedSatisfaction = FixedFleshCravingSatisfaction(food?.def);
            if (fixedSatisfaction > 0f)
            {
                return fixedSatisfaction;
            }

            FleshFoodKind kind = FleshFoodKindOf(food, null);
            if (kind == FleshFoodKind.None)
            {
                return 0f;
            }

            float baseAmount = Mathf.Clamp(nutritionWanted, 0.05f, 1f);
            switch (kind)
            {
                // 기준: 갈망 게이지는 고블린 기준 9.5일에 1.0이 빠지므로 하루 약 0.105입니다.
                // 배부르게 한 끼 먹었을 때 며칠을 버티는지로 환산해 잡았습니다.
                //   인육 0.25 -> 약 2.4일
                //   고블린 고기 0.375 -> 약 3.5일 (인육의 정확히 1.5배)
                //   고블린 장기 0.50 -> 약 4.8일 (고블린 고기의 약 1.33배, 기존 등급 간격 유지)
                case FleshFoodKind.Humanlike:
                    return baseAmount * 0.25f;
                case FleshFoodKind.Goblin:
                    return baseAmount * 0.375f;
                case FleshFoodKind.GoblinOrgan:
                    return baseAmount * 0.5f;
                default:
                    return 0f;
            }
        }

        private static float FixedFleshCravingSatisfaction(ThingDef def)
        {
            // 짝이 있는 요리는 고블린판이 인간판의 정확히 1.5배입니다.
            //   HBBQ 0.25 -> GBBQ 0.375
            //   leg,armHBBQ 0.20 -> leg,armGBBQ 0.30
            //   excHBBQ 0.45 -> excGBBQ 0.675
            // 인간판 값은 위 비율 경로의 인육 기준(0.25)에 맞춰 함께 내렸습니다.
            //
            // 창자 요리(gutsausage, gutstew)는 인간 창자와 고블린 창자를 모두 재료로
            // 받기 때문에 어느 쪽으로 만들었는지 구분할 수 없어 그대로 둡니다.
            switch (def?.defName)
            {
                case "MUGB_HBBQ":
                    return 0.25f;
                case "MUGB_GBBQ":
                    return 0.375f;
                case "MUGB_legHBBQ":
                case "MUGB_armHBBQ":
                    return 0.2f;
                case "MUGB_legGBBQ":
                case "MUGB_armGBBQ":
                    return 0.3f;
                case "MUGB_gutsausage":
                    return 0.18f;
                case "MUGB_gutstew":
                    return 0.2f;
                // 뇌 스튜는 고블린 뇌가 필수 재료인 장기 등급 요리입니다. 짝이 되는
                // 인간판이 없어 1.5배 적용 대상이 아니며, GBBQ(0.375)보다 높아
                // 등급 역전도 생기지 않으므로 원래 값을 유지합니다.
                case "MUGB_Brainstew":
                    return 0.39f;
                case "MUGB_excHBBQ":
                    return 0.45f;
                case "MUGB_excGBBQ":
                    return 0.675f;
                default:
                    return 0f;
            }
        }

        // 한국어 의도: 완제품 def와 재료 목록을 함께 보고 가장 강한 등급을 돌려줍니다.
        // foodDefOverride는 영양보급기처럼 "실제로 먹게 될 def"가 따로 있는 경우에 씁니다.
        internal static FleshFoodKind FleshFoodKindOf(Thing food, ThingDef foodDefOverride)
        {
            FleshFoodKind kind = FleshFoodKindFor(foodDefOverride ?? food?.def);
            if (foodDefOverride != null && food?.def != null)
            {
                kind = Stronger(kind, FleshFoodKindFor(food.def));
            }

            CompIngredients compIngredients = food?.TryGetComp<CompIngredients>();
            if (compIngredients?.ingredients != null)
            {
                for (int i = 0; i < compIngredients.ingredients.Count; i++)
                {
                    kind = Stronger(kind, FleshFoodKindFor(compIngredients.ingredients[i]));
                }
            }

            return kind;
        }

        private static FleshFoodKind FleshFoodKindFor(ThingDef def)
        {
            if (def == null)
            {
                return FleshFoodKind.None;
            }

            if (FleshFoodKindCache.TryGetValue(def, out FleshFoodKind cached))
            {
                return cached;
            }

            FleshFoodKind kind = ResolveFleshFoodKind(def);
            FleshFoodKindCache[def] = kind;
            return kind;
        }

        private static FleshFoodKind ResolveFleshFoodKind(ThingDef def)
        {
            string defName = def.defName;
            if (defName == "Meat_Human" || defName == "MUGB_Hchunk" || defName.Contains("HumanMeat") || defName.Contains("HumanFlesh"))
            {
                return FleshFoodKind.Humanlike;
            }

            if (defName == "MUGB_Ggut" || defName == "MUGB_heart" || defName == "MUGB_brain"
                || defName.Contains("Ggut") || defName.Contains("GoblinGut") || defName.Contains("GoblinBrain") || defName.Contains("GoblinHeart"))
            {
                return FleshFoodKind.GoblinOrgan;
            }

            if (defName == "Meat_Goblin" || defName == "MUGB_Gchunk"
                || (defName.Contains("Goblin") && (defName.Contains("Meat") || defName.Contains("Flesh") || defName.Contains("BBQ"))))
            {
                return FleshFoodKind.Goblin;
            }

            if (defName == "MUGB_Hgut" || defName.Contains("Hgut") || defName.Contains("HumanGut") || defName.Contains("HumanBrain") || defName.Contains("HumanHeart"))
            {
                return FleshFoodKind.Humanlike;
            }

            if (MUGBHumanlikeFoodUtility.IsHumanlikeMeatDef(def))
            {
                return FleshFoodKind.Humanlike;
            }

            return FleshFoodKind.None;
        }

        private static FleshFoodKind Stronger(FleshFoodKind a, FleshFoodKind b)
        {
            return (FleshFoodKind)Mathf.Max((int)a, (int)b);
        }

        internal enum FleshFoodKind
        {
            None = 0,
            Humanlike = 1,
            Goblin = 2,
            GoblinOrgan = 3
        }
    }
}
