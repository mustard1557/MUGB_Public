using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    public static class GoblinRaidPerformanceUtility
    {
        private static readonly AccessTools.FieldRef<StoryWatcher_Adaptation, float> AdaptDaysRef =
            AccessTools.FieldRefAccess<StoryWatcher_Adaptation, float>("adaptDays");

        public static int PawnLossAdaptationPercent
        {
            get
            {
                MUGBSettings settings = MUGBMod.Settings;
                int value = IsKimDeokPalStorytellerActive
                    ? settings?.kimDeokPalPawnLossAdaptationPercent ?? 50
                    : settings?.pawnLossAdaptationPercent ?? 50;
                return Mathf.Clamp(value, 0, 100);
            }
        }

        // 한국어 의도: 김덕팔을 고른 경우에는 일반 스토리텔러 설정과 별개로 0/50/100% 중 하나를 사용한다.
        public static bool IsKimDeokPalStorytellerActive =>
            Find.Storyteller?.def == MUGBDefOf.MUGB_KimDeokPal;

        public static bool ShouldAdjustPawnLossAdaptation(Pawn pawn, AdaptationEvent ev)
        {
            if (PawnLossAdaptationPercent >= 100 || pawn?.RaceProps?.Humanlike != true)
            {
                return false;
            }

            // KO intent: 정착민/노예 손실로 습격점수가 크게 낮아지지 않게 한다.
            // 바닐라 StoryWatcher는 실제로 정착민 손실만 처리하므로, 노예는 포함돼도 손실량이 없으면 영향이 없다.
            if (!pawn.IsColonist && !pawn.IsSlaveOfColony)
            {
                return false;
            }

            return ev == AdaptationEvent.Died
                || ev == AdaptationEvent.Kidnapped
                || ev == AdaptationEvent.LostBecauseMapClosed;
        }

        public static float AdaptDays(StoryWatcher_Adaptation watcher)
        {
            return watcher == null ? 0f : AdaptDaysRef(watcher);
        }

        public static void SetAdaptDays(StoryWatcher_Adaptation watcher, float value)
        {
            if (watcher != null)
            {
                AdaptDaysRef(watcher) = value;
            }
        }

        public static bool ShouldBlockStagedGoblinRaidSocial(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Faction == null || !GoblinUtility.IsGoblin(pawn))
            {
                return false;
            }

            if (!pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            Lord lord = pawn.GetLord();
            return lord?.CurLordToil is LordToil_Stage;
        }
    }

    [HarmonyPatch(typeof(StoryWatcher_Adaptation), nameof(StoryWatcher_Adaptation.Notify_PawnEvent))]
    public static class StoryWatcher_Adaptation_NotifyPawnEvent_MUGBAdaptationPatch
    {
        public static void Prefix(StoryWatcher_Adaptation __instance, Pawn p, AdaptationEvent ev, out float __state)
        {
            __state = GoblinRaidPerformanceUtility.ShouldAdjustPawnLossAdaptation(p, ev)
                ? GoblinRaidPerformanceUtility.AdaptDays(__instance)
                : float.NaN;
        }

        public static void Postfix(StoryWatcher_Adaptation __instance, float __state)
        {
            if (float.IsNaN(__state))
            {
                return;
            }

            float after = GoblinRaidPerformanceUtility.AdaptDays(__instance);
            float loss = __state - after;
            if (loss <= 0f)
            {
                return;
            }

            float vanillaFactor = GoblinRaidPerformanceUtility.PawnLossAdaptationPercent / 100f;
            float restored = loss * (1f - vanillaFactor);
            GoblinRaidPerformanceUtility.SetAdaptDays(__instance, after + restored);
        }
    }

    [HarmonyPatch(typeof(SocialInteractionUtility), nameof(SocialInteractionUtility.CanInitiateInteraction))]
    public static class SocialInteractionUtility_CanInitiateInteraction_MUGBStagedGoblinRaidPatch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && GoblinRaidPerformanceUtility.ShouldBlockStagedGoblinRaidSocial(pawn))
            {
                __result = false;
            }
        }
    }
}
