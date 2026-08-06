using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class CompProperties_GoblinMirrorable : CompProperties
    {
        public string labelKey = "MUGB_MirrorSculptureLabel";
        public string descKey = "MUGB_MirrorSculptureDesc";

        public CompProperties_GoblinMirrorable()
        {
            compClass = typeof(CompGoblinMirrorable);
        }
    }

    public class CompProperties_GoblinBeaconMirrorable : CompProperties_GoblinMirrorable
    {
        public CompProperties_GoblinBeaconMirrorable()
        {
            compClass = typeof(CompGoblinBeaconMirrorable);
            labelKey = "MUGB_MirrorBeaconLabel";
            descKey = "MUGB_MirrorBeaconDesc";
        }
    }

    [StaticConstructorOnStartup]
    public class CompGoblinMirrorable : ThingComp
    {
        private const string MirrorIconPath = "UI/Icons/MGB_mirroricon";
        private bool mirrored;
        private static Texture2D cachedMirrorIcon;

        public bool Mirrored => mirrored;
        protected CompProperties_GoblinMirrorable Props => (CompProperties_GoblinMirrorable)props;

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref mirrored, "mirrored");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent == null || parent.Destroyed)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = Props.labelKey.Translate(),
                defaultDesc = Props.descKey.Translate(),
                icon = cachedMirrorIcon ??= (ContentFinder<Texture2D>.Get(MirrorIconPath, reportFailure: false) ?? BaseContent.BadTex),
                action = delegate
                {
                    mirrored = !mirrored;
                    if (parent?.Map != null)
                    {
                        parent.DirtyMapMesh(parent.Map);
                    }
                }
            };
        }
    }

    public class CompGoblinBeaconMirrorable : CompGoblinMirrorable
    {
        public override bool DontDrawParent()
        {
            return Mirrored;
        }

        public override void PostPrintOnto(SectionLayer layer)
        {
            base.PostPrintOnto(layer);
            if (!Mirrored || parent?.Graphic == null)
            {
                return;
            }

            Graphic graphic = parent.Graphic;
            Vector2 size = graphic.ShouldDrawRotated
                ? graphic.drawSize
                : (parent.Rotation.IsHorizontal ? graphic.drawSize.Rotated() : graphic.drawSize);
            if (parent.MultipleItemsPerCellDrawn())
            {
                size *= 0.8f;
            }

            float angle = graphic.ShouldDrawRotated
                ? parent.Rotation.AsAngle + graphic.DrawRotatedExtraAngleOffset
                : 0f;
            Vector3 center = parent.TrueCenter() + graphic.DrawOffset(parent.Rotation);
            Printer_Plane.PrintPlane(layer, center, size, graphic.MatAt(parent.Rotation, parent), angle, flipUv: true);
            graphic.ShadowGraphic?.Print(layer, parent, 0f);
        }

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (!Mirrored || parent?.Graphic == null)
            {
                return;
            }

            Graphic graphic = parent.Graphic;
            Vector2 drawSize = graphic.drawSize;
            Matrix4x4 matrix = default;
            matrix.SetTRS(drawLoc, Quaternion.identity, new Vector3(-drawSize.x, 1f, drawSize.y));
            Graphics.DrawMesh(MeshPool.plane10Flip, matrix, graphic.MatAt(parent.Rotation, parent), 0);
            SilhouetteUtility.DrawGraphicSilhouette(parent, drawLoc);
        }
    }

    public class Building_GoblinSculpture : Building_Art
    {
        private CompGoblinMirrorable MirrorComp => GetComp<CompGoblinMirrorable>();

        public override void Print(SectionLayer layer)
        {
            if (MirrorComp?.Mirrored != true)
            {
                base.Print(layer);
                return;
            }

            Graphic graphic = Graphic;
            if (graphic == null)
            {
                base.Print(layer);
                return;
            }

            Vector2 size = graphic.ShouldDrawRotated
                ? graphic.drawSize
                : (Rotation.IsHorizontal ? graphic.drawSize.Rotated() : graphic.drawSize);

            if (this.MultipleItemsPerCellDrawn())
            {
                size *= 0.8f;
            }

            float angle = graphic.ShouldDrawRotated ? Rotation.AsAngle + graphic.DrawRotatedExtraAngleOffset : 0f;
            Vector3 center = this.TrueCenter() + graphic.DrawOffset(Rotation);
            Material material = graphic.MatAt(Rotation, this);
            Printer_Plane.PrintPlane(layer, center, size, material, angle, flipUv: true);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (MirrorComp?.Mirrored != true)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            Graphic graphic = Graphic;
            if (graphic == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            Vector2 drawSize = graphic.drawSize;
            Matrix4x4 matrix = default;
            matrix.SetTRS(drawLoc, Quaternion.identity, new Vector3(-drawSize.x, 1f, drawSize.y));
            Graphics.DrawMesh(MeshPool.plane10Flip, matrix, graphic.MatAt(Rotation, this), 0);
        }
    }

    public class Building_GoblinSculptureGrand : Building_GoblinSculpture
    {
    }

    public class Building_GoblinSculptureSmall : Building_GoblinSculpture
    {
    }
}
