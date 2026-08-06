using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MUGB.Patches
{
    public static class GoblinBirthStrainUtility
    {
        private static readonly int[] Limits = { 0, 2, 4, 6, 8, 10 };

        public static int CurrentLimit => NormalizeLimit(MUGBMod.Settings?.goblinBirthStrainLimit ?? 4);

        public static int NormalizeLimit(int limit)
        {
            int closest = Limits[0];
            int closestDistance = System.Math.Abs(limit - closest);
            for (int i = 1; i < Limits.Length; i++)
            {
                int distance = System.Math.Abs(limit - Limits[i]);
                if (distance < closestDistance)
                {
                    closest = Limits[i];
                    closestDistance = distance;
                }
            }

            return closest;
        }

        public static int NextLimit(int current)
        {
            int normalized = NormalizeLimit(current);
            int index = System.Array.IndexOf(Limits, normalized);
            return Limits[(index + 1) % Limits.Length];
        }

        public static void ApplySettingChange(int oldLimit, int newLimit)
        {
            int normalizedOld = NormalizeLimit(oldLimit);
            int normalizedNew = NormalizeLimit(newLimit);
            if (normalizedOld == normalizedNew || Current.Game == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                List<Pawn> pawns = map?.mapPawns?.AllPawns;
                if (pawns == null)
                {
                    continue;
                }

                for (int i = pawns.Count - 1; i >= 0; i--)
                {
                    Pawn pawn = pawns[i];
                    Hediff_GoblinBirthStrain strain = GetOrMigrateStrain(pawn, normalizedNew);
                    if (strain == null)
                    {
                        continue;
                    }

                    if (normalizedNew <= 0)
                    {
                        pawn.health.RemoveHediff(strain);
                    }
                    else
                    {
                        strain.ApplyLimitChange(normalizedNew, clampBelowLethalLimit: normalizedNew < normalizedOld);
                    }
                }
            }
        }

        public static bool MigrateLegacyStrain(Pawn pawn)
        {
            Hediff existing = pawn?.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinBirthStrain);
            if (existing == null || existing is Hediff_GoblinBirthStrain)
            {
                return false;
            }

            GetOrMigrateStrain(pawn, CurrentLimit);
            return true;
        }

        public static Hediff_GoblinBirthStrain GetOrMigrateStrain(Pawn pawn, int limit)
        {
            if (pawn?.health?.hediffSet == null || MUGBDefOf.MUGB_GoblinBirthStrain == null)
            {
                return null;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinBirthStrain);
            if (existing is Hediff_GoblinBirthStrain current)
            {
                return current;
            }

            if (existing == null)
            {
                return null;
            }

            int legacyCount = Mathf.Max(1, Mathf.RoundToInt(existing.Severity));
            pawn.health.RemoveHediff(existing);
            if (limit <= 0)
            {
                return null;
            }

            Hediff_GoblinBirthStrain migrated = HediffMaker.MakeHediff(
                MUGBDefOf.MUGB_GoblinBirthStrain,
                pawn) as Hediff_GoblinBirthStrain;
            if (migrated == null)
            {
                return null;
            }

            if (legacyCount >= limit)
            {
                legacyCount = System.Math.Max(0, limit - 1);
            }
            migrated.SetBirthCount(legacyCount, limit);
            pawn.health.AddHediff(migrated);
            return migrated;
        }
    }

    public class Hediff_GoblinBirthStrain : HediffWithComps
    {
        private int birthCount = -1;

        public int BirthCount
        {
            get
            {
                EnsureMigrated();
                return birthCount;
            }
        }

        public override string LabelInBrackets
        {
            get
            {
                int limit = GoblinBirthStrainUtility.CurrentLimit;
                return limit > 0 ? $"{BirthCount}/{limit}" : base.LabelInBrackets;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref birthCount, "mugbGoblinBirthCount", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureMigrated();
                ApplyLimitChange(GoblinBirthStrainUtility.CurrentLimit, clampBelowLethalLimit: true);
            }
        }

        public void SetBirthCount(int count, int limit)
        {
            birthCount = System.Math.Max(0, count);
            UpdateSeverity(limit);
        }

        public void ApplyLimitChange(int limit, bool clampBelowLethalLimit)
        {
            EnsureMigrated();
            if (limit <= 0)
            {
                birthCount = 0;
                Severity = 0f;
                return;
            }

            if (clampBelowLethalLimit && birthCount >= limit)
            {
                birthCount = System.Math.Max(0, limit - 1);
            }

            UpdateSeverity(limit);
        }

        private void EnsureMigrated()
        {
            if (birthCount < 0)
            {
                birthCount = Mathf.Max(1, Mathf.RoundToInt(Severity));
            }
        }

        private void UpdateSeverity(int limit)
        {
            if (birthCount <= 0 || limit <= 0)
            {
                Severity = 0f;
                return;
            }

            if (birthCount >= limit)
            {
                Severity = 4f;
                return;
            }

            int livingMaximum = System.Math.Max(1, limit - 1);
            float progress = birthCount / (float)livingMaximum;
            Severity = progress <= (1f / 3f) ? 1f : (progress <= (2f / 3f) ? 2f : 3f);
        }
    }

    public static class GoblinLitterSizeUtility
    {
        public const float MinMultiplier = 0.25f;
        public const float MaxMultiplier = 2f;
        private const float Step = 0.25f;
        private const int MaxLitterSize = 8;

        public static float NormalizeMultiplier(float multiplier)
        {
            float clamped = Mathf.Clamp(multiplier, MinMultiplier, MaxMultiplier);
            return Mathf.Round(clamped / Step) * Step;
        }

        public static int ScaleCount(int baseCount, float multiplier)
        {
            float normalized = NormalizeMultiplier(multiplier);
            return Mathf.Clamp(Mathf.FloorToInt(baseCount * normalized + 0.5f), 1, MaxLitterSize);
        }

        public static int ScaleCurrentCount(int baseCount)
        {
            float multiplier = MUGBMod.Settings?.goblinLitterSizeMultiplier ?? 1f;
            return ScaleCount(baseCount, multiplier);
        }

        public static int RollMaternalLitterBonus(Pawn mother)
        {
            SimpleCurve litterSizeCurve = mother?.RaceProps?.litterSizeCurve;
            if (litterSizeCurve == null)
            {
                return 0;
            }

            int nativeLitterSize = Mathf.Max(1, Mathf.RoundToInt(Rand.ByCurve(litterSizeCurve)));
            return Mathf.Clamp(nativeLitterSize - 1, 0, MaxLitterSize - 1);
        }

        public static void GetExpectedRange(bool hobgoblinFather, float multiplier, out int min, out int max)
        {
            min = ScaleCount(hobgoblinFather ? 2 : 3, multiplier);
            max = ScaleCount(4, multiplier);
        }
    }

    public static class PregnancyUtility_PregnancyChanceForPartners_GoblinLactation_Patch
    {
        public static void Postfix(Pawn woman, Pawn man, ref float __result)
        {
            if (!(GoblinUtility.HasGoblinCoreMarker(man) || GoblinUtility.HasGoblinCoreMarker(woman))
                || woman?.health?.hediffSet == null)
            {
                return;
            }

            Hediff lactating = woman.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating);
            float factor = lactating?.CurStage?.fertilityFactor ?? 1f;
            if (factor > 0f && factor < 1f)
            {
                __result /= factor;
            }
        }
    }

    public static class RJW_HediffBasePregnancy_PostBirth_GoblinRecovery_Patch
    {
        public static void Postfix(Pawn mother, Pawn father, Pawn baby)
        {
            if (GoblinBirthUtility.IsGoblinPregnancy(mother, father, baby))
            {
                GoblinBirthUtility.ApplyRapidPostpartumRecovery(mother);
            }
        }
    }

    public static class RJWMenstruation_GoNextStage_GoblinRecovery_Patch
    {
        public static void Postfix(object __instance, object __0)
        {
            if (__instance == null || __0 == null || __0.ToString() != "Recover")
            {
                return;
            }

            Pawn pawn = AccessTools.Property(__instance.GetType(), "Pawn")?.GetValue(__instance, null) as Pawn;
            if (!GoblinPostpartumRecoveryUtility.HasRapidRecoveryMarker(pawn))
            {
                return;
            }

            GoblinPostpartumRecoveryUtility.SetMenstruationRecoveryTicks(__instance);
        }
    }

    public static class GoblinPostpartumRecoveryUtility
    {
        public const int VanillaExhaustionTicks = 15000;
        public const int MenstruationRecoveryTicks = 90000;
        private const string MenstruationCompTypeName = "RJW_Menstruation.HediffComp_Menstruation";

        public static bool HasRapidRecoveryMarker(Pawn pawn)
        {
            return MUGBDefOf.MUGB_GoblinRapidPostpartum != null
                && pawn?.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinRapidPostpartum) != null;
        }

        public static void ShortenActiveMenstruationRecovery(Pawn pawn)
        {
            if (!HasRapidRecoveryMarker(pawn))
            {
                return;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                List<HediffComp> comps = (hediffs[i] as HediffWithComps)?.comps;
                if (comps == null)
                {
                    continue;
                }

                for (int j = 0; j < comps.Count; j++)
                {
                    object comp = comps[j];
                    if (comp?.GetType().FullName == MenstruationCompTypeName
                        && AccessTools.Field(comp.GetType(), "curStage")?.GetValue(comp)?.ToString() == "Recover")
                    {
                        SetMenstruationRecoveryTicks(comp);
                    }
                }
            }
        }

        public static void SetMenstruationRecoveryTicks(object menstruationComp)
        {
            FieldInfo intervalField = AccessTools.Field(menstruationComp?.GetType(), "currentIntervalTicks");
            if (intervalField?.GetValue(menstruationComp) is int currentTicks && currentTicks > MenstruationRecoveryTicks)
            {
                intervalField.SetValue(menstruationComp, MenstruationRecoveryTicks);
            }
        }
    }

    public static class PregnancyUtility_ApplyBirthOutcome_Patch
    {
        // 아기는 이 메서드 안에서 생성되고, 일부 외부 모드는 그 생성 시점의 성별을 보고
        // 신체부위를 붙입니다. 본편이 나중에 성별만 남성으로 덮으면 부위가 어긋난 채 남으므로,
        // 생성 전에 "이번 아기가 순수 고블린인지"를 표시해 두어 성별을 미리 고정합니다.
        // 어미 종족을 따르는 하프고블린은 표시하지 않으므로 성별이 자유롭게 정해집니다.
        public static void Prefix(Pawn geneticMother, Pawn father)
        {
            GoblinBirthUtility.BeginExpectedNewborn(geneticMother, father);
        }

        public static void Postfix(
            Thing __result,
            RitualOutcomePossibility outcome,
            Pawn geneticMother,
            Thing birtherThing,
            Pawn father)
        {
            try
            {
                GoblinBirthUtility.PostProcessBirth(__result, outcome, geneticMother, birtherThing, father);
            }
            finally
            {
                // 표시는 이 출산에만 유효해야 하므로 예외가 나도 반드시 지웁니다.
                GoblinBirthUtility.EndExpectedNewborn();
            }
        }
    }

    public static class GoblinBirthUtility
    {
        private static bool patched;

        // 한국어 참고: 출산 한 건 동안만 켜지는 표시입니다. 출산은 메인 스레드에서 순차로 처리되고
        // Prefix에서 켜고 Postfix의 finally에서 반드시 끄므로 남아 있을 일이 없습니다.
        // 매 틱 도는 코드가 아니라 출산 시 한 번만 켜졌다 꺼집니다.
        private static bool expectingFullGoblinNewborn;

        // 한국어 의도: 표시는 "다음에 생성되는 아기 1명"에만 적용되어야 합니다.
        // 띤 아비의 출산은 3~4명, 홉 아비의 출산은 2~4명이고 각 아기의 결과가 따로 굴려지는데,
        // 표시를 켜둔 채로 두면 뒤이어 생성되는 하프고블린 형제까지 남성으로 고정돼 버립니다.
        // 그래서 한 번 읽히면 즉시 꺼지는 1회용으로 둡니다.
        public static bool ConsumeExpectingFullGoblinNewborn()
        {
            bool expecting = expectingFullGoblinNewborn;
            expectingFullGoblinNewborn = false;
            return expecting;
        }

        public static void BeginExpectedNewborn(Pawn geneticMother, Pawn father)
        {
            expectingFullGoblinNewborn = false;
            if (geneticMother == null || !GoblinUtility.IsGoblin(father))
            {
                return;
            }

            HediffComp_MUGBGoblinPregnancyPlan plan = GetActivePregnancyPlan(geneticMother);
            GoblinBirthResult? next = plan?.PeekNextResult();
            if (next == null)
            {
                // 계획이 없으면 아비가 고블린일 때 순수 고블린이 기본값이므로 남성으로 봅니다.
                expectingFullGoblinNewborn = true;
                return;
            }

            expectingFullGoblinNewborn = next.Value == GoblinBirthResult.ThinGoblin
                || next.Value == GoblinBirthResult.Hobgoblin;
        }

        public static void EndExpectedNewborn()
        {
            expectingFullGoblinNewborn = false;
        }

        public enum GoblinBirthResult
        {
            ThinGoblin,
            Hobgoblin,
            MotherXenotype
        }

        public static void PatchAfterDefsLoaded(Harmony harmony)
        {
            if (patched || harmony == null)
            {
                return;
            }

            PatchPregnancyChanceForPartners(harmony);

            System.Reflection.MethodInfo original = AccessTools.Method(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome));
            System.Reflection.MethodInfo postfix = AccessTools.Method(typeof(PregnancyUtility_ApplyBirthOutcome_Patch), nameof(PregnancyUtility_ApplyBirthOutcome_Patch.Postfix));
            if (original == null || postfix == null)
            {
                Log.Error("[MUGB Goblin] Failed to find PregnancyUtility.ApplyBirthOutcome for goblin birth patch.");
                return;
            }

            System.Reflection.MethodInfo prefix = AccessTools.Method(typeof(PregnancyUtility_ApplyBirthOutcome_Patch), nameof(PregnancyUtility_ApplyBirthOutcome_Patch.Prefix));
            harmony.Patch(original, prefix: prefix != null ? new HarmonyMethod(prefix) : null, postfix: new HarmonyMethod(postfix));
            PatchConceptionGeneInheritance(harmony);
            PatchOptionalPostpartumCompatibility(harmony);
            patched = true;
        }

        private static void PatchPregnancyChanceForPartners(Harmony harmony)
        {
            MethodInfo original = AccessTools.Method(typeof(PregnancyUtility), nameof(PregnancyUtility.PregnancyChanceForPartners));
            MethodInfo postfix = AccessTools.Method(
                typeof(PregnancyUtility_PregnancyChanceForPartners_GoblinLactation_Patch),
                nameof(PregnancyUtility_PregnancyChanceForPartners_GoblinLactation_Patch.Postfix));
            if (original == null || postfix == null)
            {
                Log.Error("[MUGB Goblin] Failed to find PregnancyUtility.PregnancyChanceForPartners for goblin lactation patch.");
                return;
            }

            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        private static void PatchOptionalPostpartumCompatibility(Harmony harmony)
        {
            MethodInfo rjwPostBirthPostfix = AccessTools.Method(typeof(RJW_HediffBasePregnancy_PostBirth_GoblinRecovery_Patch), nameof(RJW_HediffBasePregnancy_PostBirth_GoblinRecovery_Patch.Postfix));
            System.Type rjwPregnancyType = AccessTools.TypeByName("rjw.Hediff_BasePregnancy");
            MethodInfo rjwPostBirth = rjwPregnancyType == null
                ? null
                : AccessTools.Method(rjwPregnancyType, "PostBirth", new[] { typeof(Pawn), typeof(Pawn), typeof(Pawn) });
            if (rjwPostBirth != null && rjwPostBirthPostfix != null)
            {
                harmony.Patch(rjwPostBirth, postfix: new HarmonyMethod(rjwPostBirthPostfix));

                // 같은 지점에 출생 후처리도 붙입니다. RJW 경로는 ApplyBirthOutcome을 거치지 않아
                // MUGB의 PostProcessBirth가 돌지 않으므로 여기서 관계/사상/신분을 마무리합니다.
                MethodInfo rjwNewbornPostfix = AccessTools.Method(
                    typeof(RJW_HediffBasePregnancy_PostBirth_GoblinNewbornPatch),
                    nameof(RJW_HediffBasePregnancy_PostBirth_GoblinNewbornPatch.Postfix));
                if (rjwNewbornPostfix != null)
                {
                    harmony.Patch(rjwPostBirth, postfix: new HarmonyMethod(rjwNewbornPostfix));
                }
            }

            GoblinRJWBirthCompat.Patch(harmony);

            MethodInfo menstruationPostfix = AccessTools.Method(typeof(RJWMenstruation_GoNextStage_GoblinRecovery_Patch), nameof(RJWMenstruation_GoNextStage_GoblinRecovery_Patch.Postfix));
            System.Type menstruationType = AccessTools.TypeByName("RJW_Menstruation.HediffComp_Menstruation");
            MethodInfo goNextStage = menstruationType?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "GoNextStage"
                        && parameters.Length >= 1
                        && parameters[0].ParameterType.IsEnum;
                });
            if (goNextStage != null && menstruationPostfix != null)
            {
                harmony.Patch(goNextStage, postfix: new HarmonyMethod(menstruationPostfix));
            }
        }

        private static void PatchConceptionGeneInheritance(Harmony harmony)
        {
            System.Reflection.MethodInfo postfix = AccessTools.Method(typeof(PregnancyUtility_GetInheritedGeneSet_GoblinPlan_Patch), nameof(PregnancyUtility_GetInheritedGeneSet_GoblinPlan_Patch.Postfix));
            if (postfix == null)
            {
                Log.Error("[MUGB Goblin] Failed to find goblin pregnancy plan postfix.");
                return;
            }

            foreach (MethodInfo method in typeof(PregnancyUtility).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != nameof(PregnancyUtility.GetInheritedGeneSet) && method.Name != nameof(PregnancyUtility.GetInheritedGenes))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 2 && parameters[0].ParameterType == typeof(Pawn) && parameters[1].ParameterType == typeof(Pawn))
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
        }

        public static void PostProcessBirth(Thing result, RitualOutcomePossibility outcome, Pawn geneticMother, Thing birtherThing, Pawn father)
        {
            Pawn firstChild = result as Pawn;
            if (outcome == null || outcome.positivityIndex < 0 || firstChild == null || firstChild.Dead)
            {
                return;
            }

            if (geneticMother == null || birtherThing == null || !(birtherThing is Pawn) || !geneticMother.RaceProps.Humanlike || !GoblinUtility.IsGoblin(father))
            {
                return;
            }

            ApplyRapidPostpartumRecovery(geneticMother);
            bool hobgoblinFather = GoblinUtility.IsHobgoblin(father);
            HediffComp_MUGBGoblinPregnancyPlan plan = GetActivePregnancyPlan(geneticMother);
            if (plan != null && plan.UsesPregeneratedBabies)
            {
                ApplyBirthResult(firstChild, geneticMother, plan.NextResultOrFallback(hobgoblinFather));
                NormalizeGoblinBirthRelations(firstChild, geneticMother, father);
                InheritGoblinIdeo(firstChild, geneticMother, father);
                ApplyBirthGuestStatus(firstChild, geneticMother);
                if (!plan.BirthStrainApplied)
                {
                    ApplyGoblinBirthMood(geneticMother);
                    ApplyGoblinBirthStrain(geneticMother);
                    plan.BirthStrainApplied = true;
                }
                return;
            }

            if (plan == null)
            {
                plan = CreateTemporaryBirthPlan(hobgoblinFather);
            }

            ApplyBirthResult(firstChild, geneticMother, plan.NextResultOrFallback(hobgoblinFather));
            NormalizeGoblinBirthRelations(firstChild, geneticMother, father);
            ApplyBirthGuestStatus(firstChild, geneticMother);

            while (plan.HasUnbornResult)
            {
                Pawn extraChild = GenerateExtraNewborn(geneticMother, birtherThing, plan.NextResultOrFallback(hobgoblinFather));
                if (extraChild == null)
                {
                    continue;
                }

                AddBirthRelationsAndSettings(extraChild, geneticMother, birtherThing as Pawn, father);
                InheritGoblinIdeo(extraChild, geneticMother, father);
                SpawnExtraNewborn(extraChild, geneticMother, birtherThing);
            }

            ApplyGoblinBirthStrain(geneticMother);
            ApplyGoblinBirthMood(geneticMother);
        }

        // 한국어 의도: RJW 경로 출산의 출생 후처리입니다.
        //
        // RJW와 RJW Menstruation은 PregnancyUtility.ApplyBirthOutcome을 거치지 않고 자체적으로
        // 아기를 스폰하므로 PostProcessBirth가 돌지 않습니다. 아기 자체는 수태 시점에
        // GoblinRJWBirthCompat이 MUGB 규격으로 만들어 두었으니, 여기서는 스폰 이후에만
        // 할 수 있는 일 세 가지만 처리합니다.
        //
        // 산자수와 출산 부담은 일부러 건드리지 않습니다. RJW 경로 기존 플레이어의 출산 규모를
        // 바꾸지 않기 위한 결정이며, 외형 문제와 무관합니다.
        public static void ApplyRJWNewbornFollowUp(Pawn mother, Pawn father, Pawn baby)
        {
            if (mother == null
                || !GoblinUtility.IsGoblin(father)
                || baby == null
                || baby.Dead
                || (!GoblinUtility.IsGoblin(baby) && !GoblinUtility.HasHalfGoblinAncestry(baby)))
            {
                return;
            }

            NormalizeGoblinBirthRelations(baby, mother, father);
            InheritGoblinIdeo(baby, mother, father);
            ApplyBirthGuestStatus(baby, mother);
        }

        public static bool IsGoblinPregnancy(Pawn mother, Pawn father, Pawn baby = null)
        {
            return GoblinUtility.HasGoblinCoreMarker(mother)
                || GoblinUtility.HasGoblinCoreMarker(father)
                || GoblinUtility.HasGoblinCoreMarker(baby);
        }

        public static void ApplyRapidPostpartumRecovery(Pawn mother)
        {
            if (mother?.health?.hediffSet == null)
            {
                return;
            }

            Hediff exhaustion = mother.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PostpartumExhaustion);
            HediffComp_Disappears exhaustionTimer = exhaustion?.TryGetComp<HediffComp_Disappears>();
            if (exhaustionTimer != null && exhaustionTimer.ticksToDisappear > GoblinPostpartumRecoveryUtility.VanillaExhaustionTicks)
            {
                exhaustionTimer.SetDuration(GoblinPostpartumRecoveryUtility.VanillaExhaustionTicks);
            }

            if (MUGBDefOf.MUGB_GoblinRapidPostpartum != null
                && mother.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinRapidPostpartum) == null)
            {
                mother.health.AddHediff(MUGBDefOf.MUGB_GoblinRapidPostpartum);
            }

            GoblinPostpartumRecoveryUtility.ShortenActiveMenstruationRecovery(mother);
        }

        public static HediffComp_MUGBGoblinPregnancyPlan GetActivePregnancyPlan(Pawn mother)
        {
            if (mother?.health?.hediffSet?.hediffs == null)
            {
                return null;
            }

            for (int i = mother.health.hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                HediffWithComps hediff = mother.health.hediffSet.hediffs[i] as HediffWithComps;
                HediffComp_MUGBGoblinPregnancyPlan comp = hediff?.TryGetComp<HediffComp_MUGBGoblinPregnancyPlan>();
                if (comp?.Initialized == true)
                {
                    return comp;
                }
            }
            return null;
        }

        private static HediffComp_MUGBGoblinPregnancyPlan CreateTemporaryBirthPlan(bool hobgoblinFather)
        {
            HediffComp_MUGBGoblinPregnancyPlan plan = new HediffComp_MUGBGoblinPregnancyPlan();
            plan.InitializeFromFather(hobgoblinFather);
            return plan;
        }

        // 한국어 의도: 순수 고블린 아기는 어미가 HAR 외계 종족이어도 인간 뼈대로 태어나게 합니다.
        //
        // 종족(race ThingDef)은 제노타입 아래층이라 제노타입만 고블린으로 바꿔서는 바뀌지 않습니다.
        // 그런데 HAR은 종족마다 머리가 붙는 위치(BaseHeadOffsetAt)와 머리/몸 크기(customHeadDrawSize)를
        // 자기 값으로 덮어씁니다. 반면 MUGB의 고블린 머리 보정과 눈/코/턱/귀 좌표는 인간 골격 기준으로
        // 맞춰둔 고정값이고 종족별 구분이 없습니다. 두 좌표계가 겹치면 얼굴 부속이 어긋납니다.
        // 뼈대를 인간으로 맞추면 이 문제와 HAR 부속(꼬리/뿔/귀) 문제가 함께 사라집니다.
        //
        // 어미 종족을 따르는 하프고블린은 어미를 닮는 것이 의도이므로 어미 종족을 그대로 둡니다.
        public static PawnKindDef NewbornKindFor(Pawn geneticMother, GoblinBirthResult result)
        {
            PawnKindDef motherKind = geneticMother?.kindDef ?? PawnKindDefOf.Colonist;
            if (result == GoblinBirthResult.MotherXenotype)
            {
                return motherKind;
            }

            return HumanRaceKindFor(motherKind);
        }

        // 어미가 이미 인간이면 어미 PawnKind를 그대로 씁니다. 외계 종족일 때만 인간 기반 기본값으로
        // 바꿉니다. 갓난 단계에서는 장비가 생성되지 않으므로 PawnKind 교체로 장비가 달라지지 않습니다.
        public static PawnKindDef HumanRaceKindFor(PawnKindDef kindDef)
        {
            if (kindDef == null || kindDef.race == ThingDefOf.Human)
            {
                return kindDef ?? PawnKindDefOf.Colonist;
            }

            return PawnKindDefOf.Colonist;
        }

        private static Pawn GenerateExtraNewborn(Pawn geneticMother, Thing birtherThing, GoblinBirthResult roll)
        {
            XenotypeDef forcedXenotype = XenotypeFor(roll, geneticMother);
            PawnKindDef kind = NewbornKindFor(geneticMother, roll);
            Faction faction = birtherThing.Faction;

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                faction,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: false,
                allowDead: false,
                allowDowned: true,
                // Goblin births explicitly add the one wanted maternal relation below.
                // Do not let generic pawn generation add unrelated family links.
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                colonistRelationChanceFactor: 1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                biocodeWeaponChance: 0f,
                biocodeApparelChance: 0f,
                extraPawnForExtraRelationChance: null,
                relationWithExtraPawnChanceFactor: 1f,
                validatorPreGear: null,
                validatorPostGear: null,
                forcedTraits: null,
                prohibitedTraits: null,
                minChanceToRedressWorldPawn: null,
                fixedBiologicalAge: null,
                fixedChronologicalAge: null,
                fixedGender: roll == GoblinBirthResult.MotherXenotype ? (Gender?)null : Gender.Male,
                fixedLastName: null,
                fixedBirthName: null,
                fixedTitle: null,
                fixedIdeo: null,
                // 한국어 의도: 아기는 어미/정착지 이데올로기를 물려받게 둡니다.
                // 이데올로기를 비워두면 바닐라 정신붕괴가 IdeoChange를 골라 예외를 냅니다.
                forceNoIdeo: !ModsConfig.IdeologyActive,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                forcedXenogenes: null,
                forcedEndogenes: null,
                forcedXenotype: forcedXenotype,
                forcedCustomXenotype: null,
                allowedXenotypes: null,
                forceBaselinerChance: 0f,
                developmentalStages: DevelopmentalStage.Newborn);
            request.DontGivePreArrivalPathway = true;

            Pawn child = PawnGenerator.GeneratePawn(request);
            ApplyBirthResult(child, geneticMother, roll);
            return child;
        }

        public static Pawn GeneratePregeneratedChild(Pawn geneticMother, Pawn father, GoblinBirthResult roll)
        {
            if (geneticMother == null)
            {
                return null;
            }

            XenotypeDef forcedXenotype = XenotypeFor(roll, geneticMother);
            PawnGenerationRequest request = new PawnGenerationRequest(
                NewbornKindFor(geneticMother, roll),
                geneticMother.Faction,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: false,
                allowDead: false,
                allowDowned: true,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                colonistRelationChanceFactor: 1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                biocodeWeaponChance: 0f,
                biocodeApparelChance: 0f,
                extraPawnForExtraRelationChance: null,
                relationWithExtraPawnChanceFactor: 1f,
                validatorPreGear: null,
                validatorPostGear: null,
                forcedTraits: null,
                prohibitedTraits: null,
                minChanceToRedressWorldPawn: null,
                fixedBiologicalAge: null,
                fixedChronologicalAge: null,
                fixedGender: roll == GoblinBirthResult.MotherXenotype ? (Gender?)null : Gender.Male,
                fixedLastName: null,
                fixedBirthName: null,
                fixedTitle: null,
                fixedIdeo: null,
                // 한국어 의도: 아기는 어미/정착지 이데올로기를 물려받게 둡니다.
                // 이데올로기를 비워두면 바닐라 정신붕괴가 IdeoChange를 골라 예외를 냅니다.
                forceNoIdeo: !ModsConfig.IdeologyActive,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                forcedXenogenes: null,
                forcedEndogenes: null,
                forcedXenotype: forcedXenotype,
                forcedCustomXenotype: null,
                allowedXenotypes: null,
                forceBaselinerChance: 0f,
                developmentalStages: DevelopmentalStage.Newborn);
            request.DontGivePreArrivalPathway = true;

            Pawn child = PawnGenerator.GeneratePawn(request);
            ApplyBirthResult(child, geneticMother, roll);
            return child;
        }

        public static GoblinBirthResult RollGoblinBirthResult(bool hobgoblinFather)
        {
            float value = Rand.Value;
            if (hobgoblinFather)
            {
                if (value < 0.78f)
                {
                    return GoblinBirthResult.ThinGoblin;
                }
                if (value < 0.98f)
                {
                    return GoblinBirthResult.Hobgoblin;
                }
                return GoblinBirthResult.MotherXenotype;
            }

            // 한국어 의도: 띤 고블린 아비에게서는 어미 종족을 따르는 아기가 나오지 않습니다.
            // 고블린 혈통이 아비 쪽이라는 설정을 띤 아비에서 확실히 하기 위해,
            // 예전 어미종족 2%를 띤 고블린 쪽으로 넘겼습니다. 홉 아비는 그대로 2%가 남습니다.
            if (value < 0.95f)
            {
                return GoblinBirthResult.ThinGoblin;
            }
            return GoblinBirthResult.Hobgoblin;
        }

        public static XenotypeDef XenotypeFor(GoblinBirthResult result, Pawn geneticMother)
        {
            switch (result)
            {
                case GoblinBirthResult.ThinGoblin:
                    return MUGBDefOf.MUGB_Goblin;
                case GoblinBirthResult.Hobgoblin:
                    return MUGBDefOf.MUGB_Hobgoblin;
                default:
                    return geneticMother?.genes?.Xenotype ?? XenotypeDefOf.Baseliner;
            }
        }

        public static void ApplyBirthResult(Pawn child, Pawn geneticMother, GoblinBirthResult result)
        {
            if (child?.genes == null)
            {
                return;
            }

            XenotypeDef xenotype = XenotypeFor(result, geneticMother);
            if (xenotype != null && child.genes.Xenotype != xenotype)
            {
                child.genes.SetXenotype(xenotype);
            }

            if (result == GoblinBirthResult.ThinGoblin || result == GoblinBirthResult.Hobgoblin)
            {
                child.gender = Gender.Male;
                InitializeRapidGoblinBirthAge(child);
                EnsureSingleToxicPheromoneGene(child);
                EnsureBirthCrossEyedMutation(child);
            }
            else if (result == GoblinBirthResult.MotherXenotype)
            {
                EnsureHalfGoblinAncestry(child);
            }

            GoblinUtility.EnforceGoblinStoryGraphics(child);

            // 한국어 의도: 렌더 트리를 반드시 한 번 버리게 합니다.
            //
            // HAR은 꼬리/뿔 같은 부속을 그릴지 말지를 렌더 트리를 만들 때 한 번 판정해 캐시합니다.
            // 그런데 RJW Menstruation은 아기를 만든 직후 EnsureGraphicsInitialized를 불러 트리를
            // 미리 만들어 버립니다. 그 시점은 MUGB가 제노타입을 찍기 전이라, "아직 고블린이 아닌"
            // 상태로 판정된 부속 노드가 그대로 남습니다.
            //
            // EnforceGoblinStoryGraphics는 바꿀 것이 있을 때만 트리를 버리므로, 제노타입만 바뀐
            // 경우에는 부속이 정리되지 않습니다. 출산당 1회뿐이니 여기서 무조건 버립니다.
            child.Drawer?.renderer?.SetAllGraphicsDirty();
        }


        // 한국어 의도: 갓 태어난 고블린에게 아비의 사상을 물려줍니다.
        //
        // 고블린은 짧은 어린이 단계를 거치지만 사상 노출을 충분히 쌓을 시간이 없으므로
        // 출생 시점에 부모의 사상을 명시적으로 지정합니다.
        //
        // 아비를 먼저 보는 이유는 고블린 혈통이 아비 쪽이기 때문이고, 아비에게 사상이 없으면
        // 어미 것으로 대신합니다. 출산 시 1회만 실행되므로 틱 부담은 없습니다.
        private static void InheritGoblinIdeo(Pawn child, Pawn geneticMother, Pawn father)
        {
            if (!ModsConfig.IdeologyActive || child?.ideo == null || !GoblinUtility.IsGoblin(child) || child.Ideo != null)
            {
                return;
            }

            Ideo inherited = father?.Ideo ?? geneticMother?.Ideo;
            if (inherited != null)
            {
                child.ideo.SetIdeo(inherited);
            }
        }

        private static void InitializeRapidGoblinBirthAge(Pawn child)
        {
            if (child?.ageTracker == null)
            {
                return;
            }

            // Full goblins may spend a short baby stage first (mod option, half a day by default),
            // then about 3.5 days as children, then mature at 16 and 18.
            // BirthAgeYears follows that option: 0 with the baby stage on, 3 with it off.
            GoblinPawnKindBackstoryUtility.AssignGrowingGoblinChildhood(child);
            long birthAgeTicks = GoblinAgeUtility.TicksForYears(GoblinAgeUtility.BirthAgeYears);
            child.ageTracker.AgeBiologicalTicks = birthAgeTicks;
            child.ageTracker.AgeChronologicalTicks = birthAgeTicks;

            // Entering HumanlikeTeenager can invoke the vanilla adult worker while the game is running.
            // Restore the intended MUGB placeholder after that transition so no adult backstory leaks in.
            GoblinPawnKindBackstoryUtility.AssignGrowingGoblinChildhood(child);
        }

        private static void EnsureBirthCrossEyedMutation(Pawn child)
        {
            if (child?.genes == null || child.genes.GetGene(MUGBDefOf.MUGB_Gene_CrossEyed) != null)
            {
                return;
            }

            if (Rand.ChanceSeeded(0.25f, child.thingIDNumber ^ 0x43524F53))
            {
                child.genes.AddGene(MUGBDefOf.MUGB_Gene_CrossEyed, xenogene: false);
            }
        }

        private static void EnsureHalfGoblinAncestry(Pawn child)
        {
            if (child?.genes == null
                || MUGBDefOf.MUGB_Gene_HalfGoblinAncestry == null)
            {
                return;
            }

            if (child.genes.GetGene(MUGBDefOf.MUGB_Gene_HalfGoblinAncestry) == null)
            {
                child.genes.AddGene(MUGBDefOf.MUGB_Gene_HalfGoblinAncestry, xenogene: false);
            }
            EnsureGoblinFastLearner(child);
        }

        private static void EnsureGoblinFastLearner(Pawn child)
        {
            if (child?.genes == null
                || MUGBDefOf.MUGB_Gene_GoblinFastLearner == null
                || child.genes.GetGene(MUGBDefOf.MUGB_Gene_GoblinFastLearner) != null)
            {
                return;
            }

            if (MUGBDefOf.MUGB_Gene_GoblinSlowLearner != null)
            {
                Gene slowLearner = child.genes.GetGene(MUGBDefOf.MUGB_Gene_GoblinSlowLearner);
                if (slowLearner != null)
                {
                    child.genes.RemoveGene(slowLearner);
                }
            }

            // Korean source intent: 홉고블린 아비에게서 낮은 확률로 어머니 종족을 따라 나온 하프고블린은
            // 고블린만큼은 아니어도 영리한 혈통으로 보이게 빠른학습을 붙인다.
            // 띤고블린 아비에게서는 하프고블린이 나오지 않으므로 이 경로는 홉 아비 전용이다.
            child.genes.AddGene(MUGBDefOf.MUGB_Gene_GoblinFastLearner, xenogene: false);
        }

        private static void ApplyGoblinBirthStrain(Pawn geneticMother)
        {
            if (geneticMother?.health == null || GoblinUtility.IsGoblin(geneticMother) || MUGBDefOf.MUGB_GoblinBirthStrain == null)
            {
                return;
            }

            int limit = GoblinBirthStrainUtility.CurrentLimit;
            Hediff_GoblinBirthStrain strain = GoblinBirthStrainUtility.GetOrMigrateStrain(geneticMother, limit);
            if (limit <= 0)
            {
                if (strain != null)
                {
                    geneticMother.health.RemoveHediff(strain);
                }
                return;
            }

            if (strain == null)
            {
                strain = HediffMaker.MakeHediff(MUGBDefOf.MUGB_GoblinBirthStrain, geneticMother) as Hediff_GoblinBirthStrain;
                if (strain == null)
                {
                    return;
                }
                strain.SetBirthCount(1, limit);
                geneticMother.health.AddHediff(strain);
                return;
            }

            int newCount = strain.BirthCount + 1;
            strain.SetBirthCount(newCount, limit);
            if (newCount >= limit && !geneticMother.Dead)
            {
                geneticMother.Kill(null, strain);
            }
        }

        private static void ApplyGoblinBirthMood(Pawn geneticMother)
        {
            if (geneticMother?.needs?.mood?.thoughts?.memories == null || GoblinUtility.IsGoblin(geneticMother))
            {
                return;
            }

            ThoughtDef babyBorn = DefDatabase<ThoughtDef>.GetNamedSilentFail("BabyBorn");
            if (babyBorn != null)
            {
                geneticMother.needs.mood.thoughts.memories.RemoveMemoriesOfDef(babyBorn);
            }

            if (MUGBDefOf.MUGB_GaveBirthToGoblinLitter != null)
            {
                geneticMother.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_GaveBirthToGoblinLitter);
            }
        }

        private static void EnsureSingleToxicPheromoneGene(Pawn child)
        {
            if (child?.genes == null)
            {
                return;
            }

            bool weak = Rand.ChanceSeeded(0.10f, child.thingIDNumber ^ 0x50484552);
            Gene weakGene = child.genes.GetGene(MUGBDefOf.MUGB_Gene_GoblinWeakToxicPheromone);
            Gene strongGene = child.genes.GetGene(MUGBDefOf.MUGB_Gene_GoblinStrongToxicPheromone);

            if (weak)
            {
                if (strongGene != null)
                {
                    child.genes.RemoveGene(strongGene);
                }
                if (weakGene == null)
                {
                    child.genes.AddGene(MUGBDefOf.MUGB_Gene_GoblinWeakToxicPheromone, xenogene: false);
                }
            }
            else
            {
                if (weakGene != null)
                {
                    child.genes.RemoveGene(weakGene);
                }
                if (strongGene == null)
                {
                    child.genes.AddGene(MUGBDefOf.MUGB_Gene_GoblinStrongToxicPheromone, xenogene: false);
                }
            }
        }

        private static void AddBirthRelationsAndSettings(Pawn child, Pawn geneticMother, Pawn birtherPawn, Pawn father)
        {
            NormalizeGoblinBirthRelations(child, geneticMother, father);

            if (child.playerSettings != null && geneticMother.playerSettings != null)
            {
                child.playerSettings.AreaRestrictionInPawnCurrentMap = geneticMother.playerSettings.AreaRestrictionInPawnCurrentMap;
            }

            if (birtherPawn != null)
            {
                child.mindState?.SetAutofeeder(birtherPawn, AutofeedMode.Urgent);
                TaleRecorder.RecordTale(TaleDefOf.GaveBirth, birtherPawn, child);
            }

            ApplyBirthGuestStatus(child, geneticMother);
        }

        private static void NormalizeGoblinBirthRelations(Pawn child, Pawn geneticMother, Pawn father)
        {
            if (child?.RaceProps?.IsFlesh != true || child.relations == null)
            {
                return;
            }

            // Korean source intent: 고블린 다산으로 인한 이복형제 관계망을 만들지 않는다.
            // 유전적 어머니만 Parent로 남기고, 아비는 유전자/출산 결과/C 성 계승에만 사용한다.
            if (geneticMother != null && !child.relations.DirectRelationExists(PawnRelationDefOf.Parent, geneticMother))
            {
                child.relations.AddDirectRelation(PawnRelationDefOf.Parent, geneticMother);
            }

            if (father != null && child.relations.DirectRelationExists(PawnRelationDefOf.Parent, father))
            {
                child.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, father);
            }

            child.relations.TryRemoveDirectRelation(PawnRelationDefOf.ParentBirth, father);
            GoblinPersonalNameUtility.InheritGoblinLineName(child, father);
        }

        // 한국어 의도: 어미의 신분(노예/포로)을 아기에게 물려줍니다.
        //
        // 예전에는 순수 고블린만 대상이었습니다. 그래서 어미 종족을 따라 나온 하프고블린은
        // 이 함수를 그냥 통과했고, 노예는 팩션이 Faction.OfPlayer이므로 게스트 신분이 지정되지 않은
        // 아기가 자유 정착민으로 태어나 버렸습니다. 하프고블린도 같은 규칙을 따르게 넓혔습니다.
        //
        // 포로로 태어난 고블린 표시는 13세 신분 선택 편지와 짝인 순수 고블린 전용 흐름이므로
        // 하프고블린에는 달지 않습니다. 하프고블린은 바닐라 아기->어린이 편지를 그대로 받습니다.
        private static void ApplyBirthGuestStatus(Pawn child, Pawn geneticMother)
        {
            if (child?.guest == null || geneticMother == null)
            {
                return;
            }

            bool pureGoblin = GoblinUtility.IsGoblin(child);
            if (!pureGoblin && !GoblinUtility.HasHalfGoblinAncestry(child))
            {
                return;
            }

            if (geneticMother.IsSlaveOfColony || geneticMother.IsPrisonerOfColony)
            {
                if (ModsConfig.IdeologyActive)
                {
                    child.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
                    if (pureGoblin)
                    {
                        Current.Game?.GetComponent<GoblinRapidMaturationComponent>()?.MarkCaptiveBornGoblin(child);
                    }
                }
                else
                {
                    child.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                }
                child.needs?.AddOrRemoveNeedsAsAppropriate();
                return;
            }

            if (geneticMother.Faction == Faction.OfPlayer && child.Faction != Faction.OfPlayer)
            {
                child.SetFaction(Faction.OfPlayer);
                child.guest.SetGuestStatus(null);
            }
        }

        private static void SpawnExtraNewborn(Pawn child, Pawn geneticMother, Thing birtherThing)
        {
            IntVec3? nearCell = null;
            Pawn birtherPawn = birtherThing as Pawn;
            if (birtherPawn?.Spawned == true)
            {
                int? sleepingSlot;
                IntVec3 birthCenter = birtherPawn.CurrentBed(out sleepingSlot)?.GetFootSlotPos(sleepingSlot.Value) ?? birtherPawn.PositionHeld;
                nearCell = CellFinder.RandomClosewalkCellNear(birthCenter, birtherPawn.Map, 1, delegate(IntVec3 cell)
                {
                    if (cell == birtherPawn.PositionHeld)
                    {
                        return false;
                    }
                    Building building = birtherPawn.Map.edificeGrid[cell];
                    return building == null || building.def?.IsBed != true;
                });
            }

            if (PawnUtility.TrySpawnHatchedOrBornPawn(child, birtherThing, nearCell))
            {
                if (birtherPawn?.Spawned == true)
                {
                    birtherPawn.GetLord()?.AddPawn(child);
                    child.caller?.DoCall();
                }
                return;
            }

            Find.WorldPawns.PassToWorld(child, PawnDiscardDecideMode.Discard);
        }

        public static IEnumerable<Gizmo> AddDebugBirthGizmos(IEnumerable<Gizmo> gizmos, Pawn pawn)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos || pawn?.Map == null || pawn.RaceProps?.Humanlike != true || pawn.gender != Gender.Female)
            {
                yield break;
            }

            yield return MakeDebugBirthCommand(pawn, hobgoblinFather: false);
            yield return MakeDebugBirthCommand(pawn, hobgoblinFather: true);
        }

        private static Command_Action MakeDebugBirthCommand(Pawn mother, bool hobgoblinFather)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = hobgoblinFather ? "DEV: MUGB birth - hobgoblin father" : "DEV: MUGB birth - goblin father",
                defaultDesc = "Immediately runs a MUGB goblin birth test on this pawn, using a matching goblin father from the current map.",
                action = delegate
                {
                    Pawn father = FindFatherOnMap(mother.Map, hobgoblinFather);
                    if (father == null)
                    {
                        Messages.Message(hobgoblinFather ? "No hobgoblin father found on this map." : "No goblin father found on this map.", mother, MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }

                    PregnancyUtility.ApplyBirthOutcome(
                        RitualOutcomeEffectDefOf.ChildBirth.BestOutcome,
                        1f,
                        null,
                        null,
                        mother,
                        mother,
                        father,
                        null,
                        null,
                        null,
                        preventLetter: true);
                    Messages.Message($"MUGB birth test complete. Father: {father.LabelShort}.", mother, MessageTypeDefOf.PositiveEvent, historical: false);
                }
            };
            return command;
        }

        private static Pawn FindFatherOnMap(Map map, bool hobgoblin)
        {
            if (map == null)
            {
                return null;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.gender != Gender.Male || !GoblinUtility.IsGoblin(pawn))
                {
                    continue;
                }

                if (hobgoblin == GoblinUtility.IsHobgoblin(pawn))
                {
                    return pawn;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Pawn_GetGizmos_GoblinBirthDebugPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = GoblinBirthUtility.AddDebugBirthGizmos(__result, __instance);
        }
    }

    public static class PregnancyUtility_GetInheritedGeneSet_GoblinPlan_Patch
    {
        public static void Postfix(Pawn father, Pawn mother)
        {
            GoblinPregnancyPlanInitializer.NotePotentialGoblinConception(mother, father);
        }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.PostAdd))]
    public static class Hediff_PostAdd_GoblinPregnancyPlan_Patch
    {
        public static void Postfix(Hediff __instance)
        {
            if (__instance is HediffWithComps hediff)
            {
                GoblinPregnancyPlanInitializer.TryInitializePregnancyHediff(hediff);
            }
        }
    }

    public static class GoblinPregnancyPlanInitializer
    {
        private static readonly Dictionary<int, Pawn> PendingGoblinFathersByMother = new Dictionary<int, Pawn>();

        public static void NotePotentialGoblinConception(Pawn mother, Pawn father)
        {
            if (mother == null || !GoblinUtility.IsGoblin(father) || mother.RaceProps?.Humanlike != true)
            {
                return;
            }

            PendingGoblinFathersByMother[mother.thingIDNumber] = father;
            HediffWithComps pregnancy = PregnancyUtility.GetPregnancyHediff(mother) as HediffWithComps;
            if (pregnancy != null)
            {
                TryInitializePregnancyHediff(pregnancy);
            }
        }

        public static void TryInitializePregnancyHediff(HediffWithComps hediff)
        {
            HediffComp_MUGBGoblinPregnancyPlan comp = hediff?.TryGetComp<HediffComp_MUGBGoblinPregnancyPlan>();
            if (comp == null || comp.Initialized || hediff.pawn == null || !hediff.def.pregnant)
            {
                return;
            }

            if (!PendingGoblinFathersByMother.TryGetValue(hediff.pawn.thingIDNumber, out Pawn father) || !GoblinUtility.IsGoblin(father))
            {
                return;
            }

            comp.Initialize(hediff.pawn, father);
            PendingGoblinFathersByMother.Remove(hediff.pawn.thingIDNumber);
        }

        // 한국어 의도: 계획 comp가 없는 임신 헤디프(RJW 계열)로 수태가 끝난 뒤 대기 항목을 비웁니다.
        // 그러지 않으면 이 표가 아비 폰 참조를 계속 들고 있게 되고, 나중에 같은 어미가
        // 다른 방식으로 임신했을 때 지난 아비가 잘못 잡힐 수 있습니다.
        public static void ClearPendingFather(Pawn mother)
        {
            if (mother != null)
            {
                PendingGoblinFathersByMother.Remove(mother.thingIDNumber);
            }
        }
    }

    public class HediffCompProperties_MUGBGoblinPregnancyPlan : HediffCompProperties
    {
        public HediffCompProperties_MUGBGoblinPregnancyPlan()
        {
            compClass = typeof(HediffComp_MUGBGoblinPregnancyPlan);
        }
    }

    public class HediffComp_MUGBGoblinPregnancyPlan : HediffComp
    {
        private const float TargetPregnancyDays = 3.75f;
        private const float VanillaHumanPregnancyDays = 18f;
        private const float ExtraSeverityPerTick = ((1f / TargetPregnancyDays) - (1f / VanillaHumanPregnancyDays)) / 60000f;

        private List<int> plannedResults;
        private int nextResultIndex;
        private bool hobgoblinFather;
        private bool initialized;
        private bool usesPregeneratedBabies;
        private bool birthStrainApplied;

        public bool Initialized => initialized;
        public bool UsesPregeneratedBabies => usesPregeneratedBabies;
        public bool HasUnbornResult => plannedResults != null && nextResultIndex < plannedResults.Count;
        public int PlannedCount => plannedResults?.Count ?? 0;

        public bool BirthStrainApplied
        {
            get => birthStrainApplied;
            set => birthStrainApplied = value;
        }

        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (!initialized || plannedResults == null)
                {
                    return null;
                }
                return $"goblin litter x{plannedResults.Count}";
            }
        }

        public override string CompDescriptionExtra
        {
            get
            {
                if (!initialized || plannedResults == null)
                {
                    return null;
                }
                return $"\n\nMUGB: This pregnancy carries a predetermined goblin litter. Expected offspring: {plannedResults.Count}. Expected duration: about {TargetPregnancyDays:0.##} days.";
            }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (initialized && parent?.def?.defName == "PregnantHuman")
            {
                severityAdjustment += ExtraSeverityPerTick;
            }
        }

        public void Initialize(Pawn mother, Pawn father)
        {
            InitializeFromFather(GoblinUtility.IsHobgoblin(father), mother);
            ApplyPregnancyDrain(mother);
            TryPrepareMenstruationBabies(mother, father);
        }

        public void InitializeFromEmbryo(Pawn mother, bool donorIsHobgoblin)
        {
            InitializeFromFather(donorIsHobgoblin, mother);
            ApplyPregnancyDrain(mother);
        }

        public void InitializeFromFather(bool fatherIsHobgoblin, Pawn mother = null)
        {
            if (initialized)
            {
                return;
            }

            hobgoblinFather = fatherIsHobgoblin;
            int baseCount = hobgoblinFather ? Rand.RangeInclusive(2, 4) : Rand.RangeInclusive(3, 4);
            int maternalBonus = GoblinLitterSizeUtility.RollMaternalLitterBonus(mother);
            int count = GoblinLitterSizeUtility.ScaleCurrentCount(baseCount + maternalBonus);
            plannedResults = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                plannedResults.Add((int)GoblinBirthUtility.RollGoblinBirthResult(hobgoblinFather));
            }
            nextResultIndex = 0;
            initialized = true;
        }

        // 한국어 의도: 결과를 소비하지 않고 미리 보기만 합니다. 아기가 생성되기 전에
        // "이번 아기가 순수 고블린인가"를 알아야 성별을 미리 고정할 수 있습니다.
        public GoblinBirthUtility.GoblinBirthResult? PeekNextResult()
        {
            if (plannedResults != null && nextResultIndex < plannedResults.Count)
            {
                return (GoblinBirthUtility.GoblinBirthResult)plannedResults[nextResultIndex];
            }

            return null;
        }

        // 한국어 의도: 계획에 없던 아기가 뒤늦게 늘어났을 때(이란성 쌍둥이) 결과를 하나 더 굴려
        // 계획 끝에 붙입니다. 그 결과대로 아기를 만들어 두면 출산 시 다시 적용되는 판정과
        // 생성 시점의 아기가 어긋나지 않습니다.
        public GoblinBirthUtility.GoblinBirthResult AppendRolledResult(bool fatherIsHobgoblin)
        {
            GoblinBirthUtility.GoblinBirthResult roll = GoblinBirthUtility.RollGoblinBirthResult(fatherIsHobgoblin);
            if (plannedResults == null)
            {
                plannedResults = new List<int>();
            }
            plannedResults.Add((int)roll);
            return roll;
        }

        public GoblinBirthUtility.GoblinBirthResult NextResultOrFallback(bool fatherIsHobgoblin)
        {
            if (plannedResults != null && nextResultIndex < plannedResults.Count)
            {
                return (GoblinBirthUtility.GoblinBirthResult)plannedResults[nextResultIndex++];
            }

            return GoblinBirthUtility.RollGoblinBirthResult(fatherIsHobgoblin);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            CopyToNewestPregnancyLikeHediff();
            RemovePregnancyDrainIfNoActivePlan();
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref plannedResults, "plannedResults", LookMode.Value);
            Scribe_Values.Look(ref nextResultIndex, "nextResultIndex");
            Scribe_Values.Look(ref hobgoblinFather, "hobgoblinFather");
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref usesPregeneratedBabies, "usesPregeneratedBabies");
            Scribe_Values.Look(ref birthStrainApplied, "birthStrainApplied");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && plannedResults == null)
            {
                plannedResults = new List<int>();
            }
        }

        private void CopyToNewestPregnancyLikeHediff()
        {
            if (!initialized || Pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            HediffComp_MUGBGoblinPregnancyPlan target = null;
            List<Hediff> hediffs = Pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                HediffWithComps hediff = hediffs[i] as HediffWithComps;
                HediffComp_MUGBGoblinPregnancyPlan comp = hediff?.TryGetComp<HediffComp_MUGBGoblinPregnancyPlan>();
                if (comp != null && comp != this)
                {
                    target = comp;
                    break;
                }
            }

            if (target == null || target.initialized)
            {
                return;
            }

            target.plannedResults = plannedResults != null ? new List<int>(plannedResults) : new List<int>();
            target.nextResultIndex = nextResultIndex;
            target.hobgoblinFather = hobgoblinFather;
            target.initialized = initialized;
            target.usesPregeneratedBabies = usesPregeneratedBabies;
            target.birthStrainApplied = birthStrainApplied;
            ApplyPregnancyDrain(Pawn);
        }

        private static void ApplyPregnancyDrain(Pawn mother)
        {
            if (mother == null || GoblinUtility.IsGoblin(mother) || MUGBDefOf.MUGB_GoblinPregnancyDrain == null)
            {
                return;
            }

            if (!mother.health.hediffSet.HasHediff(MUGBDefOf.MUGB_GoblinPregnancyDrain))
            {
                mother.health.AddHediff(MUGBDefOf.MUGB_GoblinPregnancyDrain);
            }
        }

        private void RemovePregnancyDrainIfNoActivePlan()
        {
            Pawn pawn = Pawn;
            if (pawn == null || MUGBDefOf.MUGB_GoblinPregnancyDrain == null)
            {
                return;
            }

            if (GoblinBirthUtility.GetActivePregnancyPlan(pawn) != null)
            {
                return;
            }

            Hediff drain = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinPregnancyDrain);
            if (drain != null)
            {
                pawn.health.RemoveHediff(drain);
            }
        }

        private void TryPrepareMenstruationBabies(Pawn mother, Pawn father)
        {
            if (mother == null || father == null || plannedResults.NullOrEmpty())
            {
                return;
            }

            HediffComp pregeneratedComp = FindPregeneratedBabiesComp(parent as HediffWithComps);
            if (pregeneratedComp == null)
            {
                return;
            }

            FieldInfo babiesField = pregeneratedComp.GetType().GetField("babies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (babiesField == null || !typeof(List<Pawn>).IsAssignableFrom(babiesField.FieldType))
            {
                return;
            }

            List<Pawn> babies = new List<Pawn>();
            for (int i = 0; i < plannedResults.Count; i++)
            {
                Pawn child = GoblinBirthUtility.GeneratePregeneratedChild(mother, father, (GoblinBirthUtility.GoblinBirthResult)plannedResults[i]);
                if (child != null)
                {
                    babies.Add(child);
                }
            }

            if (babies.Count > 0)
            {
                babiesField.SetValue(pregeneratedComp, babies);
                usesPregeneratedBabies = true;
            }
        }

        private static HediffComp FindPregeneratedBabiesComp(HediffWithComps hediff)
        {
            if (hediff?.comps == null)
            {
                return null;
            }

            for (int i = 0; i < hediff.comps.Count; i++)
            {
                HediffComp comp = hediff.comps[i];
                if (comp?.GetType().FullName == "RJW_Menstruation.HediffComp_PregeneratedBabies")
                {
                    return comp;
                }
            }
            return null;
        }
    }

    public class ThoughtWorker_GoblinPregnancyBurden : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.RaceProps?.Humanlike != true || GoblinUtility.IsGoblin(p) || MUGBSurgeryUtility.HasNosePickedLobotomy(p))
            {
                return ThoughtState.Inactive;
            }

            HediffComp_MUGBGoblinPregnancyPlan plan = GoblinBirthUtility.GetActivePregnancyPlan(p);
            if (plan?.Initialized != true)
            {
                return ThoughtState.Inactive;
            }

            int stage = 0;
            Hediff strain = p.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_GoblinBirthStrain);
            if (strain != null)
            {
                stage = System.Math.Min(3, System.Math.Max(0, (int)System.Math.Floor(strain.Severity)));
            }
            return ThoughtState.ActiveAtStage(stage);
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.OpinionOf))]
    public static class Pawn_RelationsTracker_OpinionOf_GoblinChildAffection_Patch
    {
        public static void Postfix(Pawn ___pawn, Pawn other, ref int __result)
        {
            __result += GoblinIncestUtility.RestoredIncestOpinionOffset(___pawn, other);

            if (__result <= 0 || !GoblinParentOpinionUtility.ShouldReduceParentOpinion(___pawn, other))
            {
                return;
            }

            __result = System.Math.Max(1, (int)System.Math.Round(__result * 0.2f));
        }
    }

    public static class GoblinParentOpinionUtility
    {
        public static bool ShouldReduceParentOpinion(Pawn observer, Pawn target)
        {
            if (observer == null || target == null || observer == target)
            {
                return false;
            }

            if (GoblinUtility.IsGoblin(observer) || !GoblinUtility.IsGoblin(target))
            {
                return false;
            }

            return target.relations?.DirectRelationExists(PawnRelationDefOf.Parent, observer) == true
                || observer.relations?.DirectRelationExists(PawnRelationDefOf.Parent, target) == true;
        }
    }
}
