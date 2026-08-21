using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class PawnRenderNodeProperties_GoblinAddon : PawnRenderNodeProperties
    {
        public string bodyPartDefName;
        public string bodyPartSide;
        public string tuningKey;
        public bool drawSouth = true;
        public bool drawNorth;
        public bool drawEast;
        public bool drawWest;
        public bool mirrorEastForWest = true;
        public float addonScale = 1f;
        public Vector2 thinSouthOffset;
        public Vector2 thinNorthOffset;
        public Vector2 thinEastOffset;
        public Vector2 thinWestOffset;
        public Vector2 maleSouthOffset;
        public Vector2 maleNorthOffset;
        public Vector2 maleEastOffset;
        public Vector2 maleWestOffset;
    }

    public class PawnRenderNode_GoblinAddon : PawnRenderNode_AttachmentHead
    {
        public PawnRenderNode_GoblinAddon(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Mesh GetMesh(PawnDrawParms parms)
        {
            if (meshSet == null)
            {
                return null;
            }

            // 바닐라 Graphic_Multi 규칙을 따른다.
            // west 텍스처가 없을 때만 Graphic.WestFlipped가 켜지므로, 별도 west 텍스처가 있는 얼굴 부속물은 다시 뒤집지 않는다.
            Vector2 size = MeshPool.GetMetaData(meshSet.MeshAt(parms.facing)).size;
            Graphic graphic = PrimaryGraphic;
            bool flip = graphic != null && ((parms.facing == Rot4.West && graphic.WestFlipped) || (parms.facing == Rot4.East && graphic.EastFlipped));
            if (FlipGraphic(parms))
            {
                flip = !flip;
            }

            return MeshPool.GridPlane(size, flip);
        }
    }

    public static class GoblinClosedEyeUtility
    {
        private const string LeftClosedPath = "Things/Pawn/Addons/Eyes/MGB_EyeLeft1 closed";
        private const string RightClosedPath = "Things/Pawn/Addons/Eyes/MGB_EyeRight1 closed";
        private const int DeadClosedEyeSeed = 0x4D474245;

        private static readonly Dictionary<string, Graphic> ClosedEyeGraphics = new Dictionary<string, Graphic>();

        public static void ScheduleInitialize()
        {
            LongEventHandler.ExecuteWhenFinished(Initialize);
        }

        private static void Initialize()
        {
            CacheClosedEyeGraphic("EyeLeft", LeftClosedPath);
            CacheClosedEyeGraphic("EyeRight", RightClosedPath);
        }

        public static bool TryGetClosedEyeGraphic(Pawn pawn, string tuningKey, Rot4 facing, out Graphic graphic)
        {
            graphic = null;
            if (!ShouldUseClosedEyes(pawn) || !ClosedTextureExistsFor(tuningKey, facing))
            {
                return false;
            }

            return ClosedEyeGraphics.TryGetValue(tuningKey, out graphic) && graphic != null;
        }

        private static void CacheClosedEyeGraphic(string tuningKey, string path)
        {
            if (ClosedEyeGraphics.ContainsKey(tuningKey))
            {
                return;
            }

            ClosedEyeGraphics[tuningKey] = GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, Vector2.one, Color.white);
        }

        private static bool ClosedTextureExistsFor(string tuningKey, Rot4 facing)
        {
            if (tuningKey == "EyeLeft")
            {
                return facing == Rot4.South || facing == Rot4.West;
            }

            if (tuningKey == "EyeRight")
            {
                return facing == Rot4.South || facing == Rot4.East;
            }

            return false;
        }

        private static bool ShouldUseClosedEyes(Pawn pawn)
        {
            if (!GoblinUtility.IsGoblin(pawn))
            {
                return false;
            }

            if (pawn.Dead)
            {
                return DeadPawnGetsClosedEyes(pawn);
            }

            if (pawn.health?.capacities?.CanBeAwake == false)
            {
                return DeadPawnGetsClosedEyes(pawn);
            }

            return pawn.jobs?.curDriver?.asleep == true;
        }

        private static bool DeadPawnGetsClosedEyes(Pawn pawn)
        {
            return Gen.HashCombineInt(pawn.thingIDNumber, DeadClosedEyeSeed) % 100 < 50;
        }
    }

    public static class GoblinGoneAddonUtility
    {
        private static readonly Dictionary<string, Graphic> GoneGraphics = new Dictionary<string, Graphic>();

        private static readonly Dictionary<string, string> GoneGraphicPaths = new Dictionary<string, string>
        {
            { "EarLeft", "Things/Pawn/Addons/Ears/Base/MGB_EarLeft gone" },
            { "EarRight", "Things/Pawn/Addons/Ears/Base/MGB_EarRight gone" },
            { "EyeLeft", "Things/Pawn/Addons/Eyes/MGB_EyeLeft1 gone" },
            { "EyeRight", "Things/Pawn/Addons/Eyes/MGB_EyeRight1 gone" },
            { "Nose", "Things/Pawn/Addons/Nose/MGB_Nose1 gone" }
        };

        public static void ScheduleInitialize()
        {
            LongEventHandler.ExecuteWhenFinished(Initialize);
        }

        private static void Initialize()
        {
            foreach (KeyValuePair<string, string> entry in GoneGraphicPaths)
            {
                if (!GoneGraphics.ContainsKey(entry.Key))
                {
                    GoneGraphics[entry.Key] = GraphicDatabase.Get<Graphic_Multi>(
                        entry.Value,
                        ShaderDatabase.Cutout,
                        Vector2.one,
                        Color.white);
                }
            }
        }

        public static bool TryGetGoneGraphic(string tuningKey, Rot4 facing, out Graphic graphic)
        {
            graphic = null;
            if (!GoneTextureExistsFor(tuningKey, facing))
            {
                return false;
            }

            return GoneGraphics.TryGetValue(tuningKey, out graphic) && graphic != null;
        }

        public static bool GoneTextureExistsFor(string tuningKey, Rot4 facing)
        {
            if (tuningKey == "EarLeft" || tuningKey == "EyeLeft")
            {
                return facing == Rot4.South || facing == Rot4.West;
            }

            if (tuningKey == "EarRight" || tuningKey == "EyeRight")
            {
                return facing == Rot4.South || facing == Rot4.East;
            }

            if (tuningKey == "Nose")
            {
                return true;
            }

            return false;
        }
    }

    public class PawnRenderNodeWorker_GoblinAddon : PawnRenderNodeWorker_FlipWhenCrawling
    {
        private static readonly HashSet<string> EarHidingHeadgearDefNames = new HashSet<string>
        {
            "Apparel_AdvancedHelmet",
            "Apparel_HatHood",
            "Apparel_Headwrap",
            "Apparel_Hijab",
            "Apparel_CultistHood",
            "Apparel_CeremonialCultistHood",
            "Apparel_CeremonialCultistMask",
            "Apparel_PowerArmorHelmet",
            "Apparel_VacsuitHelmet",
            "MUGB_Apparel_WarHelmetA",
            "MUGB_Apparel_WarHelmetB",
            "MUGB_Apparel_KettleHelmetA",
            "MUGB_Apparel_CrudeHelmetB"
        };

        [ThreadStatic]
        private static bool queryingHeadgearVisibility;

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNodeProperties_GoblinAddon props = node.Props as PawnRenderNodeProperties_GoblinAddon;
            if (AddonPartIsMissing(parms.pawn, props) && GoblinGoneAddonUtility.TryGetGoneGraphic(props.tuningKey, parms.facing, out Graphic goneGraphic))
            {
                return goneGraphic;
            }

            if (props != null && GoblinClosedEyeUtility.TryGetClosedEyeGraphic(parms.pawn, props.tuningKey, parms.facing, out Graphic closedEyeGraphic))
            {
                return closedEyeGraphic;
            }

            return base.GetGraphic(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            PawnRenderNodeProperties_GoblinAddon props = node.Props as PawnRenderNodeProperties_GoblinAddon;
            if (props == null)
            {
                return offset;
            }

            Vector2 gbrOffset = OffsetForBodyAndFacing(props, parms);
            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            string formKey = GoblinUtility.GoblinVisualTuningFormKey(parms.pawn);
            Vector2 fineTune = settings.OffsetForAddon(props.tuningKey, parms.facing, formKey);
            float juvenileFactor = GoblinUtility.JuvenileAddonOffsetFactor(parms.pawn);
            // 다른 모드가 인간 머리 크기를 줄이면 머리와 부속 텍스처는 메시 단계에서 같이 줄지만
            // 아래 좌표는 절대값이라 그대로 남습니다. 그만큼 좌표도 같이 줄여야 부속이 제자리에 붙습니다.
            // 보정이 필요 없는 경우에는 (1,1)이라 계산 결과가 완전히 같습니다.
            Vector2 headFactor = Patches.GoblinHeadScaleCompat.AddonOffsetFactor(parms.pawn, parms.Portrait);
            offset.x += (gbrOffset.x + settings.addonHorizontalOffset + fineTune.x) * juvenileFactor * headFactor.x;
            offset.z += (gbrOffset.y + settings.addonVerticalOffset + fineTune.y) * juvenileFactor * headFactor.y;
            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNodeProperties_GoblinAddon props = node.Props as PawnRenderNodeProperties_GoblinAddon;
            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            float addonScale = props?.addonScale ?? 1f;
            string formKey = GoblinUtility.GoblinVisualTuningFormKey(parms.pawn);
            return base.ScaleFor(node, parms) * addonScale * settings.addonScale * settings.goblinGlobalRenderScale * GoblinUtility.JuvenileAddonScaleFactor(parms.pawn) * settings.ScaleForAddon(props?.tuningKey, parms.facing, formKey);
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            float layer = base.LayerFor(node, parms);
            PawnRenderNodeProperties_GoblinAddon props = node.Props as PawnRenderNodeProperties_GoblinAddon;
            if (props == null)
            {
                return layer;
            }

            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            string formKey = GoblinUtility.GoblinVisualTuningFormKey(parms.pawn);
            return layer + settings.LayerOffsetForAddon(props.tuningKey, parms.facing, formKey);
        }

        public override MaterialPropertyBlock GetMaterialPropertyBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
        {
            MaterialPropertyBlock block = base.GetMaterialPropertyBlock(node, material, parms);
            PawnRenderNodeProperties_GoblinAddon props = node.Props as PawnRenderNodeProperties_GoblinAddon;
            if (!IsEyeAddon(props) || RotDrawModeFor(parms) != RotDrawMode.Rotting)
            {
                return block;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            Color rottenColor = PawnRenderUtility.GetRottenColor(Color.white);
            block.SetColor(ShaderPropertyIDs.Color, rottenColor);
            block.SetColor(ShaderPropertyIDs.ColorTwo, rottenColor);
            return block;
        }

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            Pawn pawn = parms.pawn;
            if (!GoblinUtility.IsGoblin(pawn) || pawn.health?.hediffSet == null)
            {
                return false;
            }

            PawnRenderNodeProperties_GoblinAddon props = node.Props as PawnRenderNodeProperties_GoblinAddon;
            if (props == null || !DrawsForFacing(props, parms.facing))
            {
                return false;
            }

            if (IsEarAddon(props) && EarHidingHeadgearIsVisible(node, parms))
            {
                return false;
            }

            if (props.bodyPartDefName.NullOrEmpty())
            {
                return true;
            }

            if (HasBodyPart(pawn, props.bodyPartDefName, props.bodyPartSide))
            {
                return true;
            }

            return GoblinGoneAddonUtility.GoneTextureExistsFor(props.tuningKey, parms.facing);
        }

        private static bool AddonPartIsMissing(Pawn pawn, PawnRenderNodeProperties_GoblinAddon props)
        {
            return pawn?.health?.hediffSet != null
                && props != null
                && !props.bodyPartDefName.NullOrEmpty()
                && !HasBodyPart(pawn, props.bodyPartDefName, props.bodyPartSide);
        }

        private static RotDrawMode RotDrawModeFor(PawnDrawParms parms)
        {
            return parms.rotDrawMode != RotDrawMode.Fresh
                ? parms.rotDrawMode
                : parms.pawn?.Drawer?.renderer?.CurRotDrawMode ?? RotDrawMode.Fresh;
        }

        private static bool IsEyeAddon(PawnRenderNodeProperties_GoblinAddon props)
        {
            return props?.tuningKey == "EyeLeft" || props?.tuningKey == "EyeRight";
        }

        private static bool IsEarAddon(PawnRenderNodeProperties_GoblinAddon props)
        {
            return props.tuningKey == "EarLeft" || props.tuningKey == "EarRight";
        }

        // 귀를 숨기는 기준은 "투구를 착용했는가"가 아니라 "투구가 지금 실제로 그려지는가"입니다.
        // 모자 숨김 계열 모드(AB's Head Apparel Tweaker, Hats Display Selection,
        // Show Hair With Hats 등)는 어패럴을 벗기지 않은 채 헤드 어패럴 노드의 CanDrawNow를 끄거나
        // 노드 자체를 만들지 않습니다. 초상화의 Prefs.HatsOnlyOnMap도 같은 경로입니다.
        // 착용 목록만 보면 그런 상황에서 투구도 귀도 없는 민머리가 됩니다.
        // 그래서 같은 렌더 트리에 있는 해당 투구 노드에게 같은 parms로 직접 물어봅니다.
        private static bool EarHidingHeadgearIsVisible(PawnRenderNode node, PawnDrawParms parms)
        {
            // 값싼 선행 검사. 귀를 가리는 투구를 아예 안 입었으면 트리를 훑지 않습니다.
            if (!WearsEarHidingHeadgear(parms.pawn))
            {
                return false;
            }

            // 애니메이션 재생 중에는 숨김 판정을 건너뛰고 귀를 그립니다.
            // RJW 애니메이션 계열은 투구를 화면에서 치우면서도 CanDrawNow에는 "그려진다"고 답합니다.
            // (측정 결과 알파 1.0, 스케일 정상, 위치 정상 — 게임 입장에서는 정상 렌더입니다.)
            // 그대로 두면 투구도 귀도 없는 민머리가 되므로, 애니메이션 동안에는 귀를 살립니다.
            // currentAnimation은 바닐라 필드라 애니메이션 모드가 없으면 항상 null입니다.
            if (node?.tree?.currentAnimation != null)
            {
                return false;
            }

            // 서드파티가 헤드 어패럴 CanDrawNow 안에서 다시 부속 상태를 조회하면 무한 재귀가 됩니다.
            // 그때는 판정을 포기하고 착용 여부(기존 동작)로 폴백합니다.
            // 병렬 pre-render 경로에서 호출되므로 스레드마다 별도 플래그여야 합니다.
            if (queryingHeadgearVisibility)
            {
                return true;
            }

            PawnRenderNode root = node?.tree?.rootNode;
            if (root == null)
            {
                return true;
            }

            queryingHeadgearVisibility = true;
            try
            {
                // 노드를 하나도 못 찾았다면 모드가 노드 생성 단계에서 걷어낸 것이므로 귀를 그립니다.
                return TryResolveHeadgearVisibility(root, parms, out bool visible) && visible;
            }
            catch (Exception)
            {
                return true;
            }
            finally
            {
                queryingHeadgearVisibility = false;
            }
        }

        // 반환값은 "해당 투구 노드를 트리에서 찾았는가", visible은 "그중 하나라도 그려지는가"입니다.
        private static bool TryResolveHeadgearVisibility(PawnRenderNode node, PawnDrawParms parms, out bool visible)
        {
            visible = false;
            bool found = false;

            if (node is PawnRenderNode_Apparel apparelNode
                && apparelNode.apparel?.def != null
                && EarHidingHeadgearDefNames.Contains(apparelNode.apparel.def.defName))
            {
                found = true;
                if (node.Worker?.CanDrawNow(node, parms) == true)
                {
                    visible = true;
                    return true;
                }
            }

            PawnRenderNode[] children = node.children;
            if (children != null)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    PawnRenderNode child = children[i];
                    if (child == null)
                    {
                        continue;
                    }

                    if (TryResolveHeadgearVisibility(child, parms, out bool childVisible))
                    {
                        found = true;
                        if (childVisible)
                        {
                            visible = true;
                            return true;
                        }
                    }
                }
            }

            return found;
        }

        private static bool WearsEarHidingHeadgear(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return false;
            }

            for (int i = 0; i < wornApparel.Count; i++)
            {
                ThingDef def = wornApparel[i]?.def;
                if (def != null && EarHidingHeadgearDefNames.Contains(def.defName))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 OffsetForBodyAndFacing(PawnRenderNodeProperties_GoblinAddon props, PawnDrawParms parms)
        {
            bool hobgoblin = GoblinUtility.IsHobgoblin(parms.pawn);
            if (parms.facing == Rot4.North)
            {
                return hobgoblin ? props.maleNorthOffset : props.thinNorthOffset;
            }

            if (parms.facing == Rot4.East)
            {
                return hobgoblin ? props.maleEastOffset : props.thinEastOffset;
            }

            if (parms.facing == Rot4.West)
            {
                Vector2 west = hobgoblin ? props.maleWestOffset : props.thinWestOffset;
                if (west == Vector2.zero && props.mirrorEastForWest)
                {
                    Vector2 east = hobgoblin ? props.maleEastOffset : props.thinEastOffset;
                    return new Vector2(-east.x, east.y);
                }

                return west;
            }

            return hobgoblin ? props.maleSouthOffset : props.thinSouthOffset;
        }

        private static bool DrawsForFacing(PawnRenderNodeProperties_GoblinAddon props, Rot4 facing)
        {
            if (facing == Rot4.North)
            {
                return props.drawNorth;
            }

            if (facing == Rot4.East)
            {
                return props.drawEast;
            }

            if (facing == Rot4.West)
            {
                return props.drawWest || (props.drawEast && props.mirrorEastForWest);
            }

            return props.drawSouth;
        }

        private static bool HasBodyPart(Pawn pawn, string bodyPartDefName, string side)
        {
            BodyPartDef bodyPartDef = BodyPartDefNamed(bodyPartDefName);
            if (bodyPartDef == null)
            {
                return true;
            }

            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.def != bodyPartDef)
                {
                    continue;
                }

                if (side.NullOrEmpty())
                {
                    return true;
                }

                if (part.def.IsMirroredPart)
                {
                    if (side.Equals("Left", StringComparison.OrdinalIgnoreCase) && part.flipGraphic)
                    {
                        return true;
                    }

                    if (side.Equals("Right", StringComparison.OrdinalIgnoreCase) && !part.flipGraphic)
                    {
                        return true;
                    }
                }

                string label = part.untranslatedCustomLabel ?? part.customLabel ?? string.Empty;
                if (label.IndexOf(side, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static BodyPartDef BodyPartDefNamed(string defName)
        {
            // Called from RimWorld's parallel pawn pre-render path. DefDatabase is immutable
            // after loading; keeping a lazily-written Dictionary here corrupts it under load.
            return DefDatabase<BodyPartDef>.GetNamedSilentFail(defName);
        }
    }

    public class PawnRenderNodeProperties_GoblinEar : PawnRenderNodeProperties
    {
        public string earSide;
    }

    public class PawnRenderNodeWorker_GoblinEar : PawnRenderNodeWorker_FlipWhenCrawling
    {
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            PawnRenderNodeProperties_GoblinEar props = node.Props as PawnRenderNodeProperties_GoblinEar;
            float side = string.Equals(props?.earSide, "Right", StringComparison.OrdinalIgnoreCase) ? 1f : -1f;

            if (parms.facing == Rot4.North)
            {
                side *= -1f;
            }
            else if (parms.facing == Rot4.East)
            {
                side = 1f;
            }
            else if (parms.facing == Rot4.West)
            {
                side = -1f;
            }

            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            float juvenileFactor = GoblinUtility.JuvenileAddonOffsetFactor(parms.pawn);
            offset.x += side * settings.addonHorizontalOffset * juvenileFactor;
            offset.z += settings.addonVerticalOffset * juvenileFactor;
            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            return base.ScaleFor(node, parms) * settings.addonScale * settings.goblinGlobalRenderScale * GoblinUtility.JuvenileAddonScaleFactor(parms.pawn);
        }

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            Pawn pawn = parms.pawn;
            if (!GoblinUtility.IsGoblin(pawn) || pawn.health?.hediffSet == null)
            {
                return false;
            }

            PawnRenderNodeProperties_GoblinEar props = node.Props as PawnRenderNodeProperties_GoblinEar;
            string side = props?.earSide;
            if (side.NullOrEmpty())
            {
                return HasAnyEar(pawn);
            }

            return HasEarOnSide(pawn, side);
        }

        private static bool HasAnyEar(Pawn pawn)
        {
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.def == EarDef)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEarOnSide(Pawn pawn, string side)
        {
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.def != EarDef)
                {
                    continue;
                }

                string label = part.untranslatedCustomLabel ?? part.customLabel ?? string.Empty;
                if (label.IndexOf(side, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static BodyPartDef EarDef => DefDatabase<BodyPartDef>.GetNamedSilentFail("Ear");
    }
}
