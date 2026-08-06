using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    public static class MUGBPassingGroupFrequencyUtility
    {
        private static readonly Dictionary<StorytellerCompProperties_FactionInteraction, float> OriginalRates =
            new Dictionary<StorytellerCompProperties_FactionInteraction, float>();

        public static void ApplyStorytellerFrequency()
        {
            int percent = Mathf.Clamp(MUGBMod.Settings?.passingGroupFrequencyPercent ?? 110, 0, 200);
            float configuredFactor = percent / 100f;

            foreach (StorytellerDef storyteller in DefDatabase<StorytellerDef>.AllDefsListForReading)
            {
                if (storyteller?.comps == null)
                {
                    continue;
                }

                float storytellerFactor = storyteller.defName == "MUGB_KimDeokPal" ? 1.15f : 1f;
                for (int i = 0; i < storyteller.comps.Count; i++)
                {
                    if (!(storyteller.comps[i] is StorytellerCompProperties_FactionInteraction props)
                        || props.incident?.defName != "TravelerGroup")
                    {
                        continue;
                    }

                    if (!OriginalRates.TryGetValue(props, out float originalRate))
                    {
                        originalRate = props.baseIncidentsPerYear;
                        OriginalRates.Add(props, originalRate);
                    }

                    props.baseIncidentsPerYear = originalRate * configuredFactor * storytellerFactor;
                }
            }
        }
    }

    public static class MUGBTravelerRelationGenerationContext
    {
        [ThreadStatic]
        private static int depth;

        public static bool Active => depth > 0;

        public static void Enter()
        {
            depth++;
        }

        public static void Exit()
        {
            depth = Math.Max(0, depth - 1);
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_TravelerGroup), "TryExecuteWorker")]
    public static class IncidentWorker_TravelerGroup_RelationPatch
    {
        public static void Prefix()
        {
            MUGBTravelerRelationGenerationContext.Enter();
        }

        public static Exception Finalizer(Exception __exception)
        {
            MUGBTravelerRelationGenerationContext.Exit();
            return __exception;
        }
    }
}
