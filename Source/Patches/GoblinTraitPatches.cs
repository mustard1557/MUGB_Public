using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    // 한국어 참고: '여성스러움'(MGB_Feminine) 특성의 자연 출현 토글입니다.
    // 폰 생성 시 특성 추첨은 TraitDef.GetGenderSpecificCommonality를 가중치로 사용하므로,
    // 토글이 꺼져 있으면 이 특성의 가중치만 0으로 만듭니다. 폰 생성 순간에만 호출되는 경로라 틱 비용이 없고,
    // 폰카인드 forcedTraits(시나리오 NPC 강제 부착)와 이미 특성을 가진 폰에는 영향을 주지 않습니다.
    [HarmonyPatch(typeof(TraitDef), nameof(TraitDef.GetGenderSpecificCommonality))]
    public static class TraitDef_GetGenderSpecificCommonality_FeminineTogglePatch
    {
        public static void Postfix(TraitDef __instance, ref float __result)
        {
            if (__result <= 0f || MUGBMod.Settings == null || MUGBMod.Settings.enableFeminineTrait)
            {
                return;
            }

            if (__instance.defName == "MGB_Feminine")
            {
                __result = 0f;
            }
        }
    }
}
