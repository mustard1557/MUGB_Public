using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    // 한국어 참고: 고블린 아기에게는 바닐라 이름짓기 편지를 띄우지 않습니다.
    //
    // 고블린은 한 번에 3~4마리가 태어나는데 바닐라 편지는 그중 한 마리만 이름을 묻습니다.
    // 게다가 고블린은 반나절 남짓의 아기 단계와 짧은 어린이 단계를 지나 16세에 유년기 백스토리를
    // 받는 구조라, 갓 태어난 시점에 정식 이름을 정하는 흐름 자체가 맞지 않습니다.
    // 그래서 아기 때는 "고블린 아기" 같은 임시 이름만 달고 있다가 16세에 무작위 고블린 이름을
    // 자동으로 받습니다. 편지는 여기서 막습니다.
    //
    // 편지가 만들어질 때만 호출되는 속성이라 틱 비용은 없습니다.
    [HarmonyPatch(typeof(ChoiceLetter_BabyBirth), nameof(ChoiceLetter_BabyBirth.CanShowInLetterStack), MethodType.Getter)]
    public static class ChoiceLetter_BabyBirth_CanShowInLetterStack_Patch
    {
        public static void Postfix(ChoiceLetter_BabyBirth __instance, ref bool __result)
        {
            if (!__result || __instance == null || PawnField == null)
            {
                return;
            }

            if (PawnField.GetValue(__instance) is Pawn baby && GoblinUtility.IsGoblin(baby))
            {
                __result = false;
            }
        }

        private static readonly System.Reflection.FieldInfo PawnField = AccessTools.Field(typeof(ChoiceLetter_BabyBirth), "pawn");
    }

    // Pure goblins are born directly into their short child stage. Their legal status is decided
    // once at age 13 instead of immediately through the vanilla Baby-to-Child letter.
    [HarmonyPatch(typeof(ChoiceLetter_BabyToChild), nameof(ChoiceLetter_BabyToChild.CanShowInLetterStack), MethodType.Getter)]
    public static class ChoiceLetter_BabyToChild_CanShowInLetterStack_GoblinPatch
    {
        public static void Postfix(ChoiceLetter_BabyToChild __instance, ref bool __result)
        {
            if (__result
                && __instance?.lookTargets.TryGetPrimaryTarget().Thing is Pawn child
                && GoblinUtility.IsGoblin(child))
            {
                __result = false;
            }
        }
    }
}
