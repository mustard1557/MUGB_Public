using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace MUGB
{
    public static class GoblinIncestUtility
    {
        public static bool ShouldIgnoreIncestThought(Pawn pawn)
        {
            return GoblinUtility.IsGoblin(pawn);
        }

        public static bool ShouldIgnoreManualIncestBlock(Pawn initiator)
        {
            return GoblinUtility.IsGoblin(initiator);
        }

        public static int RestoredIncestOpinionOffset(Pawn observer, Pawn other)
        {
            if (!GoblinUtility.IsGoblin(observer) || observer == null || other == null)
            {
                return 0;
            }

            IEnumerable<PawnRelationDef> relations = PawnRelationUtility.GetRelations(observer, other);
            if (relations == null)
            {
                return 0;
            }

            int restored = 0;
            foreach (PawnRelationDef relation in relations)
            {
                if (relation == null || relation.incestOpinionOffset >= 0 || !relation.familyByBloodRelation)
                {
                    continue;
                }

                restored += (int)System.Math.Round((-relation.incestOpinionOffset) * 0.5f);
            }

            return restored;
        }
    }

    [HarmonyPatch(typeof(RelationsUtility), "Incestuous")]
    public static class RelationsUtility_Incestuous_GoblinPatch
    {
        public static void Postfix(Pawn one, Pawn two, ref bool __result)
        {
            if (__result && GoblinIncestUtility.ShouldIgnoreManualIncestBlock(one))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    public static class ThoughtWorker_Incestuous_CurrentSocialStateInternal_GoblinPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThoughtWorker_Incestuous), "CurrentSocialStateInternal");
        }

        public static void Postfix(Pawn pawn, Pawn other, ref ThoughtState __result)
        {
            if (__result.Active && GoblinIncestUtility.ShouldIgnoreIncestThought(pawn))
            {
                __result = ThoughtState.Inactive;
            }
        }
    }

    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory), typeof(ThoughtDef), typeof(Pawn), typeof(Precept))]
    public static class MemoryThoughtHandler_TryGainMemory_GoblinOrganMood_Patch
    {
        public static bool Prefix(MemoryThoughtHandler __instance, ThoughtDef def, Pawn otherPawn)
        {
            Pawn pawn = __instance?.pawn;
            if (GoblinSlaveMarriageUtility.TryReplaceSlaveMarriageSpouseDeathThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinSlaveMarriageUtility.TryReplaceSlaveMarriageLovinThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinChildThoughtUtility.TryReplaceGoblinChildDeathThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinChildThoughtUtility.TryReplaceGoblinBabyBornThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinChildThoughtUtility.ShouldBlockKilledGoblinChildThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinDeathThoughtUtility.TryReplaceGoblinSiblingDeathThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinDeathThoughtUtility.TryReplaceGoblinColonistDeathThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinDeathThoughtUtility.TryReplaceGoblinFriendDeathThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinSocialFightThoughtUtility.TryReplaceGoblinBrawlThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (GoblinSocialFightThoughtUtility.TryReplaceGoblinInsultThought(pawn, def, otherPawn))
            {
                return false;
            }

            if (MUGBSurgeryUtility.ShouldNullifyMugbThought(pawn, def))
            {
                return false;
            }

            if (GoblinThoughtMoodUtility.ShouldBlockGoblinMemory(pawn, def)
                || GoblinThoughtMoodUtility.ShouldBlockExtraCorpseMemory(pawn, def))
            {
                return false;
            }

            return !GoblinOrganHarvestThoughtUtility.ShouldBlock(pawn, def);
        }

        public static void Postfix(MemoryThoughtHandler __instance)
        {
            GoblinThoughtMoodUtility.CleanupGoblinMemories(__instance?.pawn);
        }
    }

    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory), typeof(Thought_Memory), typeof(Pawn))]
    public static class MemoryThoughtHandler_TryGainMemoryMemory_GoblinOrganMood_Patch
    {
        public static bool Prefix(MemoryThoughtHandler __instance, Thought_Memory newThought, Pawn otherPawn)
        {
            Pawn pawn = __instance?.pawn;
            Pawn thoughtOtherPawn = newThought?.otherPawn ?? otherPawn;
            if (GoblinSlaveMarriageUtility.TryReplaceSlaveMarriageSpouseDeathThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinSlaveMarriageUtility.TryReplaceSlaveMarriageLovinThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinChildThoughtUtility.TryReplaceGoblinChildDeathThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinChildThoughtUtility.TryReplaceGoblinBabyBornThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinChildThoughtUtility.ShouldBlockKilledGoblinChildThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinDeathThoughtUtility.TryReplaceGoblinSiblingDeathThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinDeathThoughtUtility.TryReplaceGoblinColonistDeathThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinDeathThoughtUtility.TryReplaceGoblinFriendDeathThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinSocialFightThoughtUtility.TryReplaceGoblinBrawlThought(pawn, newThought?.def, thoughtOtherPawn))
            {
                return false;
            }

            if (GoblinSocialFightThoughtUtility.TryReplaceGoblinInsultThought(pawn, newThought, thoughtOtherPawn))
            {
                return false;
            }

            GoblinDeathThoughtUtility.TryShortenMemoryDuration(pawn, newThought);
            if (MUGBSurgeryUtility.ShouldNullifyMugbThought(pawn, newThought?.def))
            {
                return false;
            }

            if (GoblinThoughtMoodUtility.ShouldBlockGoblinMemory(pawn, newThought?.def)
                || GoblinThoughtMoodUtility.ShouldBlockExtraCorpseMemory(pawn, newThought?.def))
            {
                return false;
            }

            return !GoblinOrganHarvestThoughtUtility.ShouldBlock(pawn, newThought?.def);
        }

        public static void Postfix(MemoryThoughtHandler __instance)
        {
            GoblinThoughtMoodUtility.CleanupGoblinMemories(__instance?.pawn);
        }
    }

    public static class GoblinSocialFightThoughtUtility
    {
        private const string VanillaAngeringFightDefName = "HadAngeringFight";
        private const string VanillaCatharticFightDefName = "HadCatharticFight";
        private const string VanillaInsultedDefName = "Insulted";
        private const string VanillaSlightedDefName = "Slighted";
        private const int SocialFightCooldownTicks = GenDate.TicksPerDay * 3 / 2;

        public static bool TryReplaceGoblinBrawlThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            string defName = thought?.defName;
            if ((defName != VanillaAngeringFightDefName && defName != VanillaCatharticFightDefName)
                || !GoblinUtility.IsGoblin(pawn)
                || !GoblinUtility.IsGoblin(otherPawn)
                || MUGBDefOf.MUGB_GoblinBrawlBond == null
                || MUGBDefOf.MUGB_GoblinBrawlRelief == null)
            {
                return false;
            }

            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinBrawlBond, otherPawn);
            memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinBrawlRelief);
            return true;
        }

        public static bool TryReplaceGoblinInsultThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            ThoughtDef replacement = GetGoblinInsultReplacement(pawn, thought, otherPawn);
            if (replacement == null)
            {
                return false;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(replacement, otherPawn);
            return true;
        }

        public static bool TryReplaceGoblinInsultThought(Pawn pawn, Thought_Memory thought, Pawn otherPawn)
        {
            ThoughtDef replacement = GetGoblinInsultReplacement(pawn, thought?.def, otherPawn);
            if (replacement == null)
            {
                return false;
            }

            Thought_Memory replacementMemory = ThoughtMaker.MakeThought(replacement) as Thought_Memory;
            if (replacementMemory == null)
            {
                return false;
            }

            replacementMemory.moodPowerFactor = thought.moodPowerFactor;
            if (thought is Thought_MemorySocial originalSocial
                && replacementMemory is Thought_MemorySocial replacementSocial)
            {
                float originalBaseOpinion = thought.CurStage.baseOpinionOffset;
                if (originalBaseOpinion != 0f)
                {
                    replacementSocial.opinionOffset *= originalSocial.opinionOffset / originalBaseOpinion;
                }
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(replacementMemory, otherPawn);
            return true;
        }

        private static ThoughtDef GetGoblinInsultReplacement(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || thought == null || otherPawn == null)
            {
                return null;
            }

            if (thought.defName == VanillaInsultedDefName)
            {
                return MUGBDefOf.MUGB_GoblinInsulted;
            }

            return thought.defName == VanillaSlightedDefName ? MUGBDefOf.MUGB_GoblinSlighted : null;
        }

        public static bool ShouldSuppressGoblinSocialFight(Pawn pawn, Pawn otherPawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || !GoblinUtility.IsGoblin(otherPawn))
            {
                return false;
            }

            return HasActiveSocialFightCooldown(pawn) || HasActiveSocialFightCooldown(otherPawn);
        }

        private static bool HasActiveSocialFightCooldown(Pawn pawn)
        {
            ThoughtDef cooldownDef = MUGBDefOf.MUGB_GoblinBrawlRelief;
            if (cooldownDef == null)
            {
                return false;
            }

            Thought_Memory memory = pawn.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(cooldownDef);
            return memory != null && memory.age < SocialFightCooldownTicks;
        }
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.SocialFightPossible))]
    public static class PawnInteractionsTracker_SocialFightPossible_GoblinCooldown_Patch
    {
        public static bool Prefix(Pawn ___pawn, Pawn otherPawn, ref bool __result)
        {
            if (!GoblinSocialFightThoughtUtility.ShouldSuppressGoblinSocialFight(___pawn, otherPawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    public static class GoblinOrganHarvestThoughtUtility
    {
        private static readonly HashSet<string> OrganHarvestThoughtDefNames = new HashSet<string>
        {
            "HarvestedOrgan_Bloodlust",
            "KnowGuestOrganHarvested",
            "KnowColonistOrganHarvested",
            "MyOrganHarvested"
        };

        public static bool ShouldBlock(Pawn pawn, ThoughtDef thought)
        {
            return GoblinUtility.IsGoblin(pawn)
                && thought != null
                && OrganHarvestThoughtDefNames.Contains(thought.defName);
        }
    }

    public static class GoblinChildThoughtUtility
    {
        private const string VanillaBabyBornDefName = "BabyBorn";

        private static readonly HashSet<string> ChildDeathThoughtDefNames = new HashSet<string>
        {
            "MySonDied",
            "MyDaughterDied"
        };

        public static bool TryReplaceGoblinBabyBornThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (!GoblinUtility.IsGoblin(pawn)
                || thought?.defName != VanillaBabyBornDefName
                || MUGBDefOf.MUGB_GoblinBabyBorn == null)
            {
                return false;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinBabyBorn, otherPawn);
            return true;
        }

        public static bool TryReplaceGoblinChildDeathThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (pawn == null || thought == null || otherPawn == null || GoblinUtility.IsGoblin(pawn) || !GoblinUtility.IsGoblin(otherPawn))
            {
                return false;
            }

            if (!ChildDeathThoughtDefNames.Contains(thought.defName))
            {
                return false;
            }

            if (otherPawn.relations?.DirectRelationExists(PawnRelationDefOf.Parent, pawn) != true)
            {
                return false;
            }

            if (MUGBDefOf.MUGB_GoblinChildDiedRelief != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinChildDiedRelief, otherPawn);
            }
            return true;
        }

        public static bool ShouldBlockKilledGoblinChildThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            return pawn != null
                && thought?.defName == "KilledChild"
                && !GoblinUtility.IsGoblin(pawn)
                && GoblinUtility.IsGoblin(otherPawn)
                && otherPawn.DevelopmentalStage.Juvenile();
        }

        public static int NonGoblinEnslavedChildrenCount(Pawn parent)
        {
            if (parent?.relations?.Children == null)
            {
                return 0;
            }

            return parent.relations.Children.Count(child => child.DevelopmentalStage.Juvenile() && !child.Dead && child.IsSlave && !GoblinUtility.IsGoblin(child));
        }

        public static int NonGoblinChildrenWithMoodCount(Pawn parent, FloatRange moodRange)
        {
            if (parent?.relations?.Children == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Pawn child in parent.relations.Children)
            {
                if (!GoblinUtility.IsGoblin(child)
                    && ThoughtWorker_RelatedChildMoodBase.InSameMapOrCaravan(parent, child)
                    && ThoughtWorker_RelatedChildMoodBase.IsChildWithMood(child, moodRange))
                {
                    count++;
                }
            }
            return count;
        }

        public static int NonGoblinYoungstersWithMoodInColony(Pawn pawn, FloatRange moodRange)
        {
            if (pawn == null)
            {
                return 0;
            }

            IEnumerable<Pawn> pawns = Enumerable.Empty<Pawn>();
            if (pawn.Spawned)
            {
                pawns = pawn.Map.mapPawns.FreeColonistsSpawned;
            }
            else
            {
                Caravan caravan = pawn.GetCaravan();
                if (caravan != null)
                {
                    pawns = caravan.PawnsListForReading.Where(candidate => candidate.IsColonist);
                }
            }

            int count = 0;
            foreach (Pawn candidate in pawns)
            {
                if (!GoblinUtility.IsGoblin(candidate)
                    && candidate.RaceProps.Humanlike
                    && !candidate.DevelopmentalStage.Adult()
                    && !PawnRelationDefOf.Parent.Worker.InRelation(candidate, pawn)
                    && ThoughtWorker_RelatedChildMoodBase.IsChildWithMood(candidate, moodRange))
                {
                    count++;
                }
            }
            return count;
        }
    }

    [HarmonyPatch(typeof(ThoughtWorker_MyChildEnslaved), nameof(ThoughtWorker_MyChildEnslaved.MoodMultiplier))]
    public static class ThoughtWorker_MyChildEnslaved_MoodMultiplier_GoblinChildPatch
    {
        public static void Postfix(Pawn p, ref float __result, ThoughtDef ___def)
        {
            int count = GoblinChildThoughtUtility.NonGoblinEnslavedChildrenCount(p);
            __result = UnityEngine.Mathf.Min(___def.stackLimit, count);
        }
    }

    [HarmonyPatch]
    public static class ThoughtWorker_MyChildEnslaved_CurrentState_GoblinChildPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThoughtWorker_MyChildEnslaved), "CurrentStateInternal");
        }

        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (__result.Active && GoblinChildThoughtUtility.NonGoblinEnslavedChildrenCount(p) <= 0)
            {
                __result = ThoughtState.Inactive;
            }
        }
    }

    [HarmonyPatch]
    public static class ThoughtWorker_RelatedChildMoodBase_CurrentState_GoblinChildPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThoughtWorker_RelatedChildMoodBase), "CurrentStateInternal");
        }

        public static void Postfix(ThoughtWorker __instance, Pawn p, ref ThoughtState __result)
        {
            if (!__result.Active)
            {
                return;
            }

            if (__instance is ThoughtWorker_MyChildrenHappy)
            {
                if (GoblinChildThoughtUtility.NonGoblinChildrenWithMoodCount(p, ThoughtWorker_MyChildHappy.HappyMoodRange) <= 1)
                {
                    __result = ThoughtState.Inactive;
                }
                return;
            }
            if (__instance is ThoughtWorker_MyChildHappy)
            {
                if (GoblinChildThoughtUtility.NonGoblinChildrenWithMoodCount(p, ThoughtWorker_MyChildHappy.HappyMoodRange) != 1)
                {
                    __result = ThoughtState.Inactive;
                }
                return;
            }
            if (__instance is ThoughtWorker_MyChildrenSad)
            {
                if (GoblinChildThoughtUtility.NonGoblinChildrenWithMoodCount(p, ThoughtWorker_MyChildSad.SadMoodRange) <= 1)
                {
                    __result = ThoughtState.Inactive;
                }
                return;
            }
            if (__instance is ThoughtWorker_MyChildSad)
            {
                if (GoblinChildThoughtUtility.NonGoblinChildrenWithMoodCount(p, ThoughtWorker_MyChildSad.SadMoodRange) != 1)
                {
                    __result = ThoughtState.Inactive;
                }
            }
        }
    }

    [HarmonyPatch]
    public static class ThoughtWorker_YoungstersMoodBase_CurrentState_GoblinChildPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThoughtWorker_YoungstersMoodBase), "CurrentStateInternal");
        }

        public static void Postfix(ThoughtWorker __instance, Pawn p, ref ThoughtState __result)
        {
            if (!__result.Active)
            {
                return;
            }

            FloatRange moodRange;
            if (__instance is ThoughtWorker_YoungstersHappy)
            {
                moodRange = ThoughtWorker_MyChildHappy.HappyMoodRange;
            }
            else if (__instance is ThoughtWorker_YoungstersSad)
            {
                moodRange = ThoughtWorker_MyChildSad.SadMoodRange;
            }
            else
            {
                return;
            }

            if (GoblinChildThoughtUtility.NonGoblinYoungstersWithMoodInColony(p, moodRange) <= 0)
            {
                __result = ThoughtState.Inactive;
            }
        }
    }

    [HarmonyPatch]
    public static class ThoughtWorker_YoungstersMoodBase_MoodMultiplier_GoblinChildPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThoughtWorker_YoungstersMoodBase), nameof(ThoughtWorker_YoungstersMoodBase.MoodMultiplier));
        }

        public static void Postfix(ThoughtWorker __instance, Pawn p, ref float __result, ThoughtDef ___def)
        {
            FloatRange moodRange;
            if (__instance is ThoughtWorker_YoungstersHappy)
            {
                moodRange = ThoughtWorker_MyChildHappy.HappyMoodRange;
            }
            else if (__instance is ThoughtWorker_YoungstersSad)
            {
                moodRange = ThoughtWorker_MyChildSad.SadMoodRange;
            }
            else
            {
                return;
            }

            __result = UnityEngine.Mathf.Min(___def.stackLimit, GoblinChildThoughtUtility.NonGoblinYoungstersWithMoodInColony(p, moodRange));
        }
    }

    [HarmonyPatch(typeof(Thought_Memory), nameof(Thought_Memory.MoodOffset))]
    public static class Thought_Memory_MoodOffset_GoblinDeathSensitivityPatch
    {
        public static void Postfix(Thought_Memory __instance, ref float __result)
        {
            if (GoblinDeathThoughtUtility.TryAdjustSiblingDeath(__instance?.pawn, __instance?.def, ref __result))
            {
                return;
            }

            if (GoblinThoughtMoodUtility.TryAdjustGoblinMemoryMood(__instance?.pawn, __instance?.def, ref __result))
            {
                return;
            }

            GoblinThoughtMoodUtility.TryClampGoblinIdeologyMood(__instance, ref __result);

            if (__result >= 0f || !GoblinUtility.IsGoblin(__instance?.pawn) || !GoblinDeathThoughtUtility.ShouldQuarter(__instance.def))
            {
                return;
            }

            __result *= 0.25f;
        }
    }

    [HarmonyPatch(typeof(Thought), nameof(Thought.MoodOffset))]
    public static class Thought_MoodOffset_GoblinNeedsPatch
    {
        public static void Postfix(Thought __instance, ref float __result)
        {
            GoblinThoughtMoodUtility.TryAdjustGoblinSituationalMood(__instance?.pawn, __instance?.def, ref __result);
            GoblinThoughtMoodUtility.TryClampGoblinIdeologyMood(__instance?.pawn, __instance?.def, ref __result);
        }
    }

    [HarmonyPatch(typeof(Thought), nameof(Thought.Description), MethodType.Getter)]
    public static class Thought_Description_GoblinVoicePatch
    {
        public static void Postfix(Thought __instance, ref string __result)
        {
            GoblinThoughtDescriptionUtility.TryReplace(__instance, ref __result);
        }
    }

    public static class GoblinThoughtDescriptionUtility
    {
        private static readonly Dictionary<string, string> DescriptionKeys = new Dictionary<string, string>
        {
            { "AteLavishMeal", "MUGB_GoblinThought_AteLavishMeal" },
            { "AteFineMeal", "MUGB_GoblinThought_AteFineMeal" },
            { "AteRawFood", "MUGB_GoblinThought_AteRawFood" },
            { "AteKibble", "MUGB_GoblinThought_AteKibble" },
            { "AteCorpse", "MUGB_GoblinThought_AteCorpse" },
            { "AteHumanlikeMeatDirect", "MUGB_GoblinThought_AteHumanlikeMeatDirect" },
            { "AteHumanlikeMeatAsIngredient", "MUGB_GoblinThought_AteHumanlikeMeatAsIngredient" },
            { "AteInsectMeatDirect", "MUGB_GoblinThought_AteInsectMeatDirect" },
            { "AteInsectMeatAsIngredient", "MUGB_GoblinThought_AteInsectMeatAsIngredient" },
            { "AteRottenFood", "MUGB_GoblinThought_AteRottenFood" },
            { "NeedRest", "MUGB_GoblinThought_NeedRest" },
            { "SleptOnGround", "MUGB_GoblinThought_SleptOnGround" },
            { "SleptInCold", "MUGB_GoblinThought_SleptInCold" },
            { "SleptInHeat", "MUGB_GoblinThought_SleptInHeat" },
            { "SleepDisturbed", "MUGB_GoblinThought_SleepDisturbed" },
            { "AteWithoutTable", "MUGB_GoblinThought_AteWithoutTable" },
            { "EnvironmentDark", "MUGB_GoblinThought_EnvironmentDark" },
            { "KnowColonistDied", "MUGB_GoblinThought_KnowColonistDied" },
            { "ColonistBanished", "MUGB_GoblinThought_ColonistBanished" },
            { "ColonistBanishedToDie", "MUGB_GoblinThought_ColonistBanished" },
            { "PrisonerBanishedToDie", "MUGB_GoblinThought_ColonistBanished" },
            { "WitnessedDeathAlly", "MUGB_GoblinThought_WitnessedDeathAlly" },
            { "WitnessedDeathNonAlly", "MUGB_GoblinThought_WitnessedDeathNonAlly" },
            { "ObservedLayingCorpse", "MUGB_GoblinThought_ObservedLayingCorpse" },
            { "ObservedLayingRottingCorpse", "MUGB_GoblinThought_ObservedLayingRottingCorpse" },
            { "ButcheredHumanlikeCorpse", "MUGB_GoblinThought_ButcheredHumanlikeCorpse" },
            { "KnowButcheredHumanlikeCorpse", "MUGB_GoblinThought_KnowButcheredHumanlikeCorpse" },
            { "PawnWithGoodOpinionDied", "MUGB_GoblinThought_PawnWithGoodOpinionDied" },
            { "PawnWithBadOpinionDied", "MUGB_GoblinThought_PawnWithBadOpinionDied" },
            { "MySonDied", "MUGB_GoblinThought_MyChildDied" },
            { "MyDaughterDied", "MUGB_GoblinThought_MyChildDied" },
            { "MyHusbandDied", "MUGB_GoblinThought_MySpouseDied" },
            { "MyWifeDied", "MUGB_GoblinThought_MySpouseDied" },
            { "MyLoverDied", "MUGB_GoblinThought_MySpouseDied" },
            { "MyFianceDied", "MUGB_GoblinThought_MySpouseDied" },
            { "MyFianceeDied", "MUGB_GoblinThought_MySpouseDied" },
            { "MyFatherDied", "MUGB_GoblinThought_MyFatherDied" },
            { "MyMotherDied", "MUGB_GoblinThought_MyMotherDied" },
            { "MyBrotherDied", "MUGB_GoblinThought_MySiblingDied" },
            { "MySisterDied", "MUGB_GoblinThought_MySiblingDied" },
            { "MyHalfSiblingDied", "MUGB_GoblinThought_MySiblingDied" },
            { "ApparelDamaged", "MUGB_GoblinThought_ApparelDamaged" },
            { "DeadMansApparel", "MUGB_GoblinThought_DeadMansApparel" },
            { "HumanLeatherApparelSad", "MUGB_GoblinThought_HumanLeatherApparel" },
            { "GotSomeLovin", "MUGB_GoblinThought_GotSomeLovin" },
            { "RebuffedMyRomanceAttemptMood", "MUGB_GoblinThought_RebuffedMyRomanceAttempt" },
            { "WasEnslaved", "MUGB_GoblinThought_WasEnslaved" },
            { "ObservedTerror", "MUGB_GoblinThought_ObservedTerror" }
        };

        public static void TryReplace(Thought thought, ref string description)
        {
            if (!GoblinUtility.IsGoblin(thought?.pawn) || thought.def == null)
            {
                return;
            }

            string key;
            if (thought.def.defName == "NeedBeauty")
            {
                key = thought.CurStageIndex <= 2
                    ? "MUGB_GoblinThought_NeedBeautyUgly"
                    : "MUGB_GoblinThought_NeedBeautyBeautiful";
            }
            else if (thought.def.defName == "NeedRoomSize")
            {
                if (thought.CurStageIndex > 1)
                {
                    return;
                }
                key = "MUGB_GoblinThought_NeedRoomSizeCramped";
            }
            else if (!DescriptionKeys.TryGetValue(thought.def.defName, out key))
            {
                return;
            }

            description = key.Translate();
        }
    }

    public static class GoblinThoughtMoodUtility
    {
        private const int MaxCorpseMemories = 2;
        private static readonly FieldInfo ThoughtMemorySourcePreceptField = AccessTools.Field(typeof(Thought_Memory), "sourcePrecept");

        private static readonly HashSet<string> BlockedGoblinMemoryThoughts = new HashSet<string>
        {
            "KilledChild",
            "MyChildHappy",
            "MyChildrenHappy",
            "MyChildSad",
            "MyChildrenSad",
            "YoungstersHappy",
            "YoungstersSad",
            "EnslavedChild",
            "BabySick",
            "ChildInGrowthVat",
            "MyParentHappy",
            "MyParentsHappy",
            "CryingBaby",
            "MyCryingBaby",
            "GigglingBaby",
            "MyGigglingBaby"
        };

        private static readonly HashSet<string> BlockedGoblinSituationalThoughts = new HashSet<string>
        {
            "NeedLearning",
            "MyChildHappy",
            "MyChildrenHappy",
            "MyChildSad",
            "MyChildrenSad",
            "YoungstersHappy",
            "YoungstersSad",
            "EnslavedChild",
            "BabySick",
            "ChildInGrowthVat",
            "MyParentHappy",
            "MyParentsHappy"
        };

        private static readonly HashSet<string> GoblinCorpseThoughts = new HashSet<string>
        {
            "ObservedLayingCorpse",
            "ObservedLayingRottingCorpse"
        };

        private static readonly HashSet<string> GoblinBanishedThoughts = new HashSet<string>
        {
            "ColonistBanished",
            "ColonistBanishedToDie",
            "PrisonerBanishedToDie",
            "DeniedJoining"
        };

        private static readonly Dictionary<string, float> GoblinMemoryMoodOverrides = new Dictionary<string, float>
        {
            { "AteRawFood", 0f },
            { "AteKibble", -3f },
            { "AteCorpse", 3f },
            { "AteRottenFood", -3f },
            { "SleptOnGround", -2f },
            { "SleptInCold", -8f },
            { "SleptInHeat", 0f },
            { "SleepDisturbed", -2f },
            { "AteWithoutTable", -1f },
            { "GotSomeLovin", 15f },
            { "RebuffedMyRomanceAttemptMood", -4f },
            { "ObservedTerror", -4f },
            { "WasEnslaved", -25f },
            { "WitnessedDeathAlly", -1f },
            { "WitnessedDeathNonAlly", 2f },
            { "ButcheredHumanlikeCorpse", 2f },
            { "KnowButcheredHumanlikeCorpse", 1f },
            { "PawnWithBadOpinionDied", 15f },
            { "MySonDied", -5f },
            { "MyDaughterDied", -5f },
            { "MyHusbandDied", -1f },
            { "MyWifeDied", -1f },
            { "MyLoverDied", -1f },
            { "MyFianceDied", -1f },
            { "MyFianceeDied", -1f },
            { "MyFatherDied", -1f },
            { "MyMotherDied", -1f }
        };

        private static readonly Dictionary<string, float> GoblinSituationalMoodOverrides = new Dictionary<string, float>
        {
            { "EnvironmentDark", 0f },
            // 0단계(해진 복장)는 ThoughtWorker_ApparelDamaged_GoblinIgnoreFirstStagePatch에서
            // 아예 끄기 때문에, 고블린에게 켜질 수 있는 단계는 1단계(낡은 옷)뿐입니다.
            // 따라서 여기 값 하나가 곧 1단계 기분값입니다.
            { "ApparelDamaged", -1f },
            { "DeadMansApparel", 0f },
            { "HumanLeatherApparelSad", 8f }
        };

        public static bool ShouldBlockGoblinMemory(Pawn pawn, ThoughtDef thought)
        {
            return GoblinUtility.IsGoblin(pawn)
                && thought != null
                && BlockedGoblinMemoryThoughts.Contains(thought.defName);
        }

        public static bool ShouldBlockExtraCorpseMemory(Pawn pawn, ThoughtDef thought)
        {
            if (!GoblinUtility.IsGoblin(pawn) || thought == null || !GoblinCorpseThoughts.Contains(thought.defName))
            {
                return false;
            }

            int count = pawn.needs?.mood?.thoughts?.memories?.Memories?.Count(memory => memory?.def == thought) ?? 0;
            return count >= MaxCorpseMemories;
        }

        public static bool TryAdjustGoblinMemoryMood(Pawn pawn, ThoughtDef thought, ref float moodOffset)
        {
            if (!GoblinUtility.IsGoblin(pawn) || thought == null)
            {
                return false;
            }

            if (BlockedGoblinMemoryThoughts.Contains(thought.defName))
            {
                moodOffset = 0f;
                return true;
            }

            if (GoblinBanishedThoughts.Contains(thought.defName))
            {
                moodOffset = 1f;
                return true;
            }

            if (thought.defName == "ObservedLayingCorpse")
            {
                moodOffset = -1f;
                return true;
            }

            if (thought.defName == "ObservedLayingRottingCorpse")
            {
                moodOffset = -3f;
                return true;
            }

            if (GoblinMemoryMoodOverrides.TryGetValue(thought.defName, out float overriddenMood))
            {
                moodOffset = overriddenMood;
                return true;
            }

            return false;
        }

        public static bool TryAdjustGoblinSituationalMood(Pawn pawn, ThoughtDef thought, ref float moodOffset)
        {
            if (!GoblinUtility.IsGoblin(pawn) || thought == null)
            {
                return false;
            }

            if (BlockedGoblinSituationalThoughts.Contains(thought.defName))
            {
                moodOffset = 0f;
                return true;
            }

            if (GoblinSituationalMoodOverrides.TryGetValue(thought.defName, out float overriddenMood))
            {
                moodOffset = overriddenMood;
                return true;
            }

            if (thought.defName == "NeedBeauty")
            {
                moodOffset = moodOffset > 0f ? 1f : 0f;
                return true;
            }

            if (thought.defName == "NeedRoomSize" && moodOffset < 0f)
            {
                moodOffset = 0f;
                return true;
            }

            if (thought.defName == "NeedFood" && moodOffset < 0f)
            {
                moodOffset -= 5f;
                return true;
            }

            return false;
        }

        public static void TryClampGoblinIdeologyMood(Thought_Memory memory, ref float moodOffset)
        {
            if (memory == null || moodOffset >= -5f || !GoblinUtility.IsGoblin(memory.pawn))
            {
                return;
            }

            if (ThoughtMemorySourcePreceptField?.GetValue(memory) is Precept)
            {
                moodOffset = -5f;
            }
        }

        public static void TryClampGoblinIdeologyMood(Pawn pawn, ThoughtDef thought, ref float moodOffset)
        {
            if (!GoblinUtility.IsGoblin(pawn) || thought == null || moodOffset >= -5f)
            {
                return;
            }

            string workerName = thought.workerClass?.Name;
            if (!workerName.NullOrEmpty() && workerName.Contains("Precept"))
            {
                moodOffset = -5f;
            }
        }

        public static void CleanupGoblinMemories(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            List<Thought_Memory> list = memories?.Memories;
            if (list == null)
            {
                return;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Thought_Memory memory = list[i];
                if (memory?.def == null)
                {
                    continue;
                }

                if (BlockedGoblinMemoryThoughts.Contains(memory.def.defName))
                {
                    memories.RemoveMemory(memory);
                    continue;
                }

                CapGoblinMemoryDuration(memory);
            }
        }

        public static void CapGoblinMemoryDuration(Thought_Memory memory)
        {
            if (memory == null || memory.permanent)
            {
                return;
            }

            int maxTicks = GenDate.TicksPerDay * 10;
            if (memory.DurationTicks > maxTicks)
            {
                memory.durationTicksOverride = maxTicks;
            }
        }
    }

    public static class GoblinDeathThoughtUtility
    {
        private const float GoblinDeathMemoryDurationFactor = 0.25f;
        private const string ColonistDeathThoughtDefName = "KnowColonistDied";
        private const string FriendDeathThoughtDefName = "PawnWithGoodOpinionDied";
        private static readonly HashSet<string> SiblingDeathThoughtDefNames = new HashSet<string>
        {
            "MyBrotherDied",
            "MySisterDied",
            "MyHalfSiblingDied",
            "MyBrotherLost",
            "MySisterLost",
            "MyHalfSiblingLost",
            "KilledMyBrother",
            "KilledMySister"
        };

        public static bool TryAdjustSiblingDeath(Pawn pawn, ThoughtDef thought, ref float moodOffset)
        {
            if (!GoblinUtility.IsGoblin(pawn) || !IsSiblingDeathOrLost(thought) || moodOffset >= 0f)
            {
                return false;
            }

            moodOffset = -2f;
            return true;
        }

        public static bool TryReplaceGoblinSiblingDeathThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (!GoblinUtility.IsGoblin(pawn)
                || thought == null
                || thought == MUGBDefOf.MUGB_GoblinSiblingDied
                || !IsSiblingDeathOrLost(thought))
            {
                return false;
            }

            if (MUGBDefOf.MUGB_GoblinSiblingDied != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinSiblingDied, otherPawn);
            }
            return true;
        }

        public static bool TryReplaceGoblinColonistDeathThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (!GoblinUtility.IsGoblin(pawn) || thought?.defName != ColonistDeathThoughtDefName)
            {
                return false;
            }

            if (MUGBDefOf.MUGB_GoblinColonistDied != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinColonistDied, otherPawn);
            }
            return true;
        }

        public static bool TryReplaceGoblinFriendDeathThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (!GoblinUtility.IsGoblin(pawn)
                || thought?.defName != FriendDeathThoughtDefName)
            {
                return false;
            }

            if (MUGBDefOf.MUGB_GoblinFriendDied != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_GoblinFriendDied, otherPawn);
            }
            return true;
        }

        public static void TryShortenMemoryDuration(Pawn pawn, Thought_Memory memory)
        {
            if (!GoblinUtility.IsGoblin(pawn) || memory == null || memory.permanent)
            {
                return;
            }

            if (IsSiblingDeathOrLost(memory.def))
            {
                memory.durationTicksOverride = GenDate.TicksPerDay * 3;
                GoblinThoughtMoodUtility.CapGoblinMemoryDuration(memory);
                return;
            }

            if (ShouldQuarter(memory.def))
            {
                int duration = memory.DurationTicks;
                if (duration > 0)
                {
                    memory.durationTicksOverride = UnityEngine.Mathf.Max(GenDate.TicksPerDay / 4, (int)(duration * GoblinDeathMemoryDurationFactor));
                }
            }

            GoblinThoughtMoodUtility.CapGoblinMemoryDuration(memory);
        }

        public static bool ShouldQuarter(ThoughtDef thought)
        {
            string name = thought?.defName;
            if (name.NullOrEmpty())
            {
                return false;
            }

            return name.Contains("Died")
                || name.Contains("Death")
                || name.Contains("Lost")
                || name.StartsWith("KilledMy")
                || name == "KilledChild"
                || name == "KnowColonistDied"
                || name == "WitnessedDeathAlly"
                || name == "WitnessedDeathNonAlly"
                || name == "WitnessedDeathFamily"
                || name == "PawnWithGoodOpinionDied"
                || name == "PawnWithGoodOpinionLost";
        }

        private static bool IsSiblingDeathOrLost(ThoughtDef thought)
        {
            return thought != null && SiblingDeathThoughtDefNames.Contains(thought.defName);
        }
    }
}
