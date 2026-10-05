using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    // Keep random event timing; favor enemy raids and increase their points by 30%.
    public class StorytellerCompProperties_RandomMain_KimDeokPal : StorytellerCompProperties_RandomMain
    {
        public StorytellerCompProperties_RandomMain_KimDeokPal()
        {
            compClass = typeof(StorytellerComp_RandomMain_KimDeokPal);
        }
    }

    public class StorytellerComp_RandomMain_KimDeokPal : StorytellerComp_RandomMain
    {
        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            foreach (FiringIncident firingIncident in base.MakeIntervalIncidents(target))
            {
                // ThreatBig 전체가 아니라 실제 Raid worker에만 적용한다.
                if (firingIncident?.def?.Worker is IncidentWorker_RaidEnemy && firingIncident.parms?.points > 0f)
                {
                    firingIncident.parms.points *= 1.30f;
                }

                yield return firingIncident;
            }
        }
    }

    [HarmonyPatch(typeof(StorytellerComp), "IncidentChanceFinal")]
    public static class StorytellerComp_IncidentChanceFinal_KimDeokPalPatch
    {
        public static void Postfix(StorytellerComp __instance, IncidentDef def, ref float __result)
        {
            if (__instance is StorytellerComp_RandomMain_KimDeokPal
                && Find.Storyteller?.def == MUGBDefOf.MUGB_KimDeokPal
                && def?.Worker is IncidentWorker_RaidEnemy)
            {
                __result *= 1.8f;
            }
        }
    }

    [HarmonyPatch(typeof(Storyteller), "InitializeStorytellerComps")]
    public static class Storyteller_InitializeStorytellerComps_KimDeokPalPatch
    {
        public static void Postfix(Storyteller __instance)
        {
            if (__instance?.def != MUGBDefOf.MUGB_KimDeokPal || __instance.storytellerComps == null)
            {
                return;
            }

            for (int i = 0; i < __instance.storytellerComps.Count; i++)
            {
                StorytellerComp comp = __instance.storytellerComps[i];
                if (comp is StorytellerComp_RandomMain randomMain && !(comp is StorytellerComp_RandomMain_KimDeokPal))
                {
                    __instance.storytellerComps[i] = new StorytellerComp_RandomMain_KimDeokPal
                    {
                        props = MakeKimDeokPalRandomMainProps((StorytellerCompProperties_RandomMain)randomMain.props)
                    };
                    continue;
                }

                if (comp is StorytellerComp_OnOffCycle && comp.props is StorytellerCompProperties_OnOffCycle onOffProps
                    && onOffProps.incident?.defName == "GiveQuest_Beggars")
                {
                    comp.props = MakeKimDeokPalBeggarQuestProps(onOffProps);
                }
            }
        }

        private static StorytellerCompProperties_RandomMain_KimDeokPal MakeKimDeokPalRandomMainProps(StorytellerCompProperties_RandomMain source)
        {
            StorytellerCompProperties_RandomMain_KimDeokPal result = new StorytellerCompProperties_RandomMain_KimDeokPal
            {
                minDaysPassed = source.minDaysPassed,
                minIncChancePopulationIntentFactor = source.minIncChancePopulationIntentFactor,
                allowedTargetTags = source.allowedTargetTags?.ToList(),
                disallowedTargetTags = source.disallowedTargetTags?.ToList(),
                enableIfAnyModActive = source.enableIfAnyModActive?.ToList(),
                disableIfAnyModActive = source.disableIfAnyModActive?.ToList(),
                // 랜디 전체 사건 빈도는 약 1.23배, ThreatBig 가중치는 약 1.44배로 올려 실제 대형 위협은 약 1.8배가 된다.
                mtbDays = 1.10f,
                maxThreatBigIntervalDays = 9f,
                randomPointsFactorRange = source.randomPointsFactorRange,
                skipThreatBigIfRaidBeacon = source.skipThreatBigIfRaidBeacon,
                spaceMtbDayFactor = source.spaceMtbDayFactor,
                spaceMinSpacingDays = source.spaceMinSpacingDays,
                categoryWeights = source.categoryWeights?.Select(entry => new IncidentCategoryEntry
                {
                    category = entry.category,
                    weight = entry.category == IncidentCategoryDefOf.ThreatBig ? entry.weight * (2.2f / 1.4f) : entry.weight
                }).ToList()
            };
            return result;
        }

        private static StorytellerCompProperties_OnOffCycle MakeKimDeokPalBeggarQuestProps(StorytellerCompProperties_OnOffCycle source)
        {
            return new StorytellerCompProperties_OnOffCycle
            {
                minDaysPassed = source.minDaysPassed,
                minIncChancePopulationIntentFactor = source.minIncChancePopulationIntentFactor,
                allowedTargetTags = source.allowedTargetTags?.ToList(),
                disallowedTargetTags = source.disallowedTargetTags?.ToList(),
                enableIfAnyModActive = source.enableIfAnyModActive?.ToList(),
                disableIfAnyModActive = source.disableIfAnyModActive?.ToList(),
                incident = source.incident,
                // 한국어 의도: 바닐라 60일 주기보다 자주, 그러나 연속 요청이 되지 않도록 24일 주기/12일 최소 간격.
                onDays = 24f,
                offDays = source.offDays,
                onDaysNoTreeConnectors = source.onDaysNoTreeConnectors,
                offDaysNoTreeConnectors = source.offDaysNoTreeConnectors,
                minSpacingDays = 12f,
                numIncidentsRange = source.numIncidentsRange,
                acceptFractionByDaysPassedCurve = source.acceptFractionByDaysPassedCurve,
                acceptPercentFactorPerThreatPointsCurve = source.acceptPercentFactorPerThreatPointsCurve,
                acceptPercentFactorPerProgressScoreCurve = source.acceptPercentFactorPerProgressScoreCurve,
                forceRaidEnemyBeforeDaysPassed = source.forceRaidEnemyBeforeDaysPassed
            };
        }
    }
}
