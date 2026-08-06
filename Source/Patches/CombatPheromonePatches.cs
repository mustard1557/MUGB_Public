using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class MUGBCombatPheromoneMapComponent : MapComponent
    {
        private const int CheckIntervalTicks = 500;
        private const float SwarmRange = 5f;
        private const float CommandRange = 6f;
        private const int MaxSwarmCount = 4;

        private readonly List<Pawn> commonGoblinEmitters = new List<Pawn>();
        private readonly List<Pawn> commandEmitters = new List<Pawn>();
        private readonly List<Pawn> recipients = new List<Pawn>();
        private readonly Dictionary<Pawn, Room> roomCache = new Dictionary<Pawn, Room>();

        public MUGBCombatPheromoneMapComponent(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            int ticksGame = Find.TickManager.TicksGame;
            if ((ticksGame + map.uniqueID) % CheckIntervalTicks != 0)
            {
                return;
            }

            TickCombatPheromones();
        }

        private void TickCombatPheromones()
        {
            commonGoblinEmitters.Clear();
            commandEmitters.Clear();
            recipients.Clear();
            roomCache.Clear();

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsActiveGoblin(pawn))
                {
                    continue;
                }

                if (IsCommonGoblinPheromonePawn(pawn))
                {
                    commonGoblinEmitters.Add(pawn);
                    recipients.Add(pawn);
                }
                else if (IsHobgoblinCommandPawn(pawn))
                {
                    commandEmitters.Add(pawn);
                }
            }

            for (int i = 0; i < recipients.Count; i++)
            {
                Pawn recipient = recipients[i];
                int swarmCount = SwarmCountFor(recipient);
                bool commandActive = HasCommandFor(recipient);

                ApplyOrRemove(recipient, MUGBDefOf.MUGB_GoblinSwarmPheromoneBuff, SeverityForSwarmCount(swarmCount));
                ApplyOrRemove(recipient, MUGBDefOf.MUGB_HobgoblinCommandPheromoneBuff, commandActive ? 1f : 0f);
            }
        }

        private static bool IsActiveGoblin(Pawn pawn)
        {
            return pawn?.Spawned == true
                && !pawn.Dead
                && !pawn.Downed
                && pawn.RaceProps?.Humanlike == true
                && GoblinUtility.IsGoblin(pawn);
        }

        private static bool IsCommonGoblinPheromonePawn(Pawn pawn)
        {
            return !GoblinUtility.IsHobgoblin(pawn)
                && pawn?.genes?.GetGene(MUGBDefOf.MUGB_Gene_GoblinSwarmPheromone) != null;
        }

        private static bool IsHobgoblinCommandPawn(Pawn pawn)
        {
            return GoblinUtility.IsHobgoblin(pawn)
                && pawn?.genes?.GetGene(MUGBDefOf.MUGB_Gene_HobgoblinCommandPheromone) != null;
        }

        private int SwarmCountFor(Pawn recipient)
        {
            int count = 0;
            for (int i = 0; i < commonGoblinEmitters.Count; i++)
            {
                Pawn emitter = commonGoblinEmitters[i];
                if (emitter == recipient)
                {
                    continue;
                }

                if (CanSharePheromone(emitter, recipient, SwarmRange))
                {
                    count++;
                    if (count >= MaxSwarmCount)
                    {
                        return MaxSwarmCount;
                    }
                }
            }

            return count;
        }

        private bool HasCommandFor(Pawn recipient)
        {
            for (int i = 0; i < commandEmitters.Count; i++)
            {
                if (CanSharePheromone(commandEmitters[i], recipient, CommandRange))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanSharePheromone(Pawn emitter, Pawn recipient, float range)
        {
            if (emitter == null || recipient == null || emitter == recipient)
            {
                return false;
            }

            if (!recipient.Position.InHorDistOf(emitter.Position, range))
            {
                return false;
            }

            Room recipientRoom = RoomFor(recipient);
            Room emitterRoom = RoomFor(emitter);
            bool sameRoom = recipientRoom != null && recipientRoom == emitterRoom;
            if (sameRoom)
            {
                return true;
            }

            bool recipientOutdoors = recipientRoom == null || recipientRoom.OutdoorsForWork;
            bool emitterOutdoors = emitterRoom == null || emitterRoom.OutdoorsForWork;
            return recipientOutdoors && emitterOutdoors;
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

        private static float SeverityForSwarmCount(int count)
        {
            if (count <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp(count, 1, MaxSwarmCount) / (float)MaxSwarmCount;
        }

        private static void ApplyOrRemove(Pawn pawn, HediffDef hediffDef, float severity)
        {
            if (pawn?.health == null || hediffDef == null)
            {
                return;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (severity <= 0f)
            {
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }

                return;
            }

            if (existing == null)
            {
                existing = pawn.health.AddHediff(hediffDef);
            }

            if (existing != null)
            {
                existing.Severity = Mathf.Clamp01(severity);
            }
        }
    }
}
