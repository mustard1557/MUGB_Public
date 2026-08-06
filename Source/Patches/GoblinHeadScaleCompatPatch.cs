using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    /*
        [파일 개요]
        HAR이 인간 머리 크기를 줄이는 모드(예: Hooman cute)와 함께 켰을 때
        고블린 얼굴 부속이 어긋나는 문제를 처리합니다.

        [배경 - HAR이 머리를 줄이는 경로]
        1. 종족 XML의 alienPartGenerator/customHeadDrawSize
        2. -> LifeStageAgeAlien.customHeadDrawSize (정의 해석 단계에서 복사)
        3. -> AlienPartGenerator.AlienComp.customHeadDrawSize (렌더 트리를 새로 만들 때 폰별로 복사)
        4. -> HumanlikeMeshPoolUtility 의 GetHumanlikeHeadSetForPawn / GetHumanlikeHairSetForPawn /
              GetHumanlikeBeardSetForPawn 앞에 붙은 HAR prefix가 메시 크기에 곱함

        Hooman cute는 이 값을 Human ThingDef 자체에 (0.75, 0.75)로 넣습니다.
        고블린은 HAR 외계 종족이 아니라 Human 기반 바이오텍 제노타입이라 그대로 같이 맞습니다.

        [왜 부속만 어긋나는가]
        고블린 머리 노드(PawnRenderNode_Head)와 얼굴 부속 노드(PawnRenderNode_AttachmentHead 계열)는
        둘 다 위 메시셋을 쓰므로 텍스처는 같이 75%로 줄어듭니다. 크기는 문제가 없습니다.
        그런데 MUGB가 PawnRenderNodeWorker_GoblinAddon.OffsetFor 에서 더하는 부속 좌표는
        머리 크기와 무관한 절대값이라 줄지 않습니다. 그래서 부속이 원래 거리에 그대로 남아
        작아진 머리 바깥으로 벌어져 보입니다.

        [대응 - 두 단계]
        1) 머리 크기 예외처리 (harHeadSizeExemption, 기본 켜짐)
           고블린 폰의 AlienComp 머리 배율을 (1,1)로 되돌립니다. 머리도 부속도 줄지 않으므로
           MUGB의 렌더 계산은 하나도 달라지지 않습니다. 기존에 맞춰둔 좌표가 그대로 맞습니다.

        2) 부속 좌표 비율 유지 (addonFollowHeadScale, 기본 켜짐)
           1)을 끈 사람을 위한 안전망입니다. 실제로 적용되는 머리 배율만큼 부속 좌표도 같이 줄입니다.
           크기는 이미 메시 단계에서 줄어 있으므로 절대 건드리지 않습니다. 여기서 또 곱하면
           부속만 배율의 제곱으로 작아집니다.

        [한계]
        머리 크기를 HAR 경로가 아닌 다른 방식으로 바꾸는 모드는 이 파일이 읽어낼 수 없습니다.
        현재 확인된 머리 크기 조정 모드는 전부 위 1~4 경로를 씁니다.
    */
    [StaticConstructorOnStartup]
    public static class GoblinHeadScaleCompat
    {
        private const string AlienCompTypeName = "AlienRace.AlienPartGenerator+AlienComp";
        private const string LifeStageAgeAlienTypeName = "AlienRace.LifeStageAgeAlien";
        private const string HeadDrawSizeFieldName = "customHeadDrawSize";
        private const string PortraitHeadDrawSizeFieldName = "customPortraitHeadDrawSize";

        private static Type alienCompType;
        private static FieldInfo compHeadDrawSizeField;
        private static FieldInfo compPortraitHeadDrawSizeField;

        // 시작할 때 한 번 채우고 그 뒤로는 읽기만 합니다. 렌더는 병렬로 도는 구간이 있어
        // 나중에 쓰기가 섞이면 Dictionary가 깨지므로, 만든 뒤에는 절대 수정하지 않습니다.
        private static Dictionary<LifeStageAge, HeadDrawSizes> lifeStageHeadSizes;

        /// <summary>
        /// 인간 머리 크기를 실제로 바꾸는 모드가 켜져 있을 때만 참입니다.
        /// 거짓이면 아래 Harmony 패치를 아예 걸지 않으므로, 그런 모드를 안 쓰는 사람에게는
        /// 이 파일이 존재하지 않는 것과 완전히 같습니다.
        /// </summary>
        public static bool Active { get; private set; }

        static GoblinHeadScaleCompat()
        {
            try
            {
                alienCompType = AccessTools.TypeByName(AlienCompTypeName);
                if (alienCompType == null)
                {
                    // HAR이 없으면 머리 크기를 건드리는 경로 자체가 없습니다.
                    return;
                }

                compHeadDrawSizeField = AccessTools.Field(alienCompType, HeadDrawSizeFieldName);
                compPortraitHeadDrawSizeField = AccessTools.Field(alienCompType, PortraitHeadDrawSizeFieldName);
                if (compHeadDrawSizeField == null || compPortraitHeadDrawSizeField == null)
                {
                    Log.Warning(
                        "[MUGB] HAR is loaded but AlienComp head draw size fields were not found. "
                        + "Goblin face addons may be misaligned when another mod resizes human heads.");
                    return;
                }

                // 정의는 이 시점에 이미 해석되어 있으므로 표를 바로 만들 수 있습니다.
                // 표를 봐야 "머리 크기를 실제로 바꾸는 모드가 있는지"를 알 수 있습니다.
                //
                // 판단은 한쪽으로만 기울입니다. "확실히 아무도 안 건드린다"를 확인했을 때만 패치를 거릅니다.
                // 표를 못 읽었으면 모르는 것이지 없는 것이 아니므로, 그냥 걸고 경고를 남깁니다.
                // 여기서 조용히 빠져나가면 기능 전체가 소리 없이 죽고 원인을 찾을 수 없게 됩니다.
                bool tableBuilt = BuildLifeStageTable();
                string sample = null;
                if (tableBuilt && !AnyLifeStageResizesHead(out sample))
                {
                    return;
                }

                Active = true;
                ApplyHarmonyPatch();
                if (!Active)
                {
                    return;
                }

                if (tableBuilt)
                {
                    Log.Message(
                        "[MUGB] Another mod resizes human heads through HAR (" + sample + "). "
                        + "Goblin head compatibility is enabled; see the MUGB visual tuning window for the two options.");
                }
                else
                {
                    Log.Warning(
                        "[MUGB] HAR is loaded but the human life stage head draw sizes could not be read. "
                        + "The goblin head size lock is enabled anyway, but moving face parts with the head size cannot work. "
                        + "Turn on the head size lock in the MUGB visual tuning window if goblin faces look wrong.");
                }
            }
            catch (Exception e)
            {
                Active = false;
                Log.Error("[MUGB] Failed to set up the HAR head-scale compatibility patch: " + e);
            }
        }

        private static void ApplyHarmonyPatch()
        {
            MethodInfo target = AccessTools.Method(typeof(PawnRenderTree), "TrySetupGraphIfNeeded");
            if (target == null)
            {
                Active = false;
                Log.Warning("[MUGB] PawnRenderTree.TrySetupGraphIfNeeded was not found; the goblin head size lock is off.");
                return;
            }

            // HAR도 같은 메서드에 prefix를 붙여 폰별 머리 배율을 채웁니다.
            // Priority.Last로 HAR보다 뒤에 실행되게 해서 그 값을 덮어씁니다.
            new Harmony("mustard1557.mugb.goblin.headscale").Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(PawnRenderTree_TrySetupGraphIfNeeded_Patch),
                    nameof(PawnRenderTree_TrySetupGraphIfNeeded_Patch.Prefix))
                {
                    priority = Priority.Last
                });
        }

        private static bool AnyLifeStageResizesHead(out string sample)
        {
            sample = null;
            if (lifeStageHeadSizes == null)
            {
                return false;
            }

            foreach (KeyValuePair<LifeStageAge, HeadDrawSizes> entry in lifeStageHeadSizes)
            {
                if (entry.Value.World != Vector2.one || entry.Value.Portrait != Vector2.one)
                {
                    sample = $"life stage {entry.Key.def?.defName ?? "?"}: "
                        + $"world {entry.Value.World}, portrait {entry.Value.Portrait}";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 렌더 트리를 새로 만든 직후에 호출합니다. 예외처리가 켜져 있으면 이 폰의 머리 배율을 1로 되돌립니다.
        /// 예외처리를 끄면 HAR이 트리를 다시 만들 때 원래 값을 다시 넣으므로 되돌리기가 자동으로 됩니다.
        /// </summary>
        public static void ApplyHeadDrawSizeExemption(Pawn pawn)
        {
            if (!Active || pawn == null)
            {
                return;
            }

            MUGBSettings settings = MUGBMod.Settings;
            if (settings == null || !settings.harHeadSizeExemption)
            {
                return;
            }

            if (!GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            object alienComp = FindAlienComp(pawn);
            if (alienComp == null)
            {
                return;
            }

            compHeadDrawSizeField.SetValue(alienComp, Vector2.one);
            compPortraitHeadDrawSizeField.SetValue(alienComp, Vector2.one);
        }

        /// <summary>
        /// 얼굴 부속 좌표에 곱할 배율입니다. 보정이 필요 없으면 (1,1)을 돌려줍니다.
        /// 크기 배율이 아니라 좌표 배율입니다. 크기는 메시 단계에서 이미 적용되어 있습니다.
        /// </summary>
        public static Vector2 AddonOffsetFactor(Pawn pawn, bool portrait)
        {
            // 아래 세 줄이 기본 설정에서 타는 전부입니다. HAR이 없거나 예외처리가 켜져 있으면
            // 머리가 애초에 줄지 않으므로 보정할 것도 없습니다.
            if (!Active)
            {
                return Vector2.one;
            }

            MUGBSettings settings = MUGBMod.Settings;
            if (settings == null || settings.harHeadSizeExemption || !settings.addonFollowHeadScale)
            {
                return Vector2.one;
            }

            Dictionary<LifeStageAge, HeadDrawSizes> table = lifeStageHeadSizes;
            LifeStageAge lifeStage = pawn?.ageTracker?.CurLifeStageRace;
            if (table == null || lifeStage == null || !table.TryGetValue(lifeStage, out HeadDrawSizes sizes))
            {
                return Vector2.one;
            }

            return portrait ? sizes.Portrait : sizes.World;
        }

        private static object FindAlienComp(Pawn pawn)
        {
            List<ThingComp> comps = pawn.AllComps;
            if (comps == null)
            {
                return null;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                ThingComp comp = comps[i];
                if (comp != null && alienCompType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }

            return null;
        }

        private static bool BuildLifeStageTable()
        {
            try
            {
                Type lifeStageAlienType = AccessTools.TypeByName(LifeStageAgeAlienTypeName);
                FieldInfo headField = lifeStageAlienType == null
                    ? null
                    : AccessTools.Field(lifeStageAlienType, HeadDrawSizeFieldName);
                FieldInfo portraitField = lifeStageAlienType == null
                    ? null
                    : AccessTools.Field(lifeStageAlienType, PortraitHeadDrawSizeFieldName);
                List<LifeStageAge> lifeStages = ThingDefOf.Human?.race?.lifeStageAges;
                if (headField == null || portraitField == null || lifeStages == null)
                {
                    return false;
                }

                Dictionary<LifeStageAge, HeadDrawSizes> table = new Dictionary<LifeStageAge, HeadDrawSizes>();
                for (int i = 0; i < lifeStages.Count; i++)
                {
                    LifeStageAge lifeStage = lifeStages[i];
                    if (lifeStage == null || !lifeStageAlienType.IsInstanceOfType(lifeStage))
                    {
                        continue;
                    }

                    table[lifeStage] = new HeadDrawSizes(
                        Sanitize((Vector2)headField.GetValue(lifeStage)),
                        Sanitize((Vector2)portraitField.GetValue(lifeStage)));
                }

                lifeStageHeadSizes = table;
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to read HAR head draw sizes for the human life stages: " + e);
                return false;
            }
        }

        // HAR은 "지정 안 함"을 0으로 표현합니다. 0을 그대로 곱하면 부속이 좌표 원점으로 몰립니다.
        private static Vector2 Sanitize(Vector2 value)
        {
            if (value.x <= 0f || value.y <= 0f)
            {
                return Vector2.one;
            }

            return value;
        }

        private readonly struct HeadDrawSizes
        {
            public readonly Vector2 World;
            public readonly Vector2 Portrait;

            public HeadDrawSizes(Vector2 world, Vector2 portrait)
            {
                World = world;
                Portrait = portrait;
            }
        }
    }

    /// <summary>
    /// 반드시 렌더 노드가 만들어지기 "전"에 값을 되돌려야 합니다.
    /// PawnRenderNode 생성자가 meshSet = MeshSetFor(pawn) 으로 메시 크기를 그 자리에서 확정하기 때문에,
    /// 노드를 만든 뒤에 배율을 바꿔봐야 이미 만들어진 노드에는 반영되지 않습니다.
    ///
    /// HAR도 같은 메서드에 prefix를 붙여 폰별 머리 배율을 채웁니다. HarmonyPriority(Last)로
    /// HAR보다 뒤에 실행되게 해서, HAR이 채운 값을 덮어씁니다.
    ///
    /// 이 패치는 [HarmonyPatch] 특성을 일부러 달지 않았습니다. PatchAll이 무조건 걸어버리면
    /// 머리 크기를 건드리는 모드가 없는 사람도 매 프레임 이 메서드를 거치게 됩니다.
    /// 대신 GoblinHeadScaleCompat이 그런 모드를 실제로 발견했을 때만 직접 붙입니다.
    /// 못 찾으면 패치가 존재하지 않으므로 비용이 문자 그대로 0입니다.
    ///
    /// 붙은 경우에도 트리가 이미 있으면 Resolved 검사에서 바로 빠집니다.
    /// </summary>
    public static class PawnRenderTree_TrySetupGraphIfNeeded_Patch
    {
        public static void Prefix(PawnRenderTree __instance)
        {
            if (__instance.Resolved)
            {
                return;
            }

            GoblinHeadScaleCompat.ApplyHeadDrawSizeExemption(__instance.pawn);
        }
    }
}
