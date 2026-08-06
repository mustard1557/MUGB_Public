using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    // 한국어 참고: 이데올로기가 없는 폰에게 이데올로기 관련 정신붕괴가 걸리지 않게 막습니다.
    //
    // 고블린은 생성 시 forceNoIdeo로 만들어져 Ideo가 없습니다. 그런데 바닐라 정신붕괴 추첨은
    // 이데올로기 유무를 확인하지 않고 IdeoChange를 고를 수 있고, MentalState_IdeoChange.PreStart가
    // 폰의 Ideo를 그대로 참조하다 NullReferenceException을 내며 매 틱 로그를 채웁니다.
    //
    // 고블린만이 아니라 "이데올로기가 없는 폰" 전체를 기준으로 막습니다. 이데올로기가 없으면
    // 애초에 이 붕괴가 성립하지 않으므로, 다른 모드의 이데올로기 없는 폰도 같은 예외를 피합니다.
    // 이데올로기가 있는 폰은 바닐라 그대로 동작합니다.
    //
    // 정신붕괴 후보를 고를 때만 호출되므로 틱 비용은 없습니다.
    [HarmonyPatch(typeof(MentalBreakWorker_IdeoChange), nameof(MentalBreakWorker_IdeoChange.BreakCanOccur))]
    public static class MentalBreakWorker_IdeoChange_BreakCanOccur_Patch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && pawn?.Ideo == null)
            {
                __result = false;
            }
        }
    }
}
