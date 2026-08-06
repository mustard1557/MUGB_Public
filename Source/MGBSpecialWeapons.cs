using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace MUGB
{
    public class CompProperties_LimitedShots : CompProperties
    {
        public int maxShots = 1;
        public string ammoLabel;

        public CompProperties_LimitedShots()
        {
            compClass = typeof(CompLimitedShots);
        }
    }

    public class CompLimitedShots : ThingComp
    {
        private int shotsRemaining = -1;

        public CompProperties_LimitedShots Props => (CompProperties_LimitedShots)props;

        public int ShotsRemaining
        {
            get
            {
                EnsureInitialized();
                return shotsRemaining;
            }
        }

        public int MaxShots => Mathf.Max(0, Props.maxShots);

        public override string CompInspectStringExtra()
        {
            return $"{AmmoLabel}: {ShotsRemaining}/{Props.maxShots}";
        }

        public string AmmoLabel => string.IsNullOrEmpty(Props.ammoLabel)
            ? "MUGB_LimitedWeaponAmmo".Translate().ToString()
            : GoblinSpecialWeaponText.Resolve(Props.ammoLabel);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref shotsRemaining, "mugbShotsRemaining", -1);
        }

        public bool TryConsumeShot()
        {
            EnsureInitialized();
            if (shotsRemaining <= 0)
            {
                return false;
            }

            shotsRemaining--;
            if (shotsRemaining <= 0 && parent != null && !parent.Destroyed)
            {
                parent.Destroy();
            }
            return true;
        }

        private void EnsureInitialized()
        {
            if (shotsRemaining < 0)
            {
                shotsRemaining = Mathf.Max(0, Props.maxShots);
            }
        }
    }

    public class CompProperties_ThrownSpearAbility : CompProperties
    {
        public ThingDef projectileDef;
        public int cooldownTicks = 10800;
        public int warmupTicks = 90;
        public float range = 16f;
        public float baseHitChance = 0.6f;
        public float missRadius = 2f;
        public SoundDef soundCast;
        public string iconPath;
        public string label = "MUGB_ThrowSpearLabel";
        public string description = "MUGB_ThrowSpearDesc";

        public CompProperties_ThrownSpearAbility()
        {
            compClass = typeof(CompThrownSpearAbility);
        }
    }

    public class CompThrownSpearAbility : ThingComp
    {
        private int nextUseTick;

        public CompProperties_ThrownSpearAbility Props => (CompProperties_ThrownSpearAbility)props;

        public int TicksUntilReady => Mathf.Max(0, nextUseTick - Find.TickManager.TicksGame);

        public Texture2D CommandIcon
        {
            get
            {
                if (!Props.iconPath.NullOrEmpty())
                {
                    return ContentFinder<Texture2D>.Get(Props.iconPath, reportFailure: false) ?? parent.def.uiIcon;
                }
                return parent.def.uiIcon;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextUseTick, "mugbThrownSpearNextUseTick");
        }

        public bool CanUse(Pawn pawn, out string reason)
        {
            reason = null;
            if (pawn?.Spawned != true || pawn.Downed || pawn.Map == null)
            {
                reason = "MUGB_AbilityUnavailable".Translate();
                return false;
            }
            if (!pawn.Drafted)
            {
                reason = "MUGB_AbilityMustBeDrafted".Translate();
                return false;
            }
            if (Props.projectileDef == null)
            {
                reason = "MUGB_AbilityNoProjectile".Translate();
                return false;
            }
            int ticks = TicksUntilReady;
            if (ticks > 0)
            {
                reason = "MUGB_AbilityRecharging".Translate(ticks.ToStringTicksToPeriod());
                return false;
            }
            return true;
        }

        public void BeginTargeting(Pawn pawn)
        {
            if (!CanUse(pawn, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            TargetingParameters parameters = TargetingParameters.ForAttackAny();
            parameters.canTargetLocations = true;
            parameters.validator = target => IsValidTarget(pawn, target, out _);

            Find.Targeter.BeginTargeting(
                parameters,
                target => StartThrowJob(pawn, target),
                delegate
                {
                    if (pawn?.Spawned == true)
                    {
                        GenDraw.DrawRadiusRing(pawn.Position, Props.range);
                    }
                });
        }

        public void DebugRecharge()
        {
            nextUseTick = 0;
        }

        private bool IsValidTarget(Pawn pawn, TargetInfo target, out string reason)
        {
            reason = null;
            if (pawn?.Map == null || !target.IsValid || target.Cell == pawn.Position)
            {
                reason = "MUGB_AbilityInvalidTarget".Translate();
                return false;
            }
            if (!target.Cell.InBounds(pawn.Map))
            {
                reason = "MUGB_AbilityOutOfBounds".Translate();
                return false;
            }
            if (pawn.Position.DistanceTo(target.Cell) > Props.range)
            {
                reason = "MUGB_AbilityOutOfRange".Translate();
                return false;
            }
            if (!GenSight.LineOfSight(pawn.Position, target.Cell, pawn.Map))
            {
                reason = "MUGB_AbilityNoLineOfSight".Translate();
                return false;
            }
            return true;
        }

        private void StartThrowJob(Pawn pawn, LocalTargetInfo target)
        {
            if (!CanUse(pawn, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            TargetInfo targetInfo = new TargetInfo(target.Cell, pawn.Map);
            if (!IsValidTarget(pawn, targetInfo, out reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_ThrowSpear, target);
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool TryThrowNow(Pawn pawn, LocalTargetInfo target)
        {
            if (!CanUse(pawn, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            TargetInfo targetInfo = new TargetInfo(target.Cell, pawn.Map);
            if (!IsValidTarget(pawn, targetInfo, out reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            LocalTargetInfo shotTarget = AdjustedShotTarget(pawn, target, out bool intendedHit);
            Projectile projectile = (Projectile)GenSpawn.Spawn(Props.projectileDef, pawn.Position, pawn.Map);
            ProjectileHitFlags hitFlags = intendedHit
                ? ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetPawns
                : ProjectileHitFlags.NonTargetPawns;
            Props.soundCast?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            projectile.Launch(pawn, pawn.DrawPos, shotTarget, target, hitFlags, preventFriendlyFire: true, parent);
            nextUseTick = Find.TickManager.TicksGame + Mathf.Max(1, Props.cooldownTicks);
            return true;
        }

        private LocalTargetInfo AdjustedShotTarget(Pawn pawn, LocalTargetInfo target, out bool intendedHit)
        {
            intendedHit = Rand.Chance(AdjustedHitChance(pawn));
            if (intendedHit || pawn?.Map == null)
            {
                return target;
            }

            IntVec3 origin = target.Cell;
            int radius = Mathf.Max(1, Mathf.CeilToInt(Props.missRadius));
            for (int i = 0; i < 12; i++)
            {
                IntVec3 cell = origin + new IntVec3(Rand.RangeInclusive(-radius, radius), 0, Rand.RangeInclusive(-radius, radius));
                if (cell != origin && cell.InBounds(pawn.Map) && cell.DistanceTo(origin) <= Props.missRadius)
                {
                    return new LocalTargetInfo(cell);
                }
            }

            return target;
        }

        private float AdjustedHitChance(Pawn pawn)
        {
            float manipulation = pawn?.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 1f;
            return Mathf.Clamp01(Props.baseHitChance * manipulation);
        }
    }

    internal static class GoblinSpecialWeaponText
    {
        public static string Resolve(string text)
        {
            if (text.NullOrEmpty())
            {
                return string.Empty;
            }

            return text.CanTranslate() ? text.Translate().ToString() : text;
        }
    }

    public class CompProperties_SpearChargeAbility : CompProperties
    {
        public int cooldownTicks = 2700;
        public float range = 4f;
        public float chargeCellsPerSecond = 2.34f;
        public float damageAmount = 35f;
        public float armorPenetration = 0.70f;
        public int targetStunTicks = 30;
        public int selfStunTicks = 45;
        public SoundDef soundStart;
        public SoundDef soundImpact;
        public string iconPath;
        public string label = "MUGB_SpearChargeLabel";
        public string description = "MUGB_SpearChargeDesc";

        public CompProperties_SpearChargeAbility()
        {
            compClass = typeof(CompSpearChargeAbility);
        }
    }

    public class CompSpearChargeAbility : ThingComp
    {
        private int nextUseTick;

        public CompProperties_SpearChargeAbility Props => (CompProperties_SpearChargeAbility)props;

        public int TicksUntilReady => Mathf.Max(0, nextUseTick - Find.TickManager.TicksGame);

        public Texture2D CommandIcon
        {
            get
            {
                if (!Props.iconPath.NullOrEmpty())
                {
                    return ContentFinder<Texture2D>.Get(Props.iconPath, reportFailure: false) ?? parent.def.uiIcon;
                }
                return parent.def.uiIcon;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextUseTick, "mugbSpearChargeNextUseTick");
        }

        public bool CanUse(Pawn pawn, out string reason)
        {
            reason = null;
            if (pawn?.Spawned != true || pawn.Downed || pawn.Map == null)
            {
                reason = "MUGB_AbilityUnavailable".Translate();
                return false;
            }
            if (!pawn.Drafted && pawn.Faction == Faction.OfPlayer)
            {
                reason = "MUGB_AbilityMustBeDrafted".Translate();
                return false;
            }
            int ticks = TicksUntilReady;
            if (ticks > 0)
            {
                reason = "MUGB_AbilityRecharging".Translate(ticks.ToStringTicksToPeriod());
                return false;
            }
            return true;
        }

        public void BeginTargeting(Pawn pawn)
        {
            if (!CanUse(pawn, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            TargetingParameters parameters = TargetingParameters.ForAttackAny();
            parameters.canTargetLocations = true;
            parameters.validator = target => IsValidOrderTarget(pawn, target, out _);

            Find.Targeter.BeginTargeting(
                parameters,
                target => StartChargeJob(pawn, target),
                delegate
                {
                    if (pawn?.Spawned == true)
                    {
                        GenDraw.DrawRadiusRing(pawn.Position, Props.range);
                    }
                });
        }

        public void DebugRecharge()
        {
            nextUseTick = 0;
        }

        public bool TryAICast(Pawn pawn, Pawn target)
        {
            if (!CanUse(pawn, out _) || target?.Spawned != true || target.Dead || target.Downed)
            {
                return false;
            }
            if (pawn.Position.DistanceTo(target.Position) < 1.8f || pawn.Position.DistanceTo(target.Position) > Props.range)
            {
                return false;
            }
            if (!IsValidTarget(pawn, new TargetInfo(target.Position, pawn.Map), out _))
            {
                return false;
            }
            if (CrowdedByAllies(pawn, target.Position))
            {
                return false;
            }

            StartChargeJob(pawn, new LocalTargetInfo(target.Position));
            return true;
        }

        public void NotifyUsed()
        {
            nextUseTick = Find.TickManager.TicksGame + Mathf.Max(1, Props.cooldownTicks);
        }

        private bool IsValidTarget(Pawn pawn, TargetInfo target, out string reason)
        {
            reason = null;
            if (pawn?.Map == null || !target.IsValid || target.Cell == pawn.Position)
            {
                reason = "MUGB_AbilityInvalidTarget".Translate();
                return false;
            }
            if (!target.Cell.InBounds(pawn.Map))
            {
                reason = "MUGB_AbilityOutOfBounds".Translate();
                return false;
            }
            if (pawn.Position.DistanceTo(target.Cell) > Props.range)
            {
                reason = "MUGB_AbilityOutOfRange".Translate();
                return false;
            }
            if (!GenSight.LineOfSight(pawn.Position, target.Cell, pawn.Map))
            {
                reason = "MUGB_AbilityNoLineOfSight".Translate();
                return false;
            }
            return true;
        }

        private bool IsValidOrderTarget(Pawn pawn, TargetInfo target, out string reason)
        {
            reason = null;
            if (pawn?.Map == null || !target.IsValid || target.Cell == pawn.Position)
            {
                reason = "MUGB_AbilityInvalidTarget".Translate();
                return false;
            }
            if (!target.Cell.InBounds(pawn.Map))
            {
                reason = "MUGB_AbilityOutOfBounds".Translate();
                return false;
            }
            if (IsValidTarget(pawn, target, out _))
            {
                return true;
            }
            if (GoblinSpearChargeUtility.TryFindChargeStartCell(pawn, target.Cell, Props.range, out _))
            {
                return true;
            }
            reason = "MUGB_AbilityOutOfRange".Translate();
            return false;
        }

        private void StartChargeJob(Pawn pawn, LocalTargetInfo target)
        {
            if (!CanUse(pawn, out string reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            TargetInfo targetInfo = new TargetInfo(target.Cell, pawn.Map);
            if (!IsValidOrderTarget(pawn, targetInfo, out reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_SpearCharge, target.Cell);
            job.playerForced = pawn.Faction == Faction.OfPlayer;
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private bool CrowdedByAllies(Pawn pawn, IntVec3 targetCell)
        {
            int allies = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(targetCell, 1.5f, true))
            {
                if (!cell.InBounds(pawn.Map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(pawn.Map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn other && other != pawn && other.Faction == pawn.Faction && !other.Dead && !other.Downed)
                    {
                        allies++;
                        if (allies >= 3)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }

    public static class GoblinSpearChargeUtility
    {
        public static bool TryFindChargeStartCell(Pawn pawn, IntVec3 targetCell, float range, out IntVec3 startCell)
        {
            startCell = IntVec3.Invalid;
            if (pawn?.Map == null || !targetCell.InBounds(pawn.Map))
            {
                return false;
            }

            if (pawn.Position.DistanceTo(targetCell) <= range && GenSight.LineOfSight(pawn.Position, targetCell, pawn.Map))
            {
                startCell = pawn.Position;
                return true;
            }

            List<IntVec3> candidates = GenRadial.RadialCellsAround(targetCell, range, true)
                .Where(cell => cell.InBounds(pawn.Map)
                    && cell != targetCell
                    && cell.Standable(pawn.Map)
                    && GenSight.LineOfSight(cell, targetCell, pawn.Map)
                    && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                .ToList();

            if (candidates.Count <= 0)
            {
                return false;
            }

            startCell = candidates[0];
            return true;
        }
    }

    public class JobDriver_ThrowGoblinSpear : JobDriver
    {
        private const TargetIndex TargetInd = TargetIndex.A;

        private CompThrownSpearAbility SpearComp => pawn?.equipment?.Primary?.TryGetComp<CompThrownSpearAbility>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => SpearComp == null || !SpearComp.CanUse(pawn, out _));
            this.FailOn(() => !job.GetTarget(TargetInd).IsValid);

            Toil warmup = ToilMaker.MakeToil("WarmupGoblinSpearThrow");
            warmup.initAction = delegate
            {
                LocalTargetInfo target = job.GetTarget(TargetInd);
                int warmupTicks = Mathf.Max(1, SpearComp?.Props?.warmupTicks ?? 90);
                pawn.rotationTracker.FaceTarget(target);
                pawn.stances?.SetStance(new Stance_Warmup(warmupTicks, target, pawn.equipment?.PrimaryEq?.PrimaryVerb));
            };
            warmup.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(TargetInd));
            };
            warmup.defaultCompleteMode = ToilCompleteMode.Delay;
            warmup.defaultDuration = Mathf.Max(1, SpearComp?.Props?.warmupTicks ?? 90);
            yield return warmup;

            Toil throwSpear = ToilMaker.MakeToil("ThrowGoblinSpear");
            throwSpear.initAction = delegate
            {
                SpearComp?.TryThrowNow(pawn, job.GetTarget(TargetInd));
            };
            throwSpear.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return throwSpear;
        }
    }

    public class JobDriver_GoblinSpearCharge : JobDriver
    {
        private const TargetIndex TargetInd = TargetIndex.A;
        private bool struck;
        private IntVec3 lastStepDelta;
        private readonly List<IntVec3> chargeCells = new List<IntVec3>();
        private int nextChargeCellIndex;
        private int nextChargeStepTick;

        private CompSpearChargeAbility ChargeComp => pawn?.equipment?.Primary?.TryGetComp<CompSpearChargeAbility>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => ChargeComp == null || !ChargeComp.CanUse(pawn, out _));
            this.FailOn(() => !job.GetTarget(TargetInd).IsValid);

            Toil approach = ToilMaker.MakeToil("ApproachGoblinSpearChargeRange");
            approach.initAction = delegate
            {
                IntVec3 targetCell = job.GetTarget(TargetInd).Cell;
                if (!GoblinSpearChargeUtility.TryFindChargeStartCell(pawn, targetCell, ChargeComp.Props.range, out IntVec3 startCell))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (startCell == pawn.Position)
                {
                    ReadyForNextToil();
                    return;
                }

                pawn.pather.StartPath(startCell, PathEndMode.OnCell);
            };
            approach.tickAction = delegate
            {
                IntVec3 targetCell = job.GetTarget(TargetInd).Cell;
                if (pawn.Position.DistanceTo(targetCell) <= ChargeComp.Props.range && GenSight.LineOfSight(pawn.Position, targetCell, pawn.Map))
                {
                    pawn.pather.StopDead();
                    ReadyForNextToil();
                    return;
                }

                if (!pawn.pather.Moving)
                {
                    ReadyForNextToil();
                }
            };
            approach.defaultCompleteMode = ToilCompleteMode.Never;
            yield return approach;

            Toil charge = ToilMaker.MakeToil("GoblinSpearCharge");
            charge.initAction = delegate
            {
                struck = false;
                IntVec3 targetCell = job.GetTarget(TargetInd).Cell;
                chargeCells.Clear();
                foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(pawn.Position, targetCell))
                {
                    if (cell != pawn.Position)
                    {
                        chargeCells.Add(cell);
                    }
                }

                nextChargeCellIndex = 0;
                nextChargeStepTick = Find.TickManager.TicksGame;
                lastStepDelta = chargeCells.Count > 0 ? chargeCells[0] - pawn.Position : StepDeltaToward(pawn.Position, targetCell);
                ChargeComp?.Props?.soundStart?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                pawn.pather.StopDead();
                if (lastStepDelta != IntVec3.Zero)
                {
                    pawn.rotationTracker.FaceCell(pawn.Position + lastStepDelta);
                }
            };
            charge.tickAction = delegate
            {
                if (pawn.Map == null || struck)
                {
                    ReadyForNextToil();
                    return;
                }

                if (Find.TickManager.TicksGame < nextChargeStepTick)
                {
                    return;
                }

                if (nextChargeCellIndex >= chargeCells.Count)
                {
                    TryStrikeReachAhead();
                    ReadyForNextToil();
                    return;
                }

                IntVec3 nextCell = chargeCells[nextChargeCellIndex];
                IntVec3 stepDelta = nextCell - pawn.Position;
                if (stepDelta == IntVec3.Zero)
                {
                    nextChargeCellIndex++;
                    return;
                }

                lastStepDelta = stepDelta;
                pawn.rotationTracker.FaceCell(nextCell);

                // 한국어 의도: 돌진 중에는 길찾기를 다시 하지 않는다. 벽/가구에 막히면 피해 가지 않고 그 자리에서 멈춘다.
                if (!nextCell.InBounds(pawn.Map) || !nextCell.WalkableBy(pawn.Map, pawn))
                {
                    ReadyForNextToil();
                    return;
                }

                Pawn hitAhead = FirstChargeImpactPawnAt(nextCell);
                if (hitAhead != null)
                {
                    Strike(hitAhead);
                    struck = true;
                    ReadyForNextToil();
                    return;
                }

                pawn.Position = nextCell;
                FleckMaker.ThrowDustPuff(pawn.DrawPos - stepDelta.ToVector3() * 0.16f, pawn.Map, 0.45f);
                nextChargeCellIndex++;
                nextChargeStepTick = Find.TickManager.TicksGame + ChargeTicksPerCell();
            };
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            yield return charge;

            Toil finish = ToilMaker.MakeToil("FinishGoblinSpearCharge");
            finish.initAction = delegate
            {
                ChargeComp?.NotifyUsed();
                int selfStunTicks = Mathf.Max(1, ChargeComp?.Props?.selfStunTicks ?? 45);
                pawn.stances?.stunner?.StunFor(selfStunTicks, pawn, addBattleLog: false, showMote: true);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }

        private int ChargeTicksPerCell()
        {
            float cellsPerSecond = Mathf.Max(1f, ChargeComp?.Props?.chargeCellsPerSecond ?? 1f);
            return Mathf.Clamp(Mathf.RoundToInt(60f / cellsPerSecond), 3, 20);
        }

        private IntVec3 StepDeltaToward(IntVec3 from, IntVec3 to)
        {
            int dx = Mathf.Clamp(to.x - from.x, -1, 1);
            int dz = Mathf.Clamp(to.z - from.z, -1, 1);
            if (dx == 0 && dz == 0)
            {
                return IntVec3.Zero;
            }
            return new IntVec3(dx, 0, dz);
        }

        private Pawn FirstChargeImpactPawnAt(IntVec3 cell)
        {
            if (!cell.InBounds(pawn.Map))
            {
                return null;
            }

            List<Thing> things = cell.GetThingList(pawn.Map);
            for (int i = 0; i < things.Count; i++)
            {
                // Korean source intent: 창돌진은 방향을 잡고 밀어붙이는 공격이라 적/아군/다운 여부와 무관하게
                // 경로상 첫 생체 폰에게 피해를 준다. 시전자 본인과 사망한 폰만 제외한다.
                if (things[i] is Pawn other
                    && other != pawn
                    && other.RaceProps?.IsFlesh == true
                    && !other.Dead)
                {
                    return other;
                }
            }
            return null;
        }

        private bool TryStrikeReachAhead()
        {
            if (lastStepDelta == IntVec3.Zero)
            {
                lastStepDelta = StepDeltaToward(pawn.Position, job.GetTarget(TargetInd).Cell);
            }
            if (lastStepDelta == IntVec3.Zero)
            {
                return false;
            }

            Pawn hitPawn = FirstChargeImpactPawnAt(pawn.Position + lastStepDelta);
            if (hitPawn == null)
            {
                return false;
            }

            Strike(hitPawn);
            struck = true;
            return true;
        }

        private void Strike(Pawn target)
        {
            CompProperties_SpearChargeAbility props = ChargeComp?.Props;
            props?.soundImpact?.PlayOneShot(new TargetInfo(target.Position, target.Map));
            DamageInfo dinfo = new DamageInfo(DamageDefOf.Stab, props?.damageAmount ?? 20f, props?.armorPenetration ?? 0.6f, -1f, pawn, null, pawn.equipment?.Primary?.def);
            target.TakeDamage(dinfo);
            if (!target.Dead)
            {
                TryKnockBack(target, lastStepDelta);
            }
            int stunTicks = Mathf.Max(0, props?.targetStunTicks ?? 30);
            if (stunTicks > 0 && !target.Dead)
            {
                target.stances?.stunner?.StunFor(stunTicks, pawn, addBattleLog: false, showMote: true);
            }
        }

        private void TryKnockBack(Pawn target, IntVec3 direction)
        {
            if (direction == IntVec3.Zero || target?.Map != pawn.Map)
            {
                return;
            }

            int distance = Rand.RangeInclusive(1, 2);
            for (int i = 0; i < distance; i++)
            {
                IntVec3 destination = target.Position + direction;
                if (!destination.InBounds(target.Map) || !destination.WalkableBy(target.Map, target) || PawnAt(destination, target.Map, target) != null)
                {
                    break;
                }

                target.pather.StopDead();
                target.Position = destination;
            }
        }

        private static Pawn PawnAt(IntVec3 cell, Map map, Pawn ignoredPawn)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && other != ignoredPawn && !other.Dead)
                {
                    return other;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TickRare))]
    public static class Pawn_TickRare_GoblinSpearChargeAIPatch
    {
        public static void Postfix(Pawn __instance)
        {
            Pawn pawn = __instance;
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.Faction == Faction.OfPlayer)
            {
                return;
            }

            CompSpearChargeAbility charge = pawn.equipment?.Primary?.TryGetComp<CompSpearChargeAbility>();
            if (charge == null || !charge.CanUse(pawn, out _))
            {
                return;
            }

            Job job = pawn.CurJob;
            if (job == null || job.def != JobDefOf.AttackMelee || !job.targetA.HasThing || !(job.targetA.Thing is Pawn target))
            {
                return;
            }

            // Conservative AI: only occasional charges while already trying to melee the same enemy.
            if (!Rand.Chance(0.18f))
            {
                return;
            }

            charge.TryAICast(pawn, target);
        }
    }

    public class Verb_LimitedShoot : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            CompLimitedShots limited = EquipmentSource?.TryGetComp<CompLimitedShots>();
            if (limited != null && limited.ShotsRemaining <= 0)
            {
                return false;
            }

            bool result = base.TryCastShot();
            if (result)
            {
                limited?.TryConsumeShot();
            }
            return result;
        }
    }

    public class Projectile_GoblinDart : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Pawn pawn = hitThing as Pawn;
            base.Impact(hitThing, blockedByShield);
            if (pawn != null && !pawn.Dead && pawn.RaceProps?.IsMechanoid != true)
            {
                AddStackedHediff(pawn, MUGBDefOf.MUGB_GoblinDartPoison, 1f, 5f);
            }
        }

        internal static void AddStackedHediff(Pawn pawn, HediffDef def, float amount, float maxSeverity)
        {
            if (pawn?.health == null || def == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(def, pawn);
                hediff.Severity = Mathf.Min(amount, maxSeverity);
                pawn.health.AddHediff(hediff);
            }
            else
            {
                hediff.Severity = Mathf.Min(maxSeverity, hediff.Severity + amount);
            }
        }
    }

    public class Projectile_ChainSnare : Bullet
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                DefDatabase<SoundDef>.GetNamedSilentFail("MUGB_GoblinChainSnareProjectile")?.PlayOneShot(new TargetInfo(Position, map));
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float ticksElapsed = Mathf.Max(0f, StartingTicksToImpact - ticksToImpact);
            float degreesPerTick = def?.projectile != null ? def.projectile.speed * 6f / 8f : 18.75f;
            float spin = ticksElapsed * degreesPerTick;
            Quaternion rotation = ExactRotation * Quaternion.AngleAxis(spin, Vector3.up);
            Graphics.DrawMesh(ProjectileDrawMeshUtility.MeshFor(def), drawLoc, rotation, DrawMat, 0);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Pawn pawn = hitThing as Pawn;
            base.Impact(hitThing, blockedByShield);
            if (pawn != null && !pawn.Dead)
            {
                Projectile_GoblinDart.AddStackedHediff(pawn, MUGBDefOf.MUGB_ChainSnare, 1f, 4f);
            }
        }
    }

    public class Projectile_GoblinHandcannonBomb : Projectile_Explosive
    {
        protected override void Explode()
        {
            float originalRadius = def.projectile.explosionRadius;
            CompMUGBSpecialWeapon special = equipment?.TryGetComp<CompMUGBSpecialWeapon>();
            if (special?.Has(MUGBSpecialWeaponOptionDatabase.OverchargedShot) == true)
            {
                def.projectile.explosionRadius = originalRadius + 1f;
            }

            try
            {
                base.Explode();
            }
            finally
            {
                def.projectile.explosionRadius = originalRadius;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float ticksElapsed = Mathf.Max(0f, StartingTicksToImpact - ticksToImpact);
            float degreesPerTick = def?.projectile != null ? def.projectile.speed * 6f / 18f : 8f;
            float spin = ticksElapsed * degreesPerTick;
            Quaternion rotation = ExactRotation * Quaternion.AngleAxis(spin, Vector3.up);
            Graphics.DrawMesh(ProjectileDrawMeshUtility.MeshFor(def), drawLoc, rotation, DrawMat, 0);
        }
    }

    internal static class ProjectileDrawMeshUtility
    {
        public static Mesh MeshFor(ThingDef def)
        {
            Vector2 drawSize = def?.graphicData?.drawSize ?? Vector2.one;
            return MeshPool.GridPlane(drawSize);
        }
    }

    public class Verb_GoblinSmokeShoot : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            bool result = base.TryCastShot();
            if (result)
            {
                GoblinMuzzleSmokeUtility.ThrowSmoke(CasterPawn, currentTarget);
            }
            return result;
        }
    }

    public class Verb_GoblinSmokeLaunchProjectile : Verb_LaunchProjectile
    {
        protected override bool TryCastShot()
        {
            bool result = base.TryCastShot();
            if (result)
            {
                GoblinMuzzleSmokeUtility.ThrowSmoke(CasterPawn, currentTarget);
            }
            return result;
        }
    }

    public static class GoblinMuzzleSmokeUtility
    {
        private const float MuzzleTextureForwardAngle = 45f;
        private const float GunAimingForwardOffset = 0.3f;
        private const float GunAimingLeftOffset = 0.035f;
        private const float MuzzleDistanceFromPawn = 1.1f;
        private const float MuzzleFlashScaleMultiplier = 1.6f;
        private const float GunVisualAngleOffset = -5f;

        public static void ThrowSmoke(Pawn caster, LocalTargetInfo target)
        {
            if (caster?.Map == null)
            {
                return;
            }

            Map map = caster.Map;
            if (!GenView.ShouldSpawnMotesAt(caster.DrawPos, map) || map.moteCounter.SaturatedLowPriority)
            {
                return;
            }

            Vector3 origin = caster.DrawPos;
            Vector3 direction = target.CenterVector3 - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = caster.Rotation.FacingCell.ToVector3();
            }
            direction.Normalize();

            float aimAngle = direction.AngleFlat();
            float visualAimAngle = VisualAimAngleFor(caster?.equipment?.Primary?.def, aimAngle);
            Vector3 visualDirection = Quaternion.AngleAxis(visualAimAngle, Vector3.up) * Vector3.forward;
            Vector3 weaponDrawOffset = new Vector3(0f, 0f, GunAimingForwardOffset).RotatedBy(visualAimAngle)
                + new Vector3(-GunAimingLeftOffset, 0f, 0f).RotatedBy(visualAimAngle);
            Vector3 muzzle = origin + weaponDrawOffset + visualDirection * MuzzleDistanceFromPawn;
            muzzle.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            ThrowMuzzleFlash(caster, muzzle, visualDirection, visualAimAngle, map);
            ThrowVanillaSmoke(muzzle, visualDirection, map);
        }

        private static void ThrowMuzzleFlash(Pawn caster, Vector3 muzzle, Vector3 direction, float aimAngle, Map map)
        {
            if (MUGBDefOf.MUGB_FlintlockMuzzleFlash == null)
            {
                return;
            }

            MoteThrown flash = (MoteThrown)ThingMaker.MakeThing(MUGBDefOf.MUGB_FlintlockMuzzleFlash);
            flash.Scale = MuzzleFlashScaleFor(caster?.equipment?.Primary?.def);
            flash.rotationRate = 0f;
            flash.exactRotation = aimAngle - MuzzleTextureForwardAngle;
            flash.exactPosition = muzzle + direction * 0.05f;
            flash.SetVelocity(direction.AngleFlat(), 0.02f);
            GenSpawn.Spawn(flash, muzzle.ToIntVec3(), map, WipeMode.Vanish);
        }

        private static void ThrowVanillaSmoke(Vector3 muzzle, Vector3 direction, Map map)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 smokePos = muzzle - direction * Rand.Range(0.02f, 0.12f)
                    + new Vector3(Rand.Range(-0.08f, 0.08f), 0f, Rand.Range(-0.08f, 0.08f));
                FleckMaker.ThrowSmoke(smokePos, map, Rand.Range(0.9f, 1.25f));
            }
        }

        private static float MuzzleFlashScaleFor(ThingDef weaponDef)
        {
            float scale;
            switch (weaponDef?.defName)
            {
                case "MUGB_GoblinMusket":
                    scale = 1.15f;
                    break;
                case "MUGB_GoblinHandgonne":
                    scale = 1.15f;
                    break;
                case "MUGB_GoblinHandcannon":
                    scale = 1.35f;
                    break;
                default:
                    scale = 1f;
                    break;
            }

            return scale * MuzzleFlashScaleMultiplier;
        }

        private static float VisualAimAngleFor(ThingDef weaponDef, float aimAngle)
        {
            return IsMugbGunWeapon(weaponDef) ? aimAngle + GunVisualAngleOffset : aimAngle;
        }

        private static bool IsMugbGunWeapon(ThingDef weaponDef)
        {
            switch (weaponDef?.defName)
            {
                case "MUGB_GoblinArquebus":
                case "MUGB_GoblinMusket":
                case "MUGB_GoblinHandgonne":
                case "MUGB_GoblinHandcannon":
                    return true;
                default:
                    return false;
            }
        }
    }

    public class Verb_BoomstickMelee : Verb_MeleeAttackDamage
    {
        protected override bool TryCastShot()
        {
            bool result = base.TryCastShot();
            if (result && CasterPawn != null && Rand.Chance(0.45f))
            {
                GoblinBoomstickUtility.TryArm(CasterPawn, EquipmentSource);
            }
            return result;
        }
    }

    public static class GoblinBoomstickUtility
    {
        public static bool TryArm(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn?.Map == null || pawn.Dead)
            {
                return false;
            }

            IntVec3 cell = pawn.Position;
            Map map = pawn.Map;
            Thing wick = ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinBoomstickWick);
            GenSpawn.Spawn(wick, cell, map);
            if (wick is GoblinBoomstickWick boomWick)
            {
                boomWick.instigator = pawn;
            }

            if (weapon != null && !weapon.Destroyed)
            {
                weapon.Destroy();
            }
            return true;
        }
    }

    public class GoblinBoomstickWick : ThingWithComps
    {
        public Pawn instigator;
        private int ticksLeft = 90;
        private FleckDef burningWick;
        private Sustainer hissSustainer;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref instigator, "instigator");
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 90);
        }

        protected override void Tick()
        {
            base.Tick();
            if (Map != null)
            {
                if (hissSustainer == null)
                {
                    hissSustainer = SoundDefOf.HissSmall.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                }
                else
                {
                    hissSustainer.Maintain();
                }
            }
            if (Map != null && ticksLeft % 8 == 0)
            {
                burningWick ??= DefDatabase<FleckDef>.GetNamedSilentFail("BurningWick");
                if (burningWick != null)
                {
                    FleckMaker.Static(DrawPos + new Vector3(0f, 0f, 0.15f), Map, burningWick, 1f);
                }
            }
            ticksLeft--;
            if (ticksLeft <= 0)
            {
                Detonate();
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (hissSustainer != null && !hissSustainer.Ended)
            {
                hissSustainer.End();
            }
            hissSustainer = null;
            base.Destroy(mode);
        }

        private void Detonate()
        {
            Map map = Map;
            IntVec3 pos = Position;
            Lord suicideLord = instigator?.GetLord();
            if (!Destroyed)
            {
                Destroy();
            }
            if (map == null)
            {
                return;
            }

            GenExplosion.DoExplosion(pos, map, 2.5f, DamageDefOf.Bomb, instigator, 90, 0.35f);
            for (int i = 0; i < 4; i++)
            {
                FleckMaker.ThrowSmoke(pos.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * 0.5f, map, Rand.Range(1.1f, 1.8f));
            }
            MUGB_SuicideSapperUtility.NotifyBoomstickDetonated(suicideLord);
        }
    }

    public class CompProperties_GoblinShamanStaff : CompProperties
    {
        public float radius = 6f;
        public int shieldCooldownTicks = 15000;
        public int shieldDurationTicks = 2400;
        public float shieldThreatRange = 18f;
        public int shieldNearbyAlliesRequired = 2;

        public CompProperties_GoblinShamanStaff()
        {
            compClass = typeof(CompGoblinShamanStaff);
        }
    }

    public class CompGoblinShamanStaff : ThingComp
    {
        private int lastShieldUsedTick = -9999999;
        private GoblinStaffMobileShield activeShield;

        public CompProperties_GoblinShamanStaff Props => (CompProperties_GoblinShamanStaff)props;

        public int ShieldTicksUntilReady => Mathf.Max(0, lastShieldUsedTick + Props.shieldCooldownTicks - Find.TickManager.TicksGame);
        public bool HasActiveShield => activeShield != null && !activeShield.Destroyed;
        public int ActiveShieldHitPoints
        {
            get
            {
                ValidateShieldReference();
                return activeShield?.CurrentHitPoints ?? 0;
            }
        }
        public int ActiveShieldMaxHitPoints
        {
            get
            {
                ValidateShieldReference();
                return activeShield?.ShieldMaxHitPoints ?? 0;
            }
        }
        public int ActiveShieldTicksRemaining
        {
            get
            {
                ValidateShieldReference();
                return activeShield?.TicksRemaining ?? 0;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastShieldUsedTick, "mugbStaffLastShieldUsedTick", -9999999);
            Scribe_References.Look(ref activeShield, "mugbStaffActiveShield");
        }

        public bool CanDeployShield(Pawn caster, bool requireDrafted, out string reason)
        {
            reason = null;
            ValidateShieldReference();
            if (caster?.Map == null || caster.Dead || caster.Downed)
            {
                reason = "MUGB_GoblinStaffShieldUnavailable".Translate();
                return false;
            }

            if (requireDrafted && !caster.Drafted)
            {
                reason = "MUGB_GoblinStaffShieldMustBeDrafted".Translate();
                return false;
            }

            if (parent != caster.equipment?.Primary)
            {
                reason = "MUGB_GoblinStaffShieldUnavailable".Translate();
                return false;
            }

            if (HasActiveShield)
            {
                reason = "MUGB_GoblinStaffShieldAlreadyActive".Translate();
                return false;
            }

            int ticks = ShieldTicksUntilReady;
            if (ticks > 0)
            {
                reason = "MUGB_GoblinStaffShieldRecharging".Translate(ticks.ToStringTicksToPeriod());
                return false;
            }

            if (MUGBDefOf.MUGB_GoblinStaffMobileShield == null)
            {
                reason = "MUGB_GoblinStaffShieldUnavailable".Translate();
                return false;
            }

            return true;
        }

        public bool TryDeployShield(Pawn caster, bool requireDrafted = true, bool playerFeedback = true)
        {
            if (!CanDeployShield(caster, requireDrafted, out string reason))
            {
                if (playerFeedback && !reason.NullOrEmpty())
                {
                    Messages.Message(reason, caster, MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }

            Thing shieldThing = ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinStaffMobileShield);
            if (!(shieldThing is GoblinStaffMobileShield shield))
            {
                return false;
            }

            GenSpawn.Spawn(shield, caster.Position, caster.Map, WipeMode.Vanish);
            shield.Initialize(caster, Props.shieldDurationTicks);
            activeShield = shield;
            lastShieldUsedTick = Find.TickManager.TicksGame;
            return true;
        }

        public bool ShouldAutoDeployShield(Pawn caster)
        {
            if (!CanDeployShield(caster, requireDrafted: false, out _))
            {
                return false;
            }

            if (caster.Faction == null || caster.Faction == Faction.OfPlayer)
            {
                return false;
            }

            IReadOnlyList<Pawn> pawns = caster.Map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return false;
            }

            int nearbyHostiles = 0;
            int nearbyAllies = 0;
            float allyRadius = Props.radius + 0.5f;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == null || other.Dead || other == caster)
                {
                    continue;
                }

                float distance = other.Position.DistanceTo(caster.Position);
                if (other.Faction == caster.Faction)
                {
                    if (distance <= allyRadius)
                    {
                        nearbyAllies++;
                    }
                    continue;
                }

                if (other.HostileTo(caster) && distance <= Props.shieldThreatRange)
                {
                    nearbyHostiles++;
                    if (nearbyHostiles >= 2)
                    {
                        break;
                    }
                }
            }

            if (nearbyHostiles <= 0)
            {
                return false;
            }

            if (nearbyAllies < Mathf.Max(1, Props.shieldNearbyAlliesRequired) && nearbyHostiles < 2)
            {
                return false;
            }

            if (NearbyFactionShieldExists(caster))
            {
                return false;
            }

            return true;
        }

        private bool NearbyFactionShieldExists(Pawn caster)
        {
            if (caster?.Map == null || MUGBDefOf.MUGB_GoblinStaffMobileShield == null)
            {
                return false;
            }

            List<Thing> shields = caster.Map.listerThings?.ThingsOfDef(MUGBDefOf.MUGB_GoblinStaffMobileShield);
            if (shields == null)
            {
                return false;
            }

            for (int i = 0; i < shields.Count; i++)
            {
                if (shields[i] is GoblinStaffMobileShield shield
                    && !shield.Destroyed
                    && shield.OwnerFaction == caster.Faction
                    && shield.Position.DistanceTo(caster.Position) <= Props.radius + 3f)
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateShieldReference()
        {
            if (activeShield != null && activeShield.Destroyed)
            {
                activeShield = null;
            }
        }

    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Pawn_GetGizmos_GoblinSpecialWeaponPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Faction != Faction.OfPlayer || __instance.equipment?.Primary == null)
            {
                return;
            }

            CompGoblinShamanStaff staff = __instance.equipment.Primary.TryGetComp<CompGoblinShamanStaff>();
            CompLimitedShots limitedShots = __instance.equipment.Primary.TryGetComp<CompLimitedShots>();
            CompThrownSpearAbility thrownSpear = __instance.equipment.Primary.TryGetComp<CompThrownSpearAbility>();
            CompSpearChargeAbility spearCharge = __instance.equipment.Primary.TryGetComp<CompSpearChargeAbility>();
            if (staff == null && limitedShots == null && thrownSpear == null && spearCharge == null)
            {
                return;
            }

            __result = AddSpecialWeaponGizmos(__result, __instance, staff, limitedShots, thrownSpear, spearCharge);
        }

        private static IEnumerable<Gizmo> AddSpecialWeaponGizmos(IEnumerable<Gizmo> gizmos, Pawn pawn, CompGoblinShamanStaff staff, CompLimitedShots limitedShots, CompThrownSpearAbility thrownSpear, CompSpearChargeAbility spearCharge)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }

            if (limitedShots != null)
            {
                Command_Action ammo = new Command_Action
                {
                    defaultLabel = $"{limitedShots.AmmoLabel}: {limitedShots.ShotsRemaining}/{limitedShots.MaxShots}",
                    defaultDesc = "MUGB_LimitedWeaponDesc".Translate(),
                    icon = limitedShots.parent.def.uiIcon,
                    action = delegate { }
                };
                ammo.Disable("MUGB_LimitedWeaponCounter".Translate());
                yield return ammo;
            }

            if (thrownSpear != null)
            {
                bool canThrow = thrownSpear.CanUse(pawn, out string throwReason);
                Command_Action throwCommand = new Command_Action
                {
                    defaultLabel = GoblinSpecialWeaponText.Resolve(thrownSpear.Props.label),
                    defaultDesc = GoblinSpecialWeaponText.Resolve(thrownSpear.Props.description),
                    icon = thrownSpear.CommandIcon,
                    action = () => thrownSpear.BeginTargeting(pawn),
                    onHover = () => DrawAbilityRangeIfUsable(pawn, thrownSpear.Props.range, canThrow)
                };

                if (!canThrow)
                {
                    throwCommand.Disable(throwReason);
                }
                yield return throwCommand;

                if (DebugSettings.godMode)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Recharge spear throw",
                        defaultDesc = "Immediately reset this spear throw cooldown.",
                        icon = thrownSpear.CommandIcon,
                        action = thrownSpear.DebugRecharge
                    };
                }
            }

            if (spearCharge != null)
            {
                bool canCharge = spearCharge.CanUse(pawn, out string chargeReason);
                Command_Action chargeCommand = new Command_Action
                {
                    defaultLabel = GoblinSpecialWeaponText.Resolve(spearCharge.Props.label),
                    defaultDesc = GoblinSpecialWeaponText.Resolve(spearCharge.Props.description),
                    icon = spearCharge.CommandIcon,
                    action = () => spearCharge.BeginTargeting(pawn),
                    onHover = () => DrawAbilityRangeIfUsable(pawn, spearCharge.Props.range, canCharge)
                };

                if (!canCharge)
                {
                    chargeCommand.Disable(chargeReason);
                }
                yield return chargeCommand;

                if (DebugSettings.godMode)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Recharge spear charge",
                        defaultDesc = "Immediately reset this spear charge cooldown.",
                        icon = spearCharge.CommandIcon,
                        action = spearCharge.DebugRecharge
                    };
                }
            }

            if (staff == null)
            {
                yield break;
            }

            Texture2D shieldIcon = ContentFinder<Texture2D>.Get("UI/Abilities/BulletShield", reportFailure: false) ?? staff.parent.def.uiIcon;
            bool canDeployShield = staff.CanDeployShield(pawn, requireDrafted: true, out string shieldReason);
            Command_Action shieldCommand = new Command_Action
            {
                defaultLabel = "MUGB_GoblinStaffShieldLabel".Translate(),
                defaultDesc = "MUGB_GoblinStaffShieldDesc".Translate(),
                icon = shieldIcon,
                action = () => staff.TryDeployShield(pawn),
                onHover = () => DrawAbilityRangeIfUsable(pawn, GetStaffShieldRadius(), canDeployShield)
            };

            if (!canDeployShield)
            {
                shieldCommand.Disable(shieldReason);
            }
            yield return shieldCommand;

            if (staff.HasActiveShield)
            {
                yield return new Gizmo_GoblinStaffShieldStatus(staff);
            }

            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Recharge staff shield",
                    defaultDesc = "Immediately reset this staff shield cooldown.",
                    icon = shieldIcon,
                    action = delegate
                    {
                        typeof(CompGoblinShamanStaff)
                            .GetField("lastShieldUsedTick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            ?.SetValue(staff, -9999999);
                    }
                };
            }
        }

        private static void DrawAbilityRangeIfUsable(Pawn pawn, float range, bool usable)
        {
            if (usable && pawn?.Spawned == true && pawn.Map != null && range > 0f)
            {
                GenDraw.DrawRadiusRing(pawn.Position, range);
            }
        }

        private static float GetStaffShieldRadius()
        {
            CompProperties_ProjectileInterceptor props = MUGBDefOf.MUGB_GoblinStaffMobileShield?.GetCompProperties<CompProperties_ProjectileInterceptor>();
            return props?.radius ?? 4f;
        }
    }

    [StaticConstructorOnStartup]
    public class Gizmo_GoblinStaffShieldStatus : Gizmo
    {
        private const float Width = 140f;
        private readonly CompGoblinShamanStaff staff;
        private static readonly Texture2D FullBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.31f, 0.38f, 0.62f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(Color.clear);

        public Gizmo_GoblinStaffShieldStatus(CompGoblinShamanStaff staff)
        {
            this.staff = staff;
            Order = -100f;
        }

        public override float GetWidth(float maxWidth)
        {
            return Width;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect inner = rect.ContractedBy(6f);
            Widgets.DrawWindowBackground(rect);

            Rect labelRect = inner;
            labelRect.height = rect.height / 2f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, "MUGB_GoblinStaffShieldStatus".Translate());

            int maximum = Mathf.Max(1, staff.ActiveShieldMaxHitPoints);
            int current = Mathf.Clamp(staff.ActiveShieldHitPoints, 0, maximum);
            Rect barRect = inner;
            barRect.yMin = inner.y + inner.height / 2f;
            Widgets.FillableBar(barRect, current / (float)maximum, FullBarTex, EmptyBarTex, doBorder: false);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, current + " / " + maximum);
            Text.Anchor = TextAnchor.UpperLeft;

            string remaining = staff.ActiveShieldTicksRemaining.ToStringTicksToPeriod();
            TooltipHandler.TipRegion(inner, "MUGB_GoblinStaffShieldStatusTip".Translate(remaining));
            return new GizmoResult(GizmoState.Clear);
        }
    }

    public class GoblinStaffMobileShield : ThingWithComps
    {
        private const int AuraRefreshIntervalTicks = 60;
        private const int AuraDurationTicks = 120;

        private Pawn owner;
        private int expireTick = -1;
        private int nextAuraRefreshTick;
        private bool expiring;
        private bool breakHandled;

        private CompProjectileInterceptor Interceptor => GetComp<CompProjectileInterceptor>();
        public Faction OwnerFaction => owner?.Faction;
        public int CurrentHitPoints => Interceptor?.currentHitPoints ?? 0;
        public int ShieldMaxHitPoints => Interceptor?.Props.hitPoints ?? 0;
        public int TicksRemaining => expireTick < 0 ? 0 : Mathf.Max(0, expireTick - Find.TickManager.TicksGame);

        public void Initialize(Pawn pawn, int durationTicks)
        {
            owner = pawn;
            expireTick = Find.TickManager.TicksGame + Mathf.Max(1, durationTicks);
            nextAuraRefreshTick = Find.TickManager.TicksGame;

            SoundDef startup = DefDatabase<SoundDef>.GetNamedSilentFail("Broadshield_Startup")
                ?? DefDatabase<SoundDef>.GetNamedSilentFail("BulletShieldGenerator_Reactivate");
            startup?.PlayOneShot(new TargetInfo(Position, Map));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref expireTick, "expireTick", -1);
            Scribe_Values.Look(ref nextAuraRefreshTick, "nextAuraRefreshTick", 0);
            Scribe_Values.Look(ref expiring, "expiring", false);
            Scribe_Values.Look(ref breakHandled, "breakHandled", false);
        }

        protected override void Tick()
        {
            base.Tick();
            if (Destroyed || Map == null)
            {
                return;
            }

            FollowOwner();
            if (Find.TickManager.TicksGame >= nextAuraRefreshTick)
            {
                RefreshCohesionAura();
                nextAuraRefreshTick = Find.TickManager.TicksGame + AuraRefreshIntervalTicks;
            }

            if (!expiring && expireTick >= 0 && Find.TickManager.TicksGame >= expireTick)
            {
                expiring = true;
                RemoveCohesionAura();
                Destroy(DestroyMode.Vanish);
                return;
            }

            if (!breakHandled && ShieldWasBroken())
            {
                breakHandled = true;
                PlayBreakEffect();
                RemoveCohesionAura();
                Destroy(DestroyMode.Vanish);
            }
        }

        private void RefreshCohesionAura()
        {
            HediffDef auraDef = MUGBDefOf.MUGB_StaffShieldCohesion;
            Faction faction = OwnerFaction;
            if (auraDef == null || faction == null || Map?.mapPawns == null)
            {
                return;
            }

            float radius = Interceptor?.Props.radius ?? 4f;
            List<Pawn> pawns = Map.mapPawns.SpawnedPawnsInFaction(faction);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.Faction != faction || pawn.Dead || !pawn.RaceProps.Humanlike || pawn.Position.DistanceTo(Position) > radius)
                {
                    continue;
                }

                Hediff aura = pawn.health?.hediffSet?.GetFirstHediffOfDef(auraDef);
                if (aura == null)
                {
                    aura = HediffMaker.MakeHediff(auraDef, pawn);
                    pawn.health.AddHediff(aura);
                }

                HediffComp_Disappears disappears = aura.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                {
                    disappears.ticksToDisappear = AuraDurationTicks;
                }
            }
        }

        private void RemoveCohesionAura()
        {
            HediffDef auraDef = MUGBDefOf.MUGB_StaffShieldCohesion;
            Faction faction = OwnerFaction;
            Map map = Map;
            if (auraDef == null || faction == null || map?.mapPawns == null)
            {
                return;
            }

            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(faction);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Hediff aura = pawn?.health?.hediffSet?.GetFirstHediffOfDef(auraDef);
                if (aura != null && pawn.Faction == faction && !CoveredByAnotherShield(pawn, map, faction))
                {
                    pawn.health.RemoveHediff(aura);
                }
            }
        }

        private bool CoveredByAnotherShield(Pawn pawn, Map map, Faction faction)
        {
            List<Thing> shields = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < shields.Count; i++)
            {
                if (shields[i] is GoblinStaffMobileShield shield
                    && shield != this
                    && !shield.Destroyed
                    && shield.OwnerFaction == faction)
                {
                    float radius = shield.Interceptor?.Props.radius ?? 4f;
                    if (pawn.Position.DistanceTo(shield.Position) <= radius)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void FollowOwner()
        {
            if (owner?.Spawned == true && owner.Map == Map && Position != owner.Position)
            {
                Position = owner.Position;
            }
        }

        private bool ShieldWasBroken()
        {
            CompProjectileInterceptor interceptor = Interceptor;
            if (interceptor == null)
            {
                return false;
            }

            if (interceptor.currentHitPoints == 0)
            {
                return true;
            }

            return !interceptor.Active && (interceptor.OnCooldown || interceptor.Charging);
        }

        private void PlayBreakEffect()
        {
            EffecterDef effecterDef = DefDatabase<EffecterDef>.GetNamedSilentFail("MUGB_ShieldBreakLarge");
            if (effecterDef == null)
            {
                return;
            }

            TargetInfo target = new TargetInfo(Position, Map);
            Effecter effecter = effecterDef.Spawn();
            effecter.Trigger(target, target);
            effecter.Cleanup();
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TickRare))]
    public static class Pawn_TickRare_GoblinStaffShieldAIPatch
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance?.Spawned != true
                || __instance.Dead
                || __instance.Downed
                || __instance.Faction == null
                || __instance.Faction == Faction.OfPlayer)
            {
                return;
            }

            CompGoblinShamanStaff staff = __instance.equipment?.Primary?.TryGetComp<CompGoblinShamanStaff>();
            if (staff == null || !staff.ShouldAutoDeployShield(__instance))
            {
                return;
            }

            staff.TryDeployShield(__instance, requireDrafted: false, playerFeedback: false);
        }
    }
}
