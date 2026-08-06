using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace MUGB.Patches
{
    // 한국어 참고: [NL] Facial Animation(Nals.FacialAnimation) 호환 패치입니다.
    // FA는 Human ThingDef 전체에 comp를 붙이므로 Human 기반 제노타입인 고블린도
    // FA의 인간형 애니메 얼굴로 교체되어 MUGB 전용 머리/부속과 겹쳐 깨집니다.
    // ShouldDrawRaceXenoType은 렌더트리 (재)구성 시에만 호출되고 틱마다 불리지 않으므로
    // 이 postfix의 런타임 비용은 사실상 0입니다. FA 미설치 시에는 아무것도 하지 않습니다.
    [StaticConstructorOnStartup]
    public static class FacialAnimationCompatPatch
    {
        public static bool Applied { get; private set; }

        static FacialAnimationCompatPatch()
        {
            try
            {
                Type settingsType = AccessTools.TypeByName("FacialAnimation.FacialAnimationModSettings");
                if (settingsType == null)
                {
                    return;
                }

                MethodInfo target = AccessTools.Method(settingsType, "ShouldDrawRaceXenoType");
                if (target == null)
                {
                    Log.Warning("[MUGB] Facial Animation is loaded but ShouldDrawRaceXenoType was not found. Goblin faces may be overridden by Facial Animation.");
                    return;
                }

                Harmony harmony = new Harmony("mustard1557.mugb.goblin.facialanimation");
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(FacialAnimationCompatPatch), nameof(ShouldDrawRaceXenoTypePostfix)));
                Applied = true;
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to apply the Facial Animation compatibility patch: " + e);
            }
        }

        private static void ShouldDrawRaceXenoTypePostfix(Pawn pawn, ref bool __result)
        {
            if (__result
                && !(MUGBMod.Settings?.allowFacialAnimationForGoblins ?? false)
                && GoblinUtility.IsGoblin(pawn))
            {
                __result = false;
            }
        }

        public static void RefreshGoblinFaces()
        {
            if (!Applied || Current.ProgramState != ProgramState.Playing)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (GoblinUtility.IsGoblin(pawn))
                    {
                        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    }
                }
            }
        }
    }
}
