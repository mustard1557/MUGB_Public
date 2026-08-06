using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MUGB
{
    // 한국어 의도: 이데올로기 작업장(WorkSite) 퀘스트가 뜨기 시작하는 시점을 10일에서 8일로 당긴다.
    // 고블린은 인육 갈망이 10일이면 바닥나므로, 바닐라 게이트를 그대로 두면 첫 갈망 위기와
    // 첫 작업장이 정확히 같은 시점에 겹친다. 이틀을 당겨 준비할 여유를 만든다.
    // 반복자 메서드라 트랜스파일러 대신 프리픽스로 본문을 통째로 대체한다.
    [HarmonyPatch(typeof(StorytellerComp_WorkSite), nameof(StorytellerComp_WorkSite.MakeIntervalIncidents))]
    public static class StorytellerCompWorkSite_MakeIntervalIncidents_Patch
    {
        private const int MinGameTicks = 480000; // 8일 (바닐라 600000 = 10일)

        public static bool Prefix(StorytellerComp_WorkSite __instance, IIncidentTarget target,
            ref IEnumerable<FiringIncident> __result)
        {
            __result = MakeIncidents(__instance, target);
            return false;
        }

        private static IEnumerable<FiringIncident> MakeIncidents(StorytellerComp_WorkSite comp, IIncidentTarget target)
        {
            if (Find.TickManager.TicksGame < MinGameTicks)
            {
                yield break;
            }

            StorytellerCompProperties_WorkSite props = comp.Props;
            if (props?.incident == null || !props.incident.TargetAllowed(target))
            {
                yield break;
            }

            float frequency = QuestNode_Root_WorkSite.BestAppearanceFrequency();
            if (frequency <= 0f || !Rand.MTBEventOccurs(props.baseMtbDays / frequency, 60000f, 1000f))
            {
                yield break;
            }

            IncidentParms parms = comp.GenerateParms(props.incident.category, target);
            if (props.incident.Worker.CanFireNow(parms))
            {
                yield return new FiringIncident(props.incident, comp, parms);
            }
        }
    }

    // 한국어 의도: 사이트 정보창의 인원수에 이 모드가 얹는 잡부를 포함시킨다.
    // 포함하지 않으면 "적 6명"이라고 안내해 놓고 실제로는 11명이 서 있게 된다.
    [HarmonyPatch(typeof(GenStep_WorkSitePawns), nameof(GenStep_WorkSitePawns.GetEnemiesCount))]
    public static class GenStepWorkSitePawns_GetEnemiesCount_Patch
    {
        public static void Postfix(SitePartParams parms, ref int __result)
        {
            __result += MUGBWorkSiteStragglerUtility.StragglerCountFor(parms);
        }
    }
}
