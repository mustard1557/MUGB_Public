using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace MUGB
{
    public static class GoblinUtility
    {
        public const float JuvenileHeadLiftDefault = 0.08f;
        public static readonly Color GoblinTextureSkinColor = new Color32(0x6F, 0x74, 0x49, 0xFF);

        public static bool IsGoblin(Pawn pawn)
        {
            if (pawn?.genes == null || pawn.gender != Gender.Male)
            {
                return false;
            }

            return pawn.genes.Xenotype == MUGBDefOf.MUGB_Goblin
                || pawn.genes.Xenotype == MUGBDefOf.MUGB_Hobgoblin
                || pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_GoblinCore)
                || pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_HobgoblinFrame);
        }

        public static bool HasGoblinCoreMarker(Pawn pawn)
        {
            if (pawn?.genes == null)
            {
                return false;
            }

            return pawn.genes.Xenotype == MUGBDefOf.MUGB_Goblin
                || pawn.genes.Xenotype == MUGBDefOf.MUGB_Hobgoblin
                || pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_GoblinCore)
                || pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_HobgoblinFrame);
        }

        public static bool IsHobgoblin(Pawn pawn)
        {
            return pawn?.genes != null
                && pawn.gender == Gender.Male
                && (pawn.genes.Xenotype == MUGBDefOf.MUGB_Hobgoblin
                    || pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_HobgoblinFrame));
        }

        public static bool HasHalfGoblinAncestry(Pawn pawn)
        {
            return pawn?.genes?.HasActiveGene(MUGBDefOf.MUGB_Gene_HalfGoblinAncestry) == true;
        }

        public static bool IsGoblinXenotype(XenotypeDef xenotype)
        {
            return xenotype == MUGBDefOf.MUGB_Goblin || xenotype == MUGBDefOf.MUGB_Hobgoblin;
        }

        public static void TryGiveGoblinFertileMutation(Pawn pawn)
        {
            if (!IsGoblin(pawn)
                || pawn.genes == null
                || MUGBDefOf.MUGB_Gene_GoblinFertile == null
                || pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_GoblinFertile)
                || pawn.genes.GetGene(MUGBDefOf.MUGB_Gene_GoblinFertile) != null)
            {
                return;
            }

            // 한국어 의도: 고블린다산유전자는 모든 고블린에게 30% 확률로 자연 발현되는 [변]{고} 유전자입니다.
            if (Rand.ChanceSeeded(0.30f, pawn.thingIDNumber ^ 0x47464D42))
            {
                pawn.genes.AddGene(MUGBDefOf.MUGB_Gene_GoblinFertile, xenogene: false);
            }
        }

        public static bool IsCrossEyed(Pawn pawn)
        {
            return IsGoblin(pawn)
                && pawn.genes.HasActiveGene(MUGBDefOf.MUGB_Gene_CrossEyed);
        }

        public static string GoblinRenderFormKey(Pawn pawn)
        {
            if (IsHobgoblin(pawn))
            {
                return IsCrossEyed(pawn) ? "HobgoblinCrossEyed" : "Hobgoblin";
            }
            return IsCrossEyed(pawn) ? "GoblinCrossEyed" : "Goblin";
        }

        public static string GoblinVisualTuningFormKey(Pawn pawn)
        {
            string key = GoblinRenderFormKey(pawn);
            if (!NeedsJuvenileRenderCompensation(pawn))
            {
                return key;
            }

            switch (key)
            {
                case "HobgoblinCrossEyed":
                    return "HobgoblinCrossEyedChild";
                case "Hobgoblin":
                    return "HobgoblinChild";
                case "GoblinCrossEyed":
                    return "GoblinCrossEyedChild";
                default:
                    return "GoblinChild";
            }
        }

        public static bool IsSupportedTuningPawn(Pawn pawn)
        {
            return IsSupportedTuningPawn(pawn, IsGoblin(pawn));
        }

        internal static bool IsSupportedTuningPawn(Pawn pawn, bool isGoblin)
        {
            if (pawn?.story == null || pawn.RaceProps?.Humanlike != true || !pawn.DevelopmentalStage.Adult())
            {
                return false;
            }

            if (isGoblin || pawn.def == ThingDefOf.Human)
            {
                return true;
            }

            return !IsHARAlienRace(pawn);
        }

        public static string TuningUnsupportedReason(Pawn pawn)
        {
            if (pawn == null)
            {
                return "No pawn is selected.";
            }
            if (pawn.story == null || pawn.RaceProps?.Humanlike != true)
            {
                return "The selected pawn is not humanlike.";
            }
            if (!pawn.DevelopmentalStage.Adult())
            {
                return "The selected pawn is not an adult.";
            }
            if (IsHARAlienRace(pawn) && pawn.def != ThingDefOf.Human && !IsGoblin(pawn))
            {
                return "The selected pawn belongs to a HAR alien race, which is excluded.";
            }
            return "The selected pawn is not supported.";
        }

        public static string TuningProfileKey(Pawn pawn)
        {
            if (IsHobgoblin(pawn))
            {
                return "Hobgoblin";
            }
            if (IsGoblin(pawn))
            {
                return "Goblin";
            }
            return NonGoblinTuningProfileKey(pawn);
        }

        internal static string NonGoblinTuningProfileKey(Pawn pawn)
        {
            if (pawn?.def == ThingDefOf.Human)
            {
                return "Race_Human";
            }
            if (pawn?.genes?.Xenotype != null && pawn.genes.Xenotype != XenotypeDefOf.Baseliner)
            {
                return $"Xenotype_{pawn.genes.Xenotype.defName}";
            }
            return IsSupportedTuningPawn(pawn, isGoblin: false) ? $"Race_{pawn.def.defName}" : null;
        }

        public static string TuningProfileLabel(Pawn pawn)
        {
            if (IsHobgoblin(pawn))
            {
                return "Hobgoblin";
            }
            if (IsGoblin(pawn))
            {
                return "Goblin";
            }
            if (pawn?.def == ThingDefOf.Human)
            {
                return pawn.def.LabelCap.ToString();
            }
            if (pawn?.genes?.Xenotype != null && pawn.genes.Xenotype != XenotypeDefOf.Baseliner)
            {
                return pawn.genes.Xenotype.LabelCap.ToString();
            }
            return pawn?.def?.LabelCap.ToString() ?? "Unknown";
        }

        public static bool IsHARAlienRace(Pawn pawn)
        {
            return IsHARAlienRaceDef(pawn?.def);
        }

        public static bool IsHARAlienRaceDef(ThingDef def)
        {
            System.Type type = def?.GetType();
            while (type != null)
            {
                if (type.FullName == "AlienRace.ThingDef_AlienRace")
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        public static BodyTypeDef DesiredAdultBodyType(Pawn pawn)
        {
            // 실제 렌더 텍스처는 유전자/렌더 패치에서 교체하고, story.bodyType은 호환성을 위해 바닐라 체형으로 유지합니다.
            return IsHobgoblin(pawn) ? BodyTypeDefOf.Male : BodyTypeDefOf.Thin;
        }

        public static string DesiredBodyGraphicPath(Pawn pawn)
        {
            return IsHobgoblin(pawn)
                ? "Things/Pawn/MGBlike/Bodies/Naked_Male"
                : "Things/Pawn/MGBlike/Bodies/Naked_Thin";
        }

        public static string DesiredDessicatedBodyGraphicPath(Pawn pawn)
        {
            return IsHobgoblin(pawn)
                ? "Things/Pawn/MGBlike/Bodies/Dessicated/Dessicated_Male"
                : "Things/Pawn/MGBlike/Bodies/Dessicated/Dessicated_Thin";
        }

        public static float JuvenileAddonOffsetFactor(Pawn pawn)
        {
            if (!NeedsJuvenileRenderCompensation(pawn))
            {
                return 1f;
            }

            if (IsBabyLifeStage(pawn))
            {
                return BabyAddonOffsetFactor;
            }

            LifeStageDef lifeStage = pawn.ageTracker.CurLifeStage;
            float body = Mathf.Sqrt(Mathf.Max(0.01f, lifeStage.bodySizeFactor));
            float head = Mathf.Max(0.01f, lifeStage.headSizeFactor ?? 1f);
            return Mathf.Clamp(Mathf.Lerp(body, head, 0.85f) + 0.04f, 0.76f, 1f);
        }

        public static float JuvenileHeadOffsetFactor(Pawn pawn)
        {
            if (!NeedsJuvenileRenderCompensation(pawn))
            {
                return 1f;
            }

            if (IsBabyLifeStage(pawn))
            {
                return BabyHeadOffsetFactor;
            }

            return Mathf.Clamp(JuvenileAddonOffsetFactor(pawn) + 0.06f, 0.84f, 1f);
        }

        public static float JuvenileHeadLift(Pawn pawn)
        {
            return NeedsJuvenileRenderCompensation(pawn) ? JuvenileHeadLiftDefault : 0f;
        }

        // 한국어 의도: 바닐라는 청소년(13~17세)을 성인과 똑같은 크기로 그린다.
        // LifeStageDef의 bodySizeFactor(0.8)는 체력/식사량 같은 스탯이고 렌더 크기가 아니며,
        // 청소년 단계에는 어린이와 달리 bodyWidth/headSizeFactor/bodyDrawOffset이 아예 없다.
        // 그래서 고블린 청소년을 실제로 작게 보이려면 모드가 직접 축소해야 한다.
        // 어린이(12세 이하)는 바닐라가 이미 축소하므로 1을 돌려주어 이중 축소를 막는다.
        // 반환값은 오프셋과 스케일 양쪽에 함께 곱해져 폰 전체가 균일하게 줄어든다.
        public static float JuvenileRenderScaleFor(Pawn pawn)
        {
            if (!IsGoblin(pawn) || pawn?.ageTracker == null)
            {
                return 1f;
            }

            float age = pawn.ageTracker.AgeBiologicalYearsFloat;
            if (age < Patches.GoblinAgeUtility.JuvenileMinAgeYears
                || age >= Patches.GoblinAgeUtility.AdultAgeYears)
            {
                return 1f;
            }

            MUGBSettings settings = MUGBMod.Settings;
            if (settings == null)
            {
                return age < Patches.GoblinAgeUtility.JuvenileLateAgeYears
                    ? MUGBVisualTuningDefaults.JuvenileEarlyScale
                    : MUGBVisualTuningDefaults.JuvenileLateScale;
            }

            return age < Patches.GoblinAgeUtility.JuvenileLateAgeYears
                ? settings.juvenileEarlyScale
                : settings.juvenileLateScale;
        }

        public static float JuvenileAddonScaleFactor(Pawn pawn)
        {
            if (!NeedsJuvenileRenderCompensation(pawn))
            {
                return 1f;
            }

            if (IsBabyLifeStage(pawn))
            {
                return BabyAddonScaleFactor;
            }

            LifeStageDef lifeStage = pawn.ageTracker.CurLifeStage;
            return Mathf.Clamp((lifeStage.headSizeFactor ?? 1f) + 0.08f, 0.82f, 1f);
        }

        /*
            [아기 단계 렌더 보정 - 왜 별도 분기인가]

            위 세 함수의 계산식은 LifeStageDef의 bodySizeFactor/headSizeFactor에서 일반식으로 값을 뽑지만,
            마지막 Clamp의 하한(0.76 / 0.84 / 0.82)이 어린이 단계 값에 맞춰져 있습니다.
            아기 단계(bodySizeFactor 0.2, headSizeFactor 0.5)를 그 식에 그대로 넣으면 하한에 잘려
            머리보다 부속이 크게 남습니다.

            그런데 하한을 내리는 방식은 쓰지 않았습니다. 그러면 이미 잘 맞춰져 있는
            어린이/청소년/성인 고블린이 지나가는 코드가 바뀌기 때문입니다.
            계산 결과가 같다는 논증에 기대는 대신, 아기가 아닌 폰은 아래 상수를 아예 만나지 못하게
            분기로 갈라 두었습니다. 어린이 이상은 기존 식과 기존 하한을 글자 그대로 통과합니다.

            상수 값은 위 계산식에 아기 단계 수치를 넣어 클램프만 적용하지 않은 결과입니다.
            (0.532 / 0.592 / 0.58) 즉 어린이에서 이어지는 자연스러운 연장선입니다.
        */
        internal const float BabyAddonOffsetFactor = 0.53f;
        internal const float BabyHeadOffsetFactor = 0.59f;
        internal const float BabyAddonScaleFactor = 0.58f;

        // 아기 단계는 라이프스테이지로만 판정합니다. 나이로 판정하면 빠른 성장 직후
        // 라이프스테이지 캐시와 나이가 한 틱 어긋나는 순간에 보정이 튑니다.
        //
        // 옵션이 꺼져 있으면 고블린은 어린이로 태어나고 라이프스테이지 강제도 어린이로 가므로
        // 아기 고블린이 존재할 수 없습니다. 그때는 정적 bool 하나만 읽고 끝냅니다.
        // 이 함수는 부속 렌더 경로에서 프레임마다 불리므로, 옵션이 꺼진 사람에게는
        // 라이프스테이지 조회 비용조차 발생하지 않게 합니다.
        public static bool IsBabyLifeStage(Pawn pawn)
        {
            if (Patches.GoblinAgeUtility.SkipBabyStage)
            {
                return false;
            }

            return pawn?.ageTracker?.CurLifeStage?.developmentalStage == DevelopmentalStage.Baby;
        }

        private static bool NeedsJuvenileRenderCompensation(Pawn pawn)
        {
            if (!IsGoblin(pawn) || pawn?.ageTracker?.CurLifeStage == null)
            {
                return false;
            }

            // 한국어 의도: 고블린은 생물학적 18살부터 성인이다.
            // RimWorld 내부 lifeStage 캐시가 생성/빠른성장 직후 잠깐 어린이/청소년 계수를 들고 있어도,
            // 18살 이상 고블린 머리/의류/머리장비에는 어린이 보정을 다시 먹이지 않는다.
            if (pawn.ageTracker.AgeBiologicalYearsFloat >= Patches.GoblinAgeUtility.AdultAgeYears)
            {
                return false;
            }

            if (!pawn.DevelopmentalStage.Adult())
            {
                return true;
            }

            LifeStageDef lifeStage = pawn.ageTracker.CurLifeStage;
            return lifeStage.bodySizeFactor < 0.99f || (lifeStage.headSizeFactor ?? 1f) < 0.99f;
        }

        public static void EnforceGoblinStoryGraphics(Pawn pawn)
        {
            if (pawn?.story == null)
            {
                return;
            }

            bool dirty = false;
            if (!IsGoblin(pawn))
            {
                // 고블린 전용 외형이 비고블린에게 남아 있으면 유전자 제거/성별 변경 뒤 한 번 정리합니다.
                if (pawn.story.headType == MUGBDefOf.MUGB_GoblinHead)
                {
                    dirty |= pawn.story.TryGetRandomHeadFromSet(DefDatabase<HeadTypeDef>.AllDefs.Where(head => head.randomChosen));
                }

                if (pawn.DevelopmentalStage.Adult()
                    && (pawn.story.bodyType == MUGBDefOf.MUGB_GoblinThin || pawn.story.bodyType == MUGBDefOf.MUGB_HobgoblinMale))
                {
                    pawn.story.bodyType = pawn.gender == Gender.Female ? BodyTypeDefOf.Female : PawnGenerator.GetBodyTypeFor(pawn);
                    dirty = true;
                }

                if (dirty)
                {
                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                }
                return;
            }

            if (pawn.DevelopmentalStage.Adult())
            {
                BodyTypeDef desiredBody = DesiredAdultBodyType(pawn);
                if (pawn.story.bodyType != desiredBody)
                {
                    pawn.story.bodyType = desiredBody;
                    dirty = true;
                }
            }

            if (pawn.story.headType != MUGBDefOf.MUGB_GoblinHead)
            {
                pawn.story.headType = MUGBDefOf.MUGB_GoblinHead;
                dirty = true;
            }

            if (MUGBMod.Settings?.forceGoblinBaldAndNoBeard == true && pawn.story.hairDef != HairDefOf.Bald)
            {
                pawn.story.hairDef = HairDefOf.Bald;
                dirty = true;
            }

            if (MUGBMod.Settings?.forceGoblinBaldAndNoBeard == true && pawn.style != null && pawn.style.beardDef != BeardDefOf.NoBeard)
            {
                pawn.style.beardDef = BeardDefOf.NoBeard;
                dirty = true;
            }

            if (pawn.story.skinColorOverride != GoblinTextureSkinColor)
            {
                // 몸과 머리는 기본 설정에서 원본 텍스처 색을 그대로 사용합니다. story 피부색도
                // 같은 기준색으로 맞춰 손·신체 부위를 별도로 그리는 모드가 다른 초록색을 쓰지 않게 합니다.
                pawn.story.skinColorOverride = GoblinTextureSkinColor;
                dirty = true;
            }

            if (dirty)
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
    }
}
