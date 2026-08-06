using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class CompProperties_GoblinWeaponVariant : CompProperties
    {
        public List<string> texPaths;

        public CompProperties_GoblinWeaponVariant()
        {
            compClass = typeof(CompGoblinWeaponVariant);
        }
    }

    public class CompGoblinWeaponVariant : ThingComp
    {
        private int variantIndex = -1;
        private Graphic variantGraphic;
        private string cachedTexPath;

        public CompProperties_GoblinWeaponVariant Props => (CompProperties_GoblinWeaponVariant)props;

        public int VariantIndex
        {
            get
            {
                EnsureVariant();
                return variantIndex;
            }
        }

        public string TexPath
        {
            get
            {
                List<string> texPaths = Props.texPaths;
                if (texPaths == null || texPaths.Count == 0)
                {
                    return parent?.def?.graphicData?.texPath;
                }

                int index = VariantIndex;
                if (index < 0 || index >= texPaths.Count)
                {
                    index = 0;
                }

                return texPaths[index];
            }
        }

        public Graphic VariantGraphic
        {
            get
            {
                string texPath = TexPath;
                if (string.IsNullOrEmpty(texPath))
                {
                    return null;
                }

                if (variantGraphic == null || cachedTexPath != texPath)
                {
                    GraphicData graphicData = parent?.def?.graphicData;
                    Graphic baseGraphic = graphicData?.Graphic;
                    Shader shader = baseGraphic?.Shader ?? ShaderDatabase.Cutout;
                    Vector2 drawSize = baseGraphic?.drawSize ?? graphicData?.drawSize ?? Vector2.one;
                    Color color = parent?.DrawColor ?? baseGraphic?.Color ?? Color.white;
                    Color colorTwo = parent?.DrawColorTwo ?? baseGraphic?.ColorTwo ?? Color.white;
                    variantGraphic = GraphicDatabase.Get<Graphic_Single>(texPath, shader, drawSize, color, colorTwo, graphicData);
                    cachedTexPath = texPath;
                }

                return variantGraphic;
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            RerollVariant();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref variantIndex, "mugbWeaponVariantIndex", -1);
        }

        public void RerollVariant()
        {
            int count = Props.texPaths?.Count ?? 0;
            variantIndex = count > 0 ? Rand.Range(0, count) : -1;
            variantGraphic = null;
            cachedTexPath = null;
        }

        private void EnsureVariant()
        {
            int count = Props.texPaths?.Count ?? 0;
            if (count <= 0)
            {
                variantIndex = -1;
                return;
            }

            if (variantIndex < 0 || variantIndex >= count)
            {
                RerollVariant();
            }
        }
    }

    public class ThingWithComps_GoblinWeaponVariant : ThingWithComps
    {
        public override Graphic Graphic
        {
            get
            {
                Graphic specialGraphic = this.TryGetComp<CompMUGBSpecialWeapon>()?.SpecialGraphic;
                if (specialGraphic != null)
                {
                    return specialGraphic;
                }
                Graphic variantGraphic = this.TryGetComp<CompGoblinWeaponVariant>()?.VariantGraphic;
                return variantGraphic ?? base.Graphic;
            }
        }
    }

    public class Graphic_GoblinWeaponVariant : Graphic_Single
    {
        private readonly Dictionary<string, Graphic> variantGraphics = new Dictionary<string, Graphic>();

        public override Material MatSingleFor(Thing thing)
        {
            Graphic graphic = GraphicFor(thing);
            return graphic == this ? base.MatSingleFor(thing) : graphic.MatSingleFor(thing);
        }

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            Graphic graphic = GraphicFor(thing);
            return graphic == this ? base.MatAt(rot, thing) : graphic.MatAt(rot, thing);
        }

        private Graphic GraphicFor(Thing thing)
        {
            CompGoblinWeaponVariant comp = thing?.TryGetComp<CompGoblinWeaponVariant>();
            string texPath = comp?.TexPath;
            if (string.IsNullOrEmpty(texPath))
            {
                return this;
            }

            if (!variantGraphics.TryGetValue(texPath, out Graphic graphic) || graphic == null)
            {
                graphic = GraphicDatabase.Get<Graphic_Single>(texPath, Shader, drawSize, color, colorTwo, data);
                variantGraphics[texPath] = graphic;
            }

            return graphic;
        }
    }
}
