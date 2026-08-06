using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class MUGBToxicPheromoneMapComponent : MapComponent
    {
        private const int CheckIntervalTicks = 2500;
        private const float StrongIndoorDeathDays = 16f;
        private const float WeakStrengthMultiplier = 0.5f;
        private const float OutdoorMultiplier = 0.50f;
        private const float MaxStackedEmitterStrength = 2f;
        private const float StrongRange = 15f;
        private const float WeakRange = 11f;
        private const float DailyRecovery = 0.05f;

        private readonly List<Pawn> emitters = new List<Pawn>();
        private readonly Dictionary<Pawn, Room> roomCache = new Dictionary<Pawn, Room>();
        private bool cheatDisableCleanupDone;

        public MUGBToxicPheromoneMapComponent(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (MUGBMod.Settings?.disableToxicPheromonesCheat == true)
            {
                if (!cheatDisableCleanupDone)
                {
                    RemoveExistingToxicPheromoneEffects();
                    cheatDisableCleanupDone = true;
                }
                return;
            }

            cheatDisableCleanupDone = false;
            int ticksGame = Find.TickManager.TicksGame;
            if ((ticksGame + map.uniqueID) % CheckIntervalTicks != 0)
            {
                return;
            }

            TickToxicPheromones();
        }

        private void RemoveExistingToxicPheromoneEffects()
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                RemoveHediff(pawn, MUGBDefOf.MUGB_ToxicPheromoneExposure);
                RemoveHediff(pawn, MUGBDefOf.MUGB_ToxicPheromoneCollapse);
            }
        }

        private static void RemoveHediff(Pawn pawn, HediffDef hediffDef)
        {
            Hediff hediff = hediffDef == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        private void TickToxicPheromones()
        {
            emitters.Clear();
            roomCache.Clear();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (IsActiveEmitter(pawn))
                {
                    emitters.Add(pawn);
                }
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn target = pawns[i];
                if (!CanReceiveToxicPheromones(target))
                {
                    continue;
                }

                float exposureStrength = ExposureStrengthFor(target);
                if (exposureStrength > 0f)
                {
                    ApplyExposure(target, exposureStrength);
                }
                else
                {
                    RecoverExposure(target);
                }
            }
        }

        private static bool IsActiveEmitter(Pawn pawn)
        {
            return pawn?.Spawned == true
                && !pawn.Dead
                && !pawn.Downed
                && GoblinUtility.IsGoblin(pawn)
                && ToxicPheromoneStrength(pawn) > 0f;
        }

        private static bool CanReceiveToxicPheromones(Pawn pawn)
        {
            return pawn?.Spawned == true
                && !pawn.Dead
                && pawn.RaceProps?.Humanlike == true
                && !GoblinUtility.IsGoblin(pawn);
        }

        private float ExposureStrengthFor(Pawn target)
        {
            float total = 0f;
            Room targetRoom = RoomFor(target);
            bool targetOutdoors = targetRoom == null || targetRoom.OutdoorsForWork;

            for (int i = 0; i < emitters.Count; i++)
            {
                Pawn emitter = emitters[i];
                float strength = ToxicPheromoneStrength(emitter);
                if (strength <= 0f)
                {
                    continue;
                }

                float range = HasStrongToxicPheromone(emitter) ? StrongRange : WeakRange;
                if (!target.Position.InHorDistOf(emitter.Position, range))
                {
                    continue;
                }

                Room emitterRoom = RoomFor(emitter);
                bool sameRoom = targetRoom != null && targetRoom == emitterRoom;
                bool outdoorExposure = targetOutdoors && (emitterRoom == null || emitterRoom.OutdoorsForWork);
                if (!sameRoom && !outdoorExposure)
                {
                    continue;
                }

                float roomFactor = sameRoom && !targetOutdoors ? 1f : OutdoorMultiplier;
                total += strength * roomFactor * MUGBSurgeryUtility.ToxicPheromoneEmissionFactor(emitter);
                if (total >= MaxStackedEmitterStrength)
                {
                    return MaxStackedEmitterStrength * ToxicPheromoneSensitivity(target);
                }
            }

            return Mathf.Min(total, MaxStackedEmitterStrength) * ToxicPheromoneSensitivity(target);
        }

        private Room RoomFor(Pawn pawn)
        {
            if (!roomCache.TryGetValue(pawn, out Room room))
            {
                room = pawn.GetRoom(RegionType.Set_Passable);
                roomCache.Add(pawn, room);
            }

            return room;
        }

        private static float ToxicPheromoneStrength(Pawn pawn)
        {
            if (HasStrongToxicPheromone(pawn))
            {
                return 1f;
            }

            if (pawn?.genes?.GetGene(MUGBDefOf.MUGB_Gene_GoblinWeakToxicPheromone) != null)
            {
                return WeakStrengthMultiplier;
            }

            return 0f;
        }

        private static bool HasStrongToxicPheromone(Pawn pawn)
        {
            return pawn?.genes?.GetGene(MUGBDefOf.MUGB_Gene_GoblinStrongToxicPheromone) != null;
        }

        private static float ToxicPheromoneSensitivity(Pawn pawn)
        {
            float factor = MUGBSurgeryUtility.ToxicPheromoneSensitivityFactor(pawn);
            if (pawn?.genes?.GetGene(MUGBDefOf.MUGB_Gene_HalfGoblinAncestry) != null)
            {
                factor *= 0.2f;
            }

            return Mathf.Clamp(factor, 0.15f, 1f);
        }

        private static void ApplyExposure(Pawn target, float exposureStrength)
        {
            if (MUGBDefOf.MUGB_ToxicPheromoneExposure == null)
            {
                return;
            }

            // 페로몬 흡수 능력의 면역 표식이 있으면 누적하지 않습니다.
            // 이 검사는 이미 인터벌로만 도는 누적 경로 안에 있어 추가 틱 비용이 없습니다.
            if (MUGBDefOf.MUGB_ToxicPheromoneImmunity != null
                && target.health?.hediffSet?.HasHediff(MUGBDefOf.MUGB_ToxicPheromoneImmunity) == true)
            {
                return;
            }

            Hediff exposure = target.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_ToxicPheromoneExposure);
            if (exposure == null)
            {
                exposure = target.health?.AddHediff(MUGBDefOf.MUGB_ToxicPheromoneExposure);
            }

            if (exposure == null)
            {
                return;
            }

            float gain = CheckIntervalTicks / (StrongIndoorDeathDays * GenDate.TicksPerDay) * exposureStrength;
            exposure.Severity = Mathf.Clamp01(exposure.Severity + gain);
            if (exposure.Severity >= 1f)
            {
                AddCollapse(target);
            }
        }

        private static void RecoverExposure(Pawn target)
        {
            Hediff exposure = target.health?.hediffSet?.GetFirstHediffOfDef(MUGBDefOf.MUGB_ToxicPheromoneExposure);
            if (exposure == null)
            {
                return;
            }

            float recovery = DailyRecovery * CheckIntervalTicks / GenDate.TicksPerDay;
            exposure.Severity -= recovery;
            if (exposure.Severity <= 0.001f)
            {
                target.health.RemoveHediff(exposure);
            }
        }

        private static void AddCollapse(Pawn target)
        {
            if (MUGBDefOf.MUGB_ToxicPheromoneCollapse == null || target.health.hediffSet.HasHediff(MUGBDefOf.MUGB_ToxicPheromoneCollapse))
            {
                return;
            }

            target.health.AddHediff(MUGBDefOf.MUGB_ToxicPheromoneCollapse);
        }
    }

    public class HediffCompProperties_ToxicPheromoneCollapse : HediffCompProperties
    {
        public int ticksToDeath = GenDate.TicksPerDay;

        public HediffCompProperties_ToxicPheromoneCollapse()
        {
            compClass = typeof(HediffComp_ToxicPheromoneCollapse);
        }
    }

    public class HediffComp_ToxicPheromoneCollapse : HediffComp
    {
        private int ticksAlive;

        private HediffCompProperties_ToxicPheromoneCollapse Props => (HediffCompProperties_ToxicPheromoneCollapse)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (MUGBMod.Settings?.disableToxicPheromonesCheat == true || Pawn == null || Pawn.Dead)
            {
                return;
            }

            ticksAlive++;
            if (ticksAlive >= Props.ticksToDeath)
            {
                Pawn.Kill(null, parent);
            }
        }

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref ticksAlive, "ticksAlive", 0);
        }
    }
}
