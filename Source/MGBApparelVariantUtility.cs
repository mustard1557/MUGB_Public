using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace MUGB
{
    public class CompProperties_GoblinApparelVariant : CompProperties
    {
        public List<string> texPaths;
        public List<string> wornGraphicPaths;

        public CompProperties_GoblinApparelVariant()
        {
            compClass = typeof(CompGoblinApparelVariant);
        }
    }

    public class CompGoblinApparelVariant : ThingComp
    {
        private int variantIndex = -1;

        public CompProperties_GoblinApparelVariant Props => (CompProperties_GoblinApparelVariant)props;

        public int VariantIndex
        {
            get
            {
                EnsureVariant();
                return variantIndex;
            }
        }

        public string TexPath => PathAt(Props.texPaths);

        public string WornGraphicPath => PathAt(Props.wornGraphicPaths);

        public override void PostPostMake()
        {
            base.PostPostMake();
            RerollVariant();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref variantIndex, "mugbApparelVariantIndex", -1);
        }

        private string PathAt(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return null;
            }

            int index = VariantIndex;
            if (index < 0 || index >= paths.Count)
            {
                index = 0;
            }

            return paths[index];
        }

        private void RerollVariant()
        {
            int count = VariantCount;
            variantIndex = count > 0 ? Rand.Range(0, count) : -1;
        }

        private void EnsureVariant()
        {
            int count = VariantCount;
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

        private int VariantCount
        {
            get
            {
                int texCount = Props.texPaths?.Count ?? 0;
                int wornCount = Props.wornGraphicPaths?.Count ?? 0;
                return Mathf.Max(texCount, wornCount);
            }
        }
    }

    public class Graphic_GoblinApparelVariant : Graphic_Single
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
            string texPath = thing?.TryGetComp<CompGoblinApparelVariant>()?.TexPath;
            if (texPath.NullOrEmpty())
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

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.WornGraphicPath), MethodType.Getter)]
    public static class Apparel_WornGraphicPath_GoblinVariantPatch
    {
        public static void Postfix(Apparel __instance, ref string __result)
        {
            if (__instance?.def?.defName != "MUGB_Apparel_GoblinWarBanner")
            {
                return;
            }

            string variantPath = __instance.TryGetComp<CompGoblinApparelVariant>()?.WornGraphicPath;
            if (!variantPath.NullOrEmpty())
            {
                __result = variantPath;
            }
        }
    }

    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class ApparelGraphicRecordGetter_TryGetGraphicApparel_GoblinBannerMaskPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Apparel apparel, BodyTypeDef bodyType, bool forStatue, ref ApparelGraphicRecord rec, ref bool __result)
        {
            if (apparel?.def?.defName != "MUGB_Apparel_GoblinWarBanner" || apparel.WornGraphicPath.NullOrEmpty())
            {
                return;
            }

            string path = apparel.WornGraphicPath;
            Shader shader = forStatue ? ShaderDatabase.Cutout : ShaderDatabase.CutoutComplex;
            Graphic graphic = GraphicDatabase.Get<Graphic_Multi>(path, shader, apparel.def.graphicData.drawSize, apparel.DrawColor);
            rec = new ApparelGraphicRecord(graphic, apparel);
            __result = true;
        }
    }
}
