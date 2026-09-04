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
        private const float BeggarReplacementChance = 0.45f;

        public static bool Prefix(
            IncidentWorker_TravelerGroup __instance,
            IncidentParms parms,
            ref bool __result)
        {
            MUGBTravelerRelationGenerationContext.Enter();

            // Replace part of the vanilla passing-traveler incidents instead of firing an
            // additional event. The custom beggar incident uses this same worker base, so
            // the exact def check also prevents recursive replacement.
            if (Faction.OfPlayer?.def == MUGBDefOf.MUGB_PlayerGoblinFaction
                && __instance?.def?.defName == "TravelerGroup"
                && parms?.target is Map map
                && Rand.Chance(BeggarReplacementChance)
                && IncidentWorker_BeggarTravelerGroup.TryFire(map))
            {
                __result = true;
                return false;
            }

            return true;
        }

        public static Exception Finalizer(Exception __exception)
        {
            MUGBTravelerRelationGenerationContext.Exit();
            return __exception;
        }
    }
}
