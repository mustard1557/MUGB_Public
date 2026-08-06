using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.ShouldAddNodeToTree))]
    public static class PawnRenderTree_GoblinGeneAppearancePatch
    {
        public static bool Prefix(PawnRenderNodeProperties props, Pawn ___pawn, ref bool __result)
        {
            if (!GoblinBiotechAppearanceUtility.IsForeignGeneRenderNode(props)
                || !GoblinUtility.HasGoblinCoreMarker(___pawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    public static class GoblinBiotechAppearanceUtility
    {
        private static HashSet<PawnRenderNodeProperties> foreignGeneRenderNodes;

        public static void Initialize()
        {
            if (foreignGeneRenderNodes != null)
            {
                return;
            }

            HashSet<PawnRenderNodeProperties> nodes = new HashSet<PawnRenderNodeProperties>();
            foreach (GeneDef geneDef in DefDatabase<GeneDef>.AllDefsListForReading)
            {
                if (geneDef == null
                    || geneDef.modContentPack == MUGBMod.ContentPack
                    || !geneDef.HasDefinedGraphicProperties)
                {
                    continue;
                }

                foreach (PawnRenderNodeProperties props in geneDef.RenderNodeProperties)
                {
                    if (props != null)
                    {
                        nodes.Add(props);
                    }
                }
            }

            foreignGeneRenderNodes = nodes;
        }

        public static bool IsForeignGeneRenderNode(PawnRenderNodeProperties props)
        {
            if (props == null)
            {
                return false;
            }

            Initialize();
            return foreignGeneRenderNodes.Contains(props);
        }

        public static bool HasForeignSkinAppearanceGene(Pawn pawn)
        {
            if (pawn?.genes == null)
            {
                return false;
            }

            foreach (Gene gene in pawn.genes.GenesListForReading)
            {
                GeneDef def = gene?.def;
                if (gene?.Active == true
                    && def != null
                    && def != MUGBDefOf.MUGB_Gene_GoblinCore
                    && def != MUGBDefOf.MUGB_Gene_HobgoblinFrame
                    && (def.skinColorOverride.HasValue || def.skinIsHairColor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
