using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MUGB
{
    public class HediffCompProperties_GoblinGasRecovery : HediffCompProperties
    {
        public float recoveryPerSecond = 0.02f;

        public HediffCompProperties_GoblinGasRecovery()
        {
            compClass = typeof(HediffComp_GoblinGasRecovery);
        }
    }

    public class HediffComp_GoblinGasRecovery : HediffComp
    {
        private int exposedUntilTick;
        private int lastSeverityAppliedTick = -1;
        private int nextVomitTick;

        public HediffCompProperties_GoblinGasRecovery Props => (HediffCompProperties_GoblinGasRecovery)props;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref exposedUntilTick, "exposedUntilTick");
            Scribe_Values.Look(ref lastSeverityAppliedTick, "lastSeverityAppliedTick", -1);
            Scribe_Values.Look(ref nextVomitTick, "nextVomitTick");
        }

        public void MarkExposed(float severityGain, int lingerTicks = 180, float vomitChanceFactor = 1f)
        {
            int currentTick = Find.TickManager.TicksGame;
            exposedUntilTick = Math.Max(exposedUntilTick, currentTick + lingerTicks);
            if (lastSeverityAppliedTick == currentTick)
            {
                return;
            }

            lastSeverityAppliedTick = currentTick;
            parent.Severity = Mathf.Min(parent.def.maxSeverity, parent.Severity + severityGain);
            TryCauseVomit(vomitChanceFactor);
        }

        private void TryCauseVomit(float chanceFactor)
        {
            Pawn pawn = Pawn;
            if (pawn?.jobs == null || pawn.Dead || pawn.Downed || pawn.InMentalState || parent.Severity < 0.35f)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick < nextVomitTick || pawn.CurJobDef == JobDefOf.Vomit)
            {
                return;
            }

            float severityFactor = Mathf.InverseLerp(0.35f, 1f, parent.Severity);
            float chance = Mathf.Clamp01(0.06f * severityFactor * Mathf.Max(0.1f, chanceFactor));
            if (!Rand.Chance(chance))
            {
                return;
            }

            nextVomitTick = currentTick + Rand.RangeInclusive(480, 720);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (Pawn?.IsHashIntervalTick(15) != true)
            {
                return;
            }

            if (Find.TickManager.TicksGame <= exposedUntilTick)
            {
                return;
            }

            parent.Severity = Mathf.Max(0f, parent.Severity - (Props.recoveryPerSecond * 0.25f));
            if (parent.Severity <= 0.001f)
            {
                Pawn.health.RemoveHediff(parent);
            }
        }
    }

    public class HediffCompProperties_GoblinGasPresence : HediffCompProperties
    {
        public int defaultDurationTicks = 60;

        public HediffCompProperties_GoblinGasPresence()
        {
            compClass = typeof(HediffComp_GoblinGasPresence);
        }
    }

    public class HediffComp_GoblinGasPresence : HediffComp
    {
        private int activeUntilTick;

        public HediffCompProperties_GoblinGasPresence Props => (HediffCompProperties_GoblinGasPresence)props;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref activeUntilTick, "activeUntilTick");
        }

        public void Refresh(int durationTicks, float severity)
        {
            activeUntilTick = Math.Max(activeUntilTick, Find.TickManager.TicksGame + durationTicks);
            parent.Severity = Mathf.Clamp(severity, 0.01f, parent.def.maxSeverity > 0f ? parent.def.maxSeverity : 1f);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (Find.TickManager.TicksGame <= activeUntilTick)
            {
                return;
            }

            Pawn?.health?.RemoveHediff(parent);
        }
    }

    public class PawnRenderNodeProperties_GoblinSplatterOverlay : PawnRenderNodeProperties
    {
        public string texPathRoot;
        public int variantCount = 4;
        public float addonScale = 1f;
    }

    public class PawnRenderNodeWorker_GoblinSplatterOverlay : PawnRenderNodeWorker_Overlay
    {
        private static readonly ConditionalWeakTable<Pawn, PawnGoblinSplatterOverlayDrawer> Drawers =
            new ConditionalWeakTable<Pawn, PawnGoblinSplatterOverlayDrawer>();

        private static readonly string[] BileTexturePaths =
        {
            "BileOverlay/BileOverlayA",
            "BileOverlay/BileOverlayB",
            "BileOverlay/BileOverlayC",
            "BileOverlay/BileOverlayD"
        };

        public static void PrecacheGraphics()
        {
            for (int i = 0; i < BileTexturePaths.Length; i++)
            {
                MaterialPool.MatFrom(BileTexturePaths[i], ShaderDatabase.FirefoamOverlay, Color.white);
            }
        }

        protected override PawnOverlayDrawer OverlayDrawer(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            return Drawers.GetValue(pawn, p => new PawnGoblinSplatterOverlayDrawer(p));
        }

        public override bool ShouldListOnGraph(PawnRenderNode node, PawnDrawParms parms)
        {
            return HasSplatter(parms.pawn);
        }

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms)
                && parms.rotDrawMode == RotDrawMode.Fresh
                && HasSplatter(parms.pawn);
        }

        private static bool HasSplatter(Pawn pawn)
        {
            return pawn?.health?.hediffSet != null
                && !pawn.Dead
                && MUGBDefOf.MUGB_CorrosiveGoblinGiblets != null
                && pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_CorrosiveGoblinGiblets);
        }

        public class PawnGoblinSplatterOverlayDrawer : PawnOverlayDrawer
        {
            private const float TextureScaleFactor = 2.8f;
            private const float TextureTiles = 1.4f;
            private const float TextureOffsetVecMagnitude = 2f;

            public PawnGoblinSplatterOverlayDrawer(Pawn pawn)
                : base(pawn)
            {
            }

            protected override void WriteCache(CacheKey key, PawnDrawParms parms, List<DrawCall> writeTarget)
            {
                Rot4 pawnRot = key.pawnRot;
                Mesh bodyMesh = key.bodyMesh;
                OverlayLayer layer = key.layer;
                Graphic graphic = layer == OverlayLayer.Body
                    ? pawn.Drawer.renderer.BodyGraphic
                    : pawn.Drawer.renderer.HeadGraphic;

                if (bodyMesh == null || graphic == null)
                {
                    return;
                }

                Rand.PushState(pawn.thingIDNumber * (int)(layer + 1));
                try
                {
                    bool flipped = (graphic.EastFlipped && pawnRot == Rot4.East)
                        || (graphic.WestFlipped && pawnRot == Rot4.West);

                    int textureIndex = (Rand.Range(0, BileTexturePaths.Length) + pawnRot.AsInt) % BileTexturePaths.Length;
                    Material sourceMat = MaterialPool.MatFrom(BileTexturePaths[textureIndex], ShaderDatabase.FirefoamOverlay, Color.white);
                    Texture bodyTexture = graphic.MatAt(pawnRot).mainTexture;
                    if (sourceMat == null || sourceMat.mainTexture == null || bodyTexture == null)
                    {
                        return;
                    }

                    Mesh overlayMesh = flipped
                        ? MeshPool.GridPlaneFlip(Vector2.one * 0.25f)
                        : MeshPool.GridPlane(Vector2.one * 0.25f);

                    Vector3 bodySize = bodyMesh.bounds.size;
                    float scale = bodySize.magnitude * TextureScaleFactor;

                    MaterialRequest req = default(MaterialRequest);
                    req.maskTex = (Texture2D)bodyTexture;
                    req.mainTex = sourceMat.mainTexture;
                    req.color = sourceMat.color;
                    req.shader = sourceMat.shader;
                    Material material = MaterialPool.MatFrom(req);

                    Vector3 offset = Rand.InsideUnitCircleVec3 * TextureOffsetVecMagnitude;
                    Vector3 overlaySize = overlayMesh.bounds.size * scale;

                    writeTarget.Add(new DrawCall
                    {
                        overlayMat = material,
                        matrix = Matrix4x4.Scale(Vector3.one * scale),
                        overlayMesh = overlayMesh,
                        displayOverApparel = true,
                        maskTexScale = new Vector4(overlaySize.x / bodySize.x, overlaySize.z / bodySize.z),
                        mainTexScale = new Vector4(TextureTiles, TextureTiles, 1f, 1f),
                        mainTexOffset = new Vector4(offset.x, offset.z)
                    });
                }
                finally
                {
                    Rand.PopState();
                }
            }
        }
    }

    public static class GoblinStinkGasUtility
    {
        private const float DefaultGasVisualRadius = 1.7f;

        public static void ApplyLingeringExposure(Pawn pawn, float severityGain)
        {
            if (pawn?.health == null || MUGBDefOf.MUGB_StinkGasExposure == null || severityGain <= 0f)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_StinkGasExposure);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(MUGBDefOf.MUGB_StinkGasExposure, pawn);
                hediff.Severity = 0.1f;
                pawn.health.AddHediff(hediff);
            }

            HediffComp_GoblinGasRecovery comp = hediff.TryGetComp<HediffComp_GoblinGasRecovery>();
            if (comp != null)
            {
                comp.MarkExposed(severityGain);
            }
            else
            {
                hediff.Severity = Mathf.Min(hediff.def.maxSeverity, hediff.Severity + severityGain);
            }
        }

        public static void RefreshClouded(Pawn pawn, float effectScale)
        {
            if (pawn?.health == null || MUGBDefOf.MUGB_StinkGasClouded == null || effectScale <= 0f)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_StinkGasClouded);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(MUGBDefOf.MUGB_StinkGasClouded, pawn);
                pawn.health.AddHediff(hediff);
            }

            float severity = effectScale >= 0.99f ? 1f : 0.2f;
            HediffComp_GoblinGasPresence comp = hediff.TryGetComp<HediffComp_GoblinGasPresence>();
            if (comp != null)
            {
                comp.Refresh(45, severity);
            }
            else
            {
                hediff.Severity = severity;
            }
        }

        public static void ApplyCorrosiveGiblets(Pawn pawn)
        {
            if (pawn?.health == null || MUGBDefOf.MUGB_CorrosiveGoblinGiblets == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(MUGBDefOf.MUGB_CorrosiveGoblinGiblets);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(MUGBDefOf.MUGB_CorrosiveGoblinGiblets, pawn);
                pawn.health.AddHediff(hediff);
            }

            HediffComp_GoblinGasPresence comp = hediff.TryGetComp<HediffComp_GoblinGasPresence>();
            if (comp != null)
            {
                comp.Refresh(1200, 1f);
            }
            else
            {
                hediff.Severity = 1f;
            }
        }

        public static void SpawnVanillaStyleSleepGas(IntVec3 cell, Map map, float radius)
        {
            if (map == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            SpawnClampedSleepGas(cell, map, radius);
        }

        private static void SpawnClampedSleepGas(IntVec3 center, Map map, float radius)
        {
            if (!center.InBounds(map) || !map.gasGrid.GasCanMoveTo(center))
            {
                return;
            }

            map.gasGrid.AddGas(center, GasType.ToxGas, 1, canOverflow: false);
            float radiusSquared = radius * radius;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || !map.gasGrid.GasCanMoveTo(cell))
                {
                    continue;
                }

                float distanceFactor = Mathf.Clamp01(cell.DistanceToSquared(center) / radiusSquared);
                byte density = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 150f, distanceFactor));
                byte currentTox = cell.GasDensity(map, GasType.ToxGas);
                map.gasGrid.SetDirect(
                    cell,
                    cell.GasDensity(map, GasType.BlindSmoke),
                    currentTox > density ? currentTox : density,
                    cell.GasDensity(map, GasType.RotStink),
                    cell.GasDensity(map, GasType.DeadlifeDust));
                map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Gas);
            }
        }

        public static bool TryGetActiveCloudAt(Pawn pawn, out GoblinStinkGasCloud cloud)
        {
            cloud = null;
            if (pawn?.Spawned != true || pawn.Map == null || MUGBDefOf.MUGB_StinkGasCloud == null)
            {
                return false;
            }

            List<Thing> clouds = pawn.Map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_StinkGasCloud);
            if (clouds == null || clouds.Count == 0)
            {
                return false;
            }

            float bestScore = -1f;
            for (int i = 0; i < clouds.Count; i++)
            {
                GoblinStinkGasCloud gasCloud = clouds[i] as GoblinStinkGasCloud;
                if (gasCloud == null || !gasCloud.IsActiveAt(pawn.Position))
                {
                    continue;
                }

                float score = gasCloud.CurrentIntensity * gasCloud.GasPower;
                if (score > bestScore)
                {
                    bestScore = score;
                    cloud = gasCloud;
                }
            }

            return cloud != null;
        }

        public static bool TryGetActiveCloudAt(Map map, IntVec3 cell, out GoblinStinkGasCloud cloud)
        {
            cloud = null;
            if (map == null || !cell.InBounds(map) || MUGBDefOf.MUGB_StinkGasCloud == null)
            {
                return false;
            }

            List<Thing> clouds = map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_StinkGasCloud);
            if (clouds == null || clouds.Count == 0)
            {
                return false;
            }

            float bestScore = -1f;
            for (int i = 0; i < clouds.Count; i++)
            {
                GoblinStinkGasCloud gasCloud = clouds[i] as GoblinStinkGasCloud;
                if (gasCloud == null || !gasCloud.IsActiveAt(cell))
                {
                    continue;
                }

                float score = gasCloud.CurrentIntensity * gasCloud.GasPower;
                if (score > bestScore)
                {
                    bestScore = score;
                    cloud = gasCloud;
                }
            }

            return cloud != null;
        }
    }

    public class Projectile_GoblinStinkbomb : Projectile_Explosive
    {
        private const float RotationDegreesPerTick = 12f;

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float ticksElapsed = Mathf.Max(0f, StartingTicksToImpact - ticksToImpact);
            float spin = ticksElapsed * RotationDegreesPerTick;
            Quaternion rotation = ExactRotation * Quaternion.AngleAxis(spin, Vector3.up);
            Graphics.DrawMesh(ProjectileDrawMeshUtility.MeshFor(def), drawLoc, rotation, DrawMat, 0);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 cell = Position;
            Thing launcherThing = launcher;

            if (map != null)
            {
                DefDatabase<SoundDef>.GetNamedSilentFail("MUGB_GoblinStinkbombImpact")?.PlayOneShot(new TargetInfo(cell, map));
                SpawnImpactFilth(cell, map);
                ApplySplashEffects(cell, map);
                SpawnGasCloud(cell, map, launcherThing as Pawn);
            }

            base.Impact(hitThing, blockedByShield);
        }

        private static void SpawnGasCloud(IntVec3 cell, Map map, Pawn source)
        {
            if (map == null || MUGBDefOf.MUGB_StinkGasCloud == null)
            {
                return;
            }

            GoblinStinkGasUtility.SpawnVanillaStyleSleepGas(cell, map, DefaultGasRadius);
            Thing cloudThing = ThingMaker.MakeThing(MUGBDefOf.MUGB_StinkGasCloud);
            if (cloudThing is GoblinStinkGasCloud cloud)
            {
                GenSpawn.Spawn(cloud, cell, map, WipeMode.Vanish);
                cloud.Initialize(source, radius: DefaultGasRadius);
            }
        }

        private const float DefaultGasRadius = 2.5f;

        private static void SpawnImpactFilth(IntVec3 center, Map map)
        {
            ThingDef corpseBile = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_CorpseBile");
            ThingDef slime = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Slime");

            for (int i = 0; i < 2; i++)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 1);
                if (corpseBile != null)
                {
                    FilthMaker.TryMakeFilth(cell, map, corpseBile, 1);
                }
                if (slime != null)
                {
                    FilthMaker.TryMakeFilth(cell, map, slime, 1);
                }
            }
        }

        private static void ApplySplashEffects(IntVec3 center, Map map)
        {
            List<Thing> thingBuffer = new List<Thing>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 1.5f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                thingBuffer.Clear();
                thingBuffer.AddRange(cell.GetThingList(map));
                for (int i = 0; i < thingBuffer.Count; i++)
                {
                    if (thingBuffer[i] is Pawn pawn && pawn.RaceProps?.IsFlesh == true && !pawn.Dead)
                    {
                        GoblinStinkGasUtility.ApplyCorrosiveGiblets(pawn);
                    }
                }
            }
        }
    }

    public class GoblinStinkGasCloud : ThingWithComps
    {
        private const int DefaultFullDurationTicks = 480;
        private const int DefaultFadeDurationTicks = 120;
        private const float DefaultRadius = 2f;
        private const float GasGridCleanupPadding = 4f;

        private Pawn instigator;
        private int createdTick = -1;
        private float gasPower = 1f;
        private float radius = DefaultRadius;
        private int fullDurationTicks = DefaultFullDurationTicks;
        private int fadeDurationTicks = DefaultFadeDurationTicks;
        private bool goblinsImmune;

        public void Initialize(Pawn source, float gasPower = 1f, float radius = DefaultRadius, int fullDurationTicks = DefaultFullDurationTicks, int fadeDurationTicks = DefaultFadeDurationTicks, bool goblinsImmune = false)
        {
            instigator = source;
            createdTick = Find.TickManager.TicksGame;
            this.gasPower = Mathf.Max(0.01f, gasPower);
            this.radius = Mathf.Max(0.5f, radius);
            this.fullDurationTicks = Mathf.Max(60, fullDurationTicks);
            this.fadeDurationTicks = Mathf.Max(0, fadeDurationTicks);
            this.goblinsImmune = goblinsImmune;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref instigator, "instigator");
            Scribe_Values.Look(ref createdTick, "createdTick", -1);
            Scribe_Values.Look(ref gasPower, "gasPower", 1f);
            Scribe_Values.Look(ref radius, "radius", DefaultRadius);
            Scribe_Values.Look(ref fullDurationTicks, "fullDurationTicks", DefaultFullDurationTicks);
            Scribe_Values.Look(ref fadeDurationTicks, "fadeDurationTicks", DefaultFadeDurationTicks);
            Scribe_Values.Look(ref goblinsImmune, "goblinsImmune");
        }

        protected override void Tick()
        {
            base.Tick();
            if (Map == null)
            {
                return;
            }

            if (createdTick < 0)
            {
                createdTick = Find.TickManager.TicksGame;
            }

            int age = Find.TickManager.TicksGame - createdTick;
            if (age >= fullDurationTicks + fadeDurationTicks)
            {
                CleanupLeakedToxGas();
                Destroy(DestroyMode.Vanish);
                return;
            }

        }

        public override void Print(SectionLayer layer)
        {
            try
            {
                base.Print(layer);
            }
            catch
            {
                // DynamicDraw handles the visual cloud. Suppress section-layer print failures for old saved clouds.
            }
        }

        public float CurrentEffectRadius
        {
            get
            {
                return radius;
            }
        }

        public float CurrentGasGridCoverageRadius => CurrentEffectRadius;

        public float GasGridCleanupScanRadius => Mathf.Max(radius, CurrentEffectRadius) + GasGridCleanupPadding;

        public float CurrentIntensity
        {
            get
            {
                int age = Find.TickManager.TicksGame - createdTick;
                if (age <= fullDurationTicks)
                {
                    return 1f;
                }

                if (fadeDurationTicks <= 0)
                {
                    return 0f;
                }

                float fadeProgress = (age - fullDurationTicks) / (float)fadeDurationTicks;
                return Mathf.Clamp01(1f - fadeProgress);
            }
        }

        public float GasPower => gasPower;

        public bool IsActiveAt(IntVec3 cell)
        {
            if (Map == null || createdTick < 0)
            {
                return false;
            }

            if (!cell.InBounds(Map) || CurrentIntensity <= 0f)
            {
                return false;
            }

            if (goblinsImmune && cell == Position)
            {
                // noop, immunity is checked per pawn. This branch only keeps logic explicit.
            }

            float coverageRadius = CurrentGasGridCoverageRadius;
            return cell.DistanceToSquared(Position) <= coverageRadius * coverageRadius;
        }

        public bool IsPawnAffected(Pawn pawn)
        {
            return pawn != null
                && IsActiveAt(pawn.Position)
                && !(goblinsImmune && GoblinUtility.IsGoblin(pawn));
        }

        public override string LabelMouseover => "MUGB_StinkGasCloudMouseover".Translate(Mathf.RoundToInt(CurrentIntensity * 100f));

        public override string GetInspectString()
        {
            return "MUGB_StinkGasCloudInspect".Translate(Mathf.RoundToInt(CurrentIntensity * 100f), CurrentGasGridCoverageRadius.ToString("F1"));
        }

        public void CleanupLeakedToxGas()
        {
            GoblinStinkGasCleanupUtility.Cleanup(Map);
        }
    }

    public class CompProperties_GoblinPheromonePack : CompProperties
    {
        public float gasPower = 1.2f;
        public float radius = 6f;
        public int fullDurationTicks = 1800;
        public int fadeDurationTicks = 300;
        public int aiCheckIntervalTicks = 120;
        public float aiEnemyRange = 7f;

        public CompProperties_GoblinPheromonePack()
        {
            compClass = typeof(CompGoblinPheromonePack);
        }
    }

    public class CompGoblinPheromonePack : ThingComp
    {
        private const string IconPath = "Things/Apparel/Backpacks/MGB_gasBP/MGB_gasBP_south";
        private const int ReloadGutCount = 3;
        private bool used;

        public CompProperties_GoblinPheromonePack Props => (CompProperties_GoblinPheromonePack)props;

        private Apparel Apparel => parent as Apparel;
        private Pawn Wearer => Apparel?.Wearer;
        public bool Used => used;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref used, "used");
        }

        public override string CompInspectStringExtra()
        {
            return used ? "MUGB_GoblinPheromonePackSpent".Translate() : null;
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra() ?? Enumerable.Empty<Gizmo>())
            {
                yield return gizmo;
            }

            Pawn wearer = Wearer;
            if (wearer == null || wearer.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            if (used)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "MUGB_DeployGoblinPheromonePackLabel".Translate(),
                defaultDesc = "MUGB_DeployGoblinPheromonePackDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get(IconPath, reportFailure: false),
                action = delegate
                {
                    TryDeploy(wearer);
                }
            };
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn wearer = Wearer;
            if (used || wearer?.Spawned != true || wearer.Dead || wearer.Downed || wearer.Faction == Faction.OfPlayer)
            {
                return;
            }

            if (!wearer.IsHashIntervalTick(Mathf.Max(30, Props.aiCheckIntervalTicks)) || !ShouldAIDeploy(wearer))
            {
                return;
            }

            TryDeploy(wearer);
        }

        private bool ShouldAIDeploy(Pawn wearer)
        {
            Map map = wearer.Map;
            if (map == null)
            {
                return false;
            }

            int requiredHostiles = wearer.kindDef?.defName == "MUGB_GoblinKind_TunnelVanguard" ? 1 : 2;
            int hostileCount = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(wearer.Position, Props.aiEnemyRange, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn pawn
                        && pawn.RaceProps?.IsFlesh == true
                        && !pawn.Dead
                        && !pawn.Downed
                        && pawn.HostileTo(wearer)
                        && !GoblinUtility.IsGoblin(pawn)
                        && GenSight.LineOfSight(wearer.Position, pawn.Position, map))
                    {
                        hostileCount++;
                        if (hostileCount >= requiredHostiles)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void TryDeploy(Pawn wearer)
        {
            if (used || wearer?.Spawned != true || MUGBDefOf.MUGB_StinkGasCloud == null)
            {
                return;
            }

            Thing cloudThing = ThingMaker.MakeThing(MUGBDefOf.MUGB_StinkGasCloud);
            if (cloudThing is GoblinStinkGasCloud cloud)
            {
                GoblinStinkGasUtility.SpawnVanillaStyleSleepGas(wearer.Position, wearer.Map, Props.radius);
                GenSpawn.Spawn(cloud, wearer.Position, wearer.Map, WipeMode.Vanish);
                cloud.Initialize(wearer, Props.gasPower, Props.radius, Props.fullDurationTicks, Props.fadeDurationTicks);
                DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_FirefoamPopPack")?.PlayOneShot(new TargetInfo(wearer.Position, wearer.Map));
                used = true;
                Apparel pack = Apparel;
                wearer.apparel?.Remove(pack);
                if (pack != null && !pack.Destroyed)
                {
                    pack.Destroy(DestroyMode.Vanish);
                }
                wearer.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }

        private void TryStartReloadJob(Pawn wearer)
        {
            if (!used || wearer?.Map == null)
            {
                return;
            }

            Thing gut = GenClosest.ClosestThingReachable(
                wearer.Position,
                wearer.Map,
                ThingRequest.ForDef(MUGBDefOf.MUGB_Ggut),
                PathEndMode.Touch,
                TraverseParms.For(wearer, Danger.Deadly, TraverseMode.ByPawn),
                9999f,
                thing => thing.stackCount >= ReloadGutCount && !thing.IsForbidden(wearer) && wearer.CanReserve(thing));

            if (gut == null)
            {
                Messages.Message("MUGB_NoGoblinGutToReloadPack".Translate(ReloadGutCount), wearer, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_ReloadGoblinPheromonePack, gut);
            job.playerForced = true;
            wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public bool TryReloadFrom(Thing gutStack)
        {
            if (!used || gutStack?.def != MUGBDefOf.MUGB_Ggut || gutStack.stackCount < ReloadGutCount)
            {
                return false;
            }

            gutStack.stackCount -= ReloadGutCount;
            if (gutStack.stackCount <= 0 && !gutStack.Destroyed)
            {
                gutStack.Destroy();
            }
            used = false;
            Wearer?.Drawer?.renderer?.SetAllGraphicsDirty();
            return true;
        }
    }

    public class JobDriver_ReloadGoblinPheromonePack : JobDriver
    {
        private const TargetIndex GutInd = TargetIndex.A;

        private CompGoblinPheromonePack PackComp
        {
            get
            {
                List<Apparel> apparel = pawn?.apparel?.WornApparel;
                if (apparel == null)
                {
                    return null;
                }

                for (int i = 0; i < apparel.Count; i++)
                {
                    CompGoblinPheromonePack comp = apparel[i].TryGetComp<CompGoblinPheromonePack>();
                    if (comp?.Used == true)
                    {
                        return comp;
                    }
                }

                return null;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(GutInd), job, 1, 3, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(GutInd);
            this.FailOn(() => PackComp == null);
            yield return Toils_Goto.GotoThing(GutInd, PathEndMode.Touch);

            Toil reload = ToilMaker.MakeToil("ReloadGoblinPheromonePack");
            reload.defaultCompleteMode = ToilCompleteMode.Delay;
            reload.defaultDuration = 120;
            reload.WithProgressBarToilDelay(GutInd);
            yield return reload;

            Toil finish = ToilMaker.MakeToil("FinishReloadGoblinPheromonePack");
            finish.initAction = delegate
            {
                if (PackComp?.TryReloadFrom(job.GetTarget(GutInd).Thing) == true)
                {
                    Messages.Message("MUGB_GoblinPheromonePackReloaded".Translate(), pawn, MessageTypeDefOf.PositiveEvent, historical: false);
                }
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }
}
