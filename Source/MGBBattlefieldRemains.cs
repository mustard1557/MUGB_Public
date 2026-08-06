using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MUGB
{
    public sealed class Building_GoblinWarRemains : Building
    {
        private const int VariantCount = 12;
        private const int LargeVariant = 6;
        private static readonly Vector2 NormalDrawSize = new Vector2(1.45f, 1.45f);
        private static readonly Vector2 LargeDrawSize = new Vector2(1.75f, 1.75f);

        private int graphicVariant;
        private Graphic variantGraphic;

        public override Graphic Graphic
        {
            get
            {
                EnsureVariant();
                if (variantGraphic == null)
                {
                    Vector2 drawSize = graphicVariant == LargeVariant ? LargeDrawSize : NormalDrawSize;
                    variantGraphic = GraphicDatabase.Get<Graphic_Single>(
                        $"Things/Building/Ruins/GoblinBattlefieldRemains/MGB_Battleremain_{graphicVariant}",
                        ShaderDatabase.Cutout,
                        drawSize,
                        Color.white,
                        Color.white,
                        def.graphicData);
                }

                return variantGraphic;
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            EnsureVariant();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref graphicVariant, "graphicVariant", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureVariant();
                variantGraphic = null;
            }
        }

        private void EnsureVariant()
        {
            if (graphicVariant < 1 || graphicVariant > VariantCount)
            {
                graphicVariant = Rand.RangeInclusive(1, VariantCount);
            }
        }
    }

    public sealed class GenStep_GoblinBattlefieldRemains : GenStep
    {
        private const int ClusterRadius = 14;
        private const int MinObjectSpacingSquared = 4;

        public override int SeedPart => 184706231;

        public override void Generate(Map map, GenStepParams parms)
        {
            ThingDef remainsDef = DefDatabase<ThingDef>.GetNamedSilentFail("MUGB_GoblinWarRemains");
            if (map == null || remainsDef == null)
            {
                return;
            }

            List<IntVec3> placed = new List<IntVec3>();
            ThingDef medievalFootman = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_AncientFootman");
            if (medievalFootman != null)
            {
                foreach (Thing footman in map.listerThings.ThingsOfDef(medievalFootman).ToList())
                {
                    if (footman?.Spawned == true && Rand.Chance(0.45f))
                    {
                        TryPlaceNear(footman.Position, map, remainsDef, placed, 3, 8);
                        if (Rand.Chance(0.25f))
                        {
                            TryPlaceNear(footman.Position, map, remainsDef, placed, 3, 9);
                        }
                    }
                }
            }

            float mapUnits = map.Size.x * map.Size.z / 10000f;
            int independentClusters = Mathf.Clamp(Mathf.RoundToInt(mapUnits * Rand.Range(0.12f, 0.24f)), 2, 3);
            for (int i = 0; i < independentClusters; i++)
            {
                if (!TryFindClusterCenter(map, placed, out IntVec3 center))
                {
                    continue;
                }

                int count = Rand.RangeInclusive(5, 9);
                for (int j = 0; j < count; j++)
                {
                    TryPlaceNear(center, map, remainsDef, placed, 0, ClusterRadius);
                }

                ScatterRubble(center, map);
            }
        }

        private static bool TryFindClusterCenter(Map map, List<IntVec3> placed, out IntVec3 result)
        {
            for (int i = 0; i < 80; i++)
            {
                IntVec3 cell = CellFinder.RandomCell(map);
                if (ValidCell(cell, map) && placed.All(other => other.DistanceToSquared(cell) >= 64 * 64))
                {
                    result = cell;
                    return true;
                }
            }

            result = IntVec3.Invalid;
            return false;
        }

        private static bool TryPlaceNear(IntVec3 center, Map map, ThingDef def, List<IntVec3> placed, int minRadius, int maxRadius)
        {
            List<IntVec3> candidates = GenRadial.RadialCellsAround(center, maxRadius, true)
                .Where(cell => cell.DistanceToSquared(center) >= minRadius * minRadius
                    && ValidCell(cell, map)
                    && placed.All(other => other.DistanceToSquared(cell) >= MinObjectSpacingSquared))
                .InRandomOrder()
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            IntVec3 chosen = candidates[0];
            GenSpawn.Spawn(ThingMaker.MakeThing(def), chosen, map, Rot4.Random);
            placed.Add(chosen);
            return true;
        }

        private static bool ValidCell(IntVec3 cell, Map map)
        {
            return cell.InBounds(map)
                && cell.Standable(map)
                && !map.roofGrid.Roofed(cell)
                && cell.GetTerrain(map)?.IsWater != true
                && cell.GetEdifice(map) == null
                && !cell.GetThingList(map).Any(thing => thing.def.category == ThingCategory.Building);
        }

        private static void ScatterRubble(IntVec3 center, Map map)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, ClusterRadius, true))
            {
                if (cell.InBounds(map) && cell.Standable(map) && Rand.Chance(0.025f))
                {
                    FilthMaker.TryMakeFilth(cell, map, ThingDefOf.Filth_RubbleBuilding);
                }
            }
        }
    }
}
