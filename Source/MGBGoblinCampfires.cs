using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB
{
    [StaticConstructorOnStartup]
    public class Building_GoblinCampfire : Building_WorkTable
    {
        private static readonly Graphic FireGraphic = GraphicDatabase.Get<Graphic_Flicker>(
            "Things/Special/Fire",
            ShaderDatabase.TransparentPostLight,
            Vector2.one * 0.85f,
            Color.white);

        private int graphicVariant = -1;
        private Graphic variantGraphic;

        private bool DrawFireBehind => def.defName == "MUGB_fireplace";
        private const float FireplaceFireAltitudeOffset = Altitudes.AltInc * 0.25f;
        private const float FireplaceBodyAltitudeOffset = Altitudes.AltInc * 0.5f;

        public override Graphic Graphic
        {
            get
            {
                EnsureVariant();
                variantGraphic ??= GraphicDatabase.Get<Graphic_Single>(
                    def.graphicData.texPath + (graphicVariant == 1 ? "_b" : string.Empty),
                    ShaderDatabase.Cutout,
                    def.graphicData.drawSize,
                    Color.white,
                    Color.white,
                    def.graphicData);
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
            Scribe_Values.Look(ref graphicVariant, "graphicVariant", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureVariant();
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            bool drawFire = GetComp<CompRefuelable>()?.HasFuel ?? true;
            if (DrawFireBehind && drawFire)
            {
                Vector3 firePos = drawLoc + new Vector3(0.1f, FireplaceFireAltitudeOffset, 0.33f);
                FireGraphic.Draw(firePos, Rotation, this);
            }

            Vector3 bodyDrawLoc = drawLoc;
            if (DrawFireBehind)
            {
                bodyDrawLoc.y += FireplaceBodyAltitudeOffset;
            }
            base.DrawAt(bodyDrawLoc, flip);

            if (!DrawFireBehind && drawFire)
            {
                Vector3 firePos = drawLoc + new Vector3(0.1f, 0.01f, 0.25f);
                FireGraphic.Draw(firePos, Rotation, this);
            }
        }

        private void EnsureVariant()
        {
            if (graphicVariant < 0)
            {
                graphicVariant = Rand.Bool ? 1 : 0;
            }
        }
    }
}
