using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;

namespace MUGB
{
    [StaticConstructorOnStartup]
    public class MUGBMod : Mod
    {
        public static MUGBSettings Settings;
        public static ModContentPack ContentPack;
        private static readonly Dictionary<string, string> NumericBuffers = new Dictionary<string, string>();
        private static Vector2 settingsScrollPosition;
        private static int selectedFormIndex;
        private static string selectedVisualFormKey = "Goblin";
        private static int selectedPartIndex;
        private static int selectedRotIndex;
        private static bool showRenderScale = true;
        private static bool showHeadBody = true;
        private static bool showGlobalAddons = true;
        private static bool showFineTune = true;
        private static bool showOverview;
        private static int selectedApparelCategoryIndex;
        private static int selectedApparelIndex;
        private static int selectedApparelFormIndex;
        private static int selectedApparelRotIndex;
        private static int selectedApparelPawnId = -1;
        private static int lastObservedMapSelectedPawnId = -1;
        private static int apparelFormPawnId = -1;
        private static bool selectWornApparelOnNextDraw = true;
        private static string wearableApparelCacheKey;
        private static readonly List<ThingDef> WearableApparelCache = new List<ThingDef>();
        private static ThingStyleDef selectedApparelStyle;
        private static readonly List<ThingStyleDef> ApparelStyleCache = new List<ThingStyleDef>();
        private static bool applyApparelScaleToAllDirections;
        private static bool mirrorApparelEastWest;
        private static RenderTexture apparelPreviewTexture;
        private static readonly Dictionary<string, Pawn> ApparelPreviewPawns = new Dictionary<string, Pawn>();
        private static string apparelPreviewKey;
        private static bool apparelPreviewDirty = true;
        private static float apparelPreviewZoom = 1.25f;
        private static readonly string[] ApparelForms = { "Goblin", "Hobgoblin" };
        private static readonly string[] VisualFormKeys = { "Goblin", "GoblinCrossEyed", "Hobgoblin", "HobgoblinCrossEyed", "GoblinChild", "GoblinCrossEyedChild", "HobgoblinChild", "HobgoblinCrossEyedChild", "GoblinDessicated", "HobgoblinDessicated" };
        private static readonly string[] VisualFormLabels = { "Goblin", "Goblin (cross-eyed)", "Hobgoblin", "Hobgoblin (cross-eyed)", "Goblin child", "Goblin child (cross-eyed)", "Hobgoblin child", "Hobgoblin child (cross-eyed)", "Goblin (dessicated)", "Hobgoblin (dessicated)" };
        private static readonly string[] Parts = { "Head", "Body", "EarLeft", "EarRight", "EyeLeft", "EyeRight", "Nose", "Mouth" };
        private static readonly string[] ApparelCategories = { "Headgear", "Clothing", "Armor", "Outerwear", "Utility", "Shield" };
        internal static readonly Rot4[] Rotations = { Rot4.South, Rot4.North, Rot4.East, Rot4.West };
        private static readonly Rot4[] PreviewRotationOrder = { Rot4.South, Rot4.North, Rot4.East, Rot4.West };

        public MUGBMod(ModContentPack content)
            : base(content)
        {
            ContentPack = content;
            Settings = GetSettings<MUGBSettings>();
            Patches.GoblinAgeUtility.RefreshChildStageSettings();
            MUGBVisualTuningDefaults.InitializeApparelDefaults(content.RootDir);
            GoblinClosedEyeUtility.ScheduleInitialize();
            GoblinGoneAddonUtility.ScheduleInitialize();
            Harmony harmony = new Harmony("mustard1557.mugb.goblin");
            harmony.PatchAll();
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                PawnRenderNodeWorker_GoblinSplatterOverlay.PrecacheGraphics();
                MUGB.Patches.GoblinBirthUtility.PatchAfterDefsLoaded(harmony);
                MUGB.Patches.GoblinBiotechAppearanceUtility.Initialize();
                MUGB.Patches.MUGBPassingGroupFrequencyUtility.ApplyStorytellerFrequency();
            });
        }

        public override string SettingsCategory()
        {
            return "MUGB Goblin";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 1300f);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);
            if (listing.ButtonText("MUGB_SettingsOpenVisualTuning".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MUGBVisualTuning());
            }

            bool oldEnableDraftedWeaponPoseOffsets = Settings.enableDraftedWeaponPoseOffsets;
            listing.CheckboxLabeled(
                "MUGB_SettingsEnableDraftedWeaponPoseOffsetsLabel".Translate(),
                ref Settings.enableDraftedWeaponPoseOffsets,
                "MUGB_SettingsEnableDraftedWeaponPoseOffsetsDesc".Translate());
            if (oldEnableDraftedWeaponPoseOffsets != Settings.enableDraftedWeaponPoseOffsets)
            {
                Settings.Write();
            }

            listing.GapLine();
            listing.Label("MUGB_SettingsStorytellerHeader".Translate());
            int oldPassingGroupFrequencyPercent = Settings.passingGroupFrequencyPercent;
            listing.Label("MUGB_SettingsPassingGroupFrequencyLabel".Translate(Settings.passingGroupFrequencyPercent));
            Settings.passingGroupFrequencyPercent = Mathf.RoundToInt(
                listing.Slider(Settings.passingGroupFrequencyPercent, 0f, 200f) / 10f) * 10;
            listing.Label("MUGB_SettingsPassingGroupFrequencyDesc".Translate());
            if (oldPassingGroupFrequencyPercent != Settings.passingGroupFrequencyPercent)
            {
                Settings.Write();
                MUGB.Patches.MUGBPassingGroupFrequencyUtility.ApplyStorytellerFrequency();
            }

            int oldPawnLossAdaptationPercent = Settings.pawnLossAdaptationPercent;
            if (listing.ButtonText("MUGB_SettingsPawnLossAdaptationButton".Translate(Settings.pawnLossAdaptationPercent)))
            {
                Settings.pawnLossAdaptationPercent = Settings.pawnLossAdaptationPercent == 0 ? 50 : (Settings.pawnLossAdaptationPercent == 50 ? 100 : 0);
            }
            listing.Label("MUGB_SettingsPawnLossAdaptationDescription".Translate());
            // 한국어 참고: 정착민 손실로 바닐라 스토리텔러 적응도가 깎이는 정도입니다.
            // 0%는 사망/납치/맵 폐쇄로 인한 습격점수 완화 없음, 50%는 절반만 적용, 100%는 바닐라 그대로입니다.
            if (oldPawnLossAdaptationPercent != Settings.pawnLossAdaptationPercent)
            {
                Settings.Write();
            }

            int oldKimDeokPalPawnLossAdaptationPercent = Settings.kimDeokPalPawnLossAdaptationPercent;
            if (listing.ButtonText("MUGB_KimDeokPalPawnLossAdaptationButton".Translate(Settings.kimDeokPalPawnLossAdaptationPercent)))
            {
                Settings.kimDeokPalPawnLossAdaptationPercent = Settings.kimDeokPalPawnLossAdaptationPercent == 0
                    ? 50
                    : (Settings.kimDeokPalPawnLossAdaptationPercent == 50 ? 100 : 0);
            }
            listing.Label("MUGB_KimDeokPalPawnLossAdaptationDescription".Translate());
            // 한국어 참고: 김덕팔 스토리텔러 전용 정착민/노예 손실 습격 완화값. 0/50/100% 중 선택.
            if (oldKimDeokPalPawnLossAdaptationPercent != Settings.kimDeokPalPawnLossAdaptationPercent)
            {
                Settings.Write();
            }

            listing.GapLine();
            listing.Label("MUGB_SlaveMarriageSettingsHeader".Translate());
            bool oldRequireSlaveMarriagePheromonePreference = Settings.requireSlaveMarriagePheromonePreference;
            listing.CheckboxLabeled(
                "MUGB_RequireSlaveMarriagePheromonePreferenceLabel".Translate(),
                ref Settings.requireSlaveMarriagePheromonePreference,
                "MUGB_RequireSlaveMarriagePheromonePreferenceDesc".Translate());
            if (oldRequireSlaveMarriagePheromonePreference != Settings.requireSlaveMarriagePheromonePreference)
            {
                Settings.Write();
            }

            listing.Label("MUGB_SettingsCheatHeader".Translate());
            bool oldDisableToxicPheromonesCheat = Settings.disableToxicPheromonesCheat;
            listing.CheckboxLabeled(
                "MUGB_DisableToxicPheromonesCheatLabel".Translate(),
                ref Settings.disableToxicPheromonesCheat,
                "MUGB_DisableToxicPheromonesCheatDesc".Translate());
            if (oldDisableToxicPheromonesCheat != Settings.disableToxicPheromonesCheat)
            {
                Settings.Write();
            }

            listing.GapLine();
            listing.Label("MUGB_SettingsRaidsHeader".Translate());
            bool oldEnableGoblinSquadSystem = Settings.enableGoblinSquadSystem;
            listing.CheckboxLabeled(
                "MUGB_SettingsEnableGoblinSquadSystemLabel".Translate(),
                ref Settings.enableGoblinSquadSystem,
                "MUGB_SettingsEnableGoblinSquadSystemDesc".Translate());
            if (oldEnableGoblinSquadSystem != Settings.enableGoblinSquadSystem)
            {
                Settings.Write();
            }

            bool oldEnableGoblinCompositeRaids = Settings.enableGoblinCompositeRaids;
            listing.CheckboxLabeled(
                "MUGB_SettingsEnableGoblinCompositeRaidsLabel".Translate(),
                ref Settings.enableGoblinCompositeRaids,
                "MUGB_SettingsEnableGoblinCompositeRaidsDesc".Translate());
            if (oldEnableGoblinCompositeRaids != Settings.enableGoblinCompositeRaids)
            {
                Settings.Write();
            }

            listing.GapLine();
            listing.Label("MUGB_SettingsTraitsHeader".Translate());
            bool oldEnableFeminineTrait = Settings.enableFeminineTrait;
            listing.CheckboxLabeled(
                "MUGB_EnableFeminineTraitLabel".Translate(),
                ref Settings.enableFeminineTrait,
                "MUGB_EnableFeminineTraitDesc".Translate());
            // 한국어 참고: 끄면 새 폰 특성 추첨에서만 제외됩니다(GoblinTraitPatches.cs). 기존 폰과 forcedTraits는 유지됩니다.
            if (oldEnableFeminineTrait != Settings.enableFeminineTrait)
            {
                Settings.Write();
            }

            bool oldAdjustFemaleBodyTypeChances = Settings.adjustFemaleBodyTypeChances;
            listing.CheckboxLabeled(
                "MUGB_AdjustFemaleBodyTypeChancesLabel".Translate(),
                ref Settings.adjustFemaleBodyTypeChances,
                "MUGB_AdjustFemaleBodyTypeChancesDesc".Translate());
            if (oldAdjustFemaleBodyTypeChances != Settings.adjustFemaleBodyTypeChances)
            {
                Settings.Write();
            }

            bool oldAmericanBeautyStandard = Settings.americanBeautyStandard;
            listing.CheckboxLabeled(
                "MUGB_AmericanBeautyStandardLabel".Translate(),
                ref Settings.americanBeautyStandard);
            if (oldAmericanBeautyStandard != Settings.americanBeautyStandard)
            {
                Settings.Write();
            }

            listing.GapLine();
            listing.Label("MUGB_SettingsReproductionHeader".Translate());
            float oldGoblinLitterSizeMultiplier = Settings.goblinLitterSizeMultiplier;
            Settings.goblinLitterSizeMultiplier = Patches.GoblinLitterSizeUtility.NormalizeMultiplier(
                listing.Slider(Settings.goblinLitterSizeMultiplier, 0.25f, 2f));
            int litterPercent = Mathf.RoundToInt(Settings.goblinLitterSizeMultiplier * 100f);
            listing.Label("MUGB_SettingsGoblinLitterSizeLabel".Translate(litterPercent));
            Patches.GoblinLitterSizeUtility.GetExpectedRange(false, Settings.goblinLitterSizeMultiplier, out int thinMin, out int thinMax);
            Patches.GoblinLitterSizeUtility.GetExpectedRange(true, Settings.goblinLitterSizeMultiplier, out int hobMin, out int hobMax);
            string thinRange = thinMin == thinMax
                ? thinMin.ToString()
                : "MUGB_SettingsGoblinLitterSizeRange".Translate(thinMin, thinMax).ToString();
            string hobRange = hobMin == hobMax
                ? hobMin.ToString()
                : "MUGB_SettingsGoblinLitterSizeRange".Translate(hobMin, hobMax).ToString();
            listing.Label("MUGB_SettingsGoblinLitterSizeExpected".Translate(thinRange, hobRange));
            listing.Label("MUGB_SettingsGoblinLitterSizeDesc".Translate());
            if (!Mathf.Approximately(oldGoblinLitterSizeMultiplier, Settings.goblinLitterSizeMultiplier))
            {
                Settings.Write();
            }

            float oldGoblinChildStageDays = Settings.goblinChildStageDays;
            string childStageDuration = "MUGB_SettingsGoblinChildStageDaysValue".Translate(
                Settings.goblinChildStageDays.ToString("0.#", CultureInfo.InvariantCulture));
            if (listing.ButtonText("MUGB_SettingsGoblinChildStageDaysButton".Translate(childStageDuration)))
            {
                Settings.goblinChildStageDays = Patches.GoblinAgeUtility.NextChildStageDays(Settings.goblinChildStageDays);
            }
            listing.Label("MUGB_SettingsGoblinChildStageDaysDesc".Translate());
            if (!Mathf.Approximately(oldGoblinChildStageDays, Settings.goblinChildStageDays))
            {
                Patches.GoblinAgeUtility.RefreshChildStageSettings();
                Settings.Write();
            }

            int oldGoblinBirthStrainLimit = Settings.goblinBirthStrainLimit;
            string birthStrainLimit = Settings.goblinBirthStrainLimit <= 0
                ? "MUGB_SettingsGoblinBirthStrainDisabled".Translate().ToString()
                : "MUGB_SettingsGoblinBirthStrainCount".Translate(Settings.goblinBirthStrainLimit).ToString();
            if (listing.ButtonText("MUGB_SettingsGoblinBirthStrainButton".Translate(birthStrainLimit)))
            {
                Settings.goblinBirthStrainLimit = Patches.GoblinBirthStrainUtility.NextLimit(Settings.goblinBirthStrainLimit);
            }
            listing.Label("MUGB_SettingsGoblinBirthStrainDesc".Translate());
            if (oldGoblinBirthStrainLimit != Settings.goblinBirthStrainLimit)
            {
                Patches.GoblinBirthStrainUtility.ApplySettingChange(
                    oldGoblinBirthStrainLimit,
                    Settings.goblinBirthStrainLimit);
                Settings.Write();
            }

            if (Patches.FacialAnimationCompatPatch.Applied)
            {
                listing.GapLine();
                listing.Label("MUGB_SettingsCompatHeader".Translate());
                bool oldAllowFacialAnimationForGoblins = Settings.allowFacialAnimationForGoblins;
                listing.CheckboxLabeled(
                    "MUGB_AllowFacialAnimationForGoblinsLabel".Translate(),
                    ref Settings.allowFacialAnimationForGoblins,
                    "MUGB_AllowFacialAnimationForGoblinsDesc".Translate());
                // 한국어 참고: 기본 꺼짐. 꺼져 있으면 고블린은 페이셜 애니메이션 렌더에서 제외되어 MUGB 전용 얼굴을 유지합니다.
                if (oldAllowFacialAnimationForGoblins != Settings.allowFacialAnimationForGoblins)
                {
                    Settings.Write();
                    Patches.FacialAnimationCompatPatch.RefreshGoblinFaces();
                }
            }

            listing.End();
            Widgets.EndScrollView();
        }

        public static void DrawVisualTuningControls(Rect inRect)
        {
            bool visualChanged = false;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            if (listing.ButtonText("MUGB_TuningOpenApparel".Translate()))
            {
                OpenApparelTuningWindow();
            }
            listing.GapLine();
            visualChanged |= DrawSelectedRaceHeadBodyControls(listing);
            listing.GapLine();
            bool oldUseTextureColors = Settings.useTextureColors;
            listing.CheckboxLabeled("MUGB_TuningUseTextureColors".Translate(), ref Settings.useTextureColors,
                "MUGB_TuningUseTextureColorsDesc".Translate());
            visualChanged |= Settings.useTextureColors != oldUseTextureColors;
            bool oldMoveHeadgearWithHead = Settings.moveHeadgearWithHead;
            listing.CheckboxLabeled("MUGB_TuningMoveHeadgear".Translate(), ref Settings.moveHeadgearWithHead,
                "MUGB_TuningMoveHeadgearDesc".Translate());
            visualChanged |= Settings.moveHeadgearWithHead != oldMoveHeadgearWithHead;
            bool oldForceGoblinBaldAndNoBeard = Settings.forceGoblinBaldAndNoBeard;
            listing.CheckboxLabeled("MUGB_TuningForceBald".Translate(), ref Settings.forceGoblinBaldAndNoBeard,
                "MUGB_TuningForceBaldDesc".Translate());
            visualChanged |= Settings.forceGoblinBaldAndNoBeard != oldForceGoblinBaldAndNoBeard;
            // 이 두 개는 HAR로 인간 머리 크기를 바꾸는 모드가 켜져 있을 때만 의미가 있으므로
            // 그런 모드가 없으면 아예 보여주지 않습니다.
            if (Patches.GoblinHeadScaleCompat.Active)
            {
                bool oldHarHeadSizeExemption = Settings.harHeadSizeExemption;
                listing.CheckboxLabeled("MUGB_TuningHarHeadExemption".Translate(), ref Settings.harHeadSizeExemption,
                    "MUGB_TuningHarHeadExemptionDesc".Translate());
                bool oldAddonFollowHeadScale = Settings.addonFollowHeadScale;
                listing.CheckboxLabeled("MUGB_TuningAddonFollowHead".Translate(), ref Settings.addonFollowHeadScale,
                    "MUGB_TuningAddonFollowHeadDesc".Translate());
                if (Settings.harHeadSizeExemption != oldHarHeadSizeExemption
                    || Settings.addonFollowHeadScale != oldAddonFollowHeadScale)
                {
                    // 예외처리는 렌더 트리를 새로 만들 때 적용되므로, 선택 폰만이 아니라
                    // 지도 위 고블린 전체를 다시 만들게 합니다.
                    visualChanged = true;
                    MarkGoblinGraphicsDirty();
                }
            }
            listing.GapLine();
            visualChanged |= DrawExportControls(listing);
            listing.GapLine();
            if (DrawSectionHeader(listing, "MUGB_TuningRenderScale".Translate(), ref showRenderScale))
            {
                listing.Label("MUGB_TuningRenderScaleDesc".Translate());
                visualChanged |= DrawVisualScaleControls(listing);
            }
            if (DrawSectionHeader(listing, "MUGB_TuningHeadBodyGlobal".Translate(), ref showHeadBody))
            {
                listing.Label("MUGB_TuningHeadBodyGlobalDesc".Translate());
                visualChanged |= DrawOffsetControls(listing, "MUGB_TuningHeadGlobal".Translate(), ref Settings.headHorizontalOffset, ref Settings.headVerticalOffset);
                visualChanged |= DrawOffsetControls(listing, "MUGB_TuningBodyGlobal".Translate(), ref Settings.bodyHorizontalOffset, ref Settings.bodyVerticalOffset);
                listing.Label("MUGB_TuningPawnAltitudeDesc".Translate());
                visualChanged |= DrawFloatControl(listing, "MUGB_TuningPawnAltitude".Translate(), ref Settings.pawnDrawAltitudeOffset, -1f, 1f, MUGBVisualTuningDefaults.PawnDrawAltitudeOffset);
            }
            if (DrawSectionHeader(listing, "MUGB_TuningAddonGlobal".Translate(), ref showGlobalAddons))
            {
                listing.Label("MUGB_TuningAddonGlobalDesc".Translate());
                visualChanged |= DrawAddonControls(listing, DisplayTuningToken("All"), ref Settings.addonHorizontalOffset, ref Settings.addonVerticalOffset, ref Settings.addonScale, ref Settings.addonLayerOffset);
                visualChanged |= DrawAddonControls(listing, DisplayTuningToken("Ears"), ref Settings.earOffsetX, ref Settings.earOffsetY, ref Settings.earScale, ref Settings.earLayerOffset);
                visualChanged |= DrawAddonControls(listing, DisplayTuningToken("Eyes"), ref Settings.eyeOffsetX, ref Settings.eyeOffsetY, ref Settings.eyeScale, ref Settings.eyeLayerOffset);
                visualChanged |= DrawAddonControls(listing, DisplayTuningToken("Nose"), ref Settings.noseOffsetX, ref Settings.noseOffsetY, ref Settings.noseScale, ref Settings.noseLayerOffset);
                visualChanged |= DrawAddonControls(listing, DisplayTuningToken("Mouth"), ref Settings.mouthOffsetX, ref Settings.mouthOffsetY, ref Settings.mouthScale, ref Settings.mouthLayerOffset);
            }
            if (DrawSectionHeader(listing, "MUGB_TuningDirectionalFine".Translate(), ref showFineTune))
            {
                listing.Label("MUGB_TuningDirectionalFineDesc".Translate());
                visualChanged |= DrawSelectedDirectionalEditor(listing);
            }
            if (DrawSectionHeader(listing, "MUGB_TuningOverview".Translate(), ref showOverview))
            {
                DrawGroupedOverview(listing);
            }
            listing.End();
            if (visualChanged)
            {
                Settings.Write();
                MarkPreviewPawnGraphicsDirty();
                MarkApparelPreviewDirty();
            }
        }

        public static void OpenApparelTuningWindow()
        {
            if (Find.WindowStack.WindowOfType<Dialog_MUGBApparelTuning>() == null)
            {
                apparelFormPawnId = -1;
                Find.WindowStack.Add(new Dialog_MUGBApparelTuning());
            }
        }

        private static bool DrawSectionHeader(Listing_Standard listing, string label, ref bool open)
        {
            if (listing.ButtonText($"{(open ? "[-]" : "[+]")} {label}"))
            {
                open = !open;
            }
            return open;
        }

        private static bool DrawSelectedRaceHeadBodyControls(Listing_Standard listing)
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (!GoblinUtility.IsSupportedTuningPawn(pawn) || GoblinUtility.IsGoblin(pawn))
            {
                listing.Label("MUGB_TuningSelectRacePawn".Translate());
                return false;
            }

            string profile = GoblinUtility.TuningProfileKey(pawn);
            Rot4 rot = Rotations[selectedRotIndex];
            bool changed = false;
            listing.Label("MUGB_TuningSelectedRace".Translate(GoblinUtility.TuningProfileLabel(pawn), pawn.def.defName));
            DrawRotationDropdown(listing, "Direction", Rotations, selectedRotIndex, delegate(int value)
            {
                selectedRotIndex = value;
            });
            changed |= DrawSelectedRacePartControls(listing, profile, "Head", rot);
            changed |= DrawSelectedRacePartControls(listing, profile, "Body", rot);
            return changed;
        }

        private static bool DrawSelectedRacePartControls(Listing_Standard listing, string profile, string part, Rot4 rot)
        {
            string key = $"{profile}.{part}";
            float x = Settings.GetDirectionalOffsetX(key, rot);
            float y = Settings.GetDirectionalOffsetY(key, rot);
            float scale = Settings.GetDirectionalScale(key, rot);
            float layer = Settings.GetDirectionalLayerOffset(key, rot);
            bool changed = false;
            listing.Label($"{DisplayTuningToken(part)} / {DisplayRotation(rot)}");
            changed |= DrawFloatControl(listing, $"{key}.{RotKey(rot)} X", ref x, -2f, 2f, 0f);
            Settings.SetDirectionalOffsetX(key, rot, x);
            changed |= DrawFloatControl(listing, $"{key}.{RotKey(rot)} Y", ref y, -2f, 2f, 0f);
            Settings.SetDirectionalOffsetY(key, rot, y);
            changed |= DrawFloatControl(listing, $"{key}.{RotKey(rot)} scale", ref scale, 0.5f, 1.6f, 1f);
            Settings.SetDirectionalScale(key, rot, scale);
            changed |= DrawFloatControl(listing, $"{key}.{RotKey(rot)} layer", ref layer, -50f, 50f, 0f);
            Settings.SetDirectionalLayerOffset(key, rot, layer);
            return changed;
        }

        private static bool DrawExportControls(Listing_Standard listing)
        {
            bool changed = false;
            if (listing.ButtonText("MUGB_TuningCopyVisual".Translate()))
            {
                GUIUtility.systemCopyBuffer = Settings.ExportVisualTuning();
                Messages.Message("MUGB_TuningVisualCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            if (listing.ButtonText("MUGB_TuningExportVisual".Translate()))
            {
                ExportVisualTuning();
            }
            if (listing.ButtonText("MUGB_TuningResetAllVisual".Translate()))
            {
                Settings.ResetVisualTuning();
                changed = true;
            }

            return changed;
        }

        private static void ExportVisualTuning()
        {
            string path = Path.Combine(ContentPack.RootDir, "MGB_VisualTuning_Export.txt");
            File.WriteAllText(path, Settings.ExportVisualTuning(), Encoding.UTF8);
            Messages.Message("MUGB_TuningVisualExported".Translate(path), MessageTypeDefOf.TaskCompletion, false);
        }

        private static void ExportApparelTuning()
        {
            string path = Path.Combine(ContentPack.RootDir, "MGB_ApparelTuning_Export.txt");
            File.WriteAllText(path, Settings.ExportApparelTuning(), Encoding.UTF8);
            Messages.Message("MUGB_TuningApparelExported".Translate(path), MessageTypeDefOf.TaskCompletion, false);
        }

        internal static void MarkGoblinGraphicsDirty()
        {
            if (Current.Game == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (GoblinUtility.IsGoblin(pawn))
                    {
                        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    }
                }
            }
        }

        private static void MarkPreviewPawnGraphicsDirty()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (GoblinUtility.IsGoblin(pawn))
            {
                MarkPawnGraphicsDirty(pawn);
                return;
            }

            MarkGoblinGraphicsDirty();
        }

        private static void MarkPawnGraphicsDirty(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            // 한국어 참고: 튜닝 중에는 선택 폰만 다시 그려 프리뷰 반응성과 성능을 같이 챙깁니다.
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
        }

        private static bool DrawAddonControls(Listing_Standard listing, string label, ref float offsetX, ref float offsetY, ref float scale, ref float layer)
        {
            bool changed = false;
            listing.Label($"{label}: X/Y/Scale/Layer");
            changed |= DrawFloatControl(listing, $"{label} X", ref offsetX, -0.6f, 0.6f, 0f);
            changed |= DrawFloatControl(listing, $"{label} Y", ref offsetY, -0.6f, 0.6f, 0f);
            changed |= DrawFloatControl(listing, $"{label} Scale", ref scale, 0.5f, 1.8f, 1f);
            changed |= DrawFloatControl(listing, $"{label} Layer", ref layer, -50f, 50f, 0f);
            return changed;
        }

        private static bool DrawSelectedDirectionalEditor(Listing_Standard listing)
        {
            bool changed = false;
            BuildVisualFormOptions(out List<string> formKeys, out string[] formLabels);
            List<string> capturedFormKeys = formKeys;
            selectedFormIndex = Mathf.Max(0, formKeys.IndexOf(selectedVisualFormKey));
            DrawIndexedDropdown(listing, "Form", formLabels, selectedFormIndex, delegate(int value)
            {
                selectedFormIndex = value;
                selectedVisualFormKey = capturedFormKeys[value];
            });
            DrawStringDropdown(listing, "Part", Parts, ref selectedPartIndex);
            DrawRotationDropdown(listing, "Direction", Rotations, selectedRotIndex, delegate(int value)
            {
                selectedRotIndex = value;
            });

            string key = $"{selectedVisualFormKey}.{Parts[selectedPartIndex]}";
            Rot4 rot = Rotations[selectedRotIndex];
            listing.Label("MUGB_TuningEditing".Translate($"{DisplayTuningToken(selectedVisualFormKey)}.{DisplayTuningToken(Parts[selectedPartIndex])}", DisplayRotation(rot)));

            float x = Settings.GetDirectionalOffsetX(key, rot);
            float y = Settings.GetDirectionalOffsetY(key, rot);
            float oldX = x;
            float oldY = y;
            changed |= DrawFloatControl(listing, $"{key} {RotKey(rot)} X", ref x, -2f, 2f, Settings.GetDirectionalDefaultOffsetX(key, rot));
            Settings.SetDirectionalOffsetX(key, rot, x);
            changed |= DrawFloatControl(listing, $"{key} {RotKey(rot)} Y", ref y, -2f, 2f, Settings.GetDirectionalDefaultOffsetY(key, rot));
            Settings.SetDirectionalOffsetY(key, rot, y);

            if (IsBodyOrHead(Parts[selectedPartIndex]))
            {
                float scale = Settings.GetDirectionalScale(key, rot);
                float layer = Settings.GetDirectionalLayerOffset(key, rot);
                changed |= DrawFloatControl(listing, $"{key} {RotKey(rot)} render-node scale", ref scale, 0.5f, 1.5f, Settings.GetDirectionalDefaultScale(key, rot));
                Settings.SetDirectionalScale(key, rot, scale);
                changed |= DrawFloatControl(listing, $"{key} {RotKey(rot)} render-node layer", ref layer, -5f, 5f, Settings.GetDirectionalDefaultLayerOffset(key, rot));
                Settings.SetDirectionalLayerOffset(key, rot, layer);
            }
            else
            {
                float scale = Settings.GetDirectionalScale(key, rot);
                float layer = Settings.GetDirectionalLayerOffset(key, rot);
                changed |= DrawFloatControl(listing, $"{key} {RotKey(rot)} scale", ref scale, 0.4f, 2f, Settings.GetDirectionalDefaultScale(key, rot));
                Settings.SetDirectionalScale(key, rot, scale);
                changed |= DrawFloatControl(listing, $"{key} {RotKey(rot)} layer", ref layer, -50f, 50f, Settings.GetDirectionalDefaultLayerOffset(key, rot));
                Settings.SetDirectionalLayerOffset(key, rot, layer);
            }

            if (listing.ButtonText("MUGB_TuningResetSelected".Translate($"{DisplayTuningToken(selectedVisualFormKey)}.{DisplayTuningToken(Parts[selectedPartIndex])} / {DisplayRotation(rot)}")))
            {
                changed = true;
                Settings.ResetDirectionalValues(key, rot);
            }

            return changed || Mathf.Abs(x - oldX) > 0.0001f || Mathf.Abs(y - oldY) > 0.0001f;
        }

        public static void DrawApparelTuningControls(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("MUGB_TuningApparelIntro".Translate());
            listing.Label("MUGB_TuningApparelPreviewDesc".Translate());
            listing.GapLine();

            Pawn pawn = ResolveApparelTuningPawn();
            if (!GoblinUtility.IsSupportedTuningPawn(pawn))
            {
                listing.Label(GoblinUtility.TuningUnsupportedReason(pawn));
                listing.End();
                return;
            }

            DrawApparelPawnDropdown(listing, pawn);
            SyncApparelFormToSelectedPawn(pawn);
            DrawIndexedDropdown(listing, "Category", ApparelCategories, selectedApparelCategoryIndex, delegate(int value)
            {
                selectedApparelCategoryIndex = value;
                selectedApparelIndex = 0;
                selectedApparelStyle = null;
                selectWornApparelOnNextDraw = true;
                wearableApparelCacheKey = null;
                MarkApparelPreviewDirty();
            });
            if (GoblinUtility.IsGoblin(pawn))
            {
                DrawIndexedDropdown(listing, "Form", ApparelForms, selectedApparelFormIndex, delegate(int value)
                {
                    selectedApparelFormIndex = value;
                    MarkApparelPreviewDirty();
                });
            }
            else
            {
                listing.Label("MUGB_TuningRaceProfile".Translate(GoblinUtility.TuningProfileLabel(pawn), pawn.def.defName));
            }
            DrawRotationDropdown(listing, "Direction", Rotations, selectedApparelRotIndex, delegate(int value)
            {
                selectedApparelRotIndex = value;
                MarkApparelPreviewDirty();
            });

            string filterCategory = ApparelCategories[selectedApparelCategoryIndex];
            List<ThingDef> wearable = WearableApparelForCategory(pawn, filterCategory);
            if (wearable.Count == 0)
            {
                listing.Gap();
                listing.Label("MUGB_TuningNoDefinitions".Translate(DisplayTuningToken(filterCategory)));
                listing.End();
                return;
            }

            SelectWornApparelIfRequested(pawn, filterCategory, wearable);
            if (selectedApparelIndex < 0 || selectedApparelIndex >= wearable.Count)
            {
                selectedApparelIndex = 0;
            }

            ThingDef apparelDef = wearable[selectedApparelIndex];
            string category = Patches.GoblinRenderNodeUtility.TuningCategoryFor(apparelDef);
            DrawApparelPreview(listing, pawn, Rotations[selectedApparelRotIndex], apparelDef);
            DrawApparelDropdown(listing, wearable);
            DrawApparelStyleDropdown(listing, apparelDef);
            listing.CheckboxLabeled("MUGB_TuningApplyScaleAll".Translate(), ref applyApparelScaleToAllDirections,
                "MUGB_TuningApplyScaleAllDesc".Translate());
            listing.CheckboxLabeled("MUGB_TuningMirrorEastWest".Translate(), ref mirrorApparelEastWest,
                "MUGB_TuningMirrorEastWestDesc".Translate());
            string form = ApparelTuningProfileKey(pawn);
            Rot4 rot = Rotations[selectedApparelRotIndex];
            string defName = apparelDef.defName;

            listing.Gap();
            listing.Label("MUGB_TuningPawn".Translate(pawn.LabelShortCap));
            listing.Label("MUGB_TuningTarget".Translate(apparelDef.LabelCap, defName));
            if (apparelDef.modContentPack != null)
            {
                listing.Label("MUGB_TuningSourceMod".Translate(apparelDef.modContentPack.Name));
            }

            float x = Settings.GetRenderTargetOffsetX(category, apparelDef, form, rot);
            float y = Settings.GetRenderTargetOffsetY(category, apparelDef, form, rot);
            float scale = Settings.GetRenderTargetScale(category, apparelDef, form, rot);
            float layer = Settings.GetRenderTargetLayerOffset(category, apparelDef, form, rot);
            float oldX = x;
            float oldY = y;
            float oldScale = scale;
            float oldLayer = layer;
            bool apparelChanged = false;

            apparelChanged |= DrawFloatControl(listing, $"{category}.{defName}.{form}.{RotKey(rot)} X", ref x, -2f, 2f, Settings.GetRenderTargetDefaultOffsetX(category, apparelDef, form, rot));
            Settings.SetRenderTargetOffsetX(category, apparelDef, form, rot, x);
            apparelChanged |= DrawFloatControl(listing, $"{category}.{defName}.{form}.{RotKey(rot)} Y", ref y, -2f, 2f, Settings.GetRenderTargetDefaultOffsetY(category, apparelDef, form, rot));
            Settings.SetRenderTargetOffsetY(category, apparelDef, form, rot, y);
            apparelChanged |= DrawFloatControl(listing, $"{category}.{defName}.{form}.{RotKey(rot)} scale", ref scale, 0.5f, 1.6f, Settings.GetRenderTargetDefaultScale(category, apparelDef, form, rot));
            if (applyApparelScaleToAllDirections)
            {
                foreach (Rot4 direction in Rotations)
                {
                    Settings.SetRenderTargetScale(category, apparelDef, form, direction, scale);
                }
            }
            else
            {
                Settings.SetRenderTargetScale(category, apparelDef, form, rot, scale);
            }
            apparelChanged |= DrawFloatControl(listing, $"{category}.{defName}.{form}.{RotKey(rot)} layer", ref layer, -50f, 50f, Settings.GetRenderTargetDefaultLayerOffset(category, apparelDef, form, rot));
            Settings.SetRenderTargetLayerOffset(category, apparelDef, form, rot, layer);
            if (mirrorApparelEastWest && TryGetMirrorRot(rot, out Rot4 mirrorRot))
            {
                Settings.SetRenderTargetOffsetX(category, apparelDef, form, mirrorRot, -x);
                Settings.SetRenderTargetOffsetY(category, apparelDef, form, mirrorRot, y);
                Settings.SetRenderTargetScale(category, apparelDef, form, mirrorRot, scale);
                Settings.SetRenderTargetLayerOffset(category, apparelDef, form, mirrorRot, layer);
            }
            if (apparelChanged || Mathf.Abs(x - oldX) > 0.0001f || Mathf.Abs(y - oldY) > 0.0001f || Mathf.Abs(scale - oldScale) > 0.0001f || Mathf.Abs(layer - oldLayer) > 0.0001f)
            {
                MarkApparelPreviewDirty();
            }

            listing.GapLine();
            if (listing.ButtonText("MUGB_TuningResetSelected".Translate($"{DisplayTuningToken(category)} / {defName} / {DisplayTuningToken(form)} / {DisplayRotation(rot)}")))
            {
                Settings.ResetRenderTargetValues(category, apparelDef, form, rot);
                if (mirrorApparelEastWest && TryGetMirrorRot(rot, out Rot4 resetMirrorRot))
                {
                    Settings.ResetRenderTargetValues(category, apparelDef, form, resetMirrorRot);
                }
                MarkApparelPreviewDirty();
                apparelChanged = true;
            }
            if (listing.ButtonText("MUGB_TuningCopySelectedApparel".Translate()))
            {
                GUIUtility.systemCopyBuffer = Settings.ExportRenderTargetTuning(category, apparelDef);
                Messages.Message("MUGB_TuningSelectedApparelCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            if (listing.ButtonText("MUGB_TuningCopyAllApparel".Translate()))
            {
                GUIUtility.systemCopyBuffer = Settings.ExportApparelTuning();
                Messages.Message("MUGB_TuningAllApparelCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            if (listing.ButtonText("MUGB_TuningExportAllApparel".Translate()))
            {
                ExportApparelTuning();
            }

            listing.End();
            if (apparelChanged)
            {
                Settings.Write();
            }
        }

        private static Pawn ResolveApparelTuningPawn()
        {
            Pawn selected = Find.Selector?.SingleSelectedThing as Pawn;
            int selectedId = GoblinUtility.IsSupportedTuningPawn(selected) ? selected.thingIDNumber : -1;
            if (selectedId != lastObservedMapSelectedPawnId)
            {
                lastObservedMapSelectedPawnId = selectedId;
                if (selectedId >= 0)
                {
                    selectedApparelPawnId = selectedId;
                }
            }

            List<Pawn> candidates = ApparelTuningPawns();
            Pawn remembered = candidates.FirstOrDefault(candidate => candidate.thingIDNumber == selectedApparelPawnId);
            if (remembered != null)
            {
                return remembered;
            }

            Pawn fallback = candidates.FirstOrDefault();
            selectedApparelPawnId = fallback?.thingIDNumber ?? -1;
            return fallback;
        }

        private static List<Pawn> ApparelTuningPawns()
        {
            List<Pawn> candidates = new List<Pawn>();
            if (Current.Game == null)
            {
                return candidates;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (GoblinUtility.IsSupportedTuningPawn(pawn))
                    {
                        candidates.Add(pawn);
                    }
                }
            }

            candidates.Sort((left, right) =>
            {
                int profileOrder = string.Compare(GoblinUtility.TuningProfileLabel(left), GoblinUtility.TuningProfileLabel(right), System.StringComparison.CurrentCulture);
                return profileOrder != 0
                    ? profileOrder
                    : string.Compare(left.LabelShortCap, right.LabelShortCap, System.StringComparison.CurrentCulture);
            });
            return candidates;
        }

        private static void DrawApparelPawnDropdown(Listing_Standard listing, Pawn selectedPawn)
        {
            if (!listing.ButtonText("MUGB_TuningTargetPawn".Translate(selectedPawn.LabelShortCap, GoblinUtility.TuningProfileLabel(selectedPawn))))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn pawn in ApparelTuningPawns())
            {
                Pawn optionPawn = pawn;
                string label = $"{pawn.LabelShortCap} · {GoblinUtility.TuningProfileLabel(pawn)}\n<size=10><color=#AAAAAA>{pawn.def.defName}</color></size>";
                options.Add(new FloatMenuOption(label, delegate
                {
                    selectedApparelPawnId = optionPawn.thingIDNumber;
                    apparelFormPawnId = -1;
                    selectedApparelIndex = 0;
                    selectedApparelStyle = null;
                    selectWornApparelOnNextDraw = true;
                    wearableApparelCacheKey = null;
                    MarkApparelPreviewDirty();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static bool TryGetMirrorRot(Rot4 rot, out Rot4 mirrorRot)
        {
            if (rot == Rot4.East)
            {
                mirrorRot = Rot4.West;
                return true;
            }

            if (rot == Rot4.West)
            {
                mirrorRot = Rot4.East;
                return true;
            }

            mirrorRot = Rot4.Invalid;
            return false;
        }

        private static void BuildVisualFormOptions(out List<string> keys, out string[] labels)
        {
            keys = new List<string>(VisualFormKeys);
            List<string> labelList = new List<string>(VisualFormLabels);
            foreach (Pawn pawn in ApparelTuningPawns())
            {
                if (GoblinUtility.IsGoblin(pawn))
                {
                    continue;
                }

                string key = GoblinUtility.TuningProfileKey(pawn);
                if (key.NullOrEmpty() || keys.Contains(key))
                {
                    continue;
                }

                keys.Add(key);
                labelList.Add($"{GoblinUtility.TuningProfileLabel(pawn)} ({pawn.def.defName})");
            }
            labels = labelList.ToArray();
        }

        private static void DrawApparelPreview(Listing_Standard listing, Pawn pawn, Rot4 rot, ThingDef apparelDef)
        {
            Rect rect = listing.GetRect(260f);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.55f));
            Widgets.DrawBox(rect);

            Rect labelRect = new Rect(rect.x + 58f, rect.y + 7f, rect.width - 116f, 24f);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Widgets.Label(labelRect, "MUGB_TuningDirectionValue".Translate(DisplayRotation(rot)));
            Text.Font = oldFont;
            DrawApparelPreviewZoomButtons(rect);

            float portraitSize = Mathf.Min(210f, rect.width - 124f);
            Rect portraitRect = new Rect(rect.center.x - portraitSize * 0.5f, rect.y + 34f, portraitSize, 220f);
            RenderTexture texture = GetApparelPreviewTexture(pawn, rot, apparelDef);
            if (texture != null)
            {
                GUI.DrawTexture(portraitRect, texture, ScaleMode.ScaleToFit, true);
            }

            Rect leftArrowRect = new Rect(rect.x + 8f, rect.center.y - 27f, 46f, 54f);
            Rect rightArrowRect = new Rect(rect.xMax - 54f, rect.center.y - 27f, 46f, 54f);
            if (DrawPreviewRotationButton(leftArrowRect, TexUI.ArrowTexLeft, "MUGB_TuningPreviousDirection".Translate()))
            {
                RotateApparelPreview(-1);
            }
            if (DrawPreviewRotationButton(rightArrowRect, TexUI.ArrowTexRight, "MUGB_TuningNextDirection".Translate()))
            {
                RotateApparelPreview(1);
            }

            listing.Gap();
        }

        private static void DrawApparelPreviewZoomButtons(Rect rect)
        {
            Rect minusRect = new Rect(rect.xMax - 104f, rect.y + 7f, 30f, 24f);
            Rect resetRect = new Rect(rect.xMax - 72f, rect.y + 7f, 36f, 24f);
            Rect plusRect = new Rect(rect.xMax - 34f, rect.y + 7f, 30f, 24f);
            if (Widgets.ButtonText(minusRect, "-"))
            {
                apparelPreviewZoom = Mathf.Max(0.75f, apparelPreviewZoom - 0.25f);
                MarkApparelPreviewDirty();
            }
            TooltipHandler.TipRegion(minusRect, "MUGB_TuningZoomOut".Translate());

            if (Widgets.ButtonText(resetRect, $"{apparelPreviewZoom:0.0}x"))
            {
                apparelPreviewZoom = 1.25f;
                MarkApparelPreviewDirty();
            }
            TooltipHandler.TipRegion(resetRect, "MUGB_TuningZoomReset".Translate());

            if (Widgets.ButtonText(plusRect, "+"))
            {
                apparelPreviewZoom = Mathf.Min(2f, apparelPreviewZoom + 0.25f);
                MarkApparelPreviewDirty();
            }
            TooltipHandler.TipRegion(plusRect, "MUGB_TuningZoomIn".Translate());
        }

        private static bool DrawPreviewRotationButton(Rect rect, Texture2D icon, string tooltip)
        {
            bool hovered = Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(rect, hovered
                ? new Color(0.3f, 0.32f, 0.34f, 0.95f)
                : new Color(0.18f, 0.19f, 0.21f, 0.92f));
            Widgets.DrawBox(rect, hovered ? 2 : 1);
            TooltipHandler.TipRegion(rect, tooltip);
            return Widgets.ButtonImage(rect.ContractedBy(10f), icon, Color.white, Color.white);
        }

        private static void RotateApparelPreview(int direction)
        {
            Rot4 current = Rotations[selectedApparelRotIndex];
            int currentIndex = System.Array.IndexOf(PreviewRotationOrder, current);
            int nextIndex = (currentIndex + direction + PreviewRotationOrder.Length) % PreviewRotationOrder.Length;
            selectedApparelRotIndex = System.Array.IndexOf(Rotations, PreviewRotationOrder[nextIndex]);
            MarkApparelPreviewDirty();
        }

        private static RenderTexture GetApparelPreviewTexture(Pawn pawn, Rot4 rot, ThingDef apparelDef)
        {
            EnsureApparelPreviewTexture();
            string key = $"{pawn.thingIDNumber}.{ApparelTuningProfileKey(pawn)}.{rot.AsInt}.{apparelDef?.defName}.{selectedApparelStyle?.defName ?? "Standard"}.{apparelPreviewZoom:0.00}";
            if (apparelPreviewDirty || apparelPreviewKey != key || !apparelPreviewTexture.IsCreated())
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = apparelPreviewTexture;
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                RenderTexture.active = previous;

                RenderPawnWithPreviewApparel(pawn, apparelDef, selectedApparelStyle, rot);
                apparelPreviewKey = key;
                apparelPreviewDirty = false;
            }

            return apparelPreviewTexture;
        }

        private static void EnsureApparelPreviewTexture()
        {
            if (apparelPreviewTexture != null && apparelPreviewTexture.width == 256 && apparelPreviewTexture.height == 256)
            {
                return;
            }

            if (apparelPreviewTexture != null)
            {
                apparelPreviewTexture.Release();
                UnityEngine.Object.Destroy(apparelPreviewTexture);
            }

            apparelPreviewTexture = new RenderTexture(256, 256, 24)
            {
                name = "MUGB_ApparelPreview",
                useMipMap = false,
                filterMode = FilterMode.Bilinear
            };
            apparelPreviewDirty = true;
        }

        private static void MarkApparelPreviewDirty()
        {
            apparelPreviewDirty = true;
        }

        private static void SyncApparelFormToSelectedPawn(Pawn pawn)
        {
            if (pawn == null || apparelFormPawnId == pawn.thingIDNumber)
            {
                return;
            }

            apparelFormPawnId = pawn.thingIDNumber;
            selectedApparelFormIndex = GoblinUtility.IsHobgoblin(pawn) ? 1 : 0;
            selectedApparelIndex = 0;
            selectedApparelStyle = null;
            selectWornApparelOnNextDraw = true;
            wearableApparelCacheKey = null;
            MarkApparelPreviewDirty();
        }

        private static string ApparelTuningProfileKey(Pawn pawn)
        {
            return GoblinUtility.IsGoblin(pawn) ? ApparelForms[selectedApparelFormIndex] : GoblinUtility.TuningProfileKey(pawn);
        }

        private static void SelectWornApparelIfRequested(Pawn pawn, string category, List<ThingDef> wearable)
        {
            if (!selectWornApparelOnNextDraw)
            {
                return;
            }

            selectWornApparelOnNextDraw = false;
            if (pawn?.apparel?.WornApparel == null)
            {
                return;
            }

            foreach (Apparel worn in pawn.apparel.WornApparel)
            {
                if (!Patches.GoblinRenderNodeUtility.MatchesTuningFilter(worn.def, category))
                {
                    continue;
                }

                int index = wearable.IndexOf(worn.def);
                if (index >= 0)
                {
                    selectedApparelIndex = index;
                    selectedApparelStyle = worn.StyleDef;
                    MarkApparelPreviewDirty();
                    return;
                }
            }
        }

        private static List<ThingDef> WearableApparelForCategory(Pawn pawn, string category)
        {
            string cacheKey = $"{pawn?.thingIDNumber}.{category}";
            if (wearableApparelCacheKey == cacheKey)
            {
                return WearableApparelCache;
            }

            WearableApparelCache.Clear();
            wearableApparelCacheKey = cacheKey;
            if (pawn == null)
            {
                return WearableApparelCache;
            }

            if (pawn.apparel?.WornApparel != null)
            {
                foreach (Apparel worn in pawn.apparel.WornApparel)
                {
                    if (Patches.GoblinRenderNodeUtility.MatchesTuningFilter(worn.def, category)
                        && !WearableApparelCache.Contains(worn.def))
                    {
                        WearableApparelCache.Add(worn.def);
                    }
                }
            }

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.IsApparel || def.apparel == null)
                {
                    continue;
                }

                if (Patches.GoblinRenderNodeUtility.MatchesTuningFilter(def, category))
                {
                    if (!WearableApparelCache.Contains(def))
                    {
                        WearableApparelCache.Add(def);
                    }
                }
            }

            WearableApparelCache.Sort((left, right) =>
            {
                if (category == "Headgear")
                {
                    bool leftWarVeil = left.defName == "Apparel_WarVeil";
                    bool rightWarVeil = right.defName == "Apparel_WarVeil";
                    if (leftWarVeil != rightWarVeil)
                    {
                        return leftWarVeil ? -1 : 1;
                    }
                }
                return string.Compare(left.LabelCap, right.LabelCap, System.StringComparison.CurrentCulture);
            });
            return WearableApparelCache;
        }

        private static void DrawApparelDropdown(Listing_Standard listing, List<ThingDef> wearable)
        {
            ThingDef selected = wearable[selectedApparelIndex];
            Rect row = listing.GetRect(54f);
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.DrawBox(row);
            Widgets.ThingIcon(new Rect(row.x + 5f, row.y + 5f, 44f, 44f), selected);
            Rect nameRect = new Rect(row.x + 57f, row.y + 5f, row.width - 64f, 24f);
            Rect detailRect = new Rect(row.x + 57f, row.y + 29f, row.width - 64f, 20f);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Widgets.Label(nameRect, selected.LabelCap);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(detailRect, $"{selected.defName} · {ApparelSourceName(selected)}");
            GUI.color = oldColor;
            Text.Font = oldFont;
            TooltipHandler.TipRegion(row, $"{selected.LabelCap}\n{selected.defName}\n{ApparelSourceName(selected)}");

            if (Widgets.ButtonInvisible(row))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = 0; i < wearable.Count; i++)
                {
                    int optionIndex = i;
                    ThingDef def = wearable[i];
                    FloatMenuOption option = new FloatMenuOption($"{def.LabelCap}\n<size=10><color=#AAAAAA>{def.defName} · {ApparelSourceName(def)}</color></size>", delegate
                    {
                        selectedApparelIndex = optionIndex;
                        selectedApparelStyle = null;
                        MarkApparelPreviewDirty();
                    }, def);
                    option.tooltip = new TipSignal($"{def.LabelCap}\n{def.defName}\n{ApparelSourceName(def)}");
                    options.Add(option);
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static string ApparelSourceName(ThingDef def)
        {
            return def?.modContentPack?.Name ?? "MUGB_TuningCore".Translate();
        }

        private static void DrawApparelStyleDropdown(Listing_Standard listing, ThingDef apparelDef)
        {
            List<ThingStyleDef> styles = ApparelStylesFor(apparelDef);
            if (selectedApparelStyle != null && !styles.Contains(selectedApparelStyle))
            {
                selectedApparelStyle = null;
                MarkApparelPreviewDirty();
            }

            string selectedLabel = selectedApparelStyle?.LabelCap ?? "MUGB_TuningStandard".Translate();
            if (!listing.ButtonText("MUGB_TuningStyle".Translate(selectedLabel)))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("MUGB_TuningStandard".Translate(), delegate
                {
                    selectedApparelStyle = null;
                    MarkApparelPreviewDirty();
                }, apparelDef, null, true)
            };
            foreach (ThingStyleDef style in styles)
            {
                ThingStyleDef optionStyle = style;
                string label = optionStyle.LabelCap.NullOrEmpty() ? optionStyle.defName : optionStyle.LabelCap.ToString();
                options.Add(new FloatMenuOption(label, delegate
                {
                    selectedApparelStyle = optionStyle;
                    MarkApparelPreviewDirty();
                }, apparelDef, optionStyle));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<ThingStyleDef> ApparelStylesFor(ThingDef apparelDef)
        {
            ApparelStyleCache.Clear();
            if (apparelDef == null)
            {
                return ApparelStyleCache;
            }

            if (!apparelDef.randomStyle.NullOrEmpty())
            {
                foreach (ThingStyleChance chance in apparelDef.randomStyle)
                {
                    if (chance?.StyleDef != null && !ApparelStyleCache.Contains(chance.StyleDef))
                    {
                        ApparelStyleCache.Add(chance.StyleDef);
                    }
                }
            }

            foreach (StyleCategoryDef category in DefDatabase<StyleCategoryDef>.AllDefsListForReading)
            {
                if (category.thingDefStyles.NullOrEmpty())
                {
                    continue;
                }

                foreach (ThingDefStyle mapping in category.thingDefStyles)
                {
                    if (mapping.ThingDef == apparelDef && mapping.StyleDef != null && !ApparelStyleCache.Contains(mapping.StyleDef))
                    {
                        ApparelStyleCache.Add(mapping.StyleDef);
                    }
                }
            }

            ApparelStyleCache.Sort((left, right) => string.Compare(left.LabelCap, right.LabelCap, System.StringComparison.CurrentCulture));
            return ApparelStyleCache;
        }

        private static Pawn GetApparelPreviewPawn(Pawn sourcePawn, ThingDef apparelDef, ThingStyleDef styleDef)
        {
            Apparel sourceApparel = sourcePawn?.apparel?.WornApparel?.FirstOrDefault(apparel => apparel.def == apparelDef);
            ThingDef stuff = apparelDef == null ? null : sourceApparel?.Stuff ?? GenStuff.DefaultStuffFor(apparelDef);
            string cacheKey = ApparelPreviewPawnKey(sourcePawn, apparelDef, stuff, styleDef, sourceApparel);
            if (ApparelPreviewPawns.TryGetValue(cacheKey, out Pawn cached))
            {
                return cached;
            }

            bool goblin = GoblinUtility.IsGoblin(sourcePawn);
            XenotypeDef xenotype = goblin
                ? (selectedApparelFormIndex == 1 ? MUGBDefOf.MUGB_Hobgoblin : MUGBDefOf.MUGB_Goblin)
                : sourcePawn?.genes?.Xenotype;
            PawnKindDef kind = goblin ? PawnKindDefOf.Colonist : sourcePawn.kindDef;
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                faction: null,
                context: PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                fixedBiologicalAge: sourcePawn.ageTracker.AgeBiologicalYearsFloat,
                fixedGender: goblin ? Gender.Male : sourcePawn.gender,
                forceNoIdeo: true,
                forcedXenotype: xenotype,
                developmentalStages: DevelopmentalStage.Adult,
                forceNoGear: true);
            Pawn previewPawn = PawnGenerator.GeneratePawn(request);
            if (goblin)
            {
                Gene crossEyed = previewPawn.genes?.GetGene(MUGBDefOf.MUGB_Gene_CrossEyed);
                if (GoblinUtility.IsCrossEyed(sourcePawn) && crossEyed == null)
                {
                    previewPawn.genes?.AddGene(MUGBDefOf.MUGB_Gene_CrossEyed, xenogene: false);
                }
                else if (!GoblinUtility.IsCrossEyed(sourcePawn) && crossEyed != null)
                {
                    previewPawn.genes.RemoveGene(crossEyed);
                }
                GoblinUtility.EnforceGoblinStoryGraphics(previewPawn);
            }
            CopyPawnPreviewAppearance(sourcePawn, previewPawn, copyHeadAndBody: !goblin);
            if (goblin)
            {
                GoblinUtility.EnforceGoblinStoryGraphics(previewPawn);
            }
            if (apparelDef != null && previewPawn.apparel != null)
            {
                Apparel previewApparel = ThingMaker.MakeThing(apparelDef, stuff) as Apparel;
                if (previewApparel != null)
                {
                    previewApparel.StyleDef = styleDef;
                    CopyApparelColor(sourceApparel, previewApparel);
                    previewPawn.apparel.Wear(previewApparel, dropReplacedApparel: false);
                    previewPawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    PortraitsCache.SetDirty(previewPawn);
                }
            }
            ApparelPreviewPawns[cacheKey] = previewPawn;
            return previewPawn;
        }

        private static string ApparelPreviewPawnKey(Pawn sourcePawn, ThingDef apparelDef, ThingDef stuff, ThingStyleDef styleDef, Apparel sourceApparel)
        {
            string hair = sourcePawn?.story?.hairDef?.defName ?? "none";
            string beard = sourcePawn?.style?.beardDef?.defName ?? "none";
            string faceTattoo = sourcePawn?.style?.FaceTattoo?.defName ?? "none";
            string bodyTattoo = sourcePawn?.style?.BodyTattoo?.defName ?? "none";
            string color = sourceApparel?.DrawColor.ToString() ?? "default";
            return $"{sourcePawn?.thingIDNumber ?? -1}.{ApparelTuningProfileKey(sourcePawn)}.{apparelDef?.defName ?? "none"}.{stuff?.defName ?? "none"}.{styleDef?.defName ?? "Standard"}.{hair}.{beard}.{faceTattoo}.{bodyTattoo}.{color}";
        }

        private static void CopyPawnPreviewAppearance(Pawn sourcePawn, Pawn previewPawn, bool copyHeadAndBody)
        {
            if (sourcePawn?.story != null && previewPawn.story != null)
            {
                if (copyHeadAndBody)
                {
                    previewPawn.story.headType = sourcePawn.story.headType;
                    previewPawn.story.bodyType = sourcePawn.story.bodyType;
                }
                previewPawn.story.hairDef = sourcePawn.story.hairDef;
                previewPawn.story.HairColor = sourcePawn.story.HairColor;
                previewPawn.story.skinColorOverride = sourcePawn.story.skinColorOverride;
            }
            if (sourcePawn?.style != null && previewPawn.style != null)
            {
                previewPawn.style.beardDef = sourcePawn.style.beardDef ?? BeardDefOf.NoBeard;
                previewPawn.style.FaceTattoo = sourcePawn.style.FaceTattoo ?? TattooDefOf.NoTattoo_Face;
                previewPawn.style.BodyTattoo = sourcePawn.style.BodyTattoo ?? TattooDefOf.NoTattoo_Body;
            }
        }

        private static void CopyApparelColor(Apparel source, Apparel target)
        {
            CompColorable sourceColor = source?.TryGetComp<CompColorable>();
            CompColorable targetColor = target?.TryGetComp<CompColorable>();
            if (sourceColor == null || targetColor == null)
            {
                return;
            }

            if (sourceColor.Active)
            {
                targetColor.SetColor(sourceColor.Color);
            }
            else if (targetColor.Active)
            {
                targetColor.Disable();
            }
        }

        private static void RenderPawnWithPreviewApparel(Pawn sourcePawn, ThingDef apparelDef, ThingStyleDef styleDef, Rot4 rot)
        {
            Pawn pawn = GetApparelPreviewPawn(sourcePawn, apparelDef, styleDef);
            Find.PawnCacheRenderer.RenderPawn(pawn, apparelPreviewTexture, Vector3.zero, apparelPreviewZoom, 0f, rot, true, true, true, false);
        }

        private static void DrawIndexedDropdown(Listing_Standard listing, string label, string[] options, int selectedIndex, System.Action<int> setter)
        {
            if (selectedIndex < 0 || selectedIndex >= options.Length)
            {
                selectedIndex = 0;
                setter(0);
            }

            if (listing.ButtonText($"{DisplayTuningToken(label)}: {DisplayTuningToken(options[selectedIndex])}"))
            {
                List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();
                for (int i = 0; i < options.Length; i++)
                {
                    int optionIndex = i;
                    menuOptions.Add(new FloatMenuOption(DisplayTuningToken(options[optionIndex]), delegate
                    {
                        setter(optionIndex);
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }
        }

        private static void DrawStringDropdown(Listing_Standard listing, string label, string[] options, ref int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= options.Length)
            {
                selectedIndex = 0;
            }

            int capturedIndex = selectedIndex;
            if (listing.ButtonText($"{DisplayTuningToken(label)}: {DisplayTuningToken(options[capturedIndex])}"))
            {
                List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();
                for (int i = 0; i < options.Length; i++)
                {
                    int optionIndex = i;
                    menuOptions.Add(new FloatMenuOption(DisplayTuningToken(options[optionIndex]), delegate
                    {
                        if (label == "Form")
                        {
                            selectedFormIndex = optionIndex;
                        }
                        else if (label == "Part")
                        {
                            selectedPartIndex = optionIndex;
                        }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }
        }

        private static void DrawRotationDropdown(Listing_Standard listing, string label, Rot4[] options, int selectedIndex, System.Action<int> setter)
        {
            if (selectedIndex < 0 || selectedIndex >= options.Length)
            {
                selectedIndex = 0;
                setter(0);
            }

            if (listing.ButtonText($"{DisplayTuningToken(label)}: {DisplayRotation(options[selectedIndex])}"))
            {
                List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();
                for (int i = 0; i < options.Length; i++)
                {
                    int optionIndex = i;
                    menuOptions.Add(new FloatMenuOption(DisplayRotation(options[optionIndex]), delegate
                    {
                        setter(optionIndex);
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }
        }

        private static bool IsBodyOrHead(string part)
        {
            return part == "Body" || part == "Head";
        }

        private static void DrawGroupedOverview(Listing_Standard listing)
        {
            string form = selectedVisualFormKey;
            listing.GapLine();
            listing.Label("MUGB_TuningGroupedOverview".Translate(DisplayTuningToken(form)));
            DrawOverviewGroup(listing, "MUGB_TuningAllAddons".Translate(), new[] { "EarLeft", "EarRight", "EyeLeft", "EyeRight", "Nose", "Mouth" });
            DrawOverviewGroup(listing, DisplayTuningToken("Eyes"), new[] { "EyeLeft", "EyeRight" });
            DrawOverviewGroup(listing, DisplayTuningToken("Ears"), new[] { "EarLeft", "EarRight" });
            DrawOverviewGroup(listing, DisplayTuningToken("Nose"), new[] { "Nose" });
            DrawOverviewGroup(listing, DisplayTuningToken("Mouth"), new[] { "Mouth" });
            DrawOverviewGroup(listing, $"{DisplayTuningToken("Head")}/{DisplayTuningToken("Body")}", new[] { "Head", "Body" });
        }

        private static void DrawOverviewGroup(Listing_Standard listing, string title, string[] parts)
        {
            listing.Label($"[{title}]");
            foreach (string part in parts)
            {
                foreach (Rot4 rot in Rotations)
                {
                    string key = $"{selectedVisualFormKey}.{part}";
                    string rotLabel = RotKey(rot);
                    float x = Settings.GetDirectionalOffsetX(key, rot);
                    float y = Settings.GetDirectionalOffsetY(key, rot);
                    string valueText;
                    if (IsBodyOrHead(part))
                    {
                        float scale = Settings.GetDirectionalScale(key, rot);
                        valueText = $"{DisplayTuningToken(part)} {DisplayRotation(rot)}: X {x:0.##}, Y {y:0.##}, S {scale:0.##}";
                    }
                    else
                    {
                        float scale = Settings.GetDirectionalScale(key, rot);
                        float layer = Settings.GetDirectionalLayerOffset(key, rot);
                        valueText = $"{DisplayTuningToken(part)} {DisplayRotation(rot)}: X {x:0.##}, Y {y:0.##}, S {scale:0.##}, L {layer:0.##}";
                    }

                    if (listing.ButtonText(valueText))
                    {
                        selectedPartIndex = System.Array.IndexOf(Parts, part);
                        selectedRotIndex = System.Array.IndexOf(Rotations, rot);
                    }
                }
            }
            listing.Gap();
        }

        private static string RotKey(Rot4 rot)
        {
            if (rot == Rot4.North)
            {
                return "north";
            }

            if (rot == Rot4.East)
            {
                return "east";
            }

            if (rot == Rot4.West)
            {
                return "west";
            }

            return "south";
        }

        private static string DisplayRotation(Rot4 rot)
        {
            return DisplayTuningToken(RotKey(rot));
        }

        private static string DisplayTuningToken(string token)
        {
            string key;
            switch (token)
            {
                case "Goblin": key = "MUGB_TuningTokenGoblin"; break;
                case "Hobgoblin": key = "MUGB_TuningTokenHobgoblin"; break;
                case "GoblinCrossEyed": key = "MUGB_TuningTokenGoblinCrossEyed"; break;
                case "HobgoblinCrossEyed": key = "MUGB_TuningTokenHobgoblinCrossEyed"; break;
                case "GoblinChild": key = "MUGB_TuningTokenGoblinChild"; break;
                case "GoblinCrossEyedChild": key = "MUGB_TuningTokenGoblinChildCrossEyed"; break;
                case "HobgoblinChild": key = "MUGB_TuningTokenHobgoblinChild"; break;
                case "HobgoblinCrossEyedChild": key = "MUGB_TuningTokenHobgoblinChildCrossEyed"; break;
                case "GoblinDessicated": key = "MUGB_TuningTokenGoblinDessicated"; break;
                case "HobgoblinDessicated": key = "MUGB_TuningTokenHobgoblinDessicated"; break;
                case "Goblin (cross-eyed)": key = "MUGB_TuningTokenGoblinCrossEyed"; break;
                case "Hobgoblin (cross-eyed)": key = "MUGB_TuningTokenHobgoblinCrossEyed"; break;
                case "Goblin child": key = "MUGB_TuningTokenGoblinChild"; break;
                case "Goblin child (cross-eyed)": key = "MUGB_TuningTokenGoblinChildCrossEyed"; break;
                case "Hobgoblin child": key = "MUGB_TuningTokenHobgoblinChild"; break;
                case "Hobgoblin child (cross-eyed)": key = "MUGB_TuningTokenHobgoblinChildCrossEyed"; break;
                case "Goblin (dessicated)": key = "MUGB_TuningTokenGoblinDessicated"; break;
                case "Hobgoblin (dessicated)": key = "MUGB_TuningTokenHobgoblinDessicated"; break;
                case "Form": key = "MUGB_TuningTokenForm"; break;
                case "Part": key = "MUGB_TuningTokenPart"; break;
                case "Direction": key = "MUGB_TuningTokenDirection"; break;
                case "Category": key = "MUGB_TuningTokenCategory"; break;
                case "Head": key = "MUGB_TuningTokenHead"; break;
                case "Body": key = "MUGB_TuningTokenBody"; break;
                case "EarLeft": key = "MUGB_TuningTokenEarLeft"; break;
                case "EarRight": key = "MUGB_TuningTokenEarRight"; break;
                case "EyeLeft": key = "MUGB_TuningTokenEyeLeft"; break;
                case "EyeRight": key = "MUGB_TuningTokenEyeRight"; break;
                case "Ears": key = "MUGB_TuningTokenEars"; break;
                case "Eyes": key = "MUGB_TuningTokenEyes"; break;
                case "Nose": key = "MUGB_TuningTokenNose"; break;
                case "Mouth": key = "MUGB_TuningTokenMouth"; break;
                case "All": key = "MUGB_TuningTokenAll"; break;
                case "Headgear": key = "MUGB_TuningTokenHeadgear"; break;
                case "Clothing": key = "MUGB_TuningTokenClothing"; break;
                case "Armor": key = "MUGB_TuningTokenArmor"; break;
                case "Outerwear": key = "MUGB_TuningTokenOuterwear"; break;
                case "Utility": key = "MUGB_TuningTokenUtility"; break;
                case "Shield": key = "MUGB_TuningTokenShield"; break;
                case "north": key = "MUGB_TuningTokenNorth"; break;
                case "south": key = "MUGB_TuningTokenSouth"; break;
                case "east": key = "MUGB_TuningTokenEast"; break;
                case "west": key = "MUGB_TuningTokenWest"; break;
                default: return token;
            }

            return key.Translate();
        }

        private static void DrawDirectionalAddonControls(Listing_Standard listing, string form, string key, string label)
        {
            listing.Label("MUGB_TuningDirectionalPart".Translate(DisplayTuningToken(label)));
            string scopedKey = $"{form}.{key}";
            DrawDirectionControls(listing, scopedKey, Rot4.South, "south");
            DrawDirectionControls(listing, scopedKey, Rot4.North, "north");
            DrawDirectionControls(listing, scopedKey, Rot4.East, "east");
            DrawDirectionControls(listing, scopedKey, Rot4.West, "west");
            listing.Gap();
        }

        private static bool DrawDirectionControls(Listing_Standard listing, string key, Rot4 rot, string label)
        {
            bool changed = false;
            float x = Settings.GetDirectionalOffsetX(key, rot);
            float y = Settings.GetDirectionalOffsetY(key, rot);
            float scale = Settings.GetDirectionalScale(key, rot);
            float layer = Settings.GetDirectionalLayerOffset(key, rot);

            changed |= DrawFloatControl(listing, $"{key} {label} X", ref x, -2.0f, 2.0f, Settings.GetDirectionalDefaultOffsetX(key, rot));
            Settings.SetDirectionalOffsetX(key, rot, x);
            changed |= DrawFloatControl(listing, $"{key} {label} Y", ref y, -2.0f, 2.0f, Settings.GetDirectionalDefaultOffsetY(key, rot));
            Settings.SetDirectionalOffsetY(key, rot, y);
            changed |= DrawFloatControl(listing, $"{key} {label} scale", ref scale, 0.4f, 2.0f, Settings.GetDirectionalDefaultScale(key, rot));
            Settings.SetDirectionalScale(key, rot, scale);
            changed |= DrawFloatControl(listing, $"{key} {label} layer", ref layer, -50f, 50f, Settings.GetDirectionalDefaultLayerOffset(key, rot));
            Settings.SetDirectionalLayerOffset(key, rot, layer);
            return changed;
        }

        private static void DrawDirectionalOffsetControls(Listing_Standard listing, string key, string label)
        {
            listing.Label("MUGB_TuningDirectionalPosition".Translate(DisplayTuningToken(label)));
            DrawDirectionalOffsetControl(listing, key, Rot4.South, "south");
            DrawDirectionalOffsetControl(listing, key, Rot4.North, "north");
            DrawDirectionalOffsetControl(listing, key, Rot4.East, "east");
            DrawDirectionalOffsetControl(listing, key, Rot4.West, "west");
            listing.Gap();
        }

        private static bool DrawDirectionalOffsetControl(Listing_Standard listing, string key, Rot4 rot, string label)
        {
            bool changed = false;
            float x = Settings.GetDirectionalOffsetX(key, rot);
            float y = Settings.GetDirectionalOffsetY(key, rot);
            changed |= DrawFloatControl(listing, $"{key} {label} X", ref x, -2.0f, 2.0f, Settings.GetDirectionalDefaultOffsetX(key, rot));
            Settings.SetDirectionalOffsetX(key, rot, x);
            changed |= DrawFloatControl(listing, $"{key} {label} Y", ref y, -2.0f, 2.0f, Settings.GetDirectionalDefaultOffsetY(key, rot));
            Settings.SetDirectionalOffsetY(key, rot, y);
            return changed;
        }

        private static void DrawLegacyAddonControls(Listing_Standard listing, string label, ref float offsetX, ref float offsetY, ref float scale)
        {
            listing.Label($"{label} X: {offsetX:0.00}");
            offsetX = listing.Slider(offsetX, -0.6f, 0.6f);
            listing.Label($"{label} Y: {offsetY:0.00}");
            offsetY = listing.Slider(offsetY, -0.6f, 0.6f);
            listing.Label($"{label} scale: {scale:0.00}");
            scale = listing.Slider(scale, 0.5f, 1.8f);
        }

        private static void DrawTransformControls(Listing_Standard listing, string label, ref float offsetX, ref float offsetY, ref float scale, float minScale, float maxScale)
        {
            listing.Label($"{label} X: {offsetX:0.00}");
            offsetX = listing.Slider(offsetX, -0.6f, 0.6f);
            listing.Label($"{label} Y: {offsetY:0.00}");
            offsetY = listing.Slider(offsetY, -0.6f, 0.6f);
            listing.Label($"{label} scale: {scale:0.00}");
            scale = listing.Slider(scale, minScale, maxScale);
        }

        private static bool DrawOffsetControls(Listing_Standard listing, string label, ref float offsetX, ref float offsetY)
        {
            bool changed = false;
            changed |= DrawFloatControl(listing, $"{label} X", ref offsetX, -0.6f, 0.6f, 0f);
            changed |= DrawFloatControl(listing, $"{label} Y", ref offsetY, -0.6f, 0.6f, 0f);
            return changed;
        }

        private static bool DrawVisualScaleControls(Listing_Standard listing)
        {
            bool changed = false;
            changed |= DrawFloatControl(listing, "MUGB_TuningBodyVisualScale".Translate(), ref Settings.bodyScale, 0.6f, 1.4f, MUGBVisualTuningDefaults.BodyScale);
            changed |= DrawFloatControl(listing, "MUGB_TuningHeadVisualScale".Translate(), ref Settings.headScale, 0.5f, 1.3f, MUGBVisualTuningDefaults.HeadScale);
            changed |= DrawFloatControl(listing, "MUGB_TuningGoblinGlobalScale".Translate(), ref Settings.goblinGlobalRenderScale, 0.75f, 1.5f, MUGBVisualTuningDefaults.GoblinGlobalRenderScale);
            changed |= DrawFloatControl(listing, "MUGB_TuningJuvenileEarlyScale".Translate(), ref Settings.juvenileEarlyScale, 0.6f, 1f, MUGBVisualTuningDefaults.JuvenileEarlyScale);
            changed |= DrawFloatControl(listing, "MUGB_TuningJuvenileLateScale".Translate(), ref Settings.juvenileLateScale, 0.6f, 1f, MUGBVisualTuningDefaults.JuvenileLateScale);
            return changed;
        }

        private static void DrawLayerControls(Listing_Standard listing)
        {
            DrawFloatControl(listing, "MUGB_TuningEarLayerOffset".Translate(), ref Settings.earLayerOffset, -30f, 30f, 0f);
            DrawFloatControl(listing, "MUGB_TuningEyeLayerOffset".Translate(), ref Settings.eyeLayerOffset, -30f, 30f, 0f);
            DrawFloatControl(listing, "MUGB_TuningMouthLayerOffset".Translate(), ref Settings.mouthLayerOffset, -30f, 30f, 0f);
            DrawFloatControl(listing, "MUGB_TuningNoseLayerOffset".Translate(), ref Settings.noseLayerOffset, -30f, 30f, 0f);
        }

        private static bool DrawFloatControl(Listing_Standard listing, string label, ref float value, float min, float max, float defaultValue)
        {
            float beforeSlider = value;
            string bufferKey = label;
            if (!NumericBuffers.TryGetValue(bufferKey, out string buffer))
            {
                buffer = value.ToString("0.###", CultureInfo.InvariantCulture);
                NumericBuffers[bufferKey] = buffer;
            }

            Rect rect = listing.GetRect(58f);
            Rect labelRect = new Rect(rect.x, rect.y + 2f, rect.width, 24f);
            Rect sliderRect = new Rect(rect.x, labelRect.yMax + 6f, rect.width * 0.58f, 18f);
            Rect fieldRect = new Rect(sliderRect.xMax + 8f, labelRect.yMax + 1f, rect.width * 0.28f, 28f);
            Rect resetRect = new Rect(fieldRect.xMax + 8f, labelRect.yMax + 1f, rect.xMax - fieldRect.xMax - 8f, 28f);

            bool wordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(labelRect, $"{label}: {value:0.###}");
            Text.WordWrap = wordWrap;
            value = GUI.HorizontalSlider(sliderRect, value, min, max);
            bool sliderChanged = Mathf.Abs(value - beforeSlider) > 0.0001f;
            if (sliderChanged)
            {
                buffer = value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            string edited = Widgets.TextField(fieldRect, buffer);
            if (edited != buffer)
            {
                buffer = edited;
                if (TryParseFloatInput(buffer, out float parsed))
                {
                    value = Mathf.Clamp(parsed, min, max);
                }
            }

            NumericBuffers[bufferKey] = buffer;
            if (GUI.Button(resetRect, "R"))
            {
                value = defaultValue;
                NumericBuffers[bufferKey] = defaultValue.ToString("0.###", CultureInfo.InvariantCulture);
            }

            return Mathf.Abs(value - beforeSlider) > 0.0001f;
        }

        private static bool TryParseFloatInput(string input, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string normalized = input.Trim().Replace(',', '.');
            if (normalized == "-" || normalized == "." || normalized == "-.")
            {
                return false;
            }

            return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    public static class MUGBDebugActions
    {
        [DebugAction("MUGB", "Open goblin visual tuning")]
        public static void OpenGoblinVisualTuning()
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_MUGBVisualTuning());
        }

        [DebugAction("MUGB", "Open goblin apparel tuning")]
        public static void OpenGoblinApparelTuning()
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            MUGBMod.OpenApparelTuningWindow();
        }

        [DebugAction("MUGB", "Test goblin edge tunnel raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinEdgeTunnelRaid()
        {
            OpenGoblinTunnelRaidDebugMenu(MUGBDefOf.MUGB_GoblinTunnelArrival);
        }

        [DebugAction("MUGB", "Test goblin center tunnel raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinCenterTunnelRaid()
        {
            OpenGoblinTunnelRaidDebugMenu(MUGBDefOf.MUGB_GoblinTunnelArrivalCenter);
        }

        [DebugAction("MUGB", "Test goblin mortar tunnel siege", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinMortarTunnelSiege()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null) return;
            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinSavageMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCultists);
            if (faction == null)
            {
                Messages.Message("No goblin faction exists in this world.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in new[] { 850f, 1200f, 3000f, 5000f })
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, Find.CurrentMap);
                    parms.forced = true; parms.target = Find.CurrentMap; parms.faction = faction; parms.points = localPoints;
                    parms.raidStrategy = MUGBDefOf.MUGB_GoblinMortarTunnelSiege;
                    parms.raidArrivalMode = MUGBDefOf.MUGB_GoblinMortarTunnelArrival;
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("MUGB", "Test goblin sapper raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinSapperRaid()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null) return;
            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinSavageMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinTribe);
            if (faction == null)
            {
                Messages.Message("No goblin faction exists in this world.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in new[] { 850f, 1200f, 2000f, 4000f })
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, Find.CurrentMap);
                    parms.forced = true;
                    parms.target = Find.CurrentMap;
                    parms.faction = faction;
                    parms.points = localPoints;
                    parms.raidStrategy = MUGBDefOf.MUGB_GoblinSapperRaid;
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("MUGB", "Test goblin two-pronged sapper raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinCompositeSapperRaid()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null) return;
            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinSavageMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinTribe);
            if (faction == null)
            {
                Messages.Message("No goblin faction exists in this world.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in new[] { 1800f, 2500f, 4000f, 6000f })
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, Find.CurrentMap);
                    parms.forced = true;
                    parms.target = Find.CurrentMap;
                    parms.faction = faction;
                    parms.points = localPoints;
                    parms.raidStrategy = MUGBDefOf.MUGB_GoblinCompositeSapperRaid;
                    parms.raidArrivalMode = MUGBDefOf.MUGB_GoblinCompositeTwoDirections;
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("MUGB", "Test goblin boomstick sapper raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinSuicideSapperRaid()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null) return;
            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinSavageMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinTribe);
            if (faction == null)
            {
                Messages.Message("No goblin faction exists in this world.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in new[] { 850f, 1200f, 1800f, 3000f, 5000f })
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, Find.CurrentMap);
                    parms.forced = true;
                    parms.target = Find.CurrentMap;
                    parms.faction = faction;
                    parms.points = localPoints;
                    parms.raidStrategy = MUGBDefOf.MUGB_GoblinSuicideSapperRaid;
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("MUGB", "Test goblin cultist skip abduction", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestGoblinCultistSkipAbduction()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null)
            {
                return;
            }

            if (!ModsConfig.AnomalyActive)
            {
                Messages.Message("MUGB_GoblinCultistSkipAbductionRequiresAnomaly".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail("MUGB_GoblinCultistSkipAbduction");
            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCultists);
            if (incident == null || faction == null || faction.defeated)
            {
                Messages.Message("MUGB_GoblinCultistSkipAbductionCannotStart".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in new[] { 850f, 1200f, 2000f, 4000f })
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, Find.CurrentMap);
                    parms.forced = true;
                    parms.target = Find.CurrentMap;
                    parms.faction = faction;
                    parms.points = localPoints;
                    if (!incident.Worker.TryExecute(parms))
                    {
                        Messages.Message("MUGB_GoblinCultistSkipAbductionCannotStart".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("MUGB", "Test wandering beggars passing", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestWanderingBeggarTravelers()
        {
            if (!IncidentWorker_BeggarTravelerGroup.TryFire(Find.CurrentMap))
            {
                Messages.Message("MUGB_BeggarTravelersCannotStart".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        [DebugAction("MUGB", "Test goblin caravan ambush", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void TestGoblinCaravanAmbush()
        {
            PlanetTile tile = GenWorld.MouseTile();
            Caravan caravan = Find.WorldObjects.Caravans
                .FirstOrDefault(candidate => candidate.Faction == Faction.OfPlayer && candidate.Tile == tile);
            if (caravan == null)
            {
                Messages.Message("MUGB_GoblinCaravanAmbushDebugNeedCaravan".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in new[] { 100f, 200f, 500f, 1500f })
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                        MUGBDefOf.MUGB_GoblinCaravanAmbush.category,
                        caravan);
                    parms.forced = true;
                    parms.target = caravan;
                    parms.points = localPoints;
                    MUGBDefOf.MUGB_GoblinCaravanAmbush.Worker.TryExecute(parms);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("MUGB", "Test collective slave marriage", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestCollectiveSlaveMarriage()
        {
            if (!Prefs.DevMode || !ModsConfig.IdeologyActive || Find.CurrentMap == null)
            {
                return;
            }

            Ideo playerIdeo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            Precept_Ritual ritual = playerIdeo?.GetAllPreceptsOfType<Precept_Ritual>()
                .FirstOrDefault(precept => precept.def == MUGBDefOf.MUGB_SlaveMarriageCeremony);
            if (ritual == null)
            {
                Messages.Message("MUGB_SlaveMarriageDebugNeedRitual".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            Thing targetThing = cell.GetThingList(map).FirstOrDefault(thing =>
                thing.def == ThingDefOf.RitualSpot || thing.def.isAltar);
            TargetInfo target = targetThing != null ? new TargetInfo(targetThing) : new TargetInfo(cell, map);
            Pawn organizer = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (organizer == null)
            {
                Messages.Message("MUGB_SlaveMarriageDebugNeedColonist".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            RitualOutcomeEffectDef outcome = DefDatabase<RitualOutcomeEffectDef>.GetNamed("MUGB_SlaveMarriageCeremony");
            Find.WindowStack.Add(new Dialog_BeginRitual(
                ritual.Label,
                ritual,
                target,
                map,
                assignments =>
                {
                    ritual.behavior.TryExecuteOn(target, organizer, ritual, null, assignments, playerForced: true);
                    return true;
                },
                organizer,
                null,
                outcome: outcome));
        }

        private static void OpenGoblinTunnelRaidDebugMenu(PawnsArrivalModeDef arrivalMode)
        {
            if (!Prefs.DevMode || Find.CurrentMap == null)
            {
                return;
            }

            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinSavageMedieval)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCultists)
                ?? Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilMedieval);
            if (faction == null)
            {
                Messages.Message("No goblin faction exists in this world.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (float points in DebugActionsUtility.PointsOptions(extended: true))
            {
                float localPoints = points;
                options.Add(new DebugMenuOption(points + " points", DebugMenuOptionMode.Action, delegate
                {
                    StorytellerComp storytellerComp = Find.Storyteller.storytellerComps.FirstOrDefault(x => x is StorytellerComp_OnOffCycle || x is StorytellerComp_RandomMain);
                    IncidentParms parms = storytellerComp != null
                        ? storytellerComp.GenerateParms(IncidentCategoryDefOf.ThreatBig, Find.CurrentMap)
                        : StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, Find.CurrentMap);
                    parms.forced = true;
                    parms.target = Find.CurrentMap;
                    parms.faction = faction;
                    parms.points = localPoints;
                    parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                    parms.raidArrivalMode = arrivalMode;
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }
    }

    public class Dialog_MUGBVisualTuning : Window
    {
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(560f, 760f);

        public Dialog_MUGBVisualTuning()
        {
            doCloseX = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            focusWhenOpened = false;
            closeOnAccept = false;
            closeOnCancel = false;
            draggable = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "MUGB_TuningVisualWindowTitle".Translate());
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, 3600f);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            MUGBMod.DrawVisualTuningControls(viewRect);
            Widgets.EndScrollView();
        }
    }

    public class Dialog_MUGBApparelTuning : Window
    {
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(680f, 720f);

        public Dialog_MUGBApparelTuning()
        {
            doCloseX = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            focusWhenOpened = false;
            closeOnAccept = false;
            closeOnCancel = false;
            draggable = true;
            resizeable = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 initialSize = InitialSize;
            float height = Mathf.Min(initialSize.y, UI.screenHeight - 120f);
            float x = Mathf.Max(0f, UI.screenWidth - initialSize.x - 32f);
            float y = Mathf.Max(0f, 72f);
            windowRect = new Rect(x, y, initialSize.x, height).Rounded();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "MUGB_TuningApparelWindowTitle".Translate());
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, 1200f);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            MUGBMod.DrawApparelTuningControls(viewRect);
            Widgets.EndScrollView();
        }
    }

    public static class MUGBVisualTuningDefaults
    {
        public const float BodyScale = 1.15f;
        public const float HeadScale = 0.9f;
        public const float GoblinGlobalRenderScale = 1.025f;
        public const float PawnDrawAltitudeOffset = 0.01f;
        // 한국어 참고: 고블린 청소년 렌더 축소 비율입니다. 바닐라는 청소년을 성인과 같은 크기로 그리므로
        // 모드가 직접 줄입니다. 오프셋과 스케일에 함께 곱해져 몸/머리/부속/의류가 비율을 유지한 채 축소됩니다.
        public const float JuvenileEarlyScale = 0.8f;
        public const float JuvenileLateScale = 0.9f;
        // 한국어 참고: 얼굴 부속(귀/눈/코/입)의 스칼라 베이스 오프셋/스케일 기본값입니다.
        // 방향 튜닝 딕셔너리와 달리 이 값들은 baked 폴백이 없어, 예전에는 ModSettings 파일이 있는
        // 환경(개발자)에서만 값이 채워지고 신규 설치에서는 0/1로 떨어져 부속이 어긋났습니다.
        // 배포 튜닝값을 기본값으로 고정해 config 없이도 동일하게 렌더되도록 합니다.
        public const float EarOffsetX = 0.425726146f;
        public const float EarOffsetY = -0.500414968f;
        public const float EarScale = 1.12f;
        public const float EyeOffsetX = 0.420746893f;
        public const float EyeOffsetY = -0.236514539f;
        public const float EyeScale = 1.2f;
        public const float NoseOffsetX = 0.420746893f;
        public const float NoseOffsetY = -0.252508909f;
        public const float NoseScale = 1.00701416f;
        public const float MouthOffsetX = 0.420746893f;
        public const float MouthOffsetY = -0.261410803f;
        public const float MouthScale = 1f;
        public const float EarLayerOffset = 0f;
        public const float EyeLayerOffset = 0f;
        public const float NoseLayerOffset = 3.12511206f;
        public const float MouthLayerOffset = 2.0999999f;
        // 배포용 기본 비주얼 튜닝값입니다. 사시 폼은 눈값만 별도로 유지하고 나머지는 일반 폼 값을 따르며, 저장된 ModSettings 값이 있으면 그 값이 우선됩니다.
        private static readonly Dictionary<string, float> GoblinFace1Directional = new Dictionary<string, float>
        {
            { "Goblin.Head.south.x", 0f },
            { "Goblin.Head.south.y", -0.195f },
            { "Goblin.Head.south.scale", 1f },
            { "Goblin.Head.south.layer", -5f },
            { "Goblin.Head.north.x", 0f },
            { "Goblin.Head.north.y", -0.224f },
            { "Goblin.Head.north.scale", 1f },
            { "Goblin.Head.north.layer", 0f },
            { "Goblin.Head.east.x", 0.014f },
            { "Goblin.Head.east.y", -0.195f },
            { "Goblin.Head.east.scale", 1f },
            { "Goblin.Head.east.layer", 0f },
            { "Goblin.Head.west.x", -0.014f },
            { "Goblin.Head.west.y", -0.195f },
            { "Goblin.Head.west.scale", 1f },
            { "Goblin.Head.west.layer", 0f },
            { "GoblinCrossEyed.Head.south.x", 0f },
            { "GoblinCrossEyed.Head.south.y", -0.195f },
            { "GoblinCrossEyed.Head.south.scale", 1f },
            { "GoblinCrossEyed.Head.south.layer", -5f },
            { "GoblinCrossEyed.Head.north.x", 0f },
            { "GoblinCrossEyed.Head.north.y", -0.224f },
            { "GoblinCrossEyed.Head.north.scale", 1f },
            { "GoblinCrossEyed.Head.north.layer", 0f },
            { "GoblinCrossEyed.Head.east.x", 0.014f },
            { "GoblinCrossEyed.Head.east.y", -0.195f },
            { "GoblinCrossEyed.Head.east.scale", 1f },
            { "GoblinCrossEyed.Head.east.layer", 0f },
            { "GoblinCrossEyed.Head.west.x", -0.014f },
            { "GoblinCrossEyed.Head.west.y", -0.195f },
            { "GoblinCrossEyed.Head.west.scale", 1f },
            { "GoblinCrossEyed.Head.west.layer", 0f },
            { "Hobgoblin.Head.south.x", 0f },
            { "Hobgoblin.Head.south.y", -0.273f },
            { "Hobgoblin.Head.south.scale", 0.9f },
            { "Hobgoblin.Head.south.layer", 0f },
            { "Hobgoblin.Head.north.x", 0f },
            { "Hobgoblin.Head.north.y", -0.24f },
            { "Hobgoblin.Head.north.scale", 0.92f },
            { "Hobgoblin.Head.north.layer", 0f },
            { "Hobgoblin.Head.east.x", 0.009f },
            { "Hobgoblin.Head.east.y", -0.25f },
            { "Hobgoblin.Head.east.scale", 0.95f },
            { "Hobgoblin.Head.east.layer", 0f },
            { "Hobgoblin.Head.west.x", 0f },
            { "Hobgoblin.Head.west.y", -0.25f },
            { "Hobgoblin.Head.west.scale", 0.95f },
            { "Hobgoblin.Head.west.layer", 0f },
            { "HobgoblinCrossEyed.Head.south.x", 0f },
            { "HobgoblinCrossEyed.Head.south.y", -0.273f },
            { "HobgoblinCrossEyed.Head.south.scale", 0.9f },
            { "HobgoblinCrossEyed.Head.south.layer", 0f },
            { "HobgoblinCrossEyed.Head.north.x", 0f },
            { "HobgoblinCrossEyed.Head.north.y", -0.24f },
            { "HobgoblinCrossEyed.Head.north.scale", 0.92f },
            { "HobgoblinCrossEyed.Head.north.layer", 0f },
            { "HobgoblinCrossEyed.Head.east.x", 0.009f },
            { "HobgoblinCrossEyed.Head.east.y", -0.25f },
            { "HobgoblinCrossEyed.Head.east.scale", 0.95f },
            { "HobgoblinCrossEyed.Head.east.layer", 0f },
            { "HobgoblinCrossEyed.Head.west.x", 0f },
            { "HobgoblinCrossEyed.Head.west.y", -0.25f },
            { "HobgoblinCrossEyed.Head.west.scale", 0.95f },
            { "HobgoblinCrossEyed.Head.west.layer", 0f },
            { "GoblinDessicated.Head.south.x", 0f },
            { "GoblinDessicated.Head.south.y", -0.241f },
            { "GoblinDessicated.Head.south.scale", 0.766f },
            { "GoblinDessicated.Head.south.layer", -5f },
            { "GoblinDessicated.Head.north.x", 0f },
            { "GoblinDessicated.Head.north.y", -0.281f },
            { "GoblinDessicated.Head.north.scale", 0.766f },
            { "GoblinDessicated.Head.north.layer", 0f },
            { "GoblinDessicated.Head.east.x", -0.029f },
            { "GoblinDessicated.Head.east.y", -0.298f },
            { "GoblinDessicated.Head.east.scale", 0.787f },
            { "GoblinDessicated.Head.east.layer", 0f },
            { "GoblinDessicated.Head.west.x", -0.014f },
            { "GoblinDessicated.Head.west.y", -0.298f },
            { "GoblinDessicated.Head.west.scale", 0.787f },
            { "GoblinDessicated.Head.west.layer", 0f },
            { "HobgoblinDessicated.Head.south.x", 0f },
            { "HobgoblinDessicated.Head.south.y", -0.273f },
            { "HobgoblinDessicated.Head.south.scale", 0.886f },
            { "HobgoblinDessicated.Head.south.layer", 0f },
            { "HobgoblinDessicated.Head.north.x", 0f },
            { "HobgoblinDessicated.Head.north.y", -0.24f },
            { "HobgoblinDessicated.Head.north.scale", 0.886f },
            { "HobgoblinDessicated.Head.north.layer", 0f },
            { "HobgoblinDessicated.Head.east.x", 0.009f },
            { "HobgoblinDessicated.Head.east.y", -0.25f },
            { "HobgoblinDessicated.Head.east.scale", 0.911f },
            { "HobgoblinDessicated.Head.east.layer", 0f },
            { "HobgoblinDessicated.Head.west.x", 0f },
            { "HobgoblinDessicated.Head.west.y", -0.25f },
            { "HobgoblinDessicated.Head.west.scale", 0.911f },
            { "HobgoblinDessicated.Head.west.layer", 0f },
            { "Goblin.Body.south.x", 0f },
            { "Goblin.Body.south.y", 0f },
            { "Goblin.Body.south.scale", 1.05f },
            { "Goblin.Body.south.layer", 0f },
            { "Goblin.Body.north.x", 0f },
            { "Goblin.Body.north.y", 0f },
            { "Goblin.Body.north.scale", 1.11f },
            { "Goblin.Body.north.layer", 0f },
            { "Goblin.Body.east.x", 0f },
            { "Goblin.Body.east.y", -0.043f },
            { "Goblin.Body.east.scale", 0.976f },
            { "Goblin.Body.east.layer", 0f },
            { "Goblin.Body.west.x", 0f },
            { "Goblin.Body.west.y", -0.043f },
            { "Goblin.Body.west.scale", 0.976f },
            { "Goblin.Body.west.layer", 0f },
            { "GoblinCrossEyed.Body.south.x", 0f },
            { "GoblinCrossEyed.Body.south.y", 0f },
            { "GoblinCrossEyed.Body.south.scale", 1.05f },
            { "GoblinCrossEyed.Body.south.layer", 0f },
            { "GoblinCrossEyed.Body.north.x", 0f },
            { "GoblinCrossEyed.Body.north.y", 0f },
            { "GoblinCrossEyed.Body.north.scale", 1.11f },
            { "GoblinCrossEyed.Body.north.layer", 0f },
            { "GoblinCrossEyed.Body.east.x", 0f },
            { "GoblinCrossEyed.Body.east.y", -0.043f },
            { "GoblinCrossEyed.Body.east.scale", 0.976f },
            { "GoblinCrossEyed.Body.east.layer", 0f },
            { "GoblinCrossEyed.Body.west.x", 0f },
            { "GoblinCrossEyed.Body.west.y", -0.043f },
            { "GoblinCrossEyed.Body.west.scale", 0.976f },
            { "GoblinCrossEyed.Body.west.layer", 0f },
            { "Hobgoblin.Body.south.x", 0f },
            { "Hobgoblin.Body.south.y", 0f },
            { "Hobgoblin.Body.south.scale", 1f },
            { "Hobgoblin.Body.south.layer", 0f },
            { "Hobgoblin.Body.north.x", 0f },
            { "Hobgoblin.Body.north.y", 0f },
            { "Hobgoblin.Body.north.scale", 1f },
            { "Hobgoblin.Body.north.layer", 0f },
            { "Hobgoblin.Body.east.x", 0f },
            { "Hobgoblin.Body.east.y", 0f },
            { "Hobgoblin.Body.east.scale", 1f },
            { "Hobgoblin.Body.east.layer", 0f },
            { "Hobgoblin.Body.west.x", 0f },
            { "Hobgoblin.Body.west.y", 0f },
            { "Hobgoblin.Body.west.scale", 1f },
            { "Hobgoblin.Body.west.layer", 0f },
            { "HobgoblinCrossEyed.Body.south.x", 0f },
            { "HobgoblinCrossEyed.Body.south.y", 0f },
            { "HobgoblinCrossEyed.Body.south.scale", 1f },
            { "HobgoblinCrossEyed.Body.south.layer", 0f },
            { "HobgoblinCrossEyed.Body.north.x", 0f },
            { "HobgoblinCrossEyed.Body.north.y", 0f },
            { "HobgoblinCrossEyed.Body.north.scale", 1f },
            { "HobgoblinCrossEyed.Body.north.layer", 0f },
            { "HobgoblinCrossEyed.Body.east.x", 0f },
            { "HobgoblinCrossEyed.Body.east.y", 0f },
            { "HobgoblinCrossEyed.Body.east.scale", 1f },
            { "HobgoblinCrossEyed.Body.east.layer", 0f },
            { "HobgoblinCrossEyed.Body.west.x", 0f },
            { "HobgoblinCrossEyed.Body.west.y", 0f },
            { "HobgoblinCrossEyed.Body.west.scale", 1f },
            { "HobgoblinCrossEyed.Body.west.layer", 0f },
            { "GoblinDessicated.Body.south.x", 0f },
            { "GoblinDessicated.Body.south.y", 0f },
            { "GoblinDessicated.Body.south.scale", 1.05f },
            { "GoblinDessicated.Body.south.layer", 0f },
            { "GoblinDessicated.Body.north.x", 0f },
            { "GoblinDessicated.Body.north.y", 0f },
            { "GoblinDessicated.Body.north.scale", 1.11f },
            { "GoblinDessicated.Body.north.layer", 0f },
            { "GoblinDessicated.Body.east.x", 0f },
            { "GoblinDessicated.Body.east.y", 0f },
            { "GoblinDessicated.Body.east.scale", 0.976f },
            { "GoblinDessicated.Body.east.layer", 0f },
            { "GoblinDessicated.Body.west.x", 0f },
            { "GoblinDessicated.Body.west.y", -0.043f },
            { "GoblinDessicated.Body.west.scale", 0.976f },
            { "GoblinDessicated.Body.west.layer", 0f },
            { "HobgoblinDessicated.Body.south.x", 0f },
            { "HobgoblinDessicated.Body.south.y", 0f },
            { "HobgoblinDessicated.Body.south.scale", 1f },
            { "HobgoblinDessicated.Body.south.layer", 0f },
            { "HobgoblinDessicated.Body.north.x", 0f },
            { "HobgoblinDessicated.Body.north.y", 0f },
            { "HobgoblinDessicated.Body.north.scale", 1f },
            { "HobgoblinDessicated.Body.north.layer", 0f },
            { "HobgoblinDessicated.Body.east.x", 0f },
            { "HobgoblinDessicated.Body.east.y", 0f },
            { "HobgoblinDessicated.Body.east.scale", 1f },
            { "HobgoblinDessicated.Body.east.layer", 0f },
            { "HobgoblinDessicated.Body.west.x", 0f },
            { "HobgoblinDessicated.Body.west.y", 0f },
            { "HobgoblinDessicated.Body.west.scale", 1f },
            { "HobgoblinDessicated.Body.west.layer", 0f },
            { "Goblin.EarLeft.south.x", 0.045f },
            { "Goblin.EarLeft.south.y", 0.025f },
            { "Goblin.EarLeft.south.scale", 1f },
            { "Goblin.EarLeft.south.layer", -22.145f },
            { "Goblin.EarLeft.north.x", -0.452f },
            { "Goblin.EarLeft.north.y", -0.24f },
            { "Goblin.EarLeft.north.scale", 1f },
            { "Goblin.EarLeft.north.layer", 0f },
            { "Goblin.EarLeft.east.x", 0f },
            { "Goblin.EarLeft.east.y", 0f },
            { "Goblin.EarLeft.east.scale", 1f },
            { "Goblin.EarLeft.east.layer", -5.329f },
            { "Goblin.EarLeft.west.x", 0.199f },
            { "Goblin.EarLeft.west.y", 0f },
            { "Goblin.EarLeft.west.scale", 1f },
            { "Goblin.EarLeft.west.layer", -5.329f },
            { "Goblin.EarRight.south.x", -0.043f },
            { "Goblin.EarRight.south.y", 0.02f },
            { "Goblin.EarRight.south.scale", 1f },
            { "Goblin.EarRight.south.layer", -22.353f },
            { "Goblin.EarRight.north.x", -0.396f },
            { "Goblin.EarRight.north.y", -0.24f },
            { "Goblin.EarRight.north.scale", 1f },
            { "Goblin.EarRight.north.layer", 0f },
            { "Goblin.EarRight.east.x", -0.145f },
            { "Goblin.EarRight.east.y", 0f },
            { "Goblin.EarRight.east.scale", 1f },
            { "Goblin.EarRight.east.layer", -5.329f },
            { "Goblin.EarRight.west.x", 0.199f },
            { "Goblin.EarRight.west.y", 0f },
            { "Goblin.EarRight.west.scale", 1f },
            { "Goblin.EarRight.west.layer", -5.329f },
            { "Goblin.EyeLeft.south.x", 0f },
            { "Goblin.EyeLeft.south.y", 0f },
            { "Goblin.EyeLeft.south.scale", 1f },
            { "Goblin.EyeLeft.south.layer", -42.431f },
            { "Goblin.EyeLeft.north.x", 0f },
            { "Goblin.EyeLeft.north.y", 0f },
            { "Goblin.EyeLeft.north.scale", 1f },
            { "Goblin.EyeLeft.north.layer", 0f },
            { "Goblin.EyeLeft.east.x", 0f },
            { "Goblin.EyeLeft.east.y", 0f },
            { "Goblin.EyeLeft.east.scale", 1f },
            { "Goblin.EyeLeft.east.layer", 50f },
            { "Goblin.EyeLeft.west.x", -1.05f },
            { "Goblin.EyeLeft.west.y", -0.233f },
            { "Goblin.EyeLeft.west.scale", 1f },
            { "Goblin.EyeLeft.west.layer", -37.515f },
            { "Goblin.EyeRight.south.x", 0f },
            { "Goblin.EyeRight.south.y", 0f },
            { "Goblin.EyeRight.south.scale", 1f },
            { "Goblin.EyeRight.south.layer", -42.634f },
            { "Goblin.EyeRight.north.x", 0f },
            { "Goblin.EyeRight.north.y", 0f },
            { "Goblin.EyeRight.north.scale", 1f },
            { "Goblin.EyeRight.north.layer", 0f },
            { "Goblin.EyeRight.east.x", 0.24f },
            { "Goblin.EyeRight.east.y", 0f },
            { "Goblin.EyeRight.east.scale", 1f },
            { "Goblin.EyeRight.east.layer", -37.31f },
            { "Goblin.EyeRight.west.x", 0f },
            { "Goblin.EyeRight.west.y", 0f },
            { "Goblin.EyeRight.west.scale", 1f },
            { "Goblin.EyeRight.west.layer", 0f },
            { "Goblin.Nose.south.x", 0f },
            { "Goblin.Nose.south.y", 0f },
            { "Goblin.Nose.south.scale", 1f },
            { "Goblin.Nose.south.layer", -8.204f },
            { "Goblin.Nose.north.x", 0f },
            { "Goblin.Nose.north.y", 0f },
            { "Goblin.Nose.north.scale", 1f },
            { "Goblin.Nose.north.layer", 0f },
            { "Goblin.Nose.east.x", 0.18f },
            { "Goblin.Nose.east.y", 0.01f },
            { "Goblin.Nose.east.scale", 1.04f },
            { "Goblin.Nose.east.layer", -18.862f },
            { "Goblin.Nose.west.x", -0.598f },
            { "Goblin.Nose.west.y", 0.01f },
            { "Goblin.Nose.west.scale", 1.04f },
            { "Goblin.Nose.west.layer", -18.862f },
            { "Goblin.Mouth.south.x", 0f },
            { "Goblin.Mouth.south.y", -0.004f },
            { "Goblin.Mouth.south.scale", 1.099f },
            { "Goblin.Mouth.south.layer", -22.85f },
            { "Goblin.Mouth.north.x", 0f },
            { "Goblin.Mouth.north.y", 0f },
            { "Goblin.Mouth.north.scale", 1f },
            { "Goblin.Mouth.north.layer", 0f },
            { "Goblin.Mouth.east.x", 0.13f },
            { "Goblin.Mouth.east.y", 0f },
            { "Goblin.Mouth.east.scale", 1.023f },
            { "Goblin.Mouth.east.layer", -20.2f },
            { "Goblin.Mouth.west.x", -0.549f },
            { "Goblin.Mouth.west.y", 0f },
            { "Goblin.Mouth.west.scale", 1.023f },
            { "Goblin.Mouth.west.layer", -20.57f },
            { "GoblinCrossEyed.EarLeft.south.x", 0.045f },
            { "GoblinCrossEyed.EarLeft.south.y", 0.025f },
            { "GoblinCrossEyed.EarLeft.south.scale", 1f },
            { "GoblinCrossEyed.EarLeft.south.layer", -22.145f },
            { "GoblinCrossEyed.EarLeft.north.x", -0.452f },
            { "GoblinCrossEyed.EarLeft.north.y", -0.24f },
            { "GoblinCrossEyed.EarLeft.north.scale", 1f },
            { "GoblinCrossEyed.EarLeft.north.layer", 0f },
            { "GoblinCrossEyed.EarLeft.east.x", 0f },
            { "GoblinCrossEyed.EarLeft.east.y", 0f },
            { "GoblinCrossEyed.EarLeft.east.scale", 1f },
            { "GoblinCrossEyed.EarLeft.east.layer", -5.329f },
            { "GoblinCrossEyed.EarLeft.west.x", 0.199f },
            { "GoblinCrossEyed.EarLeft.west.y", 0f },
            { "GoblinCrossEyed.EarLeft.west.scale", 1f },
            { "GoblinCrossEyed.EarLeft.west.layer", -5.329f },
            { "GoblinCrossEyed.EarRight.south.x", -0.043f },
            { "GoblinCrossEyed.EarRight.south.y", 0.02f },
            { "GoblinCrossEyed.EarRight.south.scale", 1f },
            { "GoblinCrossEyed.EarRight.south.layer", -22.353f },
            { "GoblinCrossEyed.EarRight.north.x", -0.396f },
            { "GoblinCrossEyed.EarRight.north.y", -0.24f },
            { "GoblinCrossEyed.EarRight.north.scale", 1f },
            { "GoblinCrossEyed.EarRight.north.layer", 0f },
            { "GoblinCrossEyed.EarRight.east.x", -0.145f },
            { "GoblinCrossEyed.EarRight.east.y", 0f },
            { "GoblinCrossEyed.EarRight.east.scale", 1f },
            { "GoblinCrossEyed.EarRight.east.layer", -5.329f },
            { "GoblinCrossEyed.EarRight.west.x", 0.199f },
            { "GoblinCrossEyed.EarRight.west.y", 0f },
            { "GoblinCrossEyed.EarRight.west.scale", 1f },
            { "GoblinCrossEyed.EarRight.west.layer", -5.329f },
            { "GoblinCrossEyed.EyeLeft.south.x", 0.014f },
            { "GoblinCrossEyed.EyeLeft.south.y", 0f },
            { "GoblinCrossEyed.EyeLeft.south.scale", 0.85f },
            { "GoblinCrossEyed.EyeLeft.south.layer", -42.431f },
            { "GoblinCrossEyed.EyeLeft.north.x", 0f },
            { "GoblinCrossEyed.EyeLeft.north.y", 0f },
            { "GoblinCrossEyed.EyeLeft.north.scale", 1f },
            { "GoblinCrossEyed.EyeLeft.north.layer", 0f },
            { "GoblinCrossEyed.EyeLeft.east.x", 0f },
            { "GoblinCrossEyed.EyeLeft.east.y", 0f },
            { "GoblinCrossEyed.EyeLeft.east.scale", 1f },
            { "GoblinCrossEyed.EyeLeft.east.layer", 50f },
            { "GoblinCrossEyed.EyeLeft.west.x", -1.05f },
            { "GoblinCrossEyed.EyeLeft.west.y", -0.233f },
            { "GoblinCrossEyed.EyeLeft.west.scale", 1f },
            { "GoblinCrossEyed.EyeLeft.west.layer", -37.515f },
            { "GoblinCrossEyed.EyeRight.south.x", -0.014f },
            { "GoblinCrossEyed.EyeRight.south.y", 0f },
            { "GoblinCrossEyed.EyeRight.south.scale", 0.85f },
            { "GoblinCrossEyed.EyeRight.south.layer", -42.634f },
            { "GoblinCrossEyed.EyeRight.north.x", 0f },
            { "GoblinCrossEyed.EyeRight.north.y", 0f },
            { "GoblinCrossEyed.EyeRight.north.scale", 1f },
            { "GoblinCrossEyed.EyeRight.north.layer", 0f },
            { "GoblinCrossEyed.EyeRight.east.x", 0.24f },
            { "GoblinCrossEyed.EyeRight.east.y", 0f },
            { "GoblinCrossEyed.EyeRight.east.scale", 1f },
            { "GoblinCrossEyed.EyeRight.east.layer", -37.31f },
            { "GoblinCrossEyed.EyeRight.west.x", 0f },
            { "GoblinCrossEyed.EyeRight.west.y", 0f },
            { "GoblinCrossEyed.EyeRight.west.scale", 1f },
            { "GoblinCrossEyed.EyeRight.west.layer", 0f },
            { "GoblinCrossEyed.Nose.south.x", 0f },
            { "GoblinCrossEyed.Nose.south.y", 0f },
            { "GoblinCrossEyed.Nose.south.scale", 1f },
            { "GoblinCrossEyed.Nose.south.layer", -8.204f },
            { "GoblinCrossEyed.Nose.north.x", 0f },
            { "GoblinCrossEyed.Nose.north.y", 0f },
            { "GoblinCrossEyed.Nose.north.scale", 1f },
            { "GoblinCrossEyed.Nose.north.layer", 0f },
            { "GoblinCrossEyed.Nose.east.x", 0.18f },
            { "GoblinCrossEyed.Nose.east.y", 0.01f },
            { "GoblinCrossEyed.Nose.east.scale", 1.04f },
            { "GoblinCrossEyed.Nose.east.layer", -18.862f },
            { "GoblinCrossEyed.Nose.west.x", -0.598f },
            { "GoblinCrossEyed.Nose.west.y", 0.01f },
            { "GoblinCrossEyed.Nose.west.scale", 1.04f },
            { "GoblinCrossEyed.Nose.west.layer", -18.862f },
            { "GoblinCrossEyed.Mouth.south.x", 0f },
            { "GoblinCrossEyed.Mouth.south.y", -0.004f },
            { "GoblinCrossEyed.Mouth.south.scale", 1.099f },
            { "GoblinCrossEyed.Mouth.south.layer", -22.85f },
            { "GoblinCrossEyed.Mouth.north.x", 0f },
            { "GoblinCrossEyed.Mouth.north.y", 0f },
            { "GoblinCrossEyed.Mouth.north.scale", 1f },
            { "GoblinCrossEyed.Mouth.north.layer", 0f },
            { "GoblinCrossEyed.Mouth.east.x", 0.13f },
            { "GoblinCrossEyed.Mouth.east.y", 0f },
            { "GoblinCrossEyed.Mouth.east.scale", 1.023f },
            { "GoblinCrossEyed.Mouth.east.layer", -20.2f },
            { "GoblinCrossEyed.Mouth.west.x", -0.549f },
            { "GoblinCrossEyed.Mouth.west.y", 0f },
            { "GoblinCrossEyed.Mouth.west.scale", 1.023f },
            { "GoblinCrossEyed.Mouth.west.layer", -20.57f },
            { "Hobgoblin.EarLeft.south.x", 0.034f },
            { "Hobgoblin.EarLeft.south.y", -0.068f },
            { "Hobgoblin.EarLeft.south.scale", 1f },
            { "Hobgoblin.EarLeft.south.layer", -17.1f },
            { "Hobgoblin.EarLeft.north.x", -0.47f },
            { "Hobgoblin.EarLeft.north.y", -0.35f },
            { "Hobgoblin.EarLeft.north.scale", 1f },
            { "Hobgoblin.EarLeft.north.layer", -17.408f },
            { "Hobgoblin.EarLeft.east.x", 0f },
            { "Hobgoblin.EarLeft.east.y", 0f },
            { "Hobgoblin.EarLeft.east.scale", 1f },
            { "Hobgoblin.EarLeft.east.layer", -5.329f },
            { "Hobgoblin.EarLeft.west.x", 0.158f },
            { "Hobgoblin.EarLeft.west.y", -0.071f },
            { "Hobgoblin.EarLeft.west.scale", 1f },
            { "Hobgoblin.EarLeft.west.layer", -5.329f },
            { "Hobgoblin.EarRight.south.x", -0.048f },
            { "Hobgoblin.EarRight.south.y", -0.065f },
            { "Hobgoblin.EarRight.south.scale", 1f },
            { "Hobgoblin.EarRight.south.layer", -17.2f },
            { "Hobgoblin.EarRight.north.x", -0.383f },
            { "Hobgoblin.EarRight.north.y", -0.35f },
            { "Hobgoblin.EarRight.north.scale", 1f },
            { "Hobgoblin.EarRight.north.layer", -17.309f },
            { "Hobgoblin.EarRight.east.x", -0.158f },
            { "Hobgoblin.EarRight.east.y", -0.071f },
            { "Hobgoblin.EarRight.east.scale", 1f },
            { "Hobgoblin.EarRight.east.layer", -5.329f },
            { "Hobgoblin.EarRight.west.x", 0f },
            { "Hobgoblin.EarRight.west.y", 0f },
            { "Hobgoblin.EarRight.west.scale", 1f },
            { "Hobgoblin.EarRight.west.layer", -5.329f },
            { "Hobgoblin.EyeLeft.south.x", -0.004f },
            { "Hobgoblin.EyeLeft.south.y", 0.004f },
            { "Hobgoblin.EyeLeft.south.scale", 1f },
            { "Hobgoblin.EyeLeft.south.layer", -37.4f },
            { "Hobgoblin.EyeLeft.north.x", 0f },
            { "Hobgoblin.EyeLeft.north.y", 0f },
            { "Hobgoblin.EyeLeft.north.scale", 1f },
            { "Hobgoblin.EyeLeft.north.layer", 0f },
            { "Hobgoblin.EyeLeft.east.x", 0f },
            { "Hobgoblin.EyeLeft.east.y", 0f },
            { "Hobgoblin.EyeLeft.east.scale", 1f },
            { "Hobgoblin.EyeLeft.east.layer", 0f },
            { "Hobgoblin.EyeLeft.west.x", -1.055f },
            { "Hobgoblin.EyeLeft.west.y", -0.22f },
            { "Hobgoblin.EyeLeft.west.scale", 1f },
            { "Hobgoblin.EyeLeft.west.layer", -37.486f },
            { "Hobgoblin.EyeRight.south.x", 0f },
            { "Hobgoblin.EyeRight.south.y", 0.004f },
            { "Hobgoblin.EyeRight.south.scale", 1f },
            { "Hobgoblin.EyeRight.south.layer", -37.629f },
            { "Hobgoblin.EyeRight.north.x", 0f },
            { "Hobgoblin.EyeRight.north.y", 0f },
            { "Hobgoblin.EyeRight.north.scale", 1f },
            { "Hobgoblin.EyeRight.north.layer", 0f },
            { "Hobgoblin.EyeRight.east.x", 0.239f },
            { "Hobgoblin.EyeRight.east.y", 0.007f },
            { "Hobgoblin.EyeRight.east.scale", 1f },
            { "Hobgoblin.EyeRight.east.layer", -37.3f },
            { "Hobgoblin.EyeRight.west.x", 0f },
            { "Hobgoblin.EyeRight.west.y", 0f },
            { "Hobgoblin.EyeRight.west.scale", 1f },
            { "Hobgoblin.EyeRight.west.layer", 0f },
            { "Hobgoblin.Nose.south.x", 0f },
            { "Hobgoblin.Nose.south.y", -0.012f },
            { "Hobgoblin.Nose.south.scale", 1.024f },
            { "Hobgoblin.Nose.south.layer", -8.204f },
            { "Hobgoblin.Nose.north.x", 0f },
            { "Hobgoblin.Nose.north.y", 0f },
            { "Hobgoblin.Nose.north.scale", 1f },
            { "Hobgoblin.Nose.north.layer", 0f },
            { "Hobgoblin.Nose.east.x", 0.178f },
            { "Hobgoblin.Nose.east.y", 0.006f },
            { "Hobgoblin.Nose.east.scale", 1.04f },
            { "Hobgoblin.Nose.east.layer", -18.862f },
            { "Hobgoblin.Nose.west.x", -0.596f },
            { "Hobgoblin.Nose.west.y", 0.006f },
            { "Hobgoblin.Nose.west.scale", 1.04f },
            { "Hobgoblin.Nose.west.layer", -18.862f },
            { "Hobgoblin.Mouth.south.x", 0f },
            { "Hobgoblin.Mouth.south.y", 0f },
            { "Hobgoblin.Mouth.south.scale", 1.118f },
            { "Hobgoblin.Mouth.south.layer", -20.424f },
            { "Hobgoblin.Mouth.north.x", 0f },
            { "Hobgoblin.Mouth.north.y", 0f },
            { "Hobgoblin.Mouth.north.scale", 1f },
            { "Hobgoblin.Mouth.north.layer", 0f },
            { "Hobgoblin.Mouth.east.x", 0.122f },
            { "Hobgoblin.Mouth.east.y", 0.002f },
            { "Hobgoblin.Mouth.east.scale", 1f },
            { "Hobgoblin.Mouth.east.layer", -20.19f },
            { "Hobgoblin.Mouth.west.x", -0.543f },
            { "Hobgoblin.Mouth.west.y", 0.002f },
            { "Hobgoblin.Mouth.west.scale", 1f },
            { "Hobgoblin.Mouth.west.layer", -20.565f },
            { "HobgoblinCrossEyed.EarLeft.south.x", 0.034f },
            { "HobgoblinCrossEyed.EarLeft.south.y", -0.068f },
            { "HobgoblinCrossEyed.EarLeft.south.scale", 1f },
            { "HobgoblinCrossEyed.EarLeft.south.layer", -17.1f },
            { "HobgoblinCrossEyed.EarLeft.north.x", -0.47f },
            { "HobgoblinCrossEyed.EarLeft.north.y", -0.35f },
            { "HobgoblinCrossEyed.EarLeft.north.scale", 1f },
            { "HobgoblinCrossEyed.EarLeft.north.layer", -17.408f },
            { "HobgoblinCrossEyed.EarLeft.east.x", 0f },
            { "HobgoblinCrossEyed.EarLeft.east.y", 0f },
            { "HobgoblinCrossEyed.EarLeft.east.scale", 1f },
            { "HobgoblinCrossEyed.EarLeft.east.layer", -5.329f },
            { "HobgoblinCrossEyed.EarLeft.west.x", 0.158f },
            { "HobgoblinCrossEyed.EarLeft.west.y", -0.071f },
            { "HobgoblinCrossEyed.EarLeft.west.scale", 1f },
            { "HobgoblinCrossEyed.EarLeft.west.layer", -5.329f },
            { "HobgoblinCrossEyed.EarRight.south.x", -0.048f },
            { "HobgoblinCrossEyed.EarRight.south.y", -0.065f },
            { "HobgoblinCrossEyed.EarRight.south.scale", 1f },
            { "HobgoblinCrossEyed.EarRight.south.layer", -17.2f },
            { "HobgoblinCrossEyed.EarRight.north.x", -0.383f },
            { "HobgoblinCrossEyed.EarRight.north.y", -0.35f },
            { "HobgoblinCrossEyed.EarRight.north.scale", 1f },
            { "HobgoblinCrossEyed.EarRight.north.layer", -17.309f },
            { "HobgoblinCrossEyed.EarRight.east.x", -0.158f },
            { "HobgoblinCrossEyed.EarRight.east.y", -0.071f },
            { "HobgoblinCrossEyed.EarRight.east.scale", 1f },
            { "HobgoblinCrossEyed.EarRight.east.layer", -5.329f },
            { "HobgoblinCrossEyed.EarRight.west.x", 0f },
            { "HobgoblinCrossEyed.EarRight.west.y", 0f },
            { "HobgoblinCrossEyed.EarRight.west.scale", 1f },
            { "HobgoblinCrossEyed.EarRight.west.layer", -5.329f },
            { "HobgoblinCrossEyed.EyeLeft.south.x", 0.014f },
            { "HobgoblinCrossEyed.EyeLeft.south.y", 0.004f },
            { "HobgoblinCrossEyed.EyeLeft.south.scale", 0.85f },
            { "HobgoblinCrossEyed.EyeLeft.south.layer", -37.4f },
            { "HobgoblinCrossEyed.EyeLeft.north.x", 0f },
            { "HobgoblinCrossEyed.EyeLeft.north.y", 0f },
            { "HobgoblinCrossEyed.EyeLeft.north.scale", 1f },
            { "HobgoblinCrossEyed.EyeLeft.north.layer", 0f },
            { "HobgoblinCrossEyed.EyeLeft.east.x", 0f },
            { "HobgoblinCrossEyed.EyeLeft.east.y", 0f },
            { "HobgoblinCrossEyed.EyeLeft.east.scale", 1f },
            { "HobgoblinCrossEyed.EyeLeft.east.layer", 0f },
            { "HobgoblinCrossEyed.EyeLeft.west.x", -1.055f },
            { "HobgoblinCrossEyed.EyeLeft.west.y", -0.22f },
            { "HobgoblinCrossEyed.EyeLeft.west.scale", 1f },
            { "HobgoblinCrossEyed.EyeLeft.west.layer", -37.486f },
            { "HobgoblinCrossEyed.EyeRight.south.x", -0.011f },
            { "HobgoblinCrossEyed.EyeRight.south.y", 0.004f },
            { "HobgoblinCrossEyed.EyeRight.south.scale", 0.85f },
            { "HobgoblinCrossEyed.EyeRight.south.layer", -37.629f },
            { "HobgoblinCrossEyed.EyeRight.north.x", 0f },
            { "HobgoblinCrossEyed.EyeRight.north.y", 0f },
            { "HobgoblinCrossEyed.EyeRight.north.scale", 1f },
            { "HobgoblinCrossEyed.EyeRight.north.layer", 0f },
            { "HobgoblinCrossEyed.EyeRight.east.x", 0.239f },
            { "HobgoblinCrossEyed.EyeRight.east.y", 0.007f },
            { "HobgoblinCrossEyed.EyeRight.east.scale", 1f },
            { "HobgoblinCrossEyed.EyeRight.east.layer", -37.3f },
            { "HobgoblinCrossEyed.EyeRight.west.x", 0f },
            { "HobgoblinCrossEyed.EyeRight.west.y", 0f },
            { "HobgoblinCrossEyed.EyeRight.west.scale", 1f },
            { "HobgoblinCrossEyed.EyeRight.west.layer", 0f },
            { "HobgoblinCrossEyed.Nose.south.x", 0f },
            { "HobgoblinCrossEyed.Nose.south.y", -0.012f },
            { "HobgoblinCrossEyed.Nose.south.scale", 1.024f },
            { "HobgoblinCrossEyed.Nose.south.layer", -8.204f },
            { "HobgoblinCrossEyed.Nose.north.x", 0f },
            { "HobgoblinCrossEyed.Nose.north.y", 0f },
            { "HobgoblinCrossEyed.Nose.north.scale", 1f },
            { "HobgoblinCrossEyed.Nose.north.layer", 0f },
            { "HobgoblinCrossEyed.Nose.east.x", 0.178f },
            { "HobgoblinCrossEyed.Nose.east.y", 0.006f },
            { "HobgoblinCrossEyed.Nose.east.scale", 1.04f },
            { "HobgoblinCrossEyed.Nose.east.layer", -18.862f },
            { "HobgoblinCrossEyed.Nose.west.x", -0.596f },
            { "HobgoblinCrossEyed.Nose.west.y", 0.006f },
            { "HobgoblinCrossEyed.Nose.west.scale", 1.04f },
            { "HobgoblinCrossEyed.Nose.west.layer", -18.862f },
            { "HobgoblinCrossEyed.Mouth.south.x", 0f },
            { "HobgoblinCrossEyed.Mouth.south.y", 0f },
            { "HobgoblinCrossEyed.Mouth.south.scale", 1.118f },
            { "HobgoblinCrossEyed.Mouth.south.layer", -20.424f },
            { "HobgoblinCrossEyed.Mouth.north.x", 0f },
            { "HobgoblinCrossEyed.Mouth.north.y", 0f },
            { "HobgoblinCrossEyed.Mouth.north.scale", 1f },
            { "HobgoblinCrossEyed.Mouth.north.layer", 0f },
            { "HobgoblinCrossEyed.Mouth.east.x", 0.122f },
            { "HobgoblinCrossEyed.Mouth.east.y", 0.002f },
            { "HobgoblinCrossEyed.Mouth.east.scale", 1f },
            { "HobgoblinCrossEyed.Mouth.east.layer", -20.19f },
            { "HobgoblinCrossEyed.Mouth.west.x", -0.543f },
            { "HobgoblinCrossEyed.Mouth.west.y", 0.002f },
            { "HobgoblinCrossEyed.Mouth.west.scale", 1f },
            { "HobgoblinCrossEyed.Mouth.west.layer", -20.565f },
        };

        // 한국어 참고: 얼굴형 2(사시고블린)는 눈값만 별도로 보관합니다.
        private static readonly Dictionary<string, float> CrossEyedGoblinOverrides = new Dictionary<string, float>
        {
            { "Goblin.EyeLeft.south.x", 0.014f },
            { "Goblin.EyeLeft.south.scale", 0.85f },
            { "Goblin.EyeRight.south.x", -0.014f },
            { "Goblin.EyeRight.south.scale", 0.85f },
            { "Hobgoblin.EyeLeft.south.x", 0.014f },
            { "Hobgoblin.EyeLeft.south.scale", 0.85f },
            { "Hobgoblin.EyeRight.south.x", -0.011f },
            { "Hobgoblin.EyeRight.south.scale", 0.85f },
        };

        public static float GetDirectionalDefault(string key, Rot4 rot, string field, float fallback)
        {
            return TryGetDirectionalDefault(key, rot, field, out float value) ? value : fallback;
        }

        public static bool TryGetDirectionalDefault(string key, Rot4 rot, string field, out float value)
        {
            if (key == null)
            {
                value = 0f;
                return false;
            }

            bool crossEyed = TryGetCrossEyedBaseKey(key, out string baseKey, out string overrideBaseKey);
            if (crossEyed && TryGetCrossEyedDirectionalOverride(overrideBaseKey, rot, field, out value))
            {
                return true;
            }

            string fullKey = $"{key}.{RotKey(rot)}.{field}";
            if (!crossEyed && GoblinFace1Directional.TryGetValue(fullKey, out value))
            {
                return true;
            }

            if (!crossEyed)
            {
                value = 0f;
                return false;
            }

            fullKey = $"{baseKey}.{RotKey(rot)}.{field}";
            return GoblinFace1Directional.TryGetValue(fullKey, out value);
        }

        public static bool TryGetCrossEyedDirectionalOverride(string key, Rot4 rot, string field, out float value)
        {
            string fullKey = $"{key}.{RotKey(rot)}.{field}";
            return CrossEyedGoblinOverrides.TryGetValue(fullKey, out value);
        }

        public static float GetFace2DirectionalDefault(string key, Rot4 rot, string field, float fallback)
        {
            string fullKey = $"{key}.{RotKey(rot)}.{field}";
            if (CrossEyedGoblinOverrides.TryGetValue(fullKey, out float value))
            {
                return value;
            }

            return GetDirectionalDefault(key, rot, field, fallback);
        }

        private static bool TryGetCrossEyedBaseKey(string key, out string baseKey, out string overrideBaseKey)
        {
            baseKey = null;
            overrideBaseKey = null;
            if (key == null)
            {
                return false;
            }

            if (key.StartsWith("GoblinCrossEyedChild.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("GoblinCrossEyedChild.".Length);
                baseKey = "GoblinChild." + suffix;
                overrideBaseKey = "Goblin." + suffix;
                return true;
            }

            if (key.StartsWith("HobgoblinCrossEyedChild.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("HobgoblinCrossEyedChild.".Length);
                baseKey = "HobgoblinChild." + suffix;
                overrideBaseKey = "Hobgoblin." + suffix;
                return true;
            }

            if (key.StartsWith("GoblinCrossEyed.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("GoblinCrossEyed.".Length);
                baseKey = "Goblin." + suffix;
                overrideBaseKey = baseKey;
                return true;
            }

            if (key.StartsWith("HobgoblinCrossEyed.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("HobgoblinCrossEyed.".Length);
                baseKey = "Hobgoblin." + suffix;
                overrideBaseKey = baseKey;
                return true;
            }

            return false;
        }


        private static Dictionary<string, float> apparelDefaults = new Dictionary<string, float>();
        private static HashSet<string> apparelDefaultTargets = new HashSet<string>();

        public static void InitializeApparelDefaults(string rootDir)
        {
            Dictionary<string, float> loaded = new Dictionary<string, float>();
            string path = Path.Combine(rootDir, "TuningDefaults", "MGB_ApparelTuningDefaults.txt");
            if (!File.Exists(path))
            {
                path = Path.Combine(rootDir, "TuningDefaults", "MUGB_ApparelTuningDefaults.txt");
            }

            if (!File.Exists(path))
            {
                Log.Error($"[MUGB] Apparel tuning defaults are missing: {path}");
                apparelDefaults = loaded;
                apparelDefaultTargets = new HashSet<string>();
                return;
            }

            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int separator = line.LastIndexOf(':');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = NormalizeApparelDefaultKey(line.Substring(0, separator).Trim());
                    string rawValue = line.Substring(separator + 1).Trim();
                    if (!key.NullOrEmpty() && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    {
                        loaded[key] = value;
                    }
                }
            }
            apparelDefaults = loaded;
            HashSet<string> targets = new HashSet<string>();
            foreach (string key in loaded.Keys)
            {
                int categorySeparator = key.IndexOf('.');
                int defSeparator = categorySeparator < 0 ? -1 : key.IndexOf('.', categorySeparator + 1);
                if (defSeparator > categorySeparator)
                {
                    targets.Add(key.Substring(0, defSeparator));
                }
            }
            apparelDefaultTargets = targets;
        }

        public static float GetApparelDefault(string category, string defName, string formKey, Rot4 rot, string field, float fallback)
        {
            return TryGetApparelDefault(category, defName, formKey, rot, field, out float value) ? value : fallback;
        }

        public static bool TryGetApparelDefault(string category, string defName, string formKey, Rot4 rot, string field, out float value)
        {
            Dictionary<string, float> snapshot = apparelDefaults;
            string key = $"{category}.{defName}.{formKey}.{RotKey(rot)}.{field}";
            if (snapshot != null && snapshot.TryGetValue(key, out value))
            {
                return true;
            }

            if (category != "Apparel")
            {
                string legacyKey = $"Apparel.{defName}.{formKey}.{RotKey(rot)}.{field}";
                if (snapshot != null && snapshot.TryGetValue(legacyKey, out float legacyValue))
                {
                    value = legacyValue;
                    return true;
                }
            }

            if (TryGetApparelFormFallback(formKey, out string fallbackFormKey))
            {
                string fallbackKey = $"{category}.{defName}.{fallbackFormKey}.{RotKey(rot)}.{field}";
                if (snapshot != null && snapshot.TryGetValue(fallbackKey, out float fallbackValue))
                {
                    value = fallbackValue;
                    return true;
                }

                if (category != "Apparel")
                {
                    string fallbackLegacyKey = $"Apparel.{defName}.{fallbackFormKey}.{RotKey(rot)}.{field}";
                    if (snapshot != null && snapshot.TryGetValue(fallbackLegacyKey, out float fallbackLegacyValue))
                    {
                        value = fallbackLegacyValue;
                        return true;
                    }
                }
            }

            value = 0f;
            return false;
        }

        private static string NormalizeApparelDefaultKey(string key)
        {
            string[] parts = key?.Split('.');
            if (parts == null || parts.Length < 6)
            {
                return key;
            }

            int last = parts.Length - 1;
            string field = parts[last];
            string rotation = parts[last - 1];
            if ((field != "x" && field != "y" && field != "scale" && field != "layer") ||
                (rotation != "south" && rotation != "north" && rotation != "east" && rotation != "west"))
            {
                return key;
            }

            // Both legacy keys and package-scoped exports end with
            // defName.form.rotation.field. Strip the optional package scope.
            return $"{parts[0]}.{parts[last - 3]}.{parts[last - 2]}.{rotation}.{field}";
        }

        public static bool HasAnyApparelDefault(string category, string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return false;
            }

            HashSet<string> snapshot = apparelDefaultTargets;
            return snapshot != null &&
                (snapshot.Contains($"{category}.{defName}") ||
                 (category != "Apparel" && snapshot.Contains($"Apparel.{defName}")));
        }

        public static bool TryGetApparelFormFallback(string formKey, out string fallbackFormKey)
        {
            // 한국어 의도: 사시 고블린/홉고블린은 눈만 다른 폼이므로 의류와 머리장비 위치는 원본 폼 값을 그대로 따른다.
            switch (formKey)
            {
                case "GoblinCrossEyed":
                    fallbackFormKey = "Goblin";
                    return true;
                case "HobgoblinCrossEyed":
                    fallbackFormKey = "Hobgoblin";
                    return true;
                case "GoblinCrossEyedChild":
                    fallbackFormKey = "Goblin";
                    return true;
                case "HobgoblinCrossEyedChild":
                    fallbackFormKey = "Hobgoblin";
                    return true;
                case "GoblinChild":
                    fallbackFormKey = "Goblin";
                    return true;
                case "HobgoblinChild":
                    fallbackFormKey = "Hobgoblin";
                    return true;
                default:
                    fallbackFormKey = null;
                    return false;
            }
        }

        private static string RotKey(Rot4 rot)
        {
            if (rot == Rot4.North)
            {
                return "north";
            }

            if (rot == Rot4.East)
            {
                return "east";
            }

            if (rot == Rot4.West)
            {
                return "west";
            }

            return "south";
        }
    }

    public class MUGBSettings : ModSettings
    {
        public bool useTextureColors = true;
        public bool moveHeadgearWithHead = true;
        public bool forceGoblinBaldAndNoBeard = true;
        // 다른 모드가 HAR을 통해 인간 머리 크기를 줄일 때의 대응입니다. 자세한 내용은
        // Source/Patches/GoblinHeadScaleCompatPatch.cs 를 보세요.
        //
        // 기본값은 "머리 크기를 따라가되 얼굴 부속이 깨지지 않게" 쪽입니다.
        // 이 기능의 목적이 다른 모드에 대응하는 것이지 다른 모드를 무시하는 것이 아니므로,
        // 고블린 머리만 원래 크기로 고정하는 쪽은 원하는 사람이 켜도록 둡니다.
        public bool harHeadSizeExemption = false;
        public bool addonFollowHeadScale = true;
        public bool enableDraftedWeaponPoseOffsets = true;
        public int pawnLossAdaptationPercent = 50;
        public int kimDeokPalPawnLossAdaptationPercent = 50;
        public int passingGroupFrequencyPercent = 110;
        public bool enableGoblinSquadSystem = true;
        public bool enableGoblinCompositeRaids = true;
        public bool requireSlaveMarriagePheromonePreference = true;
        public bool enableFeminineTrait = true;
        public bool adjustFemaleBodyTypeChances = true;
        public bool americanBeautyStandard;
        public float goblinLitterSizeMultiplier = 1f;
        public float goblinChildStageDays = 3.5f;
        public int goblinBirthStrainLimit = 4;
        public bool disableToxicPheromonesCheat;
        public bool allowFacialAnimationForGoblins;
        public int goblinSquadSoftCap = 6;
        public int goblinSquadHardCap = 9;
        public float addonHorizontalOffset;
        public float addonVerticalOffset;
        public float addonScale = 1f;
        public float addonLayerOffset;
        public float earOffsetX = MUGBVisualTuningDefaults.EarOffsetX;
        public float earOffsetY = MUGBVisualTuningDefaults.EarOffsetY;
        public float earScale = MUGBVisualTuningDefaults.EarScale;
        public float eyeOffsetX = MUGBVisualTuningDefaults.EyeOffsetX;
        public float eyeOffsetY = MUGBVisualTuningDefaults.EyeOffsetY;
        public float eyeScale = MUGBVisualTuningDefaults.EyeScale;
        public float noseOffsetX = MUGBVisualTuningDefaults.NoseOffsetX;
        public float noseOffsetY = MUGBVisualTuningDefaults.NoseOffsetY;
        public float noseScale = MUGBVisualTuningDefaults.NoseScale;
        public float mouthOffsetX = MUGBVisualTuningDefaults.MouthOffsetX;
        public float mouthOffsetY = MUGBVisualTuningDefaults.MouthOffsetY;
        public float mouthScale = MUGBVisualTuningDefaults.MouthScale;
        public float earLayerOffset = MUGBVisualTuningDefaults.EarLayerOffset;
        public float eyeLayerOffset = MUGBVisualTuningDefaults.EyeLayerOffset;
        public float noseLayerOffset = MUGBVisualTuningDefaults.NoseLayerOffset;
        public float mouthLayerOffset = MUGBVisualTuningDefaults.MouthLayerOffset;
        public float headScale = MUGBVisualTuningDefaults.HeadScale;
        public float headHorizontalOffset;
        public float headVerticalOffset;
        public float bodyScale = MUGBVisualTuningDefaults.BodyScale;
        public float bodyHorizontalOffset;
        public float bodyVerticalOffset;
        public float goblinGlobalRenderScale = MUGBVisualTuningDefaults.GoblinGlobalRenderScale;
        public float pawnDrawAltitudeOffset = MUGBVisualTuningDefaults.PawnDrawAltitudeOffset;
        public float juvenileEarlyScale = MUGBVisualTuningDefaults.JuvenileEarlyScale;
        public float juvenileLateScale = MUGBVisualTuningDefaults.JuvenileLateScale;
        // 한국어 참고: 기존 세이브의 낡은 고블린을 로드 시 1회 교정할지 여부입니다.
        public bool repairLegacyGoblinPawns = true;
        public Dictionary<string, float> directionalTuning = new Dictionary<string, float>();
        public Dictionary<string, float> renderTargetTuning = new Dictionary<string, float>();
        private readonly object renderTuningRuntimeCacheLock = new object();
        private Dictionary<DirectionalRuntimeKey, RenderTuningValues> directionalRuntimeCache = new Dictionary<DirectionalRuntimeKey, RenderTuningValues>();
        private Dictionary<CombinedDirectionalRuntimeKey, RenderTuningValues> combinedDirectionalRuntimeCache = new Dictionary<CombinedDirectionalRuntimeKey, RenderTuningValues>();
        private Dictionary<RenderTargetRuntimeKey, RenderTuningValues> renderTargetRuntimeCache = new Dictionary<RenderTargetRuntimeKey, RenderTuningValues>();

        public void ResetVisualTuning()
        {
            useTextureColors = true;
            moveHeadgearWithHead = true;
            forceGoblinBaldAndNoBeard = true;
            harHeadSizeExemption = false;
            addonFollowHeadScale = true;
            addonHorizontalOffset = 0f;
            addonVerticalOffset = 0f;
            addonScale = 1f;
            addonLayerOffset = 0f;
            earOffsetX = MUGBVisualTuningDefaults.EarOffsetX;
            earOffsetY = MUGBVisualTuningDefaults.EarOffsetY;
            earScale = MUGBVisualTuningDefaults.EarScale;
            eyeOffsetX = MUGBVisualTuningDefaults.EyeOffsetX;
            eyeOffsetY = MUGBVisualTuningDefaults.EyeOffsetY;
            eyeScale = MUGBVisualTuningDefaults.EyeScale;
            noseOffsetX = MUGBVisualTuningDefaults.NoseOffsetX;
            noseOffsetY = MUGBVisualTuningDefaults.NoseOffsetY;
            noseScale = MUGBVisualTuningDefaults.NoseScale;
            mouthOffsetX = MUGBVisualTuningDefaults.MouthOffsetX;
            mouthOffsetY = MUGBVisualTuningDefaults.MouthOffsetY;
            mouthScale = MUGBVisualTuningDefaults.MouthScale;
            earLayerOffset = MUGBVisualTuningDefaults.EarLayerOffset;
            eyeLayerOffset = MUGBVisualTuningDefaults.EyeLayerOffset;
            noseLayerOffset = MUGBVisualTuningDefaults.NoseLayerOffset;
            mouthLayerOffset = MUGBVisualTuningDefaults.MouthLayerOffset;
            headScale = MUGBVisualTuningDefaults.HeadScale;
            headHorizontalOffset = 0f;
            headVerticalOffset = 0f;
            bodyScale = MUGBVisualTuningDefaults.BodyScale;
            bodyHorizontalOffset = 0f;
            bodyVerticalOffset = 0f;
            goblinGlobalRenderScale = MUGBVisualTuningDefaults.GoblinGlobalRenderScale;
            pawnDrawAltitudeOffset = MUGBVisualTuningDefaults.PawnDrawAltitudeOffset;
            juvenileEarlyScale = MUGBVisualTuningDefaults.JuvenileEarlyScale;
            juvenileLateScale = MUGBVisualTuningDefaults.JuvenileLateScale;
            repairLegacyGoblinPawns = true;
            directionalTuning = new Dictionary<string, float>();
            renderTargetTuning = new Dictionary<string, float>();
            InvalidateRenderTuningRuntimeCache();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref useTextureColors, "useTextureColors", true);
            Scribe_Values.Look(ref moveHeadgearWithHead, "moveHeadgearWithHead", true);
            Scribe_Values.Look(ref forceGoblinBaldAndNoBeard, "forceGoblinBaldAndNoBeard", true);
            Scribe_Values.Look(ref harHeadSizeExemption, "harHeadSizeExemption", false);
            Scribe_Values.Look(ref addonFollowHeadScale, "addonFollowHeadScale", true);
            Scribe_Values.Look(ref enableDraftedWeaponPoseOffsets, "enableDraftedWeaponPoseOffsets", true);
            Scribe_Values.Look(ref pawnLossAdaptationPercent, "pawnLossAdaptationPercent", 50);
            Scribe_Values.Look(ref kimDeokPalPawnLossAdaptationPercent, "kimDeokPalPawnLossAdaptationPercent", 50);
            Scribe_Values.Look(ref passingGroupFrequencyPercent, "passingGroupFrequencyPercent", 110);
            passingGroupFrequencyPercent = Mathf.Clamp(passingGroupFrequencyPercent, 0, 200);
            Scribe_Values.Look(ref enableGoblinSquadSystem, "enableGoblinSquadSystem", true);
            Scribe_Values.Look(ref enableGoblinCompositeRaids, "enableGoblinCompositeRaids", true);
            Scribe_Values.Look(ref requireSlaveMarriagePheromonePreference, "requireSlaveMarriagePheromonePreference", true);
            Scribe_Values.Look(ref enableFeminineTrait, "enableFeminineTrait", true);
            Scribe_Values.Look(ref adjustFemaleBodyTypeChances, "adjustFemaleBodyTypeChances", true);
            Scribe_Values.Look(ref americanBeautyStandard, "americanBeautyStandard", false);
            Scribe_Values.Look(ref goblinLitterSizeMultiplier, "goblinLitterSizeMultiplier", 1f);
            goblinLitterSizeMultiplier = Patches.GoblinLitterSizeUtility.NormalizeMultiplier(goblinLitterSizeMultiplier);
            Scribe_Values.Look(ref goblinChildStageDays, "goblinChildStageDays", 3.5f);
            goblinChildStageDays = Patches.GoblinAgeUtility.NormalizeChildStageDays(goblinChildStageDays);
            Scribe_Values.Look(ref goblinBirthStrainLimit, "goblinBirthStrainLimit", 4);
            goblinBirthStrainLimit = Patches.GoblinBirthStrainUtility.NormalizeLimit(goblinBirthStrainLimit);
            Scribe_Values.Look(ref disableToxicPheromonesCheat, "disableToxicPheromonesCheat", false);
            Scribe_Values.Look(ref allowFacialAnimationForGoblins, "allowFacialAnimationForGoblins", false);
            Scribe_Values.Look(ref goblinSquadSoftCap, "goblinSquadSoftCap", 6);
            Scribe_Values.Look(ref goblinSquadHardCap, "goblinSquadHardCap", 9);
            Scribe_Values.Look(ref addonHorizontalOffset, "addonHorizontalOffset", 0f);
            Scribe_Values.Look(ref addonVerticalOffset, "addonVerticalOffset", 0f);
            Scribe_Values.Look(ref addonScale, "addonScale", 1f);
            Scribe_Values.Look(ref addonLayerOffset, "addonLayerOffset", 0f);
            Scribe_Values.Look(ref earOffsetX, "earOffsetX", MUGBVisualTuningDefaults.EarOffsetX);
            Scribe_Values.Look(ref earOffsetY, "earOffsetY", MUGBVisualTuningDefaults.EarOffsetY);
            Scribe_Values.Look(ref earScale, "earScale", MUGBVisualTuningDefaults.EarScale);
            Scribe_Values.Look(ref eyeOffsetX, "eyeOffsetX", MUGBVisualTuningDefaults.EyeOffsetX);
            Scribe_Values.Look(ref eyeOffsetY, "eyeOffsetY", MUGBVisualTuningDefaults.EyeOffsetY);
            Scribe_Values.Look(ref eyeScale, "eyeScale", MUGBVisualTuningDefaults.EyeScale);
            Scribe_Values.Look(ref noseOffsetX, "noseOffsetX", MUGBVisualTuningDefaults.NoseOffsetX);
            Scribe_Values.Look(ref noseOffsetY, "noseOffsetY", MUGBVisualTuningDefaults.NoseOffsetY);
            Scribe_Values.Look(ref noseScale, "noseScale", MUGBVisualTuningDefaults.NoseScale);
            Scribe_Values.Look(ref mouthOffsetX, "mouthOffsetX", MUGBVisualTuningDefaults.MouthOffsetX);
            Scribe_Values.Look(ref mouthOffsetY, "mouthOffsetY", MUGBVisualTuningDefaults.MouthOffsetY);
            Scribe_Values.Look(ref mouthScale, "mouthScale", MUGBVisualTuningDefaults.MouthScale);
            Scribe_Values.Look(ref earLayerOffset, "earLayerOffset", MUGBVisualTuningDefaults.EarLayerOffset);
            Scribe_Values.Look(ref eyeLayerOffset, "eyeLayerOffset", MUGBVisualTuningDefaults.EyeLayerOffset);
            Scribe_Values.Look(ref noseLayerOffset, "noseLayerOffset", MUGBVisualTuningDefaults.NoseLayerOffset);
            Scribe_Values.Look(ref mouthLayerOffset, "mouthLayerOffset", MUGBVisualTuningDefaults.MouthLayerOffset);
            Scribe_Values.Look(ref headScale, "headScale", MUGBVisualTuningDefaults.HeadScale);
            Scribe_Values.Look(ref headHorizontalOffset, "headHorizontalOffset", 0f);
            Scribe_Values.Look(ref headVerticalOffset, "headVerticalOffset", 0f);
            Scribe_Values.Look(ref bodyScale, "bodyScale", MUGBVisualTuningDefaults.BodyScale);
            Scribe_Values.Look(ref bodyHorizontalOffset, "bodyHorizontalOffset", 0f);
            Scribe_Values.Look(ref bodyVerticalOffset, "bodyVerticalOffset", 0f);
            Scribe_Values.Look(ref goblinGlobalRenderScale, "goblinGlobalRenderScale", MUGBVisualTuningDefaults.GoblinGlobalRenderScale);
            Scribe_Values.Look(ref pawnDrawAltitudeOffset, "pawnDrawAltitudeOffset", MUGBVisualTuningDefaults.PawnDrawAltitudeOffset);
            Scribe_Values.Look(ref juvenileEarlyScale, "juvenileEarlyScale", MUGBVisualTuningDefaults.JuvenileEarlyScale);
            Scribe_Values.Look(ref juvenileLateScale, "juvenileLateScale", MUGBVisualTuningDefaults.JuvenileLateScale);
            Scribe_Values.Look(ref repairLegacyGoblinPawns, "repairLegacyGoblinPawns", true);
            Scribe_Collections.Look(ref directionalTuning, "directionalTuning", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref renderTargetTuning, "renderTargetTuning", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawnLossAdaptationPercent != 0 && pawnLossAdaptationPercent != 50 && pawnLossAdaptationPercent != 100)
                {
                    pawnLossAdaptationPercent = 50;
                }
                if (kimDeokPalPawnLossAdaptationPercent != 0 && kimDeokPalPawnLossAdaptationPercent != 50 && kimDeokPalPawnLossAdaptationPercent != 100)
                {
                    kimDeokPalPawnLossAdaptationPercent = 50;
                }
                goblinSquadSoftCap = Mathf.Clamp(goblinSquadSoftCap, 1, 9);
                goblinSquadHardCap = Mathf.Clamp(goblinSquadHardCap, goblinSquadSoftCap, 12);
                if (directionalTuning == null)
                {
                    directionalTuning = new Dictionary<string, float>();
                }
                if (renderTargetTuning == null)
                {
                    renderTargetTuning = new Dictionary<string, float>();
                }
                InvalidateRenderTuningRuntimeCache();
            }
        }

        public Vector2 OffsetForAddon(string tuningKey, Rot4 facing, string formKey)
        {
            string groupKey = AddonGroupKey(tuningKey);
            Vector2 directional = new Vector2(GetDirectionalOffsetX(tuningKey, facing), GetDirectionalOffsetY(tuningKey, facing));
            directional += new Vector2(GetDirectionalOffsetX(groupKey, facing), GetDirectionalOffsetY(groupKey, facing));
            directional += new Vector2(GetDirectionalOffsetX($"{formKey}.{tuningKey}", facing), GetDirectionalOffsetY($"{formKey}.{tuningKey}", facing));
            directional += new Vector2(GetDirectionalOffsetX($"{formKey}.{groupKey}", facing), GetDirectionalOffsetY($"{formKey}.{groupKey}", facing));
            if (groupKey == "Ear")
            {
                return new Vector2(earOffsetX, earOffsetY) + directional;
            }

            if (groupKey == "Eye")
            {
                return new Vector2(eyeOffsetX, eyeOffsetY) + directional;
            }

            if (tuningKey == "Nose")
            {
                return new Vector2(noseOffsetX, noseOffsetY) + directional;
            }

            if (tuningKey == "Mouth")
            {
                return new Vector2(mouthOffsetX, mouthOffsetY) + directional;
            }

            return directional;
        }

        public float ScaleForAddon(string tuningKey, Rot4 facing, string formKey)
        {
            string groupKey = AddonGroupKey(tuningKey);
            float directional = GetDirectionalScale(tuningKey, facing) * GetDirectionalScale(groupKey, facing) * GetDirectionalScale($"{formKey}.{tuningKey}", facing) * GetDirectionalScale($"{formKey}.{groupKey}", facing);
            if (groupKey == "Ear")
            {
                return earScale * directional;
            }

            if (groupKey == "Eye")
            {
                return eyeScale * directional;
            }

            if (tuningKey == "Nose")
            {
                return noseScale * directional;
            }

            if (tuningKey == "Mouth")
            {
                return mouthScale * directional;
            }

            return directional;
        }

        public float LayerOffsetForAddon(string tuningKey, Rot4 facing, string formKey)
        {
            string groupKey = AddonGroupKey(tuningKey);
            float directional = GetDirectionalLayerOffset(tuningKey, facing) + GetDirectionalLayerOffset(groupKey, facing) + GetDirectionalLayerOffset($"{formKey}.{tuningKey}", facing) + GetDirectionalLayerOffset($"{formKey}.{groupKey}", facing);
            if (groupKey == "Ear")
            {
                return addonLayerOffset + earLayerOffset + directional;
            }

            if (groupKey == "Eye")
            {
                return addonLayerOffset + eyeLayerOffset + directional;
            }

            if (tuningKey == "Nose")
            {
                return addonLayerOffset + noseLayerOffset + directional;
            }

            if (tuningKey == "Mouth")
            {
                return addonLayerOffset + mouthLayerOffset + directional;
            }

            return addonLayerOffset + directional;
        }

        private static string AddonGroupKey(string tuningKey)
        {
            if (tuningKey == "EarLeft" || tuningKey == "EarRight")
            {
                return "Ear";
            }

            if (tuningKey == "EyeLeft" || tuningKey == "EyeRight")
            {
                return "Eye";
            }

            return tuningKey;
        }

        public float GetDirectionalOffsetX(string key, Rot4 rot)
        {
            return GetDirectionalRuntimeValues(key, rot).OffsetX;
        }

        public float GetDirectionalOffsetY(string key, Rot4 rot)
        {
            return GetDirectionalRuntimeValues(key, rot).OffsetY;
        }

        public float GetDirectionalScale(string key, Rot4 rot)
        {
            return GetDirectionalRuntimeValues(key, rot).Scale;
        }

        public float GetDirectionalLayerOffset(string key, Rot4 rot)
        {
            return GetDirectionalRuntimeValues(key, rot).Layer;
        }

        public float GetDirectionalDefaultOffsetX(string key, Rot4 rot)
        {
            return GetDirectionalDefaultValue(key, rot, "x", 0f);
        }

        public float GetDirectionalDefaultOffsetY(string key, Rot4 rot)
        {
            return GetDirectionalDefaultValue(key, rot, "y", 0f);
        }

        public float GetDirectionalDefaultScale(string key, Rot4 rot)
        {
            return GetDirectionalDefaultValue(key, rot, "scale", 1f);
        }

        public float GetDirectionalDefaultLayerOffset(string key, Rot4 rot)
        {
            return GetDirectionalDefaultValue(key, rot, "layer", 0f);
        }

        public void SetDirectionalOffsetX(string key, Rot4 rot, float value)
        {
            SetDirectionalValue(key, rot, "x", value, 0f);
        }

        public void SetDirectionalOffsetY(string key, Rot4 rot, float value)
        {
            SetDirectionalValue(key, rot, "y", value, 0f);
        }

        public void SetDirectionalScale(string key, Rot4 rot, float value)
        {
            SetDirectionalValue(key, rot, "scale", value, 1f);
        }

        public void SetDirectionalLayerOffset(string key, Rot4 rot, float value)
        {
            SetDirectionalValue(key, rot, "layer", value, 0f);
        }

        public void ResetDirectionalValues(string key, Rot4 rot)
        {
            SetDirectionalValue(key, rot, "x", GetDirectionalDefaultOffsetX(key, rot), 0f);
            SetDirectionalValue(key, rot, "y", GetDirectionalDefaultOffsetY(key, rot), 0f);
            SetDirectionalValue(key, rot, "scale", GetDirectionalDefaultScale(key, rot), 1f);
            SetDirectionalValue(key, rot, "layer", GetDirectionalDefaultLayerOffset(key, rot), 0f);
        }

        public float GetRenderTargetOffsetX(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetRuntimeValues(category, apparelDef, formKey, rot).OffsetX;
        }

        public float GetRenderTargetOffsetY(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetRuntimeValues(category, apparelDef, formKey, rot).OffsetY;
        }

        public float GetRenderTargetScale(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetRuntimeValues(category, apparelDef, formKey, rot).Scale;
        }

        public float GetRenderTargetLayerOffset(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetRuntimeValues(category, apparelDef, formKey, rot).Layer;
        }

        public float GetRenderTargetDefaultOffsetX(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetDefault(category, apparelDef, formKey, rot, "x", 0f);
        }

        public float GetRenderTargetDefaultOffsetY(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetDefault(category, apparelDef, formKey, rot, "y", 0f);
        }

        public float GetRenderTargetDefaultScale(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetDefault(category, apparelDef, formKey, rot, "scale", 1f);
        }

        public float GetRenderTargetDefaultLayerOffset(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            return GetRenderTargetDefault(category, apparelDef, formKey, rot, "layer", 0f);
        }

        public void SetRenderTargetOffsetX(string category, ThingDef apparelDef, string formKey, Rot4 rot, float value)
        {
            SetRenderTargetValue(category, apparelDef, formKey, rot, "x", value, GetRenderTargetDefault(category, apparelDef, formKey, rot, "x", 0f));
        }

        public void SetRenderTargetOffsetY(string category, ThingDef apparelDef, string formKey, Rot4 rot, float value)
        {
            SetRenderTargetValue(category, apparelDef, formKey, rot, "y", value, GetRenderTargetDefault(category, apparelDef, formKey, rot, "y", 0f));
        }

        public void SetRenderTargetScale(string category, ThingDef apparelDef, string formKey, Rot4 rot, float value)
        {
            SetRenderTargetValue(category, apparelDef, formKey, rot, "scale", value, GetRenderTargetDefault(category, apparelDef, formKey, rot, "scale", 1f));
        }

        public void SetRenderTargetLayerOffset(string category, ThingDef apparelDef, string formKey, Rot4 rot, float value)
        {
            SetRenderTargetValue(category, apparelDef, formKey, rot, "layer", value, GetRenderTargetDefault(category, apparelDef, formKey, rot, "layer", 0f));
        }

        public void ResetRenderTargetValues(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            SetRenderTargetValue(category, apparelDef, formKey, rot, "x", GetRenderTargetDefault(category, apparelDef, formKey, rot, "x", 0f), GetRenderTargetDefault(category, apparelDef, formKey, rot, "x", 0f));
            SetRenderTargetValue(category, apparelDef, formKey, rot, "y", GetRenderTargetDefault(category, apparelDef, formKey, rot, "y", 0f), GetRenderTargetDefault(category, apparelDef, formKey, rot, "y", 0f));
            SetRenderTargetValue(category, apparelDef, formKey, rot, "scale", GetRenderTargetDefault(category, apparelDef, formKey, rot, "scale", 1f), GetRenderTargetDefault(category, apparelDef, formKey, rot, "scale", 1f));
            SetRenderTargetValue(category, apparelDef, formKey, rot, "layer", GetRenderTargetDefault(category, apparelDef, formKey, rot, "layer", 0f), GetRenderTargetDefault(category, apparelDef, formKey, rot, "layer", 0f));
        }

        private static float GetRenderTargetDefault(string category, ThingDef apparelDef, string formKey, Rot4 rot, string field, float fallback)
        {
            string defName = apparelDef?.defName;
            if (MUGBVisualTuningDefaults.TryGetApparelDefault(category, defName, formKey, rot, field, out float explicitDefault))
            {
                return explicitDefault;
            }

            if (UsesUnknownExternalApparelFallback(category, apparelDef, formKey))
            {
                if (formKey == "Goblin")
                {
                    if (field == "scale")
                    {
                        if (rot == Rot4.North)
                        {
                            return 0.78f;
                        }
                        if (rot == Rot4.South)
                        {
                            return 0.93f;
                        }
                    }
                    else if (field == "y" && rot == Rot4.North)
                    {
                        return -0.091f;
                    }
                }
                else if (formKey == "Hobgoblin" && field == "scale")
                {
                    return rot == Rot4.North ? 0.95f : 0.96f;
                }
            }

            if (UsesExternalHeadgearFallback(category, apparelDef, formKey))
            {
                // Korean source intent: unknown external headgear receives a conservative form-specific fallback.
                // Hobgoblin: west X +0.125, east X -0.125, east/west Y -0.045, south Y -0.01, south layer +9.
                // Goblin: north Y +0.153 and scale 0.94; south scale 0.96 and layer +12.8.
                if (formKey == "Goblin")
                {
                    if (rot == Rot4.North)
                    {
                        if (field == "y")
                        {
                            return 0.153f;
                        }
                        if (field == "scale")
                        {
                            return 0.94f;
                        }
                    }
                    else if (rot == Rot4.South)
                    {
                        if (field == "scale")
                        {
                            return 0.96f;
                        }
                        if (field == "layer")
                        {
                            return 12.8f;
                        }
                    }
                }
                else if (formKey == "Hobgoblin" && field == "x")
                {
                    if (rot == Rot4.West)
                    {
                        return fallback + 0.125f;
                    }
                    if (rot == Rot4.East)
                    {
                        return fallback - 0.125f;
                    }
                }
                else if (formKey == "Hobgoblin" && field == "y")
                {
                    if (rot == Rot4.East || rot == Rot4.West)
                    {
                        return fallback - 0.045f;
                    }
                    if (rot == Rot4.South)
                    {
                        return fallback - 0.01f;
                    }
                }
                else if (formKey == "Hobgoblin" && field == "layer" && rot == Rot4.South)
                {
                    return fallback + 9f;
                }
            }

            return fallback;
        }

        private static bool UsesUnknownExternalApparelFallback(string category, ThingDef apparelDef, string formKey)
        {
            if (category == "Headgear" || category == "Shield" || apparelDef?.modContentPack == null ||
                apparelDef.modContentPack.PackageId == "mustard1557.mugb.goblin")
            {
                return false;
            }

            return formKey == "Goblin" || formKey == "Hobgoblin";
        }

        private static bool UsesExternalHeadgearFallback(string category, ThingDef apparelDef, string formKey)
        {
            if (category != "Headgear" || apparelDef?.modContentPack == null ||
                apparelDef.modContentPack.PackageId == "mustard1557.mugb.goblin")
            {
                return false;
            }

            return formKey == "Goblin" || formKey == "Hobgoblin";
        }

        private float GetRenderTargetValue(string category, ThingDef apparelDef, string formKey, Rot4 rot, string field, float defaultValue)
        {
            Dictionary<string, float> snapshot = renderTargetTuning;
            if (snapshot == null)
            {
                return defaultValue;
            }

            string scopedKey = RenderTargetKey(category, apparelDef, formKey, rot, field);
            if (snapshot.TryGetValue(scopedKey, out float scopedValue))
            {
                return scopedValue;
            }

            string legacyKey = LegacyRenderTargetKey(category, apparelDef?.defName, formKey, rot, field);
            if (snapshot.TryGetValue(legacyKey, out float legacyValue))
            {
                return legacyValue;
            }

            if (category != "Apparel")
            {
                string oldScopedKey = RenderTargetKey("Apparel", apparelDef, formKey, rot, field);
                if (snapshot.TryGetValue(oldScopedKey, out float oldScopedValue))
                {
                    return oldScopedValue;
                }

                string oldLegacyKey = LegacyRenderTargetKey("Apparel", apparelDef?.defName, formKey, rot, field);
                if (snapshot.TryGetValue(oldLegacyKey, out float oldLegacyValue))
                {
                    return oldLegacyValue;
                }
            }

            if (MUGBVisualTuningDefaults.TryGetApparelFormFallback(formKey, out string fallbackFormKey))
            {
                string fallbackScopedKey = RenderTargetKey(category, apparelDef, fallbackFormKey, rot, field);
                if (snapshot.TryGetValue(fallbackScopedKey, out float fallbackScopedValue))
                {
                    return fallbackScopedValue;
                }

                string fallbackLegacyKey = LegacyRenderTargetKey(category, apparelDef?.defName, fallbackFormKey, rot, field);
                if (snapshot.TryGetValue(fallbackLegacyKey, out float fallbackLegacyValue))
                {
                    return fallbackLegacyValue;
                }

                if (category != "Apparel")
                {
                    string fallbackOldScopedKey = RenderTargetKey("Apparel", apparelDef, fallbackFormKey, rot, field);
                    if (snapshot.TryGetValue(fallbackOldScopedKey, out float fallbackOldScopedValue))
                    {
                        return fallbackOldScopedValue;
                    }

                    string fallbackOldLegacyKey = LegacyRenderTargetKey("Apparel", apparelDef?.defName, fallbackFormKey, rot, field);
                    if (snapshot.TryGetValue(fallbackOldLegacyKey, out float fallbackOldLegacyValue))
                    {
                        return fallbackOldLegacyValue;
                    }
                }
            }

            return defaultValue;
        }

        internal RenderTuningValues GetRenderTargetRuntimeValues(string category, ThingDef apparelDef, string formKey, Rot4 rot)
        {
            RenderTargetRuntimeKey cacheKey = new RenderTargetRuntimeKey(category, apparelDef, formKey, rot);
            Dictionary<RenderTargetRuntimeKey, RenderTuningValues> snapshot = Volatile.Read(ref renderTargetRuntimeCache);
            if (snapshot.TryGetValue(cacheKey, out RenderTuningValues cachedValue))
            {
                return cachedValue;
            }

            RenderTuningValues value = new RenderTuningValues(
                GetRenderTargetValue(category, apparelDef, formKey, rot, "x", GetRenderTargetDefault(category, apparelDef, formKey, rot, "x", 0f)),
                GetRenderTargetValue(category, apparelDef, formKey, rot, "y", GetRenderTargetDefault(category, apparelDef, formKey, rot, "y", 0f)),
                GetRenderTargetValue(category, apparelDef, formKey, rot, "scale", GetRenderTargetDefault(category, apparelDef, formKey, rot, "scale", 1f)),
                GetRenderTargetValue(category, apparelDef, formKey, rot, "layer", GetRenderTargetDefault(category, apparelDef, formKey, rot, "layer", 0f)));
            lock (renderTuningRuntimeCacheLock)
            {
                snapshot = Volatile.Read(ref renderTargetRuntimeCache);
                if (snapshot.TryGetValue(cacheKey, out cachedValue))
                {
                    return cachedValue;
                }

                Dictionary<RenderTargetRuntimeKey, RenderTuningValues> updated = new Dictionary<RenderTargetRuntimeKey, RenderTuningValues>(snapshot)
                {
                    [cacheKey] = value
                };
                Volatile.Write(ref renderTargetRuntimeCache, updated);
            }
            return value;
        }

        private void SetRenderTargetValue(string category, ThingDef apparelDef, string formKey, Rot4 rot, string field, float value, float defaultValue)
        {
            Dictionary<string, float> updated = renderTargetTuning == null
                ? new Dictionary<string, float>()
                : new Dictionary<string, float>(renderTargetTuning);
            string fullKey = RenderTargetKey(category, apparelDef, formKey, rot, field);
            string legacyKey = LegacyRenderTargetKey(category, apparelDef?.defName, formKey, rot, field);
            string oldScopedKey = category == "Apparel" ? null : RenderTargetKey("Apparel", apparelDef, formKey, rot, field);
            string oldLegacyKey = category == "Apparel" ? null : LegacyRenderTargetKey("Apparel", apparelDef?.defName, formKey, rot, field);
            if (Mathf.Abs(value - defaultValue) < 0.0001f)
            {
                updated.Remove(fullKey);
                updated.Remove(legacyKey);
                if (oldScopedKey != null)
                {
                    updated.Remove(oldScopedKey);
                }
                if (oldLegacyKey != null)
                {
                    updated.Remove(oldLegacyKey);
                }
            }
            else
            {
                updated[fullKey] = value;
                updated.Remove(legacyKey);
                if (oldScopedKey != null)
                {
                    updated.Remove(oldScopedKey);
                }
                if (oldLegacyKey != null)
                {
                    updated.Remove(oldLegacyKey);
                }
            }
            renderTargetTuning = updated;
            InvalidateRenderTuningRuntimeCache();
        }

        internal RenderTuningValues GetDirectionalRuntimeValues(string key, Rot4 rot)
        {
            DirectionalRuntimeKey cacheKey = new DirectionalRuntimeKey(key, rot);
            Dictionary<DirectionalRuntimeKey, RenderTuningValues> snapshot = Volatile.Read(ref directionalRuntimeCache);
            if (snapshot.TryGetValue(cacheKey, out RenderTuningValues cachedValue))
            {
                return cachedValue;
            }

            RenderTuningValues value = new RenderTuningValues(
                GetDirectionalValue(key, rot, "x", 0f),
                GetDirectionalValue(key, rot, "y", 0f),
                GetDirectionalValue(key, rot, "scale", 1f),
                GetDirectionalValue(key, rot, "layer", 0f));
            lock (renderTuningRuntimeCacheLock)
            {
                snapshot = Volatile.Read(ref directionalRuntimeCache);
                if (snapshot.TryGetValue(cacheKey, out cachedValue))
                {
                    return cachedValue;
                }

                Dictionary<DirectionalRuntimeKey, RenderTuningValues> updated = new Dictionary<DirectionalRuntimeKey, RenderTuningValues>(snapshot)
                {
                    [cacheKey] = value
                };
                Volatile.Write(ref directionalRuntimeCache, updated);
            }
            return value;
        }

        internal RenderTuningValues GetCombinedDirectionalRuntimeValues(string globalKey, string partKey, Rot4 rot)
        {
            CombinedDirectionalRuntimeKey cacheKey = new CombinedDirectionalRuntimeKey(globalKey, partKey, rot);
            Dictionary<CombinedDirectionalRuntimeKey, RenderTuningValues> snapshot = Volatile.Read(ref combinedDirectionalRuntimeCache);
            if (snapshot.TryGetValue(cacheKey, out RenderTuningValues cachedValue))
            {
                return cachedValue;
            }

            RenderTuningValues global = GetDirectionalRuntimeValues(globalKey, rot);
            RenderTuningValues part = GetDirectionalRuntimeValues(partKey, rot);
            RenderTuningValues value = new RenderTuningValues(
                global.OffsetX + part.OffsetX,
                global.OffsetY + part.OffsetY,
                global.Scale * part.Scale,
                global.Layer + part.Layer);
            lock (renderTuningRuntimeCacheLock)
            {
                snapshot = Volatile.Read(ref combinedDirectionalRuntimeCache);
                if (snapshot.TryGetValue(cacheKey, out cachedValue))
                {
                    return cachedValue;
                }

                Dictionary<CombinedDirectionalRuntimeKey, RenderTuningValues> updated = new Dictionary<CombinedDirectionalRuntimeKey, RenderTuningValues>(snapshot)
                {
                    [cacheKey] = value
                };
                Volatile.Write(ref combinedDirectionalRuntimeCache, updated);
            }
            return value;
        }

        private float GetDirectionalValue(string key, Rot4 rot, string field, float defaultValue)
        {
            float tunedDefault = GetDirectionalDefaultValue(key, rot, field, defaultValue);
            Dictionary<string, float> snapshot = directionalTuning;
            return snapshot != null && snapshot.TryGetValue(DirectionalKey(key, rot, field), out float value) ? value : tunedDefault;
        }

        private void SetDirectionalValue(string key, Rot4 rot, string field, float value, float defaultValue)
        {
            Dictionary<string, float> updated = directionalTuning == null
                ? new Dictionary<string, float>()
                : new Dictionary<string, float>(directionalTuning);
            float tunedDefault = GetDirectionalDefaultValue(key, rot, field, defaultValue);
            string fullKey = DirectionalKey(key, rot, field);
            if (Mathf.Abs(value - tunedDefault) < 0.0001f)
            {
                updated.Remove(fullKey);
            }
            else
            {
                updated[fullKey] = value;
            }
            directionalTuning = updated;
            InvalidateRenderTuningRuntimeCache();
        }

        private void InvalidateRenderTuningRuntimeCache()
        {
            lock (renderTuningRuntimeCacheLock)
            {
                Volatile.Write(ref directionalRuntimeCache, new Dictionary<DirectionalRuntimeKey, RenderTuningValues>());
                Volatile.Write(ref combinedDirectionalRuntimeCache, new Dictionary<CombinedDirectionalRuntimeKey, RenderTuningValues>());
                Volatile.Write(ref renderTargetRuntimeCache, new Dictionary<RenderTargetRuntimeKey, RenderTuningValues>());
            }
        }

        internal readonly struct RenderTuningValues
        {
            public RenderTuningValues(float offsetX, float offsetY, float scale, float layer)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Scale = scale;
                Layer = layer;
            }

            public float OffsetX { get; }
            public float OffsetY { get; }
            public float Scale { get; }
            public float Layer { get; }
        }

        private readonly struct DirectionalRuntimeKey : System.IEquatable<DirectionalRuntimeKey>
        {
            private readonly string key;
            private readonly byte rotation;

            public DirectionalRuntimeKey(string key, Rot4 rotation)
            {
                this.key = key;
                this.rotation = (byte)rotation.AsInt;
            }

            public bool Equals(DirectionalRuntimeKey other)
            {
                return key == other.key && rotation == other.rotation;
            }

            public override bool Equals(object obj)
            {
                return obj is DirectionalRuntimeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = key?.GetHashCode() ?? 0;
                    return (hash * 397) ^ rotation;
                }
            }
        }

        private readonly struct RenderTargetRuntimeKey : System.IEquatable<RenderTargetRuntimeKey>
        {
            private readonly string category;
            private readonly ThingDef apparelDef;
            private readonly string formKey;
            private readonly byte rotation;

            public RenderTargetRuntimeKey(string category, ThingDef apparelDef, string formKey, Rot4 rotation)
            {
                this.category = category;
                this.apparelDef = apparelDef;
                this.formKey = formKey;
                this.rotation = (byte)rotation.AsInt;
            }

            public bool Equals(RenderTargetRuntimeKey other)
            {
                return category == other.category && apparelDef == other.apparelDef && formKey == other.formKey && rotation == other.rotation;
            }

            public override bool Equals(object obj)
            {
                return obj is RenderTargetRuntimeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = category?.GetHashCode() ?? 0;
                    hash = (hash * 397) ^ (apparelDef?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (formKey?.GetHashCode() ?? 0);
                    return (hash * 397) ^ rotation;
                }
            }
        }

        private readonly struct CombinedDirectionalRuntimeKey : System.IEquatable<CombinedDirectionalRuntimeKey>
        {
            private readonly string globalKey;
            private readonly string partKey;
            private readonly byte rotation;

            public CombinedDirectionalRuntimeKey(string globalKey, string partKey, Rot4 rotation)
            {
                this.globalKey = globalKey;
                this.partKey = partKey;
                this.rotation = (byte)rotation.AsInt;
            }

            public bool Equals(CombinedDirectionalRuntimeKey other)
            {
                return globalKey == other.globalKey && partKey == other.partKey && rotation == other.rotation;
            }

            public override bool Equals(object obj)
            {
                return obj is CombinedDirectionalRuntimeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = globalKey?.GetHashCode() ?? 0;
                    hash = (hash * 397) ^ (partKey?.GetHashCode() ?? 0);
                    return (hash * 397) ^ rotation;
                }
            }
        }

        private float GetDirectionalDefaultValue(string key, Rot4 rot, string field, float defaultValue)
        {
            if (TryGetCrossEyedBaseKey(key, out string crossEyedBaseKey, out string crossEyedOverrideKey))
            {
                if (MUGBVisualTuningDefaults.TryGetCrossEyedDirectionalOverride(crossEyedOverrideKey, rot, field, out float crossEyedValue))
                {
                    return crossEyedValue;
                }

                return GetDirectionalValue(crossEyedBaseKey, rot, field, defaultValue);
            }

            if (MUGBVisualTuningDefaults.TryGetDirectionalDefault(key, rot, field, out float directValue))
            {
                return directValue;
            }

            if (TryGetDessicatedBaseKey(key, out string baseKey))
            {
                return GetDirectionalValue(baseKey, rot, field, defaultValue);
            }

            if (TryGetJuvenileBaseKey(key, field, out baseKey, out float additiveDefault))
            {
                return GetDirectionalValue(baseKey, rot, field, defaultValue) + additiveDefault;
            }

            return defaultValue;
        }

        private static bool TryGetDessicatedBaseKey(string key, out string baseKey)
        {
            const string GoblinPrefix = "GoblinDessicated.";
            const string HobgoblinPrefix = "HobgoblinDessicated.";
            if (key != null && key.StartsWith(GoblinPrefix, System.StringComparison.Ordinal))
            {
                baseKey = "Goblin." + key.Substring(GoblinPrefix.Length);
                return true;
            }

            if (key != null && key.StartsWith(HobgoblinPrefix, System.StringComparison.Ordinal))
            {
                baseKey = "Hobgoblin." + key.Substring(HobgoblinPrefix.Length);
                return true;
            }

            baseKey = null;
            return false;
        }

        private static bool TryGetCrossEyedBaseKey(string key, out string baseKey, out string overrideBaseKey)
        {
            baseKey = null;
            overrideBaseKey = null;
            if (key == null)
            {
                return false;
            }

            if (key.StartsWith("GoblinCrossEyedChild.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("GoblinCrossEyedChild.".Length);
                baseKey = "GoblinChild." + suffix;
                overrideBaseKey = "Goblin." + suffix;
                return true;
            }

            if (key.StartsWith("HobgoblinCrossEyedChild.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("HobgoblinCrossEyedChild.".Length);
                baseKey = "HobgoblinChild." + suffix;
                overrideBaseKey = "Hobgoblin." + suffix;
                return true;
            }

            if (key.StartsWith("GoblinCrossEyed.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("GoblinCrossEyed.".Length);
                baseKey = "Goblin." + suffix;
                overrideBaseKey = baseKey;
                return true;
            }

            if (key.StartsWith("HobgoblinCrossEyed.", System.StringComparison.Ordinal))
            {
                string suffix = key.Substring("HobgoblinCrossEyed.".Length);
                baseKey = "Hobgoblin." + suffix;
                overrideBaseKey = baseKey;
                return true;
            }

            return false;
        }

        private static bool TryGetJuvenileBaseKey(string key, string field, out string baseKey, out float additiveDefault)
        {
            baseKey = null;
            additiveDefault = 0f;
            if (key == null)
            {
                return false;
            }

            if (key.StartsWith("GoblinChild.", System.StringComparison.Ordinal))
            {
                baseKey = "Goblin." + key.Substring("GoblinChild.".Length);
            }
            else if (key.StartsWith("GoblinCrossEyedChild.", System.StringComparison.Ordinal))
            {
                baseKey = "GoblinCrossEyed." + key.Substring("GoblinCrossEyedChild.".Length);
            }
            else if (key.StartsWith("HobgoblinChild.", System.StringComparison.Ordinal))
            {
                baseKey = "Hobgoblin." + key.Substring("HobgoblinChild.".Length);
            }
            else if (key.StartsWith("HobgoblinCrossEyedChild.", System.StringComparison.Ordinal))
            {
                baseKey = "HobgoblinCrossEyed." + key.Substring("HobgoblinCrossEyedChild.".Length);
            }

            if (baseKey == null)
            {
                return false;
            }

            // 한국어 의도: 예전에는 여기서 머리 y에 JuvenileHeadLiftDefault(+0.08)를 무조건 더했다.
            // 그 값은 청소년 몸이 작아진 걸 보정하려는 것이었는데, 실제로 바닐라는 청소년을 성인 크기로
            // 그리기 때문에 보정할 대상이 없었고 머리만 들려 목이 분리돼 보였다.
            // 이제 축소는 JuvenileRenderScaleFor가 오프셋/스케일에 함께 곱해 처리하므로 덧셈 보정은 없앤다.
            return true;
        }

        private static string DirectionalKey(string key, Rot4 rot, string field)
        {
            return $"{key}.{RotKey(rot)}.{field}";
        }

        private static string RenderTargetKey(string category, ThingDef apparelDef, string formKey, Rot4 rot, string field)
        {
            return $"{category}.{RenderTargetSourceKey(apparelDef)}.{apparelDef?.defName ?? "UnknownDef"}.{formKey}.{RotKey(rot)}.{field}";
        }

        private static string LegacyRenderTargetKey(string category, string defName, string formKey, Rot4 rot, string field)
        {
            return $"{category}.{defName}.{formKey}.{RotKey(rot)}.{field}";
        }

        private static string RenderTargetSourceKey(ThingDef apparelDef)
        {
            string source = apparelDef?.modContentPack?.PackageIdPlayerFacing;
            if (source.NullOrEmpty())
            {
                source = apparelDef?.modContentPack?.Name;
            }
            if (source.NullOrEmpty())
            {
                source = "Core";
            }
            return source.Replace(' ', '_');
        }

        private static string RotKey(Rot4 rot)
        {
            if (rot == Rot4.North)
            {
                return "north";
            }

            if (rot == Rot4.East)
            {
                return "east";
            }

            if (rot == Rot4.West)
            {
                return "west";
            }

            return "south";
        }

        public string ExportApparelTuning()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("MUGB apparel tuning export");
            builder.AppendLine("한국어 참고: 장비/의류 defName별 고블린 전용 렌더 보정값입니다.");
            builder.AppendLine();
            Dictionary<string, float> snapshot = renderTargetTuning;
            if (snapshot == null || snapshot.Count == 0)
            {
                builder.AppendLine("(no apparel tuning values)");
                return builder.ToString();
            }

            foreach (KeyValuePair<string, float> pair in snapshot)
            {
                builder.AppendLine($"{pair.Key}: {pair.Value:0.###}");
            }

            return builder.ToString();
        }

        public string ExportRenderTargetTuning(string category, ThingDef apparelDef)
        {
            StringBuilder builder = new StringBuilder();
            string defName = apparelDef?.defName ?? "UnknownDef";
            builder.AppendLine($"MUGB apparel tuning export: {category}.{defName}");
            builder.AppendLine();
            Dictionary<string, float> snapshot = renderTargetTuning;
            if (snapshot == null || snapshot.Count == 0)
            {
                builder.AppendLine("(no values)");
                return builder.ToString();
            }

            string prefix = $"{category}.{RenderTargetSourceKey(apparelDef)}.{defName}.";
            string legacyPrefix = $"{category}.{defName}.";
            bool any = false;
            foreach (KeyValuePair<string, float> pair in snapshot)
            {
                if (pair.Key.StartsWith(prefix) || pair.Key.StartsWith(legacyPrefix))
                {
                    builder.AppendLine($"{pair.Key}: {pair.Value:0.###}");
                    any = true;
                }
            }

            if (!any)
            {
                builder.AppendLine("(no values for selected item)");
            }

            return builder.ToString();
        }

        public string ExportVisualTuning()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("MUGB visual tuning export");
            builder.AppendLine("Copy these values back to Codex when the goblin visuals look correct.");
            builder.AppendLine();
            builder.AppendLine($"Body X: {bodyHorizontalOffset:0.###}");
            builder.AppendLine($"Body Y: {bodyVerticalOffset:0.###}");
            builder.AppendLine($"Body VEF visual scale: {bodyScale:0.###}");
            builder.AppendLine($"Head X: {headHorizontalOffset:0.###}");
            builder.AppendLine($"Head Y: {headVerticalOffset:0.###}");
            builder.AppendLine($"Head VEF visual scale: {headScale:0.###}");
            builder.AppendLine($"Goblin global render scale: {goblinGlobalRenderScale:0.###}");
            builder.AppendLine($"Pawn draw altitude: {pawnDrawAltitudeOffset:0.###}");
            builder.AppendLine();
            builder.AppendLine($"Global addon X: {addonHorizontalOffset:0.###}");
            builder.AppendLine($"Global addon Y: {addonVerticalOffset:0.###}");
            builder.AppendLine($"Global addon scale: {addonScale:0.###}");
            builder.AppendLine($"Global addon layer: {addonLayerOffset:0.###}");
            builder.AppendLine();

            foreach (string key in new[] { "Goblin.Head", "GoblinCrossEyed.Head", "Hobgoblin.Head", "HobgoblinCrossEyed.Head", "GoblinChild.Head", "GoblinCrossEyedChild.Head", "HobgoblinChild.Head", "HobgoblinCrossEyedChild.Head", "GoblinDessicated.Head", "HobgoblinDessicated.Head", "Goblin.Body", "GoblinCrossEyed.Body", "Hobgoblin.Body", "HobgoblinCrossEyed.Body", "GoblinChild.Body", "GoblinCrossEyedChild.Body", "HobgoblinChild.Body", "HobgoblinCrossEyedChild.Body", "GoblinDessicated.Body", "HobgoblinDessicated.Body" })
            {
                builder.AppendLine($"[{key}]");
                foreach (Rot4 rot in MUGBMod.Rotations)
                {
                    builder.AppendLine($"{RotKey(rot)} X: {GetDirectionalOffsetX(key, rot):0.###}");
                    builder.AppendLine($"{RotKey(rot)} Y: {GetDirectionalOffsetY(key, rot):0.###}");
                    builder.AppendLine($"{RotKey(rot)} render-node scale: {GetDirectionalScale(key, rot):0.###}");
                    builder.AppendLine($"{RotKey(rot)} render-node layer: {GetDirectionalLayerOffset(key, rot):0.###}");
                }
                builder.AppendLine();
            }

            foreach (string key in new[] { "Goblin.EarLeft", "Goblin.EarRight", "Goblin.EyeLeft", "Goblin.EyeRight", "Goblin.Nose", "Goblin.Mouth", "GoblinCrossEyed.EarLeft", "GoblinCrossEyed.EarRight", "GoblinCrossEyed.EyeLeft", "GoblinCrossEyed.EyeRight", "GoblinCrossEyed.Nose", "GoblinCrossEyed.Mouth", "Hobgoblin.EarLeft", "Hobgoblin.EarRight", "Hobgoblin.EyeLeft", "Hobgoblin.EyeRight", "Hobgoblin.Nose", "Hobgoblin.Mouth", "HobgoblinCrossEyed.EarLeft", "HobgoblinCrossEyed.EarRight", "HobgoblinCrossEyed.EyeLeft", "HobgoblinCrossEyed.EyeRight", "HobgoblinCrossEyed.Nose", "HobgoblinCrossEyed.Mouth", "GoblinChild.EarLeft", "GoblinChild.EarRight", "GoblinChild.EyeLeft", "GoblinChild.EyeRight", "GoblinChild.Nose", "GoblinChild.Mouth", "GoblinCrossEyedChild.EarLeft", "GoblinCrossEyedChild.EarRight", "GoblinCrossEyedChild.EyeLeft", "GoblinCrossEyedChild.EyeRight", "GoblinCrossEyedChild.Nose", "GoblinCrossEyedChild.Mouth", "HobgoblinChild.EarLeft", "HobgoblinChild.EarRight", "HobgoblinChild.EyeLeft", "HobgoblinChild.EyeRight", "HobgoblinChild.Nose", "HobgoblinChild.Mouth", "HobgoblinCrossEyedChild.EarLeft", "HobgoblinCrossEyedChild.EarRight", "HobgoblinCrossEyedChild.EyeLeft", "HobgoblinCrossEyedChild.EyeRight", "HobgoblinCrossEyedChild.Nose", "HobgoblinCrossEyedChild.Mouth" })
            {
                builder.AppendLine($"[{key}]");
                foreach (Rot4 rot in MUGBMod.Rotations)
                {
                    builder.AppendLine($"{RotKey(rot)} X: {GetDirectionalOffsetX(key, rot):0.###}");
                    builder.AppendLine($"{RotKey(rot)} Y: {GetDirectionalOffsetY(key, rot):0.###}");
                    builder.AppendLine($"{RotKey(rot)} scale: {GetDirectionalScale(key, rot):0.###}");
                    builder.AppendLine($"{RotKey(rot)} layer: {GetDirectionalLayerOffset(key, rot):0.###}");
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
