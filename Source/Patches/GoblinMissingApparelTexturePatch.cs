using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    /// <summary>
    /// 고블린이 착용 텍스처가 없는 옷을 입었을 때, 그 옷을 그리지 않고 넘어갑니다.
    ///
    /// 왜 필요한가:
    /// - 바닐라 바지(Apparel_Pants)에는 원래 wornGraphicPath가 없습니다. 그래서 바닐라에서는
    ///   바지를 입어도 폰 위에 그려지지 않습니다.
    /// - Visible Pants 같은 모드는 패치로 그 필드를 추가해 "바지를 그려라"로 바꿉니다.
    ///   그런데 그 모드가 넣어 둔 텍스처 묶음에 우리 고블린이 쓰는 조합이 없으면,
    ///   바닐라 렌더가 없는 파일을 찾다가 실패하고 분홍 상자가 뜹니다.
    ///
    /// 여기서 하는 일:
    /// - 고블린이고, 그 옷의 텍스처가 실제로 존재하지 않을 때만 조용히 그리기를 건너뜁니다.
    ///   결과적으로 고블린에게는 바닐라 기본 동작(안 그림)이 그대로 유지됩니다.
    /// - 다른 종족이나 다른 옷은 건드리지 않습니다.
    ///
    /// 비용:
    /// - 이 메서드는 폰의 렌더 트리를 새로 만들 때만 호출됩니다. 매 틱 도는 코드가 아닙니다.
    /// - 파일 존재 여부는 경로별로 한 번만 확인하고 캐시에 담아 재사용합니다.
    ///
    /// 유지보수:
    /// - 텍스처가 있으면 이 패치는 아무것도 하지 않습니다. 나중에 누군가 고블린용 바지 텍스처를
    ///   제대로 넣어 주면 자동으로 비켜서고, 이 코드를 지울 필요도 없습니다.
    /// </summary>
    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class ApparelGraphicRecordGetter_SkipMissingGoblinApparelTexture
    {
        // 경로 -> 텍스처가 있는지. 렌더 트리를 다시 만들 때마다 파일을 찾지 않도록 담아 둡니다.
        private static readonly Dictionary<string, bool> TextureExistsCache = new Dictionary<string, bool>();

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Apparel apparel, BodyTypeDef bodyType, ref ApparelGraphicRecord rec, ref bool __result)
        {
            if (!ShouldSkipDrawing(apparel, bodyType))
            {
                return true;
            }

            // 원본을 실행하지 않으므로 "텍스처 없음" 오류 자체가 발생하지 않습니다.
            rec = default;
            __result = false;
            return false;
        }

        private static bool ShouldSkipDrawing(Apparel apparel, BodyTypeDef bodyType)
        {
            if (apparel?.Wearer == null)
            {
                return false;
            }

            if (!GoblinUtility.HasGoblinCoreMarker(apparel.Wearer))
            {
                return false;
            }

            string basePath = apparel.WornGraphicPath;
            if (basePath.NullOrEmpty())
            {
                // 그릴 것이 애초에 없으면 바닐라가 알아서 처리합니다.
                return false;
            }

            // 바닐라는 옷의 레이어에 따라 기본 경로를 그대로 쓰기도 하고 체형 이름을 붙이기도 합니다.
            // 어느 쪽 규칙이 걸리든 안전하도록 두 후보를 모두 확인하고,
            // 둘 다 없을 때만 건너뜁니다.
            if (TextureExists(basePath))
            {
                return false;
            }

            if (bodyType != null && TextureExists(basePath + "_" + bodyType.defName))
            {
                return false;
            }

            return true;
        }

        private static bool TextureExists(string path)
        {
            if (TextureExistsCache.TryGetValue(path, out bool exists))
            {
                return exists;
            }

            // Graphic_Multi는 방향별 파일을 쓰므로 남쪽 한 장으로 존재 여부를 판단합니다.
            // Graphic_Single처럼 방향이 없는 경우까지 보려고 경로 자체도 함께 확인합니다.
            exists = ContentFinder<Texture2D>.Get(path + "_south", false) != null
                || ContentFinder<Texture2D>.Get(path, false) != null;

            TextureExistsCache[path] = exists;
            return exists;
        }
    }

    /// <summary>
    /// MUGB apparel uses a larger ground graphic so dropped gear remains readable. RimWorld
    /// also feeds that same drawSize into worn graphics, so restore the original worn size
    /// only while constructing the apparel render record. This runs on render-tree setup,
    /// not per tick or per frame.
    /// </summary>
    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class ApparelGraphicRecordGetter_KeepMugbWornSize
    {
        private const string GoblinApparelCategoryDefName = "MUGB_GoblinApparelCategory";

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Apparel apparel, BodyTypeDef bodyType, bool forStatue,
            ref ApparelGraphicRecord rec, ref bool __result)
        {
            if (!__result || apparel?.def?.thingCategories == null
                || !apparel.def.thingCategories.Any(category => category?.defName == GoblinApparelCategoryDefName)
                || apparel.WornGraphicPath.NullOrEmpty())
            {
                return;
            }

            if (bodyType == null)
            {
                bodyType = BodyTypeDefOf.Male;
            }

            string path = apparel.def.apparel.LastLayer != ApparelLayerDefOf.Overhead
                && apparel.def.apparel.LastLayer != ApparelLayerDefOf.EyeCover
                && !apparel.RenderAsPack()
                && apparel.WornGraphicPath != BaseContent.PlaceholderImagePath
                && apparel.WornGraphicPath != BaseContent.PlaceholderGearImagePath
                    ? apparel.WornGraphicPath + "_" + bodyType.defName
                    : apparel.WornGraphicPath;

            Shader shader = ShaderDatabase.Cutout;
            if (!forStatue)
            {
                if (apparel.StyleDef?.graphicData.shaderType != null)
                {
                    shader = apparel.StyleDef.graphicData.shaderType.Shader;
                }
                else if ((apparel.StyleDef == null && apparel.def.apparel.useWornGraphicMask)
                    || (apparel.StyleDef != null && apparel.StyleDef.UseWornGraphicMask))
                {
                    shader = ShaderDatabase.CutoutComplex;
                }
            }

            Graphic graphic = GraphicDatabase.Get<Graphic_Multi>(path, shader, Vector2.one, apparel.DrawColor);
            rec = new ApparelGraphicRecord(graphic, apparel);
        }
    }
}
