using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.AllPossibleRequirements))]
    public static class PreceptRole_AllPossibleRequirements_GoblinApparelPatch
    {
        private static readonly string[] GoblinRoleApparelDefNames =
        {
            "MUGB_Apparel_CultSkinMask",
            "MUGB_Apparel_HumanHideCapeB",
            "MUGB_Apparel_HumanHideMantle"
        };

        public static void Postfix(Ideo ideo, PreceptDef def, ref List<PreceptApparelRequirement> __result)
        {
            if (ideo?.memes == null || def == null ||
                (def.defName != "IdeoRole_Leader" && def.defName != "IdeoRole_Moralist") ||
                !ideo.memes.Any(meme => meme.defName == "MUGB_ChildrenOfBlinia" || meme.defName == "MUGB_GoblinSupremacy"))
            {
                return;
            }

            __result ??= new List<PreceptApparelRequirement>();
            for (int i = GoblinRoleApparelDefNames.Length - 1; i >= 0; i--)
            {
                ThingDef apparelDef = DefDatabase<ThingDef>.GetNamedSilentFail(GoblinRoleApparelDefNames[i]);
                if (apparelDef?.apparel == null ||
                    __result.Any(candidate => candidate?.requirement?.requiredDefs?.Contains(apparelDef) == true))
                {
                    continue;
                }

                __result.Insert(0, new PreceptApparelRequirement
                {
                    requirement = new ApparelRequirement
                    {
                        bodyPartGroupsMatchAny = new List<BodyPartGroupDef>(apparelDef.apparel.bodyPartGroups),
                        requiredDefs = new List<ThingDef> { apparelDef }
                    }
                });
            }
        }
    }
}
