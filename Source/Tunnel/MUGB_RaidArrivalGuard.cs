using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MUGB
{
    internal static class MUGB_RaidArrivalGuard
    {
        public static bool IsRaid(IncidentParms parms, PawnsArrivalModeDef mode)
        {
            return parms?.target is Map
                && parms.faction != null
                && !parms.faction.IsPlayer
                && Squads.MUGB_SquadRaidUtility.IsGoblinRaidFaction(parms.faction)
                && parms.raidStrategy != null
                && parms.raidArrivalMode == mode;
        }

        public static bool ResolvePeacefulArrival(IncidentParms parms)
        {
            return TryResolveBackup(parms, out _);
        }

        public static void ArrivePeacefully(List<Pawn> pawns, IncidentParms parms)
        {
            if (pawns.NullOrEmpty()) return;
            if (TryResolveBackup(parms, out PawnsArrivalModeDef backup))
            {
                // Preserve the quest's pawn list, factions and join decisions.
                backup.Worker.Arrive(new List<Pawn>(pawns), parms);
            }
            else
            {
                Log.Error("[MUGB] No legal non-raid arrival mode for quest pawns; pawn references were preserved.");
            }
        }

        private static bool TryResolveBackup(IncidentParms parms, out PawnsArrivalModeDef backup)
        {
            backup = null;
            if (!(parms?.target is Map map)) return false;
            if (TryResolve(PawnsArrivalModeDefOf.EdgeWalkIn, map, parms))
            {
                backup = PawnsArrivalModeDefOf.EdgeWalkIn;
                return true;
            }
            foreach (PawnsArrivalModeDef candidate in DefDatabase<PawnsArrivalModeDef>.AllDefsListForReading)
            {
                // Restrict this fallback to vanilla workers, not another mod's raid-only worker.
                if (candidate == PawnsArrivalModeDefOf.EdgeWalkIn || !candidate.canBeBackup
                    || candidate.workerClass == null
                    || candidate.workerClass.Assembly != typeof(PawnsArrivalModeWorker).Assembly)
                    continue;
                if (!TryResolve(candidate, map, parms)) continue;
                backup = candidate;
                return true;
            }
            return false;
        }

        private static bool TryResolve(PawnsArrivalModeDef candidate, Map map, IncidentParms parms)
        {
            return candidate.Worker.CanUseOnMap(map)
                && candidate.Worker.TryResolveRaidSpawnCenter(parms);
        }
    }
}
