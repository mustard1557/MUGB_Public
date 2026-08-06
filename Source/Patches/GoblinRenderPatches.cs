using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(PawnRenderer), "ParallelPreRenderPawnAt")]
    public static class PawnRenderer_ParallelPreRenderPawnAt_Patch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(PawnRenderer __instance, ref Vector3 drawLoc)
        {
            ApplyGoblinDrawAltitude(__instance, ref drawLoc);
        }

        internal static void ApplyGoblinDrawAltitude(PawnRenderer renderer, ref Vector3 drawLoc)
        {
            Pawn pawn = RendererPawnField.GetValue(renderer) as Pawn;
            if (!GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            drawLoc.y += MUGBMod.Settings?.pawnDrawAltitudeOffset ?? 0f;
        }

        private static readonly FieldInfo RendererPawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    public static class PawnRenderer_RenderPawnAt_Patch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(PawnRenderer __instance, ref Vector3 drawLoc)
        {
            PawnRenderer_ParallelPreRenderPawnAt_Patch.ApplyGoblinDrawAltitude(__instance, ref drawLoc);
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(StatRequest), typeof(bool) })]
    public static class StatWorker_GetValue_Patch
    {
        public static void Postfix(StatWorker __instance, StatRequest req, ref float __result)
        {
            Pawn pawn = req.Pawn ?? req.Thing as Pawn;
            if (!GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            string statName = (StatField.GetValue(__instance) as StatDef)?.defName;
            if (statName == "VEF_CosmeticBodySize_Multiplier")
            {
                // 한국어 의도: 청소년 축소는 렌더 노드 오프셋을 직접 곱하지 않고 VEF 코스메틱 스탯으로 건다.
                // 머리장비/몸의류/부속은 각각 Head·Body의 자식 노드라, 노드마다 비율을 곱하면 부모 변환과
                // 겹쳐 R제곱으로 줄어든다. VEF 크기 스탯은 폰 전체를 일관되게 스케일하므로 그 문제가 없다.
                // 비율 계산은 이 스탯에서만 필요하므로 여기 안에서만 구한다.
                __result *= settings.bodyScale * GoblinUtility.JuvenileRenderScaleFor(pawn);
            }
            else if (statName == "VEF_HeadSize_Cosmetic")
            {
                // 한국어 의도: 머리에는 비율을 곱하지 않는다. VEF_CosmeticBodySize_Multiplier가 이미
                // 폰 전체(머리 포함)를 줄이므로 여기서 또 곱하면 머리만 R제곱으로 작아진다.
                __result *= settings.headScale;
            }
        }

        private static readonly FieldInfo StatField = AccessTools.Field(typeof(StatWorker), "stat");
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.OffsetFor))]
    public static class PawnRenderNodeWorker_OffsetFor_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (!GoblinRenderNodeUtility.TryGetTuningNode(node, out RenderTuningNodeKind nodeKind, out string category, out Apparel apparel))
            {
                return;
            }

            Pawn pawn = parms.pawn;
            MUGBSettings settings = GoblinRenderNodeUtility.Settings;
            if (GoblinUtility.IsGoblin(pawn))
            {
                ApplyGoblinOffset(nodeKind, category, apparel, parms, settings, ref __result);
                return;
            }

            if (!GoblinUtility.IsSupportedTuningPawn(pawn, isGoblin: false))
            {
                return;
            }

            string formKey = GoblinUtility.NonGoblinTuningProfileKey(pawn);
            if (nodeKind == RenderTuningNodeKind.Head || nodeKind == RenderTuningNodeKind.HeadApparel)
            {
                string partKey = GoblinRenderNodeUtility.FormPartKey(formKey, head: true);
                MUGBSettings.RenderTuningValues partTuning = settings.GetDirectionalRuntimeValues(partKey, parms.facing);
                __result.x += partTuning.OffsetX;
                __result.z += partTuning.OffsetY;
            }
            else if (nodeKind == RenderTuningNodeKind.Body || nodeKind == RenderTuningNodeKind.BodyApparel)
            {
                string partKey = GoblinRenderNodeUtility.FormPartKey(formKey, head: false);
                MUGBSettings.RenderTuningValues partTuning = settings.GetDirectionalRuntimeValues(partKey, parms.facing);
                __result.x += partTuning.OffsetX;
                __result.z += partTuning.OffsetY;
            }

            if (apparel != null)
            {
                MUGBSettings.RenderTuningValues apparelTuning = settings.GetRenderTargetRuntimeValues(category, apparel.def, formKey, parms.facing);
                __result.x += apparelTuning.OffsetX;
                __result.z += apparelTuning.OffsetY;
            }
        }

        private static void ApplyGoblinOffset(RenderTuningNodeKind nodeKind, string category, Apparel apparel, PawnDrawParms parms, MUGBSettings settings, ref Vector3 result)
        {
            Pawn pawn = parms.pawn;
            string formKey = GoblinUtility.GoblinVisualTuningFormKey(pawn);
            string apparelFormKey = GoblinUtility.IsHobgoblin(pawn) ? "Hobgoblin" : "Goblin";
            if (nodeKind == RenderTuningNodeKind.Head)
            {
                formKey = GoblinRenderNodeUtility.HeadBodyFormKey(pawn, parms);
                string partKey = GoblinRenderNodeUtility.FormPartKey(formKey, head: true);
                Vector2 gbrOffset = HeadOffsetFor(pawn, parms.facing);
                MUGBSettings.RenderTuningValues tuning = settings.GetCombinedDirectionalRuntimeValues("Head", partKey, parms.facing);
                result.x += gbrOffset.x + settings.headHorizontalOffset + tuning.OffsetX;
                result.z += gbrOffset.y + settings.headVerticalOffset + tuning.OffsetY;
            }
            else if (settings.moveHeadgearWithHead && nodeKind == RenderTuningNodeKind.HeadApparel)
            {
                string partKey = GoblinRenderNodeUtility.FormPartKey(formKey, head: true);
                Vector2 gbrOffset = HeadOffsetFor(pawn, parms.facing);
                MUGBSettings.RenderTuningValues tuning = settings.GetCombinedDirectionalRuntimeValues("Head", partKey, parms.facing);
                result.x += gbrOffset.x + settings.headHorizontalOffset + tuning.OffsetX;
                result.z += gbrOffset.y + settings.headVerticalOffset + tuning.OffsetY;
            }
            else if (nodeKind == RenderTuningNodeKind.Body)
            {
                formKey = GoblinRenderNodeUtility.HeadBodyFormKey(pawn, parms);
                string partKey = GoblinRenderNodeUtility.FormPartKey(formKey, head: false);
                MUGBSettings.RenderTuningValues tuning = settings.GetCombinedDirectionalRuntimeValues("Body", partKey, parms.facing);
                result.x += settings.bodyHorizontalOffset + tuning.OffsetX;
                result.z += settings.bodyVerticalOffset + tuning.OffsetY;
            }

            if (apparel != null)
            {
                MUGBSettings.RenderTuningValues apparelTuning = settings.GetRenderTargetRuntimeValues(category, apparel.def, apparelFormKey, parms.facing);
                result.x += apparelTuning.OffsetX;
                result.z += apparelTuning.OffsetY;
            }
        }

        private static Vector2 HeadOffsetFor(Pawn pawn, Rot4 facing)
        {
            bool hobgoblin = GoblinUtility.IsHobgoblin(pawn);
            float bodyScale = hobgoblin ? 1.1f : 0.8f;
            float juvenileFactor = GoblinUtility.JuvenileHeadOffsetFactor(pawn);
            Vector2 offset;
            if (facing == Rot4.North)
            {
                offset = hobgoblin ? new Vector2(0f, 0.25f) : new Vector2(0f, 0.18f);
                return offset * bodyScale * juvenileFactor;
            }

            if (facing == Rot4.East)
            {
                offset = hobgoblin ? new Vector2(0.09f, 0.28f) : new Vector2(0.0135f, 0.235f);
                return offset * bodyScale * juvenileFactor;
            }

            if (facing == Rot4.West)
            {
                offset = hobgoblin ? new Vector2(-0.09f, 0.28f) : new Vector2(0.0135f, 0.235f);
                return offset * bodyScale * juvenileFactor;
            }

            offset = hobgoblin ? new Vector2(0f, 0.28f) : new Vector2(0f, 0.2f);
            return offset * bodyScale * juvenileFactor;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    public static class PawnRenderNodeWorker_ScaleFor_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (!GoblinRenderNodeUtility.TryGetTuningNode(node, out RenderTuningNodeKind nodeKind, out string category, out Apparel apparel))
            {
                return;
            }

            Pawn pawn = parms.pawn;
            MUGBSettings settings = GoblinRenderNodeUtility.Settings;
            if (GoblinUtility.IsGoblin(pawn))
            {
                ApplyGoblinScale(nodeKind, category, apparel, parms, settings, ref __result);
                return;
            }

            if (!GoblinUtility.IsSupportedTuningPawn(pawn, isGoblin: false))
            {
                return;
            }

            string formKey = GoblinUtility.NonGoblinTuningProfileKey(pawn);
            if (nodeKind == RenderTuningNodeKind.Head || nodeKind == RenderTuningNodeKind.HeadApparel)
            {
                __result *= settings.GetDirectionalRuntimeValues(GoblinRenderNodeUtility.FormPartKey(formKey, head: true), parms.facing).Scale;
            }
            else if (nodeKind == RenderTuningNodeKind.Body || nodeKind == RenderTuningNodeKind.BodyApparel)
            {
                __result *= settings.GetDirectionalRuntimeValues(GoblinRenderNodeUtility.FormPartKey(formKey, head: false), parms.facing).Scale;
            }

            if (apparel != null)
            {
                __result *= settings.GetRenderTargetRuntimeValues(category, apparel.def, formKey, parms.facing).Scale;
            }
        }

        private static void ApplyGoblinScale(RenderTuningNodeKind nodeKind, string category, Apparel apparel, PawnDrawParms parms, MUGBSettings settings, ref Vector3 result)
        {
            string formKey = GoblinUtility.GoblinVisualTuningFormKey(parms.pawn);
            string apparelFormKey = GoblinUtility.IsHobgoblin(parms.pawn) ? "Hobgoblin" : "Goblin";
            if (nodeKind == RenderTuningNodeKind.Head)
            {
                formKey = GoblinRenderNodeUtility.HeadBodyFormKey(parms.pawn, parms);
                float scale = settings.GetCombinedDirectionalRuntimeValues("Head", GoblinRenderNodeUtility.FormPartKey(formKey, head: true), parms.facing).Scale;
                result *= scale * settings.goblinGlobalRenderScale;
            }
            else if (nodeKind == RenderTuningNodeKind.HeadApparel)
            {
                float scale = settings.GetCombinedDirectionalRuntimeValues("Head", GoblinRenderNodeUtility.FormPartKey(formKey, head: true), parms.facing).Scale;
                result *= scale * settings.goblinGlobalRenderScale;
            }
            else if (nodeKind == RenderTuningNodeKind.Body)
            {
                formKey = GoblinRenderNodeUtility.HeadBodyFormKey(parms.pawn, parms);
                float scale = settings.GetCombinedDirectionalRuntimeValues("Body", GoblinRenderNodeUtility.FormPartKey(formKey, head: false), parms.facing).Scale;
                result *= scale * settings.goblinGlobalRenderScale;
            }
            else if (nodeKind == RenderTuningNodeKind.BodyApparel)
            {
                float scale = settings.GetCombinedDirectionalRuntimeValues("Body", GoblinRenderNodeUtility.FormPartKey(formKey, head: false), parms.facing).Scale;
                result *= scale * settings.goblinGlobalRenderScale;
            }

            if (apparel != null)
            {
                result *= settings.GetRenderTargetRuntimeValues(category, apparel.def, apparelFormKey, parms.facing).Scale;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.CanDrawNow))]
    public static class PawnRenderNodeWorker_CanDrawNow_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (!__result || MUGBMod.Settings?.forceGoblinBaldAndNoBeard != true || !GoblinUtility.IsGoblin(parms.pawn))
            {
                return;
            }

            if (node is PawnRenderNode_Hair || node is PawnRenderNode_Beard)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.LayerFor))]
    public static class PawnRenderNodeWorker_LayerFor_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref float __result)
        {
            if (!GoblinRenderNodeUtility.TryGetTuningNode(node, out RenderTuningNodeKind nodeKind, out string category, out Apparel apparel))
            {
                return;
            }

            Pawn pawn = parms.pawn;
            MUGBSettings settings = GoblinRenderNodeUtility.Settings;
            if (GoblinUtility.IsGoblin(pawn))
            {
                ApplyGoblinLayer(nodeKind, category, apparel, parms, settings, ref __result);
                return;
            }

            if (!GoblinUtility.IsSupportedTuningPawn(pawn, isGoblin: false))
            {
                return;
            }

            string formKey = GoblinUtility.NonGoblinTuningProfileKey(pawn);
            if (nodeKind == RenderTuningNodeKind.Head || nodeKind == RenderTuningNodeKind.HeadApparel)
            {
                __result += settings.GetDirectionalRuntimeValues(GoblinRenderNodeUtility.FormPartKey(formKey, head: true), parms.facing).Layer;
            }
            else if (nodeKind == RenderTuningNodeKind.Body || nodeKind == RenderTuningNodeKind.BodyApparel)
            {
                __result += settings.GetDirectionalRuntimeValues(GoblinRenderNodeUtility.FormPartKey(formKey, head: false), parms.facing).Layer;
            }

            if (apparel != null)
            {
                __result += settings.GetRenderTargetRuntimeValues(category, apparel.def, formKey, parms.facing).Layer;
            }
        }

        private static void ApplyGoblinLayer(RenderTuningNodeKind nodeKind, string category, Apparel apparel, PawnDrawParms parms, MUGBSettings settings, ref float result)
        {
            string formKey = GoblinUtility.GoblinVisualTuningFormKey(parms.pawn);
            string apparelFormKey = GoblinUtility.IsHobgoblin(parms.pawn) ? "Hobgoblin" : "Goblin";
            if (nodeKind == RenderTuningNodeKind.Head)
            {
                formKey = GoblinRenderNodeUtility.HeadBodyFormKey(parms.pawn, parms);
                result += settings.GetCombinedDirectionalRuntimeValues("Head", GoblinRenderNodeUtility.FormPartKey(formKey, head: true), parms.facing).Layer;
            }
            else if (settings.moveHeadgearWithHead && nodeKind == RenderTuningNodeKind.HeadApparel)
            {
                result += settings.GetCombinedDirectionalRuntimeValues("Head", GoblinRenderNodeUtility.FormPartKey(formKey, head: true), parms.facing).Layer;
            }
            else if (nodeKind == RenderTuningNodeKind.Body)
            {
                formKey = GoblinRenderNodeUtility.HeadBodyFormKey(parms.pawn, parms);
                result += settings.GetCombinedDirectionalRuntimeValues("Body", GoblinRenderNodeUtility.FormPartKey(formKey, head: false), parms.facing).Layer;
            }
            else if (nodeKind == RenderTuningNodeKind.BodyApparel)
            {
                result += settings.GetCombinedDirectionalRuntimeValues("Body", GoblinRenderNodeUtility.FormPartKey(formKey, head: false), parms.facing).Layer;
            }

            if (apparel != null)
            {
                result += settings.GetRenderTargetRuntimeValues(category, apparel.def, apparelFormKey, parms.facing).Layer;
            }
        }

    }

    internal enum RenderTuningNodeKind : byte
    {
        None,
        Head,
        Body,
        HeadApparel,
        BodyApparel,
        ShieldApparel
    }

    public static class GoblinRenderNodeUtility
    {
        private static readonly MUGBSettings FallbackSettings = new MUGBSettings();
        private static Dictionary<ThingDef, ApparelClassification> apparelClassifications = new Dictionary<ThingDef, ApparelClassification>();

        internal static MUGBSettings Settings => MUGBMod.Settings ?? FallbackSettings;

        internal static bool TryGetTuningNode(PawnRenderNode node, out RenderTuningNodeKind nodeKind, out string category, out Apparel apparel)
        {
            category = null;
            apparel = null;
            if (node is PawnRenderNode_Head)
            {
                nodeKind = RenderTuningNodeKind.Head;
                return true;
            }
            if (node is PawnRenderNode_Body)
            {
                nodeKind = RenderTuningNodeKind.Body;
                return true;
            }
            if (!TryGetApparelCategory(node, out category, out apparel))
            {
                nodeKind = RenderTuningNodeKind.None;
                return false;
            }

            nodeKind = category == "Headgear"
                ? RenderTuningNodeKind.HeadApparel
                : category == "Shield"
                    ? RenderTuningNodeKind.ShieldApparel
                    : RenderTuningNodeKind.BodyApparel;
            return true;
        }

        internal static void InitializeApparelClassificationCache()
        {
            Dictionary<ThingDef, ApparelClassification> classifications = new Dictionary<ThingDef, ApparelClassification>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.IsApparel == true || def?.apparel != null || def?.defName == "Apparel_WarVeil")
                {
                    classifications[def] = ClassifyApparel(def);
                }
            }

            apparelClassifications = classifications;
        }

        public static string FormPartKey(string formKey, bool head)
        {
            switch (formKey)
            {
                case "Goblin": return head ? "Goblin.Head" : "Goblin.Body";
                case "GoblinCrossEyed": return head ? "GoblinCrossEyed.Head" : "GoblinCrossEyed.Body";
                case "GoblinChild": return head ? "GoblinChild.Head" : "GoblinChild.Body";
                case "GoblinCrossEyedChild": return head ? "GoblinCrossEyedChild.Head" : "GoblinCrossEyedChild.Body";
                case "GoblinDessicated": return head ? "GoblinDessicated.Head" : "GoblinDessicated.Body";
                case "Hobgoblin": return head ? "Hobgoblin.Head" : "Hobgoblin.Body";
                case "HobgoblinCrossEyed": return head ? "HobgoblinCrossEyed.Head" : "HobgoblinCrossEyed.Body";
                case "HobgoblinChild": return head ? "HobgoblinChild.Head" : "HobgoblinChild.Body";
                case "HobgoblinCrossEyedChild": return head ? "HobgoblinCrossEyedChild.Head" : "HobgoblinCrossEyedChild.Body";
                case "HobgoblinDessicated": return head ? "HobgoblinDessicated.Head" : "HobgoblinDessicated.Body";
                case "Race_Human": return head ? "Race_Human.Head" : "Race_Human.Body";
                default: return formKey + (head ? ".Head" : ".Body");
            }
        }

        public static string HeadBodyFormKey(Pawn pawn, PawnDrawParms parms)
        {
            if (parms.rotDrawMode == RotDrawMode.Dessicated || pawn?.Drawer?.renderer?.CurRotDrawMode == RotDrawMode.Dessicated)
            {
                return GoblinUtility.IsHobgoblin(pawn) ? "HobgoblinDessicated" : "GoblinDessicated";
            }

            return GoblinUtility.GoblinVisualTuningFormKey(pawn);
        }

        public static bool IsHeadApparelNode(PawnRenderNode node)
        {
            PawnRenderNode_Apparel apparelNode = node as PawnRenderNode_Apparel;
            Apparel apparel = apparelNode?.apparel;
            return IsHeadApparelDef(apparel?.def);
        }

        public static bool IsBodyApparelNode(PawnRenderNode node)
        {
            PawnRenderNode_Apparel apparelNode = node as PawnRenderNode_Apparel;
            Apparel apparel = apparelNode?.apparel;
            return IsBodyApparelDef(apparel?.def);
        }

        public static bool TryGetApparelCategory(PawnRenderNode node, out string category, out Apparel apparel)
        {
            category = null;
            PawnRenderNode_Apparel apparelNode = node as PawnRenderNode_Apparel;
            apparel = apparelNode?.apparel;
            ThingDef def = apparel?.def;
            if (def == null)
            {
                return false;
            }

            ApparelClassification classification = TryGetApparelClassification(def, out ApparelClassification cached)
                ? cached
                : ClassifyApparel(def);
            if (classification.IsHead)
            {
                category = "Headgear";
                return true;
            }

            if (classification.IsShield)
            {
                category = "Shield";
                return true;
            }

            if (classification.IsBody)
            {
                category = classification.Category;
                return true;
            }

            return false;
        }

        public static bool IsHeadApparelDef(ThingDef def)
        {
            if (TryGetApparelClassification(def, out ApparelClassification classification))
            {
                return classification.IsHead;
            }

            return ComputeIsHeadApparelDef(def);
        }

        private static bool ComputeIsHeadApparelDef(ThingDef def)
        {
            if (def?.defName == "Apparel_WarVeil")
            {
                return true;
            }

            if (def?.apparel?.bodyPartGroups == null)
            {
                return false;
            }

            foreach (BodyPartGroupDef group in def.apparel.bodyPartGroups)
            {
                string defName = group?.defName;
                if (defName == "FullHead" || defName == "UpperHead" || defName == "Eyes")
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsBodyApparelDef(ThingDef def)
        {
            if (TryGetApparelClassification(def, out ApparelClassification classification))
            {
                return classification.IsBody;
            }

            return def?.IsApparel == true && def.apparel != null && !ComputeIsHeadApparelDef(def) && !ComputeIsShieldApparelDef(def);
        }

        public static string TuningCategoryFor(ThingDef def)
        {
            if (TryGetApparelClassification(def, out ApparelClassification classification))
            {
                return classification.Category;
            }

            return ClassifyApparel(def).Category;
        }

        public static bool MatchesTuningFilter(ThingDef def, string category)
        {
            if (def?.IsApparel != true || def.apparel == null)
            {
                return false;
            }

            ApparelClassification classification = TryGetApparelClassification(def, out ApparelClassification cached)
                ? cached
                : ClassifyApparel(def);

            if (category == "Headgear")
            {
                return classification.IsHead;
            }
            if (category == "Shield")
            {
                return classification.IsShield;
            }
            if (classification.IsHead)
            {
                return false;
            }
            if (classification.IsShield)
            {
                return false;
            }

            bool utility = classification.IsUtility;
            bool armor = classification.IsArmor;
            bool outerwear = classification.IsOuterwear;
            if (category == "Utility")
            {
                return utility;
            }
            if (category == "Armor")
            {
                return !utility && armor;
            }
            if (category == "Outerwear")
            {
                return !utility && !armor && outerwear;
            }
            return category == "Clothing" && !utility && !armor && !outerwear;
        }

        public static bool IsShieldApparelDef(ThingDef def)
        {
            if (TryGetApparelClassification(def, out ApparelClassification classification))
            {
                return classification.IsShield;
            }

            return ComputeIsShieldApparelDef(def);
        }

        private static bool ComputeIsShieldApparelDef(ThingDef def)
        {
            if (def?.apparel?.tags == null)
            {
                return false;
            }

            foreach (string tag in def.apparel.tags)
            {
                if (tag == "MUGB_GoblinShield")
                {
                    return true;
                }
            }
            return false;
        }

        private static ApparelClassification ClassifyApparel(ThingDef def)
        {
            bool head = ComputeIsHeadApparelDef(def);
            bool shield = ComputeIsShieldApparelDef(def);
            bool body = def?.IsApparel == true && def.apparel != null && !head && !shield;
            bool utility = def?.apparel?.layers?.Contains(ApparelLayerDefOf.Belt) == true;
            bool armor = ComputeIsArmorApparel(def);
            bool outerwear = def?.apparel?.layers?.Contains(ApparelLayerDefOf.Shell) == true;
            string category = head ? "Headgear" : shield ? "Shield" : utility ? "Utility" : "Apparel";
            return new ApparelClassification(head, shield, body, utility, armor, outerwear, category);
        }

        private static bool TryGetApparelClassification(ThingDef def, out ApparelClassification classification)
        {
            classification = default;
            return def != null && apparelClassifications.TryGetValue(def, out classification);
        }

        private static bool ComputeIsArmorApparel(ThingDef def)
        {
            if (def?.defName?.IndexOf("Armor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (def?.apparel?.tags == null)
            {
                return false;
            }
            foreach (string tag in def.apparel.tags)
            {
                if (tag?.IndexOf("Armor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly struct ApparelClassification
        {
            public ApparelClassification(bool isHead, bool isShield, bool isBody, bool isUtility, bool isArmor, bool isOuterwear, string category)
            {
                IsHead = isHead;
                IsShield = isShield;
                IsBody = isBody;
                IsUtility = isUtility;
                IsArmor = isArmor;
                IsOuterwear = isOuterwear;
                Category = category;
            }

            public bool IsHead { get; }
            public bool IsShield { get; }
            public bool IsBody { get; }
            public bool IsUtility { get; }
            public bool IsArmor { get; }
            public bool IsOuterwear { get; }
            public string Category { get; }
        }
    }

    [StaticConstructorOnStartup]
    public static class GoblinRenderOptimizationCache
    {
        static GoblinRenderOptimizationCache()
        {
            LongEventHandler.ExecuteWhenFinished(GoblinRenderNodeUtility.InitializeApparelClassificationCache);
        }
    }

    [StaticConstructorOnStartup]
    public static class PawnRenderNodeApparelPheromonePackGraphicUtility
    {
        private const string EmptyPackWornGraphicPath = "Things/Apparel/Backpacks/MGB_gasBP/MGB_gasBPempty";
        private static Graphic emptyPackGraphic;

        public static bool TryGetEmptyPackGraphic(PawnRenderNode_Apparel node, out Graphic graphic)
        {
            graphic = null;
            Apparel apparel = node?.apparel;
            if (apparel?.def != MUGBDefOf.MUGB_Apparel_GoblinPheromonePack)
            {
                return false;
            }

            CompGoblinPheromonePack comp = apparel.TryGetComp<CompGoblinPheromonePack>();
            if (comp?.Used != true)
            {
                return false;
            }

            graphic = emptyPackGraphic ?? (emptyPackGraphic = GraphicDatabase.Get<Graphic_Multi>(EmptyPackWornGraphicPath, ShaderDatabase.Cutout, Vector2.one, Color.white));
            return true;
        }
    }

    [HarmonyPatch]
    public static class PawnRenderNodeApparel_GraphicFor_PheromonePackPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnRenderNode_Apparel), "GraphicFor", new[] { typeof(Pawn) });
        }

        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static void Postfix(PawnRenderNode_Apparel __instance, Pawn pawn, ref Graphic __result)
        {
            if (PawnRenderNodeApparelPheromonePackGraphicUtility.TryGetEmptyPackGraphic(__instance, out Graphic graphic))
            {
                __result = graphic;
            }
        }
    }

    [HarmonyPatch]
    public static class PawnRenderNodeApparel_GraphicsFor_PheromonePackPatch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnRenderNode_Apparel), "GraphicsFor", new[] { typeof(Pawn) });
        }

        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static void Postfix(PawnRenderNode_Apparel __instance, Pawn pawn, ref IEnumerable<Graphic> __result)
        {
            if (PawnRenderNodeApparelPheromonePackGraphicUtility.TryGetEmptyPackGraphic(__instance, out Graphic graphic))
            {
                __result = new[] { graphic };
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode_Body), nameof(PawnRenderNode_Body.GraphicFor))]
    public static class PawnRenderNodeBody_GraphicFor_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderNode_Body __instance, Pawn pawn, ref Graphic __result)
        {
            if (!GoblinUtility.IsGoblin(pawn) || pawn.story == null)
            {
                return;
            }

            string path = pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated
                ? GoblinUtility.DesiredDessicatedBodyGraphicPath(pawn)
                : GoblinUtility.DesiredBodyGraphicPath(pawn);
            Shader shader = __instance.ShaderFor(pawn);
            if (shader == null)
            {
                return;
            }

            // 고블린 몸은 바닐라 bodyType을 유지한 채 유전자 렌더 단계에서만 전용 텍스처로 교체합니다.
            RotDrawMode rotDrawMode = pawn.Drawer.renderer.CurRotDrawMode;
            bool dessicated = rotDrawMode == RotDrawMode.Dessicated;
            bool useTextureColor = MUGBMod.Settings?.useTextureColors == true && rotDrawMode == RotDrawMode.Fresh;
            if (useTextureColor || dessicated)
            {
                shader = ShaderDatabase.Cutout;
            }
            Color color = useTextureColor || dessicated ? Color.white : __instance.ColorFor(pawn);
            __result = GraphicDatabase.Get<Graphic_Multi>(path, shader, Vector2.one, color);
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode_Head), nameof(PawnRenderNode_Head.GraphicFor))]
    public static class PawnRenderNodeHead_GraphicFor_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderNode_Head __instance, Pawn pawn, ref Graphic __result)
        {
            if (!GoblinUtility.IsGoblin(pawn) || pawn.story == null || !pawn.health.hediffSet.HasHead)
            {
                return;
            }

            // Character Editor처럼 중간 렌더를 자주 요청하는 모드에서도 인간 머리가 남지 않도록 렌더 결과를 직접 교체합니다.
            RotDrawMode rotDrawMode = pawn.Drawer.renderer.CurRotDrawMode;
            bool dessicated = rotDrawMode == RotDrawMode.Dessicated;
            bool useTextureColor = MUGBMod.Settings?.useTextureColors == true && rotDrawMode == RotDrawMode.Fresh;
            Shader shader = useTextureColor || dessicated ? ShaderDatabase.Cutout : __instance.ShaderFor(pawn);
            Color color = useTextureColor || dessicated ? Color.white : __instance.ColorFor(pawn);
            string path = dessicated
                ? "Things/Pawn/MGBlike/Heads/Dessicated/Skull"
                : "Things/Pawn/MGBlike/Heads/MGB_Head1";
            __result = GraphicDatabase.Get<Graphic_Multi>(path, shader, Vector2.one, color);
        }
    }
}
