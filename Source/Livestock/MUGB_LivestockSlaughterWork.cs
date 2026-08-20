using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Livestock
{
    // 한국어 의도: 지정된 인간가축을 그 자리에서 처분합니다. 시체 해체는 도축 계획서가
    // 이어받고, 시체 운반은 바닐라 운반이 처리합니다.
    //
    // 바닐라 동물과 같은 분업이자 같은 동선입니다.
    //   동물:     조련(Handling)이 제자리 도살  →  운반  →  조리(Cooking)가 도축
    //   인간가축: 조련(Handling)이 제자리 처분  →  운반  →  조리(Cooking)가 도축
    //
    // ── 도축대까지 끌고 가는 안을 폐기한 이유 (기록) ─────────────────
    //
    // 살아있는 대상을 업고 도축 작업대까지 데려가는 설계였는데, 실제로 돌려보니 로그에
    // 이렇게 찍혔습니다.
    //
    //   Tried to add (대상) to ThingOwner but this thing is already in another container.
    //   owner=Pawn_CarryTracker, current container owner=Map-0-PlayerHome.
    //   Use TryAddOrTransfer, TryTransferToContainer, or remove the item before adding it.
    //     Verse.Pawn_CarryTracker:TryStartCarry (Verse.Thing)
    //
    // 즉 Pawn_CarryTracker.TryStartCarry(Thing)는 맵에 스폰된 물체를 맵에서 빼지 않은 채
    // 담으려다 실패합니다. 실패가 잡 종료로 이어지고 워크기버가 같은 잡을 즉시 재발급해
    // "started 10 jobs in one tick" 루프가 났습니다.
    //
    // 유저 결정으로 끌고 가기는 폐기했습니다. 다시 시도한다면 count 오버로드
    // TryStartCarry(Thing, int)를 쓰는 것이 출발점입니다. 바닐라 운반 토일이 그쪽을 쓰고,
    // 경고문이 지목한 transfer 계열도 그 안에 있습니다. 다만 검증 전에는 넣지 않습니다.
    public class WorkGiver_TakeLivestockToButcher : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForUndefined();

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        // 입구가 둘이므로 후보도 둘에서 모읍니다(설계지침 2장).
        //   1) 수동 지정 — Designation 목록. 맵 전체 폰 스캔이 아니라 비용이 작습니다.
        //   2) '고기용 가축' 처우 — 죄수 목록만 훑습니다. 죄수는 보통 한 자릿수입니다.
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map == null)
            {
                yield break;
            }

            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(MUGB_LivestockDefOf.MUGB_SlaughterHumanlike))
            {
                Thing thing = designation?.target.Thing;
                if (thing != null)
                {
                    yield return thing;
                }
            }

            if (MUGB_LivestockDefOf.MUGB_MeatLivestock == null)
            {
                yield break;
            }

            List<Pawn> prisoners = map.mapPawns.PrisonersOfColonySpawned;
            for (int i = 0; i < prisoners.Count; i++)
            {
                Pawn prisoner = prisoners[i];
                // 수동 지정까지 된 죄수는 위에서 이미 나왔으므로 중복을 피합니다.
                if (MUGB_LivestockUtility.HasMeatLivestockMode(prisoner)
                    && !MUGB_LivestockUtility.IsDesignated(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        // 싼 검사부터 봅니다. 대상이 하나도 없는 평상시에는 여기서 바로 빠지므로
        // 규율 순회조차 돌지 않습니다.
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map map = pawn?.Map;
            if (map == null)
            {
                return true;
            }

            if (!map.designationManager.AnySpawnedDesignationOfDef(MUGB_LivestockDefOf.MUGB_SlaughterHumanlike)
                && !AnyMeatLivestockPrisoner(map))
            {
                return true;
            }

            return !MUGB_LivestockUtility.PreceptAllowsButchering();
        }

        private static bool AnyMeatLivestockPrisoner(Map map)
        {
            if (MUGB_LivestockDefOf.MUGB_MeatLivestock == null)
            {
                return false;
            }

            List<Pawn> prisoners = map.mapPawns.PrisonersOfColonySpawned;
            for (int i = 0; i < prisoners.Count; i++)
            {
                if (MUGB_LivestockUtility.HasMeatLivestockMode(prisoners[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn victim) || victim == pawn)
            {
                return null;
            }

            // 설계지침 5.4: 조건을 잃은 대상은 여기서 정리합니다. 안 그러면 유령 지정이 남아
            // 워크기버가 매번 헛돕니다.
            if (!MUGB_LivestockUtility.CanEverDesignate(victim))
            {
                MUGB_LivestockUtility.CleanupDesignationIfInvalid(victim);
                return null;
            }

            if (!MUGB_LivestockUtility.IsMarkedForSlaughter(victim) || victim.InAggroMentalState)
            {
                return null;
            }

            // 예약뿐 아니라 '도달 가능한가'까지 봅니다. 이걸 빼면 못 가는 대상에게 잡이
            // 계속 발급되어 이동 토일이 즉시 실패하는 루프가 납니다.
            if (!pawn.CanReserveAndReach(victim, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, forced))
            {
                return null;
            }

            // 시체를 받아줄 계획서가 어디에도 없으면 처분하지 않습니다. 죽여봐야 그 자리에서
            // 썩기만 하므로, 플레이어가 계획서를 켤 때까지 기다리는 편이 낫습니다.
            if (!MUGB_LivestockUtility.AnyStationAcceptsCorpseOf(victim))
            {
                JobFailReason.Is("MUGB_NoUsableButcherStation".Translate());
                return null;
            }

            return JobMaker.MakeJob(MUGB_LivestockDefOf.MUGB_TakeLivestockToButcher, victim);
        }
    }

    public class JobDriver_TakeLivestockToButcher : JobDriver
    {
        private const int SlaughterDurationTicks = 240;

        private Pawn Victim => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnAggroMentalState(TargetIndex.A);
            this.FailOn(() =>
            {
                Pawn victim = Victim;
                return victim == null || victim.Dead || !MUGB_LivestockUtility.IsMarkedForSlaughter(victim);
            });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            // 대상을 세웁니다. 이게 없으면 처형당하는 놈이 태연히 돌아다니다가 죽습니다.
            // 노예 억압처럼 멈춰서 마주 보게 만듭니다.
            //
            // ThinkTree를 건드리는 것이 아니라 잡을 직접 하나 물리는 것입니다. 만료 시간을
            // 처형 시간보다 조금 길게 잡아, 중간에 잡이 깨져도 대상이 영영 멈춰 있지 않습니다.
            Toil hold = ToilMaker.MakeToil("MUGB_HoldLivestockStill");
            hold.initAction = delegate
            {
                Pawn victim = Victim;
                if (victim == null || victim.Dead || !victim.Spawned || victim.jobs == null)
                {
                    return;
                }

                Job stand = JobMaker.MakeJob(JobDefOf.Wait);
                stand.expiryInterval = SlaughterDurationTicks + 120;
                victim.jobs.StartJob(stand, JobCondition.InterruptForced, null, false, true);
            };
            hold.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return hold;

            // 서로 마주 보게 합니다. Toils_General.Wait은 작업자 쪽만 돌려주므로 직접 짭니다.
            Toil wait = ToilMaker.MakeToil("MUGB_SlaughterWait");
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.defaultDuration = SlaughterDurationTicks;
            wait.handlingFacing = true;
            wait.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.A));

                Pawn victim = Victim;
                if (victim != null && victim.Spawned && !victim.Dead)
                {
                    victim.rotationTracker.FaceTarget(pawn);
                }
            };
            yield return wait.WithProgressBarToilDelay(TargetIndex.A);

            Toil slaughter = ToilMaker.MakeToil("MUGB_SlaughterLivestock");
            slaughter.initAction = delegate
            {
                Pawn victim = Victim;
                if (victim == null || victim.Dead)
                {
                    return;
                }

                // 지정은 여기서 지우지 않습니다. Pawn.Kill 패치가 죽는 시점에 정리하므로,
                // 만에 하나 처형이 대상을 죽이지 못하면 지정이 남아 재시도됩니다.
                ExecutionUtility.DoExecutionByCut(pawn, victim);
                TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, victim);
                MUGB_LivestockUtility.ApplySlaughterThoughts(pawn, victim);

                // 시체 금지 해제.
                //
                // 바닐라는 스폰되는 물건을 홈 구역 밖이면 자동으로 금지 처리합니다
                // (ForbidUtility.SetForbiddenIfOutsideHomeArea). 노예가 홈 구역 밖에서
                // 죽으면 시체가 '상호작용 금지' 상태로 남아 아무도 도축하러 오지 않습니다.
                //
                // 우리가 일부러 잡은 것이므로 금지는 의도가 아닙니다. 여기서 풀어줍니다.
                Corpse corpse = victim.Corpse;
                if (corpse != null && corpse.Spawned)
                {
                    corpse.SetForbidden(false, false);
                }
            };
            slaughter.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return slaughter;
        }
    }
}
