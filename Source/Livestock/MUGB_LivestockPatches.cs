using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Livestock
{
    // ── 기즈모 부착 ──────────────────────────────────────────────────
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Pawn_GetGizmos_LivestockPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MUGB_LivestockUtility.CanEverDesignate(__instance))
            {
                return;
            }

            __result = MUGB_LivestockGizmos.AppendTo(__result, __instance);
        }
    }

    // ── 지정 정리: 신분 변경 ─────────────────────────────────────────
    // 설계지침 5.4: 방면·정착민 승격·판매로 신분을 잃으면 지정을 지웁니다.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Pawn_GuestTracker_SetGuestStatus_LivestockPatch
    {
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn?.Map == null)
            {
                return;
            }

            MUGB_LivestockUtility.CleanupDesignationIfInvalid(___pawn);
            TryApplyDefaultPrisonerTreatment(___pawn);

            // 자동 규칙 평가 트리거(P4). 출생·노예화·방면·승격·판매가 전부 이 경로를
            // 지나므로, 주기 검사 없이 노예 수 변동을 잡을 수 있습니다.
            MUGB_LivestockAutoRules.Current?.Notify_LivestockChanged();
        }

        // 설계지침 7.2: 바닐라 기본 처우가 MaintainOnly라 전투 때마다 손이 갑니다. 이 설정을
        // 켜면 새로 잡힌 죄수가 곧바로 '고기용 가축'으로 시작합니다.
        //
        // 안전장치: 모드 설정 기본값은 꺼짐입니다(명시적 opt-in). 켜두고 잊으면 고스탯
        // 포로나 퀘스트 인질까지 도축대로 갑니다. 그래서 알림도 한 번 띄웁니다.
        private static void TryApplyDefaultPrisonerTreatment(Pawn pawn)
        {
            if (MUGBMod.Settings?.newPrisonersAsMeatLivestock != true)
            {
                return;
            }

            PrisonerInteractionModeDef meat = MUGB_LivestockDefOf.MUGB_MeatLivestock;
            if (meat == null || pawn.guest == null || !pawn.IsPrisonerOfColony)
            {
                return;
            }

            if (!MUGB_LivestockUtility.PreceptAllowsButchering() || !MUGB_LivestockUtility.CanEverDesignate(pawn))
            {
                return;
            }

            // 이미 다른 처우가 잡혀 있으면 건드리지 않습니다. 플레이어가 정한 것을 덮어쓰면
            // 안 됩니다. 새로 잡힌 죄수만 기본값 상태(MaintainOnly)로 들어옵니다.
            PrisonerInteractionModeDef current = pawn.guest.ExclusiveInteractionMode;
            if (current != null && current != PrisonerInteractionModeDefOf.MaintainOnly)
            {
                return;
            }

            pawn.guest.SetExclusiveInteraction(meat);
            Messages.Message(
                "MUGB_NewPrisonerSetToMeatLivestock".Translate(pawn.Named("PAWN")),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }
    }

    // ── 지정 정리: 사망 ──────────────────────────────────────────────
    //
    // 지정은 폰에 붙은 속성이 아니라 맵이 들고 있는 목록의 항목입니다. 그래서 대상이 우리
    // 손이 아닌 이유로 죽으면(습격, 출혈사) 아무도 그 항목을 지우지 않습니다. 바닐라의
    // 자동 정리(DesignationManager.Notify_BuildingDespawned)는 건물 전용이라 폰에는 안 걸립니다.
    //
    // 남아도 게임 동작에는 영향이 없지만(SpawnedDesignationsOfDef가 스폰된 것만 훑음)
    // 세이브에 쌓이고 로드 시 참조 경고가 날 수 있어 여기서 정리합니다.
    //
    // Prefix인 이유: Kill이 진행되면 폰이 디스폰되어 Map이 null이 됩니다. 죽기 직전이어야
    // 맵의 지정 목록에 접근할 수 있습니다.
    //
    // Pawn.DeSpawn을 잡지 않는 이유: 운반은 대상을 디스폰시킵니다. 거기에 훅을 걸면 우리
    // 운반자가 대상을 드는 순간 지정이 지워져 잡이 곧바로 취소됩니다.
    //
    // 비용: 모든 폰의 사망 시 1회, 휴머노이드 여부 확인 후 딕셔너리 조회 한 번입니다.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Pawn_Kill_LivestockDesignationCleanupPatch
    {
        public static void Prefix(Pawn __instance)
        {
            if (__instance?.Map == null || __instance.RaceProps?.Humanlike != true)
            {
                return;
            }

            Designation designation = __instance.Map.designationManager
                .DesignationOn(__instance, MUGB_LivestockDefOf.MUGB_SlaughterHumanlike);
            if (designation != null)
            {
                __instance.Map.designationManager.RemoveDesignation(designation);
            }

            // 죽었으니 자동 지정 기록에서도 뺍니다. 안 그러면 목록이 계속 자랍니다.
            MUGB_LivestockAutoRules.Current?.Forget(__instance);

            // 머릿수가 줄었으니 자동 규칙을 다시 봅니다.
            if (__instance.IsSlaveOfColony)
            {
                MUGB_LivestockAutoRules.Current?.Notify_LivestockChanged();
            }
        }
    }
}
