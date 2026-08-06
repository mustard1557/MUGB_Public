using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MUGB
{
    [HarmonyPatch(typeof(GasGrid), nameof(GasGrid.Tick))]
    public static class GoblinStinkGasGridTickPatch
    {
        private static readonly AccessTools.FieldRef<GasGrid, Map> MapRef =
            AccessTools.FieldRefAccess<GasGrid, Map>("map");

        public static void Postfix(GasGrid __instance)
        {
            Map map = MapRef(__instance);
            GoblinStinkGasCleanupUtility.Cleanup(map);
        }
    }

    public static class GoblinStinkGasCleanupUtility
    {
        private sealed class CleanupBuffer
        {
            public readonly HashSet<IntVec3> candidates = new HashSet<IntVec3>();
            public readonly HashSet<IntVec3> coveredCells = new HashSet<IntVec3>();
        }

        private static readonly ConditionalWeakTable<Map, CleanupBuffer> Buffers =
            new ConditionalWeakTable<Map, CleanupBuffer>();

        public static void Cleanup(Map map)
        {
            if (map == null || !ModsConfig.BiotechActive || MUGBDefOf.MUGB_StinkGasCloud == null)
            {
                return;
            }

            List<Thing> things = map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_StinkGasCloud);
            if (things.NullOrEmpty())
            {
                return;
            }

            CleanupBuffer buffer = Buffers.GetOrCreateValue(map);
            buffer.candidates.Clear();
            buffer.coveredCells.Clear();

            // 기존과 같은 매 틱 정리 시점을 유지하되, 겹친 구름의 셀과 구름 목록을 반복 검사하지 않는다.
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is GoblinStinkGasCloud cloud) || !cloud.Spawned || cloud.Destroyed)
                {
                    continue;
                }

                foreach (IntVec3 cell in GenRadial.RadialCellsAround(cloud.Position, cloud.GasGridCleanupScanRadius, true))
                {
                    if (cell.InBounds(map) && cell.GasDensity(map, GasType.ToxGas) > 0)
                    {
                        buffer.candidates.Add(cell);
                    }
                }

                if (cloud.CurrentIntensity <= 0f)
                {
                    continue;
                }

                foreach (IntVec3 cell in GenRadial.RadialCellsAround(cloud.Position, cloud.CurrentGasGridCoverageRadius, true))
                {
                    if (cell.InBounds(map))
                    {
                        buffer.coveredCells.Add(cell);
                    }
                }
            }

            foreach (IntVec3 cell in buffer.candidates)
            {
                if (buffer.coveredCells.Contains(cell))
                {
                    continue;
                }

                map.gasGrid.SetDirect(
                    cell,
                    cell.GasDensity(map, GasType.BlindSmoke),
                    0,
                    cell.GasDensity(map, GasType.RotStink),
                    cell.GasDensity(map, GasType.DeadlifeDust));
                map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Gas);
            }
        }
    }
}
