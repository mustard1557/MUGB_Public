using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    /// <summary>
    /// 고블린에게 옷 손상 생각(ApparelDamaged)의 0단계만 뜨지 않게 합니다.
    ///
    /// 바닐라 ApparelDamaged는 생각 하나에 단계가 둘입니다.
    ///   0단계 ratty apparel(한국어 "해진 복장", 내구도 50% 미만) 기분 -3
    ///   1단계 tattered apparel(한국어 "낡은 옷", 내구도 20% 미만) 기분 -5
    ///
    /// 고블린은 원래 허름한 차림으로 다니는 종족이라 0단계까지 반응하면
    /// 무드도 경고창도 상시로 떠 있게 됩니다. 그래서 0단계는 아예 없던 일로 하고,
    /// 정말 다 떨어진 1단계만 남깁니다. 1단계 기분값은
    /// GoblinThoughtPatches의 GoblinSituationalMoodOverrides에서 -1로 조정합니다.
    ///
    /// 왜 이렇게 짰는가:
    /// - 예전에는 이 자리에서 바닐라 판정 로직을 통째로 다시 구현하고 __result를
    ///   무조건 덮어썼습니다. 그러면 (1) 바닐라가 로직을 바꿔도 우리가 옛 로직으로
    ///   되돌리고, (2) 같은 메서드를 건드린 다른 모드의 결과를 지우고,
    ///   (3) 조건 한 줄만 어긋나도 다른 모드의 옷 전체가 잘못 걸립니다.
    /// - 지금은 바닐라가 낸 결과를 읽기만 하고, 끄는 방향으로만 손댑니다.
    ///   Inactive를 Active로 바꾸는 경로가 없으므로 최악의 경우에도
    ///   "고블린에게 생각이 안 뜬다"로 끝나고 다른 모드에 번지지 않습니다.
    ///
    /// 방패는 여기서 처리하지 않습니다. MGB_ShieldDefs.xml에 careIfDamaged=false를
    /// 넣어 두어서 바닐라 판정이 단계를 계산하기 전에 알아서 건너뜁니다.
    /// </summary>
    [HarmonyPatch(typeof(ThoughtWorker_ApparelDamaged), "CurrentStateInternal")]
    public static class ThoughtWorker_ApparelDamaged_GoblinIgnoreFirstStagePatch
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (!__result.Active || __result.StageIndex != 0)
            {
                return;
            }

            if (GoblinUtility.IsGoblin(p))
            {
                __result = ThoughtState.Inactive;
            }
        }
    }
}
