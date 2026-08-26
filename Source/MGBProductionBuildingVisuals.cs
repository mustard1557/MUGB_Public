using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MUGB
{
    public class MUGBProductionWorkMapComponent : MapComponent
    {
        private readonly HashSet<Thing> activeBillGivers = new HashSet<Thing>();
        private int lastRefreshTick = int.MinValue;

        public MUGBProductionWorkMapComponent(Map map)
            : base(map)
        {
        }

        public bool IsPawnWorkingAt(Thing billGiver)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (lastRefreshTick != currentTick)
            {
                Refresh(currentTick);
            }

            return billGiver != null && activeBillGivers.Contains(billGiver);
        }

        private void Refresh(int currentTick)
        {
            lastRefreshTick = currentTick;
            activeBillGivers.Clear();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!(pawn.jobs?.curDriver is JobDriver_DoBill) || pawn.CurJob == null)
                {
                    continue;
                }

                Thing target = pawn.CurJob.GetTarget(JobDriver_DoBill.BillGiverInd).Thing;
                if (target != null)
                {
                    activeBillGivers.Add(target);
                }
            }
        }
    }

    public static class MUGBProductionWorkUtility
    {
        public static bool IsPawnWorkingAt(Thing billGiver)
        {
            Map map = billGiver?.Map;
            if (map == null)
            {
                return false;
            }

            MUGBProductionWorkMapComponent component = map.GetComponent<MUGBProductionWorkMapComponent>();
            if (component != null)
            {
                return component.IsPawnWorkingAt(billGiver);
            }

            // MapComponent 생성에 실패한 비정상 환경에서도 기존 동작을 유지한다.
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.jobs?.curDriver is JobDriver_DoBill
                    && pawn.CurJob?.GetTarget(JobDriver_DoBill.BillGiverInd).Thing == billGiver)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class CompProperties_GoblinFuelGlower : CompProperties_Glower
    {
        public bool glowOnlyWithFuel = true;
        public int updateIntervalTicks = 60;

        public CompProperties_GoblinFuelGlower()
        {
            compClass = typeof(CompGoblinFuelGlower);
        }
    }

    public class CompGoblinFuelGlower : CompGlower
    {
        private int nextUpdateTick;

        private CompProperties_GoblinFuelGlower GoblinProps => (CompProperties_GoblinFuelGlower)props;

        protected override bool ShouldBeLitNow
        {
            get
            {
                return base.ShouldBeLitNow && (!GoblinProps.glowOnlyWithFuel || HasFuel());
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || Find.TickManager.TicksGame < nextUpdateTick)
            {
                return;
            }

            nextUpdateTick = Find.TickManager.TicksGame + Math.Max(GoblinProps.updateIntervalTicks, 1);
            UpdateLit(parent.Map);
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            if (parent.Spawned)
            {
                UpdateLit(parent.Map);
            }
        }

        private bool HasFuel()
        {
            CompRefuelable refuelable = parent.TryGetComp<CompRefuelable>();
            return refuelable == null || refuelable.Fuel > 0f;
        }
    }

    public class CompProperties_GoblinBuildingFireVisual : CompProperties
    {
        public float fireSize = 1f;
        public bool fireOnlyWhenWorking = true;
        public int workCheckIntervalTicks = 60;
        public bool throwSmoke;
        public int smokeIntervalTicks = 45;
        public float smokeSize = 0.8f;
        public Vector3 smokeOffset = Vector3.zero;
        public Vector3 southOffset = Vector3.zero;
        public Vector3 northOffset = Vector3.zero;
        public Vector3 eastOffset = Vector3.zero;
        public Vector3 westOffset = Vector3.zero;

        public CompProperties_GoblinBuildingFireVisual()
        {
            compClass = typeof(CompGoblinBuildingFireVisual);
        }
    }

    public class CompProperties_GoblinDirectionalFireVisual : CompProperties
    {
        public float fireSize = 1f;
        public float northFireSize = -1f;
        public float southFireSize = -1f;
        public float eastFireSize = -1f;
        public float westFireSize = -1f;
        public bool fireOnlyWhenWorking;
        public int workCheckIntervalTicks = 60;
        public bool drawFireOnlySouth;

        // ko-KR 의도: 옆면 그림이 앞뒤 그림과 같은 파일인 건물을 위한 표시입니다.
        //
        // 솥단지 텍스처는 동쪽이 남쪽과, 서쪽이 북쪽과 완전히 같은 파일입니다.
        // 그래서 회전이 동/서라도 화면에 보이는 그림은 남/북이고, 불도 그 그림을
        // 기준으로 그려야 맞습니다. 이 값을 켜면 동쪽을 남쪽으로, 서쪽을 북쪽으로
        // 보고 불의 표시 여부와 크기, 위치를 정합니다.
        //
        // 네 방향 그림이 전부 다른 건물에서는 꺼 두어야 하므로 기본값은 꺼짐입니다.
        public bool sideTexturesMirrorFrontBack;
        public Vector3 offset = Vector3.zero;
        public Vector3 northOffset = Vector3.zero;
        public Vector3 southOffset = Vector3.zero;
        public Vector3 eastOffset = Vector3.zero;
        public Vector3 westOffset = Vector3.zero;

        public CompProperties_GoblinDirectionalFireVisual()
        {
            compClass = typeof(CompGoblinDirectionalFireVisual);
        }
    }

    public class CompGoblinDirectionalFireVisual : ThingComp
    {
        private readonly Dictionary<float, Graphic> fireGraphicsBySize = new Dictionary<float, Graphic>();
        private CompFlickable flickableComp;
        private bool pawnWorkingHere;
        private int nextWorkCheckTick;

        private CompProperties_GoblinDirectionalFireVisual Props => (CompProperties_GoblinDirectionalFireVisual)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickableComp = parent.GetComp<CompFlickable>();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!Props.fireOnlyWhenWorking || Find.TickManager.TicksGame < nextWorkCheckTick)
            {
                return;
            }

            nextWorkCheckTick = Find.TickManager.TicksGame + Math.Max(Props.workCheckIntervalTicks, 1);
            pawnWorkingHere = HasPawnWorkingHere();
        }

        // ko-KR 의도: 저장된 회전이 아니라 "화면에 실제로 나오는 그림"을 돌려줍니다.
        //
        // 옆면 그림이 앞뒤 그림과 같은 파일인 건물은 동쪽으로 놓아도 남쪽 그림이,
        // 서쪽으로 놓아도 북쪽 그림이 나옵니다. 불은 그 그림 위에 얹는 것이므로
        // 회전이 아니라 이 값을 기준으로 삼아야 불이 사라지거나 엉뚱한 자리에 뜨지 않습니다.
        private Rot4 VisualRotation
        {
            get
            {
                Rot4 rotation = parent.Rotation;
                if (!Props.sideTexturesMirrorFrontBack)
                {
                    return rotation;
                }

                if (rotation == Rot4.East)
                {
                    return Rot4.South;
                }

                if (rotation == Rot4.West)
                {
                    return Rot4.North;
                }

                return rotation;
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (!ShouldDrawFire())
            {
                return;
            }

            // The stewpot art uses a north/south texture convention opposite to the in-game
            // visual facing, so "south-only" fire maps to Rot4.North.
            Rot4 visualRotation = VisualRotation;
            if (Props.drawFireOnlySouth && visualRotation != Rot4.North)
            {
                return;
            }

            float fireSize = FireSizeFor(visualRotation);
            Vector3 drawPos = parent.DrawPos + OffsetFor(visualRotation);
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            FireGraphicFor(fireSize).Draw(drawPos, Rot4.North, parent);
        }

        private Graphic FireGraphicFor(float fireSize)
        {
            if (!fireGraphicsBySize.TryGetValue(fireSize, out Graphic graphic))
            {
                graphic = GraphicDatabase.Get<Graphic_GoblinFixedSizeFlicker>(
                    "Things/Special/Fire",
                    ShaderDatabase.TransparentPostLight,
                    new Vector2(fireSize, fireSize),
                    Color.white);
                fireGraphicsBySize[fireSize] = graphic;
            }

            return graphic;
        }

        private bool ShouldDrawFire()
        {
            if (!HasFuel() || flickableComp?.SwitchIsOn == false)
            {
                return false;
            }

            return !Props.fireOnlyWhenWorking || pawnWorkingHere;
        }

        private bool HasFuel()
        {
            CompRefuelable refuelable = parent.TryGetComp<CompRefuelable>();
            return refuelable == null || refuelable.Fuel > 0f;
        }

        private bool HasPawnWorkingHere()
        {
            return MUGBProductionWorkUtility.IsPawnWorkingAt(parent);
        }

        private float FireSizeFor(Rot4 rot)
        {
            if (rot == Rot4.North && Props.northFireSize > 0f)
            {
                return Props.northFireSize;
            }

            if (rot == Rot4.East && Props.eastFireSize > 0f)
            {
                return Props.eastFireSize;
            }

            if (rot == Rot4.West && Props.westFireSize > 0f)
            {
                return Props.westFireSize;
            }

            if (rot == Rot4.South && Props.southFireSize > 0f)
            {
                return Props.southFireSize;
            }

            return Props.fireSize;
        }

        private Vector3 OffsetFor(Rot4 rot)
        {
            if (rot == Rot4.North)
            {
                return Props.offset + Props.northOffset;
            }

            if (rot == Rot4.East)
            {
                return Props.offset + Props.eastOffset;
            }

            if (rot == Rot4.West)
            {
                return Props.offset + Props.westOffset;
            }

            return Props.offset + Props.southOffset;
        }
    }

    public class Graphic_GoblinFixedSizeFlicker : Graphic_Flicker
    {
        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
        {
            if (thingDef == null)
            {
                Log.ErrorOnce("MUGB fixed-size fire draw with null thingDef: " + loc, 926061601);
                return;
            }

            if (subGraphics == null)
            {
                Log.ErrorOnce("MUGB fixed-size fire has no subgraphics " + thingDef, 926061602);
                return;
            }

            int ticks = Find.TickManager.TicksGame;
            if (thing != null)
            {
                ticks += Math.Abs(thing.thingIDNumber ^ 0x80FD52);
            }

            int frameTick = ticks / 15;
            int index = Math.Abs(frameTick ^ ((thing?.thingIDNumber ?? 0) * 391)) % subGraphics.Length;
            if (index < 0 || index >= subGraphics.Length)
            {
                Log.ErrorOnce("MUGB fixed-size fire drawing out of range: " + index, 926061603);
                index = 0;
            }

            float fireSize = Mathf.Max(drawSize.x, 0.01f);
            Vector3 jitter = GenRadial.RadialPattern[frameTick % GenRadial.RadialPattern.Length].ToVector3() / GenRadial.MaxRadialPatternRadius;
            jitter *= 0.05f;
            Vector3 pos = loc + jitter * fireSize;
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(pos, Quaternion.identity, new Vector3(fireSize, 1f, fireSize));
            Graphics.DrawMesh(MeshPool.plane10, matrix, subGraphics[index].MatSingle, 0);
        }
    }

    public class CompGoblinBuildingFireVisual : ThingComp
    {
        private Graphic fireGraphic;
        private CompFlickable flickableComp;
        private bool pawnWorkingHere;
        private int nextWorkCheckTick;
        private int nextSmokeTick;

        private CompProperties_GoblinBuildingFireVisual Props => (CompProperties_GoblinBuildingFireVisual)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            flickableComp = parent.GetComp<CompFlickable>();
        }

        private Graphic FireGraphic
        {
            get
            {
                if (fireGraphic == null)
                {
                    fireGraphic = GraphicDatabase.Get<Graphic_Flicker>(
                        "Things/Special/Fire",
                        ShaderDatabase.TransparentPostLight,
                        new Vector2(Props.fireSize, Props.fireSize),
                        Color.white);
                }

                return fireGraphic;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!Props.fireOnlyWhenWorking || Find.TickManager.TicksGame < nextWorkCheckTick)
            {
                return;
            }

            nextWorkCheckTick = Find.TickManager.TicksGame + Math.Max(Props.workCheckIntervalTicks, 1);
            pawnWorkingHere = HasPawnWorkingHere();
            TryThrowSmoke();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (!ShouldDrawFire())
            {
                return;
            }

            Vector3 drawPos = parent.DrawPos + OffsetFor(parent.Rotation);
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            FireGraphic.Draw(drawPos, Rot4.North, parent);
        }

        private bool ShouldDrawFire()
        {
            if (!HasFuel() || flickableComp?.SwitchIsOn == false)
            {
                return false;
            }

            return !Props.fireOnlyWhenWorking || pawnWorkingHere;
        }

        private bool HasFuel()
        {
            CompRefuelable refuelable = parent.TryGetComp<CompRefuelable>();
            return refuelable == null || refuelable.Fuel > 0f;
        }

        private bool HasPawnWorkingHere()
        {
            return MUGBProductionWorkUtility.IsPawnWorkingAt(parent);
        }

        private Vector3 OffsetFor(Rot4 rot)
        {
            if (rot == Rot4.North)
            {
                return Props.northOffset;
            }

            if (rot == Rot4.East)
            {
                return Props.eastOffset;
            }

            if (rot == Rot4.West)
            {
                return Props.westOffset;
            }

            return Props.southOffset;
        }

        private void TryThrowSmoke()
        {
            if (!Props.throwSmoke || !pawnWorkingHere || !HasFuel() || flickableComp?.SwitchIsOn == false || parent.Map == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame < nextSmokeTick)
            {
                return;
            }

            nextSmokeTick = ticksGame + Math.Max(Props.smokeIntervalTicks, 1);
            Vector3 smokePos = parent.DrawPos + OffsetFor(parent.Rotation) + Props.smokeOffset;
            smokePos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            FleckMaker.ThrowSmoke(smokePos, parent.Map, Props.smokeSize);
        }
    }
}
