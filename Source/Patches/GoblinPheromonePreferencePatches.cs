using RimWorld;
using Verse;

namespace MUGB
{
    public class HediffCompProperties_GoblinPheromoneConditioning : HediffCompProperties
    {
        public HediffCompProperties_GoblinPheromoneConditioning()
        {
            compClass = typeof(HediffComp_GoblinPheromoneConditioning);
        }
    }

    public class HediffCompProperties_GoblinPheromonePreference : HediffCompProperties
    {
        public HediffCompProperties_GoblinPheromonePreference()
        {
            compClass = typeof(HediffComp_GoblinPheromonePreference);
        }
    }

    public class HediffComp_GoblinPheromoneConditioning : HediffComp
    {
        private const float TargetPoints = 24f;
        private const float MaxDailyGain = 6f;

        private int lastGainDay = -1;
        private int lastGoblinFoodDay = -1;
        private int lastDecayCheckDay = -1;
        private float dailyGain;

        public void AddConditioning(float amount)
        {
            if (Pawn?.health == null || amount <= 0f)
            {
                return;
            }

            int day = Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0;
            if (day != lastGainDay)
            {
                lastGainDay = day;
                dailyGain = 0f;
            }
            lastGoblinFoodDay = day;

            float gain = amount;
            float remainingDailyGain = MaxDailyGain - dailyGain;
            if (gain > remainingDailyGain)
            {
                gain = remainingDailyGain;
            }
            if (gain <= 0f)
            {
                return;
            }

            dailyGain += gain;
            parent.Severity += gain;
            if (parent.Severity < TargetPoints)
            {
                return;
            }

            if (MUGBDefOf.MUGB_GoblinPheromonePreference != null && !Pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_GoblinPheromonePreference))
            {
                Hediff preference = Pawn.health.AddHediff(MUGBDefOf.MUGB_GoblinPheromonePreference);
                preference?.TryGetComp<HediffComp_GoblinPheromonePreference>()?.InitializeFromConditioning(parent.Severity, day);
            }
            Pawn.health.RemoveHediff(parent);
        }

        public void AddForcedConditioning(float amount)
        {
            if (Pawn?.health == null || amount <= 0f)
            {
                return;
            }

            int day = Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0;
            if (day != lastGainDay)
            {
                lastGainDay = day;
                dailyGain = 0f;
            }
            lastGoblinFoodDay = day;

            parent.Severity += amount;
            if (parent.Severity < TargetPoints)
            {
                return;
            }

            if (MUGBDefOf.MUGB_GoblinPheromonePreference != null && !Pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_GoblinPheromonePreference))
            {
                Hediff preference = Pawn.health.AddHediff(MUGBDefOf.MUGB_GoblinPheromonePreference);
                preference?.TryGetComp<HediffComp_GoblinPheromonePreference>()?.InitializeFromConditioning(parent.Severity, day);
            }
            Pawn.health.RemoveHediff(parent);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            int day = CurrentDay();
            if (day == lastDecayCheckDay)
            {
                return;
            }
            lastDecayCheckDay = day;

            if (lastGoblinFoodDay < 0 || day - lastGoblinFoodDay < 2)
            {
                return;
            }

            int decayDays = day - lastGoblinFoodDay - 1;
            if (decayDays <= 0)
            {
                return;
            }

            parent.Severity = UnityEngine.Mathf.Max(0f, parent.Severity - decayDays);
            lastGoblinFoodDay = day - 1;
        }

        public override bool CompShouldRemove => parent.Severity <= 0f;

        public override string CompLabelInBracketsExtra => $"{parent.Severity:0.#}/{TargetPoints:0}";

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref lastGainDay, "lastGainDay", -1);
            Scribe_Values.Look(ref lastGoblinFoodDay, "lastGoblinFoodDay", -1);
            Scribe_Values.Look(ref lastDecayCheckDay, "lastDecayCheckDay", -1);
            Scribe_Values.Look(ref dailyGain, "dailyGain", 0f);
        }

        private static int CurrentDay()
        {
            return Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0;
        }
    }

    public class HediffComp_GoblinPheromonePreference : HediffComp
    {
        private const float MaxPreferencePoints = 24f;

        private float preferencePoints = MaxPreferencePoints;
        private int lastGoblinFoodDay = -1;
        private int lastDecayCheckDay = -1;
        private int zeroPointDay = -1;

        public override void CompPostMake()
        {
            base.CompPostMake();
            int day = CurrentDay();
            if (preferencePoints <= 0f)
            {
                preferencePoints = MaxPreferencePoints;
            }
            if (lastGoblinFoodDay < 0)
            {
                lastGoblinFoodDay = day;
            }
        }

        public void InitializeFromConditioning(float points, int day)
        {
            preferencePoints = UnityEngine.Mathf.Clamp(points, 0f, MaxPreferencePoints);
            if (preferencePoints <= 0f)
            {
                preferencePoints = MaxPreferencePoints;
            }
            lastGoblinFoodDay = day;
            lastDecayCheckDay = day;
            zeroPointDay = -1;
        }

        public void NotifyGoblinFoodEaten()
        {
            preferencePoints = MaxPreferencePoints;
            lastGoblinFoodDay = CurrentDay();
            lastDecayCheckDay = lastGoblinFoodDay;
            zeroPointDay = -1;
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            int day = CurrentDay();
            if (day == lastDecayCheckDay)
            {
                return;
            }
            lastDecayCheckDay = day;

            if (lastGoblinFoodDay < 0)
            {
                lastGoblinFoodDay = day;
                return;
            }

            if (day - lastGoblinFoodDay >= 2 && preferencePoints > 0f)
            {
                int decayDays = day - lastGoblinFoodDay - 1;
                if (decayDays > 0)
                {
                    preferencePoints = UnityEngine.Mathf.Max(0f, preferencePoints - decayDays);
                    lastGoblinFoodDay = day - 1;
                    if (preferencePoints <= 0f && zeroPointDay < 0)
                    {
                        zeroPointDay = day;
                    }
                }
            }

            if (preferencePoints <= 0f && zeroPointDay >= 0 && day - zeroPointDay >= 1)
            {
                Pawn?.health?.RemoveHediff(parent);
            }
        }

        public override string CompLabelInBracketsExtra => $"{preferencePoints:0.#}/{MaxPreferencePoints:0}";

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref preferencePoints, "preferencePoints", MaxPreferencePoints);
            Scribe_Values.Look(ref lastGoblinFoodDay, "lastGoblinFoodDay", -1);
            Scribe_Values.Look(ref lastDecayCheckDay, "lastDecayCheckDay", -1);
            Scribe_Values.Look(ref zeroPointDay, "zeroPointDay", -1);
        }

        private static int CurrentDay()
        {
            return Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0;
        }
    }

    public static class GoblinPheromonePreferenceUtility
    {
        private const float PreferenceThreshold = 24f;

        public static bool HasPreference(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

            if (MUGBDefOf.MUGB_GoblinPheromonePreference != null && pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_GoblinPheromonePreference))
            {
                return true;
            }

            return TryPromoteCompletedConditioning(pawn);
        }

        public static void TryGainConditioning(Pawn pawn, float points)
        {
            if (pawn?.health == null || points <= 0f || GoblinUtility.IsGoblin(pawn) || HasPreference(pawn))
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinPheromoneConditioning);
            if (hediff == null)
            {
                hediff = pawn.health.AddHediff(MUGBDefOf.MUGB_GoblinPheromoneConditioning);
            }

            hediff?.TryGetComp<HediffComp_GoblinPheromoneConditioning>()?.AddConditioning(points);
        }

        public static void ForceGainConditioning(Pawn pawn, float points)
        {
            if (pawn?.health == null || points <= 0f || GoblinUtility.IsGoblin(pawn) || HasPreference(pawn))
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinPheromoneConditioning);
            if (hediff == null)
            {
                hediff = pawn.health.AddHediff(MUGBDefOf.MUGB_GoblinPheromoneConditioning);
            }

            hediff?.TryGetComp<HediffComp_GoblinPheromoneConditioning>()?.AddForcedConditioning(points);
        }

        public static void NotifyPreferenceGoblinFoodEaten(Pawn pawn)
        {
            Hediff hediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinPheromonePreference);
            hediff?.TryGetComp<HediffComp_GoblinPheromonePreference>()?.NotifyGoblinFoodEaten();
        }

        public static void RemoveHumanlikeAndGoblinFoodPenalties(Pawn pawn)
        {
            RemoveMemory(pawn, "AteHumanlikeMeatDirect");
            RemoveMemory(pawn, "AteHumanlikeMeatDirectCannibal");
            RemoveMemory(pawn, "AteHumanlikeMeatAsIngredient");
            RemoveMemory(pawn, "AteHumanlikeMeatAsIngredientCannibal");
            RemoveMemory(pawn, "MUGB_AteGoblinMeatDirect");
            RemoveMemory(pawn, "MUGB_AteGoblinMeatAsIngredient");
            RemoveMemory(pawn, "MUGB_AteGoblinMeatDirectCannibal");
            RemoveMemory(pawn, "MUGB_AteGoblinMeatAsIngredientCannibal");
        }

        private static void RemoveMemory(Pawn pawn, string defName)
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (thought != null)
            {
                pawn?.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(thought);
            }
        }

        private static bool TryPromoteCompletedConditioning(Pawn pawn)
        {
            if (MUGBDefOf.MUGB_GoblinPheromoneConditioning == null || MUGBDefOf.MUGB_GoblinPheromonePreference == null)
            {
                return false;
            }

            Hediff conditioning = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinPheromoneConditioning);
            if (conditioning == null || conditioning.Severity < PreferenceThreshold - 0.001f)
            {
                return false;
            }

            Hediff preference = pawn.health.AddHediff(MUGBDefOf.MUGB_GoblinPheromonePreference);
            int day = Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0;
            preference?.TryGetComp<HediffComp_GoblinPheromonePreference>()?.InitializeFromConditioning(conditioning.Severity, day);
            pawn.health.RemoveHediff(conditioning);
            return true;
        }
    }
}
