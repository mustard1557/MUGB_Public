using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    /// <summary>
    /// 고블린 기지에서 MUGB가 추적하는 포로를 플레이어가 해방할 수 있게 합니다.
    ///
    /// 배경:
    /// - 고블린 기지는 BF 가구모드가 없을 때 포로를 말뚝에 묶은 것처럼 보이게 하려고
    ///   MUGB_Restrained를 부여합니다. 이 헤디프는 이동과 조작을 0으로 막습니다.
    /// - BF가 있으면 BF 가구가 자체 구속 헤디프를 사용하지만, BF의 구속 해제는 바닐라 포로
    ///   석방이 아닙니다. 따라서 물리적 구속 여부 대신 MUGB 기지 관리 목록으로 대상을 한정합니다.
    ///
    /// 동작:
    /// - 관리 중인 포로에게 우클릭하면 "해방하기"가 뜹니다. 작업이 끝나면 MUGB/BF 구속을
    ///   제거하고 바닐라 석방 흐름으로 맵 밖에 내보냅니다.
    /// - Released 상태와 기존 관리 목록을 함께 표식으로 사용해, 떠나는 동안 플레이어와만
    ///   상호 비적대가 됩니다. 맵을 나가면 목록에서도 제거됩니다.
    /// </summary>
    public static class GoblinRestraintUtility
    {
        public const int ReleaseInteractionTicks = 120;

        public static HediffDef RestrainedDef => DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_Restrained");

        public static bool IsRestrained(Pawn pawn)
        {
            // BF's own unbind job only removes its bed hediff; it does not release the prisoner.
            // Keep MUGB's liberation option available for the exact captives tracked by the base.
            return IsManagedBasePrisoner(pawn)
                && pawn.IsPrisoner
                && pawn.guest?.Released != true;
        }

        internal static bool IsManagedBasePrisoner(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            return map != null
                && (map.GetComponent<MUGB_MedievalBaseManager>()?.IsManagedPrisoner(pawn) == true
                    || map.GetComponent<MUGB_TribalBasePrisonManager>()?.IsManagedPrisoner(pawn) == true);
        }

        // 풀어주는 폰이 실제로 작업을 수행할 수 있는지. 대상이 아니라 시전자 쪽 조건입니다.
        public static bool CanReleaseRestraint(Pawn releaser, Pawn target, out string reason)
        {
            reason = null;
            if (releaser == null || target == null || releaser == target)
            {
                return false;
            }

            if (!IsRestrained(target))
            {
                return false;
            }

            if (!releaser.RaceProps.Humanlike || releaser.Downed || releaser.InMentalState)
            {
                reason = "MUGB_CannotReleaseRestraint_Incapable".Translate();
                return false;
            }

            if (!releaser.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                reason = "MUGB_CannotReleaseRestraint_NoPath".Translate();
                return false;
            }

            if (!releaser.CanReserve(target))
            {
                reason = "MUGB_CannotReleaseRestraint_Reserved".Translate();
                return false;
            }

            return true;
        }

        public static void ReleaseRestraint(Pawn target)
        {
            if (target?.health == null || !IsManagedBasePrisoner(target))
            {
                return;
            }

            HediffDef def = RestrainedDef;
            HediffDef bondage = DefDatabase<HediffDef>.GetNamedSilentFail("DDJY_Hediff_BondageBed");
            foreach (Hediff hediff in target.health.hediffSet.hediffs
                .Where(h => h.def == def || h.def == bondage)
                .ToList())
            {
                target.health.RemoveHediff(hediff);
            }

            // 묶인 채로 잡혀 있던 눕기 작업이 남아 있으면 끊어, 포로가 스스로 일어나게 합니다.
            if (target.CurJobDef == JobDefOf.LayDown && target.jobs != null)
            {
                target.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            target.ownership?.UnclaimBed();

            if (!target.IsPrisoner || target.guest?.Released == true)
            {
                return;
            }

            // RimWorld marks the pawn Released and immediately assigns an exit-map job.
            // Keep it in the base's serialized captive list until ExitMap; that list is the
            // precise, save-safe liberation marker and avoids adding another ticking hediff.
            GenGuest.PrisonerRelease(target);
        }

        internal static bool IsLiberatedBaseCaptive(Pawn pawn)
        {
            // Released is checked first because hostility queries are hot and almost every pawn fails here.
            return pawn?.guest?.Released == true
                && IsManagedBasePrisoner(pawn);
        }

        internal static void ForgetManagedBasePrisoner(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            if (map != null)
            {
                map.GetComponent<MUGB_MedievalBaseManager>()?.NotifyPrisonerReleased(pawn);
                map.GetComponent<MUGB_TribalBasePrisonManager>()?.NotifyPrisonerReleased(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    public static class GenHostility_HostileToThings_LiberatedBaseCaptivePatch
    {
        public static bool Prefix(Thing a, Thing b, ref bool __result)
        {
            if ((a?.Faction?.IsPlayer == true && b is Pawn captiveB && GoblinRestraintUtility.IsLiberatedBaseCaptive(captiveB))
                || (b?.Faction?.IsPlayer == true && a is Pawn captiveA && GoblinRestraintUtility.IsLiberatedBaseCaptive(captiveA)))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
    public static class GenHostility_HostileToFaction_LiberatedBaseCaptivePatch
    {
        public static bool Prefix(Thing t, Faction fac, ref bool __result)
        {
            if (fac?.IsPlayer == true
                && t is Pawn captive
                && GoblinRestraintUtility.IsLiberatedBaseCaptive(captive))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Pawn_ExitMap_ClearLiberatedBaseCaptivePatch
    {
        public static void Prefix(Pawn __instance)
        {
            GoblinRestraintUtility.ForgetManagedBasePrisoner(__instance);
        }
    }

    public class JobDriver_ReleaseRestraint : JobDriver
    {
        private Pawn Target => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !GoblinRestraintUtility.IsRestrained(Target));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_General.WaitWith(TargetIndex.A, GoblinRestraintUtility.ReleaseInteractionTicks, useProgressBar: true);
            yield return Toils_General.Do(delegate
            {
                GoblinRestraintUtility.ReleaseRestraint(Target);
            });
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetFloatMenuOptions))]
    public static class Pawn_GetFloatMenuOptions_GoblinReleaseRestraintPatch
    {
        public static void Postfix(Pawn __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (__instance == null || selPawn == null || __instance == selPawn)
            {
                return;
            }

            if (!GoblinRestraintUtility.IsRestrained(__instance))
            {
                return;
            }

            Pawn target = __instance;
            List<FloatMenuOption> options = __result?.ToList() ?? new List<FloatMenuOption>();

            if (GoblinRestraintUtility.CanReleaseRestraint(selPawn, target, out string reason))
            {
                options.Add(new FloatMenuOption(
                    "MUGB_ReleaseRestraintOption".Translate(target.LabelShortCap),
                    delegate
                    {
                        Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_ReleaseRestraint, target);
                        selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }));
            }
            else if (!reason.NullOrEmpty())
            {
                options.Add(new FloatMenuOption(
                    "MUGB_CannotReleaseRestraint".Translate(target.LabelShortCap, reason),
                    null));
            }

            __result = options;
        }
    }
}
