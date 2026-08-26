using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    /*
        [파일 개요]
        고블린 상단이 끌고 온 노예를 호위가 아니라 판매 상품으로 취급하게 만듭니다.

        [문제]
        바닐라는 상단 인원의 역할을 PawnKind 하나로만 판단합니다.

            if (p.kindDef == PawnKindDefOf.Slave) return TraderCaravanRole.Chattel;
            ...
            return TraderCaravanRole.Guard;

        그리고 Pawn_TraderTracker.Goods는 Chattel만 상품 목록에 올립니다.

        MUGB 노예 중 바닐라 Slave PawnKind를 쓰는 것은 비고블린 노예 일부뿐입니다.
        암시장은 45% 확률로 HAR 종족 PawnKind를 쓰고, 고블린 노예는 MUGB_GoblinKind_Slave를
        씁니다. 그래서 그 노예들이 상품 목록에 뜨지 않고 호위병으로 따라다니다 그대로
        돌아가 버렸습니다.

        [해결]
        신분이 노예인 상단 인원은 PawnKind와 무관하게 상품으로 봅니다.
        상단 생성기가 이미 노예 신분을 찍어 두므로 판별용 값을 새로 저장하지 않습니다.

        [틱 비용]
        새로 도는 코드가 없습니다. 역할 판정은 호위 임무 배정과 위협 판정에서만 불리고,
        여기서는 결과가 호위일 때만 필드 두 개를 봅니다.

        [기존 세이브]
        역할은 저장되는 값이 아니라 매번 계산하는 값이라 이미 와 있는 상단에도 바로 적용됩니다.
    */
    [HarmonyPatch(typeof(TraderCaravanUtility), nameof(TraderCaravanUtility.GetTraderCaravanRole))]
    public static class TraderCaravanUtility_GetTraderCaravanRole_GoblinSlavePatch
    {
        public static void Postfix(Pawn p, ref TraderCaravanRole __result)
        {
            if (__result != TraderCaravanRole.Guard || p?.guest == null || !p.guest.IsSlave)
            {
                return;
            }

            // 내 정착지 노예는 상단 인원이 아니므로 건드리지 않습니다.
            if (p.IsSlaveOfColony)
            {
                return;
            }

            __result = TraderCaravanRole.Chattel;
        }
    }
}
