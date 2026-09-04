using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 종양 활성화액 주입을 고블린 남성만 수행할 수 있게 합니다.
    //
    // RecipeDef에는 시술자 종족을 거는 필드가 없습니다. genderPrerequisite와 humanlikeOnly는
    // 전부 환자 쪽이고, 시술자 쪽은 skillRequirements뿐입니다. 그래서 이 한 곳만 패치합니다.
    //
    // Bill.PawnAllowedToStartAnew를 고른 이유:
    //   바닐라가 계획서의 폰 제한(노예 전용 / 특정 폰 지정 등)을 판정하는 바로 그 지점입니다.
    //   WorkGiver_DoBill이 후보를 고를 때 이미 이 함수를 통과시키므로, 여기서 false를 내면
    //   자격 없는 폰은 애초에 그 작업을 잡지 않습니다. 작업 도중에 끊기거나 예약이 꼬이는
    //   경로가 생기지 않습니다.
    //
    // TargetMethods로 파생 클래스까지 훑는 이유:
    //   이 메서드는 virtual이고, Harmony로 기반 클래스만 패치하면 파생 클래스가 override한
    //   경우 가상 디스패치가 그쪽으로 가서 우리 패치가 아예 실행되지 않습니다. 의료 계획서는
    //   Bill_Medical이고 그쪽이 override하는지는 게임 버전에 따라 달라질 수 있는 자리라,
    //   "이 메서드를 직접 선언한 타입"을 전부 잡아 두면 어느 쪽이든 걸립니다.
    //   선언하지 않은 타입은 목록에 들어가지 않으므로 중복 패치도 나지 않습니다.
    //
    // 재료가 없는 시술인데 고블린 남성만 가능한 이유는 시술자 자신이 공급원이기 때문입니다.
    // 그래서 성별까지 봅니다 - 여성 고블린은 활성화액을 낼 수 없습니다.
    [HarmonyPatch]
    public static class Bill_PawnAllowedToStartAnew_SeedbedSurgeon_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            const string name = nameof(Bill.PawnAllowedToStartAnew);

            MethodInfo baseMethod = AccessTools.DeclaredMethod(typeof(Bill), name, new[] { typeof(Pawn) });
            if (baseMethod != null)
            {
                yield return baseMethod;
            }

            foreach (System.Type type in new[] { typeof(Bill_Medical), typeof(Bill_Production) })
            {
                MethodInfo declared = AccessTools.DeclaredMethod(type, name, new[] { typeof(Pawn) });
                if (declared != null)
                {
                    yield return declared;
                }
            }
        }

        public static void Postfix(Bill __instance, Pawn __0, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (__instance?.recipe == null || __instance.recipe != MUGB_SeedbedDefOf.MUGB_InjectTumorActivator)
            {
                return;
            }

            __result = __0 != null
                && __0.RaceProps?.Humanlike == true
                && __0.gender == Gender.Male
                && GoblinUtility.IsGoblin(__0);
        }
    }
}
