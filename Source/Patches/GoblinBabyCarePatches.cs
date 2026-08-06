using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MUGB.Patches
{
    /*
        [파일 개요]
        고블린 아기는 정착지가 돌봐야 할 대상이 아닙니다.
        고블린은 한 번에 3~4마리가 태어나고 아기 단계는 반나절 남짓이라,
        요람으로 옮기고 온도를 맞춰주는 바닐라 육아 흐름을 그대로 태우면
        출산 한 번에 정착민 전원이 육아에 묶입니다. 고블린식으로 방치합니다.

        [적용 범위]
        전부 순수 고블린 아기(IsGoblin == true)에만 적용됩니다.
        일반 인간 아기, 다른 종족 아기, 하프고블린 아기는 첫 검사에서 걸러지고
        바닐라 코드가 손대지 않은 상태로 그대로 돕니다.

        [틱 비용]
        새로 도는 코드는 없습니다. 바닐라가 이미 호출하는 판정 함수와
        이미 갱신되는 경고 보고서에 검사 한 번씩만 얹었습니다.
        수유/젖먹임은 다른 경로라 건드리지 않았으므로 먹이려는 시도는 그대로 남습니다.

        [포대]
        바닐라는 아기 몸 위에 포대(swaddle) 렌더 노드를 한 장 덮습니다.
        고블린 아기는 포대에 싸여 있을 존재가 아니므로 그 노드만 끕니다.
        몸 자체는 이미 고블린 텍스처로 그려지고 있어 포대만 걷으면 됩니다.
    */
    [StaticConstructorOnStartup]
    public static class GoblinBabyCarePatches
    {
        private const string TemperatureCheckName = "BabyNeedsMovingForTemperatureReasons";

        // 아기 관련 경고와, 돌보지 않는 고블린 아기 때문에 가장 시끄러워질 체온 경고입니다.
        // 저체온/열사병 경고는 정착민 전원이 대상이므로, 원인이 전부 고블린 아기일 때만 지웁니다.
        // 정착민이나 성체 고블린이 하나라도 섞여 있으면 경고가 그대로 남습니다.
        private static readonly string[] BabyAlertTypeNames =
        {
            "RimWorld.Alert_AbandonedBaby",
            "RimWorld.Alert_NeedBabyCribs",
            "RimWorld.Alert_NoBabyFeeders",
            "RimWorld.Alert_LowBabyFood",
            "RimWorld.Alert_NoBabyFoodCaravan",
            "RimWorld.Alert_Hypothermia",
            "RimWorld.Alert_Heatstroke"
        };

        static GoblinBabyCarePatches()
        {
            try
            {
                Harmony harmony = new Harmony("mustard1557.mugb.goblin.babycare");
                PatchSafeTemperatureHauling(harmony);
                PatchBabyAlerts(harmony);
                PatchSwaddle(harmony);
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to apply the goblin baby care patches: " + e);
            }
        }

        // JobGiver_BringBabyToSafety(씽크트리 강제 작업)와 WorkGiver_BringBabyToSafety(육아 작업)가
        // 둘 다 이 판정을 거칩니다. 한 곳만 막으면 두 경로가 함께 멈춥니다.
        private static void PatchSafeTemperatureHauling(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ChildcareUtility), TemperatureCheckName);
            if (target == null || target.ReturnType != typeof(bool))
            {
                Log.Warning(
                    "[MUGB] ChildcareUtility." + TemperatureCheckName + " was not found. "
                    + "Colonists may still carry goblin babies to safe temperatures.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(GoblinBabyCarePatches), nameof(BabyNeedsMovingPrefix)));
        }

        public static bool BabyNeedsMovingPrefix(Pawn baby, ref bool __result)
        {
            if (!GoblinUtility.IsGoblin(baby))
            {
                return true;
            }

            // 고블린 아기는 추위/더위 때문에 옮길 대상이 아닙니다. 알아서 버티거나 죽습니다.
            __result = false;
            return false;
        }

        // 포대는 바닐라 인간 렌더 트리에 들어 있는 노드 하나(PawnRenderNodeWorker_Swaddle)입니다.
        // 그리기 판정만 끄므로 노드 구성이나 다른 폰의 렌더에는 영향이 없습니다.
        //
        // ShouldListOnGraph 대신 CanDrawNow를 고른 이유는, 노드를 그래프에서 빼면
        // 상위 노드의 자식 목록이 달라져 다른 모드의 렌더 패치와 어긋날 수 있기 때문입니다.
        // 그리지 않기만 하면 트리 모양은 바닐라와 같게 유지됩니다.
        private static void PatchSwaddle(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(PawnRenderNodeWorker_Swaddle),
                nameof(PawnRenderNodeWorker_Swaddle.CanDrawNow));
            if (target == null)
            {
                Log.Warning("[MUGB] PawnRenderNodeWorker_Swaddle.CanDrawNow was not found; goblin babies keep the vanilla swaddle.");
                return;
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(typeof(GoblinBabyCarePatches), nameof(SwaddleCanDrawNowPostfix)));
        }

        // 검사 순서가 곧 비용입니다.
        // 1) 바닐라가 이미 안 그린다고 했으면 아무것도 안 합니다. 아기가 아닌 폰은 전부 여기서 끝납니다.
        // 2) 아기 단계 옵션이 꺼져 있으면 고블린 아기가 존재할 수 없으니 정적 bool 하나로 끝냅니다.
        //    이때 남는 아기는 일반 아기뿐이고, 그쪽은 제노타입 조회 없이 통과합니다.
        // 3) 실제 제노타입 판정은 화면에 아기가 있고 옵션이 켜져 있을 때만 돕니다.
        public static void SwaddleCanDrawNowPostfix(PawnDrawParms parms, ref bool __result)
        {
            if (!__result || GoblinAgeUtility.SkipBabyStage)
            {
                return;
            }

            if (GoblinUtility.IsGoblin(parms.pawn))
            {
                __result = false;
            }
        }

        private static void PatchBabyAlerts(Harmony harmony)
        {
            HarmonyMethod postfix = new HarmonyMethod(typeof(GoblinBabyCarePatches), nameof(BabyAlertPostfix));
            for (int i = 0; i < BabyAlertTypeNames.Length; i++)
            {
                Type alertType = AccessTools.TypeByName(BabyAlertTypeNames[i]);
                if (alertType == null)
                {
                    continue;
                }

                MethodInfo getReport = AccessTools.Method(alertType, "GetReport");

                // 반드시 그 경고가 직접 선언한 메서드여야 합니다. 상속된 기반 메서드를 잡아 패치하면
                // 아기와 무관한 경고 전체에 걸려 버립니다.
                if (getReport == null
                    || getReport.DeclaringType != alertType
                    || getReport.ReturnType != typeof(AlertReport))
                {
                    Log.Warning("[MUGB] " + BabyAlertTypeNames[i] + ".GetReport was not found; that alert stays as vanilla.");
                    continue;
                }

                harmony.Patch(getReport, postfix: postfix);
            }
        }

        public static void BabyAlertPostfix(ref AlertReport __result)
        {
            if (__result.active && AllCulpritPawnsAreGoblinBabies(__result))
            {
                __result = AlertReport.Inactive;
            }
        }

        // 경고가 고블린 아기 때문에만 떠 있을 때만 참입니다.
        // 원인이 하나도 없거나 아기가 아닌 것이 섞여 있으면 거짓을 돌려 경고를 그대로 남깁니다.
        // 잘못 지우면 일반 아기 문제를 놓치게 되므로 판단은 한쪽으로만 기울입니다.
        private static bool AllCulpritPawnsAreGoblinBabies(AlertReport report)
        {
            if (!report.culpritsThings.NullOrEmpty() || !report.culpritsCaravans.NullOrEmpty())
            {
                return false;
            }

            bool sawGoblinBaby = false;

            List<Pawn> pawns = report.culpritsPawns;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (!IsGoblinBaby(pawns[i]))
                    {
                        return false;
                    }
                    sawGoblinBaby = true;
                }
            }

            List<GlobalTargetInfo> targets = report.culpritsTargets;
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!IsGoblinBaby(targets[i].Thing as Pawn))
                    {
                        return false;
                    }
                    sawGoblinBaby = true;
                }
            }

            if (report.culpritTarget.HasValue)
            {
                if (!IsGoblinBaby(report.culpritTarget.Value.Thing as Pawn))
                {
                    return false;
                }
                sawGoblinBaby = true;
            }

            return sawGoblinBaby;
        }

        private static bool IsGoblinBaby(Pawn pawn)
        {
            return pawn != null
                && GoblinUtility.IsGoblin(pawn)
                && GoblinUtility.IsBabyLifeStage(pawn);
        }
    }
}
