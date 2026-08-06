using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    /// <summary>
    /// 고블린 기지 포로에게 걸린 MUGB_Restrained(구속) 헤디프를 플레이어가 풀어줄 수 있게 합니다.
    ///
    /// 배경:
    /// - 고블린 기지는 BF 가구모드가 없을 때 포로를 말뚝에 묶은 것처럼 보이게 하려고
    ///   MUGB_Restrained를 부여합니다. 이 헤디프는 이동과 조작을 0으로 막습니다.
    /// - 이 헤디프는 순수 데이터라 스스로 풀리지 않습니다. 원래는 포로의 소속이 고블린에서
    ///   벗어날 때만(MUGB_TribalBasePrisonManager) 자동 제거됐는데, 그 전까지는 플레이어가
    ///   직접 풀 방법이 없었습니다. 그래서 우클릭 명령을 추가합니다.
    ///
    /// 동작:
    /// - 묶인 포로에게 우클릭하면 "구속 풀기"가 뜨고, 지정한 폰이 다가가 잠깐 작업한 뒤
    ///   헤디프를 제거합니다. 바닐라 "포로 잡기"와 같은 흐름이라, 풀어준 뒤에는 평소처럼
    ///   포로로 데려가거나 석방할 수 있습니다.
    /// </summary>
    public static class GoblinRestraintUtility
    {
        public const int ReleaseInteractionTicks = 120;

        public static HediffDef RestrainedDef => DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_Restrained");

        public static bool IsRestrained(Pawn pawn)
        {
            HediffDef def = RestrainedDef;
            return def != null && pawn?.health?.hediffSet?.HasHediff(def) == true;
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
            HediffDef def = RestrainedDef;
            if (def == null || target?.health == null)
            {
                return;
            }

            foreach (Hediff hediff in target.health.hediffSet.hediffs.Where(h => h.def == def).ToList())
            {
                target.health.RemoveHediff(hediff);
            }

            // 묶인 채로 잡혀 있던 눕기 작업이 남아 있으면 끊어, 포로가 스스로 일어나게 합니다.
            if (target.CurJobDef == JobDefOf.LayDown && target.jobs != null)
            {
                target.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
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
