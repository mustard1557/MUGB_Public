using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MUGB
{
    // 유물 지팡이(A/B)에 붙는 능력들의 공통 토대.
    //
    // 시전 흐름(바닐라 방식):
    // - 능력 버튼 → 대상 지정. 대상이 사거리 밖/시야 밖이면 취소하지 않고, 폰이 사거리와 시야가
    //   확보되는 칸까지 걸어간 뒤(JobDriver_UseStaffAbility) 짧게 조준(예열)하고 시전합니다.
    //
    // 성능 원칙("틱 최대한 안 먹게"):
    // - 이 컴프 자체는 매 틱 도는 로직이 없습니다. 충전량은 마지막 사용 시각만 저장해 두고,
    //   기즈모를 그리거나 사용할 때만 "지금 틱 - 저장한 틱"으로 역산합니다.
    //   · perChargeRegenTicks > 0 : 소비 후 그 간격마다 1발씩 최대치까지 회복(유물 A).
    //   · fullRefillTicks    > 0 : 다 쓰면 그 시간 뒤 한 번에 전량 회복(유물 B / 페로몬 흡수).
    public abstract class CompProperties_StaffChargedAbilityBase : CompProperties
    {
        public int maxCharges = 1;
        public int perChargeRegenTicks = 0;
        public int fullRefillTicks = 0;
        public float range = 30f;
        public bool requireLineOfSight = true;
        public int warmupTicks = 60;
        public string iconPath;
        public string label = "Staff ability";
        public string description = "A staff ability.";
    }

    public abstract class CompStaffChargedAbilityBase : ThingComp
    {
        private int charges = -1;
        private int nextRegenTick = -1;   // perChargeRegen 모델용
        private int refillTick = -1;      // fullRefill 모델용

        public CompProperties_StaffChargedAbilityBase BaseProps => (CompProperties_StaffChargedAbilityBase)props;

        public int MaxCharges => Mathf.Max(1, BaseProps.maxCharges);
        public int WarmupTicks => Mathf.Max(0, BaseProps.warmupTicks);

        public int Charges
        {
            get
            {
                RefreshCharges();
                return charges;
            }
        }

        public Texture2D CommandIcon
        {
            get
            {
                if (!BaseProps.iconPath.NullOrEmpty())
                {
                    return ContentFinder<Texture2D>.Get(BaseProps.iconPath, reportFailure: false) ?? parent.def.uiIcon;
                }
                return parent.def.uiIcon;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charges, "mugbStaffAbilCharges", -1);
            Scribe_Values.Look(ref nextRegenTick, "mugbStaffAbilRegenTick", -1);
            Scribe_Values.Look(ref refillTick, "mugbStaffAbilRefillTick", -1);
        }

        private void RefreshCharges()
        {
            int now = Find.TickManager.TicksGame;
            if (charges < 0)
            {
                charges = MaxCharges;
                return;
            }

            if (BaseProps.perChargeRegenTicks > 0)
            {
                while (charges < MaxCharges && nextRegenTick >= 0 && now >= nextRegenTick)
                {
                    charges++;
                    nextRegenTick = charges < MaxCharges ? nextRegenTick + BaseProps.perChargeRegenTicks : -1;
                }
            }

            if (BaseProps.fullRefillTicks > 0 && charges <= 0 && refillTick >= 0 && now >= refillTick)
            {
                charges = MaxCharges;
                refillTick = -1;
            }
        }

        // JobDriver가 시전을 마친 뒤 호출합니다.
        public void PerformCast(Pawn caster, LocalTargetInfo target)
        {
            ApplyEffect(caster, target);
            ConsumeCharge();
        }

        private void ConsumeCharge()
        {
            RefreshCharges();
            charges = Mathf.Max(0, charges - 1);
            int now = Find.TickManager.TicksGame;

            if (BaseProps.perChargeRegenTicks > 0 && charges < MaxCharges && nextRegenTick < 0)
            {
                nextRegenTick = now + BaseProps.perChargeRegenTicks;
            }
            if (BaseProps.fullRefillTicks > 0 && charges <= 0)
            {
                refillTick = now + BaseProps.fullRefillTicks;
            }
        }

        public int TicksUntilNextCharge
        {
            get
            {
                RefreshCharges();
                if (charges >= MaxCharges)
                {
                    return 0;
                }
                if (BaseProps.perChargeRegenTicks > 0 && nextRegenTick >= 0)
                {
                    return Mathf.Max(0, nextRegenTick - Find.TickManager.TicksGame);
                }
                if (BaseProps.fullRefillTicks > 0 && refillTick >= 0)
                {
                    return Mathf.Max(0, refillTick - Find.TickManager.TicksGame);
                }
                return 0;
            }
        }

        public bool CanUse(Pawn pawn, out string reason)
        {
            reason = null;
            if (pawn?.Spawned != true || pawn.Downed || pawn.Map == null)
            {
                reason = "MUGB_StaffAbilityUnavailable".Translate();
                return false;
            }
            if (!pawn.Drafted)
            {
                reason = "MUGB_StaffAbilityMustBeDrafted".Translate();
                return false;
            }
            if (parent != pawn.equipment?.Primary)
            {
                reason = "MUGB_StaffAbilityUnavailable".Translate();
                return false;
            }
            if (Charges <= 0)
            {
                reason = "MUGB_StaffAbilityRecharging".Translate(TicksUntilNextCharge.ToStringTicksToPeriod());
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

            TargetingParameters parameters = BuildTargetingParameters(pawn);
            Find.Targeter.BeginTargeting(
                parameters,
                target => Confirm(pawn, target),
                delegate
                {
                    if (pawn?.Spawned == true)
                    {
                        GenDraw.DrawRadiusRing(pawn.Position, BaseProps.range);
                    }
                });
        }

        // 사거리 밖/시야 밖이어도 대상 종류만 맞으면 명령을 받습니다(폰이 걸어가서 시전).
        private void Confirm(Pawn pawn, LocalTargetInfo target)
        {
            if (!CanUse(pawn, out string reason) || !IsAcceptableTarget(pawn, target, out reason))
            {
                Messages.Message(reason ?? "MUGB_StaffAbilityBadTarget".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (!TryFindCastCell(pawn, target.Cell, out _, out reason))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            int index = AbilityIndexOn(pawn.equipment?.Primary);
            if (index < 0)
            {
                return;
            }

            Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_UseStaffAbility, target);
            job.count = index;
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private int AbilityIndexOn(ThingWithComps weapon)
        {
            if (weapon == null)
            {
                return -1;
            }
            List<CompStaffChargedAbilityBase> list = weapon.AllComps.OfType<CompStaffChargedAbilityBase>().ToList();
            return list.IndexOf(this);
        }

        public bool InCastRange(Pawn pawn, IntVec3 targetCell)
        {
            if (pawn?.Map == null)
            {
                return false;
            }
            if (pawn.Position.DistanceTo(targetCell) > BaseProps.range)
            {
                return false;
            }
            return !BaseProps.requireLineOfSight || GenSight.LineOfSight(pawn.Position, targetCell, pawn.Map);
        }

        // 사거리·시야가 확보되는, 폰이 도달 가능한 시전 칸을 찾습니다.
        public bool TryFindCastCell(Pawn pawn, IntVec3 targetCell, out IntVec3 castCell, out string reason)
        {
            castCell = IntVec3.Invalid;
            reason = null;
            if (pawn?.Map == null || !targetCell.InBounds(pawn.Map))
            {
                reason = "MUGB_StaffAbilityBadTarget".Translate();
                return false;
            }

            bool los = BaseProps.requireLineOfSight;
            if (InCastRange(pawn, targetCell))
            {
                castCell = pawn.Position;
                return true;
            }

            List<IntVec3> candidates = GenRadial.RadialCellsAround(targetCell, BaseProps.range, true)
                .Where(cell => cell.InBounds(pawn.Map)
                    && cell.Standable(pawn.Map)
                    && (!los || GenSight.LineOfSight(cell, targetCell, pawn.Map))
                    && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                .ToList();

            if (candidates.Count <= 0)
            {
                reason = los ? "MUGB_StaffAbilityNoLineOfSight".Translate() : "MUGB_StaffAbilityOutOfRange".Translate();
                return false;
            }

            castCell = candidates[0];
            return true;
        }

        public void DebugRecharge()
        {
            charges = MaxCharges;
            nextRegenTick = -1;
            refillTick = -1;
        }

        protected abstract TargetingParameters BuildTargetingParameters(Pawn pawn);
        protected abstract bool IsAcceptableTarget(Pawn pawn, LocalTargetInfo target, out string reason);
        protected abstract void ApplyEffect(Pawn pawn, LocalTargetInfo target);
    }

    // ── 유물 A: 쾌락절정 (정신충격 기절) ─────────────────────────────
    public class CompProperties_StaffPsychicShock : CompProperties_StaffChargedAbilityBase
    {
        public float brainDamageChance = 0.1f;
        public CompProperties_StaffPsychicShock() { compClass = typeof(CompStaffPsychicShock); }
    }

    public class CompStaffPsychicShock : CompStaffChargedAbilityBase
    {
        public CompProperties_StaffPsychicShock Props => (CompProperties_StaffPsychicShock)props;

        protected override TargetingParameters BuildTargetingParameters(Pawn pawn)
        {
            TargetingParameters p = TargetingParameters.ForAttackAny();
            p.canTargetLocations = false;
            p.canTargetBuildings = false;
            p.validator = t => t.HasThing && t.Thing is Pawn victim && IsShockable(victim);
            return p;
        }

        private static bool IsShockable(Pawn victim)
        {
            return victim != null && !victim.Dead && victim.RaceProps != null
                && (victim.RaceProps.Humanlike || victim.RaceProps.Animal)
                && victim.GetStatValue(StatDefOf.PsychicSensitivity) > 0f;
        }

        protected override bool IsAcceptableTarget(Pawn pawn, LocalTargetInfo target, out string reason)
        {
            reason = null;
            if (!(target.Thing is Pawn victim) || !IsShockable(victim))
            {
                reason = "MUGB_StaffAbilityBadTarget".Translate();
                return false;
            }
            return true;
        }

        protected override void ApplyEffect(Pawn pawn, LocalTargetInfo target)
        {
            if (!(target.Thing is Pawn victim) || victim.health == null || !victim.Spawned)
            {
                return;
            }

            HediffDef shockDef = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicShock");
            if (shockDef != null && !victim.health.hediffSet.HasHediff(shockDef))
            {
                victim.health.AddHediff(shockDef);
            }

            if (Rand.Chance(Props.brainDamageChance))
            {
                BodyPartRecord brain = victim.health.hediffSet.GetBrain();
                if (brain != null)
                {
                    victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(1f, 4f), 0f, -1f, pawn, brain));
                }
            }

            ThingDef filthDef = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_AmnioticFluid");
            if (filthDef != null)
            {
                FilthMaker.TryMakeFilth(victim.Position, victim.Map, filthDef, Rand.RangeInclusive(2, 3));
            }

            // 시각·청각 피드백.
            FleckDef fleck = DefDatabase<FleckDef>.GetNamedSilentFail("PsycastPsychicEffect");
            if (fleck != null)
            {
                FleckMaker.Static(victim.DrawPos, victim.Map, fleck, 1.6f);
            }
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("PsychicShockLanceCast");
            sound?.PlayOneShot(new TargetInfo(victim.Position, victim.Map));
        }
    }

    // ── 유물 B: 페로몬 방출 (독가스 + EMP 즉발) ──────────────────────
    public class CompProperties_StaffPheromoneBurst : CompProperties_StaffChargedAbilityBase
    {
        public float gasRadius = 2.9f;
        public int gasDensity = 220;
        public float empRadius = 3.4f;
        public CompProperties_StaffPheromoneBurst() { compClass = typeof(CompStaffPheromoneBurst); }
    }

    public class CompStaffPheromoneBurst : CompStaffChargedAbilityBase
    {
        public CompProperties_StaffPheromoneBurst Props => (CompProperties_StaffPheromoneBurst)props;

        protected override TargetingParameters BuildTargetingParameters(Pawn pawn)
        {
            TargetingParameters p = TargetingParameters.ForAttackAny();
            p.canTargetLocations = true;
            p.canTargetSelf = false;
            return p;
        }

        protected override bool IsAcceptableTarget(Pawn pawn, LocalTargetInfo target, out string reason)
        {
            reason = null;
            if (!target.IsValid || pawn?.Map == null || !target.Cell.InBounds(pawn.Map))
            {
                reason = "MUGB_StaffAbilityBadTarget".Translate();
                return false;
            }
            return true;
        }

        protected override void ApplyEffect(Pawn pawn, LocalTargetInfo target)
        {
            Map map = pawn.Map;
            IntVec3 center = target.Cell;
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            // 독성 가스: 지정 셀 주변에 톡스가스를 채웁니다(투사체 없이 그 자리에서 터진 연출).
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.gasRadius, true))
            {
                if (cell.InBounds(map) && GenSight.LineOfSight(center, cell, map))
                {
                    map.gasGrid.AddGas(cell, GasType.ToxGas, Mathf.Clamp(Props.gasDensity, 1, 255), canOverflow: false);
                }
            }

            // EMP 폭발: 수류탄 터지는 효과(기계 무력화·방패 파괴 + 시각·청각 연출).
            GenExplosion.DoExplosion(
                center, map, Props.empRadius, DamageDefOf.EMP, pawn,
                damAmount: -1, armorPenetration: -1f,
                weapon: parent.def);
        }
    }

    // ── 유물 B: 페로몬 흡수 (유독 페로몬 중독 초기화 + 15일 면역) ──────
    public class CompProperties_StaffPheromoneAbsorb : CompProperties_StaffChargedAbilityBase
    {
        public CompProperties_StaffPheromoneAbsorb() { compClass = typeof(CompStaffPheromoneAbsorb); }
    }

    public class CompStaffPheromoneAbsorb : CompStaffChargedAbilityBase
    {
        protected override TargetingParameters BuildTargetingParameters(Pawn pawn)
        {
            TargetingParameters p = TargetingParameters.ForColonist();
            p.canTargetSelf = true;
            p.validator = t => t.HasThing && t.Thing is Pawn ally && IsFriendlyLiving(pawn, ally);
            return p;
        }

        private static bool IsFriendlyLiving(Pawn caster, Pawn ally)
        {
            return ally != null && !ally.Dead && ally.RaceProps?.Humanlike == true
                && (ally == caster || (ally.Faction != null && !ally.HostileTo(caster)));
        }

        protected override bool IsAcceptableTarget(Pawn pawn, LocalTargetInfo target, out string reason)
        {
            reason = null;
            if (!(target.Thing is Pawn ally) || !IsFriendlyLiving(pawn, ally))
            {
                reason = "MUGB_StaffAbilityBadTarget".Translate();
                return false;
            }
            return true;
        }

        protected override void ApplyEffect(Pawn pawn, LocalTargetInfo target)
        {
            if (!(target.Thing is Pawn ally) || ally.health == null || !ally.Spawned)
            {
                return;
            }

            // 유독 페로몬 관련 헤디프만 초기화합니다. 노예혼용 페로몬 적응은 절대 건드리지 않습니다.
            RemoveAll(ally, MUGBDefOf.MUGB_ToxicPheromoneExposure);
            RemoveAll(ally, DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_ToxicPheromoneCollapse"));

            HediffDef immunityDef = DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_ToxicPheromoneImmunity");
            if (immunityDef != null && !ally.health.hediffSet.HasHediff(immunityDef))
            {
                ally.health.AddHediff(immunityDef);
            }

            // 시각·청각·문구 피드백(무엇이 일어났는지 확실히 보이게).
            FleckDef fleck = DefDatabase<FleckDef>.GetNamedSilentFail("HealingCross");
            if (fleck != null)
            {
                FleckMaker.ThrowMetaIcon(ally.Position, ally.Map, fleck);
            }
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("MechSerumUsed");
            sound?.PlayOneShot(new TargetInfo(ally.Position, ally.Map));
            Messages.Message("MUGB_StaffPheromoneAbsorbApplied".Translate(ally.LabelShortCap), ally, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        private static void RemoveAll(Pawn pawn, HediffDef def)
        {
            if (def == null || pawn?.health == null)
            {
                return;
            }
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs.Where(h => h.def == def).ToList())
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }

    // ── JobDriver: 사거리·시야 확보까지 이동 → 예열 → 시전 ───────────
    public class JobDriver_UseStaffAbility : JobDriver
    {
        private const TargetIndex Ind = TargetIndex.A;

        private CompStaffChargedAbilityBase Ability
        {
            get
            {
                List<CompStaffChargedAbilityBase> list = pawn?.equipment?.Primary?.AllComps
                    .OfType<CompStaffChargedAbilityBase>().ToList();
                if (list == null || job.count < 0 || job.count >= list.Count)
                {
                    return null;
                }
                return list[job.count];
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Ability == null || !Ability.CanUse(pawn, out _));
            this.FailOn(() => !job.GetTarget(Ind).IsValid);

            Toil approach = ToilMaker.MakeToil("ApproachStaffAbility");
            approach.initAction = delegate
            {
                CompStaffChargedAbilityBase comp = Ability;
                IntVec3 targetCell = job.GetTarget(Ind).Cell;
                if (comp.InCastRange(pawn, targetCell))
                {
                    ReadyForNextToil();
                    return;
                }
                if (!comp.TryFindCastCell(pawn, targetCell, out IntVec3 castCell, out _) || castCell == pawn.Position)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.pather.StartPath(castCell, PathEndMode.OnCell);
            };
            approach.tickAction = delegate
            {
                if (Ability.InCastRange(pawn, job.GetTarget(Ind).Cell))
                {
                    pawn.pather.StopDead();
                    ReadyForNextToil();
                }
            };
            approach.defaultCompleteMode = ToilCompleteMode.Never;
            yield return approach;

            int warmupTicks = Mathf.Max(1, Ability?.WarmupTicks ?? 1);
            Toil warmup = ToilMaker.MakeToil("WarmupStaffAbility");
            warmup.initAction = delegate
            {
                LocalTargetInfo t = job.GetTarget(Ind);
                pawn.rotationTracker.FaceTarget(t);
                // 정충창처럼 조준 자세(조준선 표시)를 잡습니다. 이게 없으면 대기 시간이 즉발처럼 느껴집니다.
                pawn.stances?.SetStance(new Stance_Warmup(warmupTicks, t, pawn.equipment?.PrimaryEq?.PrimaryVerb));
            };
            warmup.tickAction = delegate { pawn.rotationTracker.FaceTarget(job.GetTarget(Ind)); };
            warmup.defaultCompleteMode = ToilCompleteMode.Delay;
            warmup.defaultDuration = warmupTicks;
            warmup.WithProgressBarToilDelay(Ind);
            yield return warmup;

            Toil doAbility = ToilMaker.MakeToil("DoStaffAbility");
            doAbility.initAction = delegate
            {
                CompStaffChargedAbilityBase comp = Ability;
                if (comp != null && comp.CanUse(pawn, out _))
                {
                    comp.PerformCast(pawn, job.GetTarget(Ind));
                }
            };
            doAbility.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return doAbility;
        }
    }

    // ── 기즈모: 장착한 유물 지팡이의 능력 버튼들 ──────────────────────
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Pawn_GetGizmos_StaffRelicAbilitiesPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Faction != Faction.OfPlayer || __instance.equipment?.Primary == null)
            {
                return;
            }

            List<CompStaffChargedAbilityBase> abilities = __instance.equipment.Primary.AllComps
                .OfType<CompStaffChargedAbilityBase>().ToList();
            if (abilities.Count == 0)
            {
                return;
            }

            __result = Append(__result, __instance, abilities);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> gizmos, Pawn pawn, List<CompStaffChargedAbilityBase> abilities)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }

            foreach (CompStaffChargedAbilityBase ability in abilities)
            {
                bool canUse = ability.CanUse(pawn, out string reason);
                string label = GoblinSpecialWeaponText.Resolve(ability.BaseProps.label);
                Command_Action command = new Command_Action
                {
                    defaultLabel = $"{label} ({ability.Charges}/{ability.MaxCharges})",
                    defaultDesc = GoblinSpecialWeaponText.Resolve(ability.BaseProps.description),
                    icon = ability.CommandIcon,
                    action = () => ability.BeginTargeting(pawn),
                    onHover = delegate
                    {
                        if (canUse && pawn?.Spawned == true && ability.BaseProps.range > 0f)
                        {
                            GenDraw.DrawRadiusRing(pawn.Position, ability.BaseProps.range);
                        }
                    }
                };
                if (!canUse)
                {
                    command.Disable(reason);
                }
                yield return command;

                if (DebugSettings.godMode)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Recharge " + label,
                        defaultDesc = "Immediately refill this staff ability.",
                        icon = ability.CommandIcon,
                        action = ability.DebugRecharge
                    };
                }
            }
        }
    }
}
