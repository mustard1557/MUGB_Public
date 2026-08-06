using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MUGB
{
    public class CompGoblinAlwaysPowered : CompPowerTrader
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            PowerOutput = 0f;
            PowerOn = true;
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            PowerOn = true;
        }

        public override string CompInspectStringExtra()
        {
            return null;
        }
    }

    public class Building_GoblinStewpot : Building_NutrientPasteDispenser, IStoreSettingsParent
    {
        private const int BulkDispenseCount = 5;
        private const int CurrentStorageSettingsVersion = 1;
        private const string FullGraphicPath = "Things/Building/Production/MGB_bigpot/GBR_stewpot";
        private const string EmptyGraphicPath = "Things/Building/Production/MGB_bigpot/GBR_stewpotempty";

        private Graphic fullGraphic;
        private Graphic emptyGraphic;
        private StorageSettings storageSettings;
        private int storageSettingsVersion;

        public CompGoblinStewpotIngredients IngredientComp => this.TryGetComp<CompGoblinStewpotIngredients>();

        public override ThingDef DispensableDef => MUGBDefOf.MUGB_gutstew ?? DefDatabase<ThingDef>.GetNamedSilentFail("MUGB_gutstew") ?? ThingDefOf.MealNutrientPaste;

        public override Graphic Graphic => IngredientComp != null && IngredientComp.HasStoredIngredients ? FullGraphic : EmptyGraphic;

        // Keep the material/comp color behavior of an ordinary building, but do not inherit
        // the nutrient paste dispenser's yellow prisoner-room tint.
        public override Color DrawColor
        {
            get
            {
                CompColorable colorable = GetComp<CompColorable>();
                if (colorable != null && colorable.Active)
                {
                    return colorable.Color;
                }

                foreach (ThingComp comp in AllComps)
                {
                    Color? forcedColor = comp.ForceColor();
                    if (forcedColor.HasValue)
                    {
                        return forcedColor.Value;
                    }
                }

                if (Stuff != null)
                {
                    return def.GetColorForStuff(Stuff);
                }

                return def.graphicData?.color ?? Color.white;
            }
        }

        private Graphic FullGraphic => fullGraphic ?? (fullGraphic = GraphicDatabase.Get<Graphic_Multi>(FullGraphicPath, ShaderDatabase.CutoutComplex, def.graphicData.drawSize, DrawColor, DrawColorTwo, def.graphicData));

        private Graphic EmptyGraphic => emptyGraphic ?? (emptyGraphic = GraphicDatabase.Get<Graphic_Multi>(EmptyGraphicPath, ShaderDatabase.CutoutComplex, def.graphicData.drawSize, DrawColor, DrawColorTwo, def.graphicData));

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (powerComp != null)
            {
                powerComp.PowerOn = true;
            }
            EnsureStorageSettings();
            NormalizeRotation();
        }

        public override void PostMake()
        {
            base.PostMake();
            EnsureStorageSettings(newSettings: true);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref storageSettings, "storageSettings", this);
            Scribe_Values.Look(ref storageSettingsVersion, "storageSettingsVersion", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureStorageSettings(newSettings: false);
            }
        }

        public override string GetInspectString()
        {
            bool forPrisoners = !this.IsSociallyProper(null, forPrisoner: false);
            string text = base.GetInspectString();

            // The vanilla dispenser already adds this line for prison cells. Replace it with
            // one consistent purpose line that is also shown for colonist use.
            string vanillaPrisonLine = "InPrisonCell".Translate();
            if (forPrisoners && !text.NullOrEmpty())
            {
                text = string.Join("\n", text.Split('\n').Where(line => line.TrimEnd('\r') != vanillaPrisonLine)).Trim();
            }

            string useLabel = (forPrisoners
                ? "MUGB_GoblinStewpotUsePrisoners"
                : "MUGB_GoblinStewpotUseColonists").Translate();
            string purpose = "MUGB_GoblinStewpotUseType".Translate(useLabel);
            return text.NullOrEmpty() ? purpose : text.TrimEnd() + "\n" + purpose;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                if (gizmo is Designator_Build)
                {
                    continue;
                }

                yield return gizmo;
            }

            Command_Action dispense = new Command_Action
            {
                defaultLabel = "MUGB_GoblinStewpotLadle".Translate(),
                defaultDesc = "MUGB_GoblinStewpotLadleDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Things/Item/Food/MGB_gutstew", reportFailure: false) ?? ContentFinder<Texture2D>.Get("UI/Icons/MGB_LadleGutStew", reportFailure: false),
                action = delegate
                {
                    GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                    DispenseGutStew();
                }
            };

            if (!CanDispenseNow(out string disabledReason))
            {
                dispense.Disable(disabledReason);
            }
            yield return dispense;

            Command_Action dispenseFive = new Command_Action
            {
                defaultLabel = "MUGB_GoblinStewpotLadleFive".Translate(),
                defaultDesc = "MUGB_GoblinStewpotLadleFiveDesc".Translate(),
                icon = dispense.icon,
                action = delegate
                {
                    GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                    TryDispenseGutStew(BulkDispenseCount);
                }
            };

            if (!CanDispenseMealsNow(BulkDispenseCount, out string bulkDisabledReason))
            {
                dispenseFive.Disable(bulkDisabledReason);
            }
            yield return dispenseFive;

            if (DebugSettings.ShowDevGizmos && IngredientComp != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Fill stew ingredients",
                    defaultDesc = "Fill the goblin stewpot's stored guts and nutrition ingredients.",
                    action = delegate
                    {
                        IngredientComp.DebugFillIngredients();
                        DirtyVisuals();
                    }
                };
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null || selPawn.Map != Map)
            {
                yield break;
            }

            if (!selPawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("MUGB_GoblinStewpotCannotUseNoPath".Translate(), null);
                yield break;
            }

            CompGoblinStewpotIngredients comp = IngredientComp;
            if (comp == null)
            {
                yield break;
            }

            Thing ingredient = comp.FindBestIngredientFor(selPawn, forced: true);
            if (ingredient != null)
            {
                yield return new FloatMenuOption("MUGB_GoblinStewpotLoadIngredient".Translate(), delegate
                {
                    Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_LoadStewpotIngredient, ingredient, this);
                    job.count = comp.CountCanLoad(ingredient);
                    job.playerForced = true;
                    if (selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                    {
                        GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                    }
                });
            }
            else if (comp.NeedsAnyIngredient)
            {
                yield return new FloatMenuOption("MUGB_GoblinStewpotCannotLoadNoIngredient".Translate(), null);
            }

            if (CanDispenseNow(out string dispenseDisabledReason))
            {
                yield return new FloatMenuOption("MUGB_GoblinStewpotLadle".Translate(), delegate
                {
                    Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_DispenseStewpotMeal, this);
                    job.playerForced = true;
                    if (selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                    {
                        GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                    }
                });
            }
            else
            {
                yield return new FloatMenuOption("MUGB_GoblinStewpotCannotLadle".Translate(dispenseDisabledReason), null);
            }
        }

        public bool CanAcceptIngredient(Thing thing)
        {
            return IngredientComp?.CanAcceptIngredient(thing) == true;
        }

        public int CountCanLoad(Thing thing)
        {
            return IngredientComp?.CountCanLoad(thing) ?? 0;
        }

        public int LoadIngredientFromThing(Thing thing)
        {
            return IngredientComp?.LoadIngredientFromThing(thing) ?? 0;
        }

        private void DispenseGutStew()
        {
            TryDispenseGutStew(1);
        }

        public bool TryDispenseGutStew()
        {
            return TryDispenseGutStew(1);
        }

        public bool TryDispenseGutStew(int mealCount)
        {
            if (!CanDispenseMealsNow(mealCount, out string disabledReason))
            {
                Messages.Message(disabledReason, this, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            Thing stew = MakeStewStack(mealCount);
            GenPlace.TryPlaceThing(stew, Position, Map, ThingPlaceMode.Near);
            return true;
        }

        public override Thing TryDispenseFood()
        {
            if (!HasEnoughFeedstockInHoppers())
            {
                return null;
            }

            return MakeStewStack(1);
        }

        private Thing MakeStewStack(int mealCount)
        {
            Thing stew = ThingMaker.MakeThing(DispensableDef);
            stew.stackCount = mealCount;
            IngredientComp.RegisterIngredients(stew);
            IngredientComp.ConsumeForMeals(mealCount);
            def.building?.soundDispense?.PlayOneShot(new TargetInfo(Position, Map));
            DirtyVisuals();
            return stew;
        }

        public override Thing FindFeedInAnyHopper()
        {
            return null;
        }

        public override bool HasEnoughFeedstockInHoppers()
        {
            return HasHeatFuel && IngredientComp != null && IngredientComp.AvailableMeals > 0;
        }

        public override Building AdjacentReachableHopper(Pawn reacher)
        {
            return null;
        }

        private new bool CanDispenseNow(out string disabledReason)
        {
            return CanDispenseMealsNow(1, out disabledReason);
        }

        private bool CanDispenseMealsNow(int mealCount, out string disabledReason)
        {
            if (!HasHeatFuel)
            {
                disabledReason = "The goblin stewpot has no fuel.";
                return false;
            }

            if (mealCount <= 0 || IngredientComp == null || IngredientComp.AvailableMeals < mealCount)
            {
                disabledReason = "The goblin stewpot does not have enough guts and meat.";
                return false;
            }

            disabledReason = null;
            return true;
        }

        private bool HasHeatFuel
        {
            get
            {
                CompRefuelable refuelable = this.TryGetComp<CompRefuelable>();
                return refuelable == null || refuelable.HasFuel;
            }
        }

        private void NormalizeRotation()
        {
            if (Rotation == Rot4.East)
            {
                Rotation = Rot4.South;
            }
            else if (Rotation == Rot4.West)
            {
                Rotation = Rot4.North;
            }
        }

        public void DirtyVisuals()
        {
            if (!Spawned)
            {
                return;
            }

            foreach (IntVec3 cell in this.OccupiedRect())
            {
                Map.mapDrawer.MapMeshDirty(cell, (ulong)MapMeshFlagDefOf.Things | (ulong)MapMeshFlagDefOf.Buildings);
            }
        }

        public bool StorageTabVisible => true;

        public StorageSettings GetStoreSettings()
        {
            EnsureStorageSettings();
            return storageSettings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return def?.building?.fixedStorageSettings;
        }

        public void Notify_SettingsChanged()
        {
        }

        private void EnsureStorageSettings(bool newSettings = false)
        {
            EnsureDefStorageFilters();

            bool created = storageSettings == null;
            if (created)
            {
                storageSettings = new StorageSettings(this);
                if (def?.building?.defaultStorageSettings != null)
                {
                    storageSettings.CopyFrom(def.building.defaultStorageSettings);
                }
            }

            if (created || newSettings)
            {
                AllowAllSupportedIngredients(storageSettings);
                storageSettingsVersion = CurrentStorageSettingsVersion;
            }
            else if (storageSettingsVersion < CurrentStorageSettingsVersion)
            {
                MigrateLegacyStorageSettings();
                storageSettingsVersion = CurrentStorageSettingsVersion;
            }
        }

        private void MigrateLegacyStorageSettings()
        {
            // Older saves can retain a mixture of removed MGB_* defs and the former MeatRaw
            // category. Restore only the official stew ingredients once; stored nutrition,
            // stored guts, auto-fill state, fuel, and every unrelated storage choice are untouched.
            AllowAllSupportedIngredients(storageSettings);
        }

        private void EnsureDefStorageFilters()
        {
            AllowAllSupportedIngredients(def?.building?.fixedStorageSettings);
            AllowAllSupportedIngredients(def?.building?.defaultStorageSettings);
        }

        private static void AllowAllSupportedIngredients(StorageSettings settings)
        {
            ThingFilter filter = settings?.filter;
            if (filter == null)
            {
                return;
            }

            foreach (ThingDef ingredientDef in MUGBHumanlikeFoodUtility.StewpotIngredientDefs)
            {
                filter.SetAllow(ingredientDef, true);
            }
        }
    }

    public class CompProperties_GoblinStewpotIngredients : CompProperties
    {
        public float nutritionCapacity = 30f;
        public float nutritionCostPerMeal = 0.45f;
        public float gutUnitsCostPerMeal = 0.2f;
        public float ingredientNutritionEfficiency = 1.6f;
        public float autoFillPercent = 1f;
        public bool showAllowAutoFillToggle = true;
        public bool showIngredientFilterToggles = true;

        public CompProperties_GoblinStewpotIngredients()
        {
            compClass = typeof(CompGoblinStewpotIngredients);
        }
    }

    public class CompGoblinStewpotIngredients : ThingComp
    {
        private float storedNutrition;
        private float storedGutUnits;
        private bool allowAutoFill = true;
        private bool allowHumanGuts = true;
        private bool allowGoblinGuts = true;
        private bool allowHumanMeat = true;
        private bool allowGoblinMeat = true;
        private bool allowOtherMeat = false;
        private List<ThingDef> ingredientDefs = new List<ThingDef>();

        public CompProperties_GoblinStewpotIngredients Props => (CompProperties_GoblinStewpotIngredients)props;

        public bool HasStoredIngredients => storedNutrition > 0.001f || storedGutUnits > 0.001f;

        public int AvailableMeals => Mathf.FloorToInt(Mathf.Min(storedNutrition / Props.nutritionCostPerMeal, storedGutUnits / Props.gutUnitsCostPerMeal));

        public bool NeedsGutIngredient => TargetGutUnits - storedGutUnits >= 0.999f;

        public bool NeedsNutritionIngredient => storedNutrition < TargetNutrition - 0.001f;

        public bool NeedsAnyIngredient => NeedsGutIngredient || NeedsNutritionIngredient;

        public bool ShouldAutoFillNow => allowAutoFill && NeedsAnyIngredient;

        private float TargetNutrition => Props.nutritionCapacity * Mathf.Clamp01(Props.autoFillPercent);

        private float TargetGutUnits => TargetNutrition / Props.nutritionCostPerMeal * Props.gutUnitsCostPerMeal;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedNutrition, "storedNutrition", 0f);
            Scribe_Values.Look(ref storedGutUnits, "storedGutUnits", 0f);
            Scribe_Values.Look(ref allowAutoFill, "allowAutoFill", true);
            Scribe_Values.Look(ref allowHumanGuts, "allowHumanGuts", true);
            Scribe_Values.Look(ref allowGoblinGuts, "allowGoblinGuts", true);
            Scribe_Values.Look(ref allowHumanMeat, "allowHumanMeat", true);
            Scribe_Values.Look(ref allowGoblinMeat, "allowGoblinMeat", true);
            Scribe_Values.Look(ref allowOtherMeat, "allowOtherMeat", false);
            Scribe_Collections.Look(ref ingredientDefs, "ingredientDefs", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && ingredientDefs == null)
            {
                ingredientDefs = new List<ThingDef>();
            }
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("MUGB_GoblinStewpotStoredNutrition".Translate(storedNutrition.ToString("0.##"), Props.nutritionCapacity.ToString("0.#")));
            builder.AppendLine("MUGB_GoblinStewpotStoredGuts".Translate(Mathf.RoundToInt(storedGutUnits), Mathf.RoundToInt(TargetGutUnits)));
            builder.Append("MUGB_GoblinStewpotAvailableServings".Translate(AvailableMeals));
            return builder.ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Props.showAllowAutoFillToggle)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "MUGB_GoblinStewpotAutoFill".Translate(),
                    defaultDesc = "MUGB_GoblinStewpotAutoFillDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Icons/MGB_AutoFillStewpot", reportFailure: false),
                    isActive = () => allowAutoFill,
                    toggleAction = delegate
                    {
                        allowAutoFill = !allowAutoFill;
                        if (allowAutoFill)
                        {
                            GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                        }
                        else
                        {
                            GoblinSlaveMarriageUtility.PlayCommandCanceledSound();
                        }
                    }
                };
            }

            if (!Props.showIngredientFilterToggles)
            {
                yield break;
            }

            yield return IngredientToggle("MUGB_GoblinStewpotAllowHumanGuts".Translate(), "MUGB_GoblinStewpotAllowHumanGutsDesc".Translate(), () => allowHumanGuts, value => allowHumanGuts = value);
            yield return IngredientToggle("MUGB_GoblinStewpotAllowGoblinGuts".Translate(), "MUGB_GoblinStewpotAllowGoblinGutsDesc".Translate(), () => allowGoblinGuts, value => allowGoblinGuts = value);
            yield return IngredientToggle("MUGB_GoblinStewpotAllowHumanMeat".Translate(), "MUGB_GoblinStewpotAllowHumanMeatDesc".Translate(), () => allowHumanMeat, value => allowHumanMeat = value);
            yield return IngredientToggle("MUGB_GoblinStewpotAllowGoblinMeat".Translate(), "MUGB_GoblinStewpotAllowGoblinMeatDesc".Translate(), () => allowGoblinMeat, value => allowGoblinMeat = value);
            yield return IngredientToggle("MUGB_GoblinStewpotAllowOtherMeat".Translate(), "MUGB_GoblinStewpotAllowOtherMeatDesc".Translate(), () => allowOtherMeat, value => allowOtherMeat = value);
        }

        private Command_Toggle IngredientToggle(string label, string desc, Func<bool> getter, Action<bool> setter)
        {
            return new Command_Toggle
            {
                defaultLabel = label,
                defaultDesc = desc,
                isActive = getter,
                toggleAction = delegate
                {
                    bool next = !getter();
                    setter(next);
                    if (next)
                    {
                        GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                    }
                    else
                    {
                        GoblinSlaveMarriageUtility.PlayCommandCanceledSound();
                    }
                }
            };
        }

        public bool CanAcceptIngredient(Thing thing)
        {
            return CanLoadIngredient(thing) && CountCanLoad(thing) > 0;
        }

        public int CountCanLoad(Thing thing)
        {
            if (thing == null || thing.stackCount <= 0)
            {
                return 0;
            }

            if (!IsAllowedIngredientDef(thing.def))
            {
                return 0;
            }

            if (IsGutDef(thing.def))
            {
                float gutDeficit = Mathf.Max(0f, TargetGutUnits - storedGutUnits);
                int maxByGut = Mathf.FloorToInt(gutDeficit + 0.001f);
                return Mathf.Clamp(maxByGut, 0, thing.stackCount);
            }

            if (storedNutrition >= Props.nutritionCapacity - 0.001f)
            {
                return 0;
            }

            float nutritionPerItem = EffectiveNutritionPerItem(thing);
            return Mathf.Clamp(Mathf.FloorToInt((Props.nutritionCapacity - storedNutrition) / nutritionPerItem), 0, thing.stackCount);
        }

        public int LoadIngredientFromThing(Thing thing)
        {
            if (!CanLoadIngredient(thing))
            {
                return 0;
            }

            int count = Mathf.Min(thing.stackCount, CountCanLoad(thing));
            if (count <= 0)
            {
                return 0;
            }

            AddStoredIngredient(thing.def, count, EffectiveNutritionPerItem(thing) * count);
            if (count >= thing.stackCount)
            {
                thing.Destroy(DestroyMode.Vanish);
            }
            else
            {
                thing.stackCount -= count;
            }
            (parent as Building_GoblinStewpot)?.DirtyVisuals();
            return count;
        }

        public void ConsumeForMeal()
        {
            ConsumeForMeals(1);
        }

        public void ConsumeForMeals(int mealCount)
        {
            int count = Mathf.Max(0, mealCount);
            storedNutrition = Mathf.Max(0f, storedNutrition - Props.nutritionCostPerMeal * count);
            storedGutUnits = Mathf.Max(0f, storedGutUnits - Props.gutUnitsCostPerMeal * count);
        }

        public void DebugFillIngredients()
        {
            storedNutrition = Props.nutritionCapacity;
            storedGutUnits = TargetGutUnits;
            if (MUGBDefOf.MUGB_Hgut != null && !ingredientDefs.Contains(MUGBDefOf.MUGB_Hgut))
            {
                ingredientDefs.Add(MUGBDefOf.MUGB_Hgut);
            }
            if (ThingDefOf.Meat_Human != null && !ingredientDefs.Contains(ThingDefOf.Meat_Human))
            {
                ingredientDefs.Add(ThingDefOf.Meat_Human);
            }
        }

        public void RegisterIngredients(Thing stew)
        {
            GoblinFoodIngredientUtility.NormalizeGutStewIngredients(stew);
        }

        public Thing FindBestIngredientFor(Pawn pawn, bool forced)
        {
            if (!forced && !ShouldAutoFillNow)
            {
                return null;
            }

            if (NeedsGutIngredient)
            {
                Thing gut = FindIngredient(pawn, forced, thing => IsGutDef(thing.def) && IsAllowedIngredientDef(thing.def));
                if (gut != null)
                {
                    return gut;
                }
            }

            if (NeedsNutritionIngredient)
            {
                Thing meat = FindIngredient(pawn, forced, thing => IsNutritionIngredient(thing.def) && !IsGutDef(thing.def) && IsAllowedIngredientDef(thing.def));
                if (meat != null)
                {
                    return meat;
                }

                return FindIngredient(pawn, forced, thing => IsNutritionIngredient(thing.def) && IsAllowedIngredientDef(thing.def));
            }

            return null;
        }

        private Thing FindIngredient(Pawn pawn, bool forced, Predicate<Thing> extraValidator)
        {
            TraverseParms traverseParms = TraverseParms.For(pawn);
            IEnumerable<Thing> candidates = MUGBHumanlikeFoodUtility.StewpotIngredientDefs
                .Where(IsAllowedIngredientDef)
                .SelectMany(def => parent.Map.listerThings.ThingsOfDef(def));
            return GenClosest.ClosestThing_Global_Reachable(parent.Position, parent.Map, candidates, PathEndMode.ClosestTouch, traverseParms, 9999f, thing =>
            {
                if (!CanAcceptIngredient(thing) || !extraValidator(thing))
                {
                    return false;
                }
                if (thing.Fogged() || thing.IsForbidden(pawn) || !pawn.CanReserve(thing, 1, -1, null, forced))
                {
                    return false;
                }
                return true;
            });
        }

        private void AddStoredIngredient(ThingDef ingredientDef, int count, float nutrition)
        {
            storedNutrition = Mathf.Min(Props.nutritionCapacity, storedNutrition + nutrition);
            if (IsGutDef(ingredientDef))
            {
                storedGutUnits += count;
            }

            if (!ingredientDefs.Contains(ingredientDef))
            {
                ingredientDefs.Add(ingredientDef);
            }
        }

        private bool CanLoadIngredient(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.def == null || thing.IsForbidden(Faction.OfPlayer))
            {
                return false;
            }

            return IsAllowedIngredientDef(thing.def);
        }

        private float EffectiveNutritionPerItem(Thing thing)
        {
            return Mathf.Max(0.001f, thing.GetStatValue(StatDefOf.Nutrition) * Props.ingredientNutritionEfficiency);
        }

        public static bool IsGutDef(ThingDef def)
        {
            return def == MUGBDefOf.MUGB_Hgut || def == MUGBDefOf.MUGB_Ggut || def?.defName == "MUGB_Hgut" || def?.defName == "MUGB_Ggut";
        }

        private bool IsAllowedIngredientDef(ThingDef def)
        {
            if (!MUGBHumanlikeFoodUtility.IsStewpotIngredientDef(def))
            {
                return false;
            }

            if (parent is Building_GoblinStewpot pot)
            {
                StorageSettings settings = pot.GetStoreSettings();
                if (settings != null && !settings.AllowedToAccept(def))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNutritionIngredient(ThingDef def)
        {
            return def != null && (def.IsMeat || def == MUGBDefOf.Meat_Goblin || def == MUGBDefOf.MUGB_Hchunk || def == MUGBDefOf.MUGB_Gchunk);
        }
    }

    public class WorkGiver_FillGoblinStewpot : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(MUGBDefOf.MUGB_bigpot);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_GoblinStewpot pot) || t.Fogged() || t.IsForbidden(pawn) || t.Faction != pawn.Faction)
            {
                return null;
            }

            CompGoblinStewpotIngredients comp = pot.IngredientComp;
            if (comp == null || (!forced && !comp.ShouldAutoFillNow))
            {
                return null;
            }

            if (!pawn.CanReserve(t, 1, -1, null, forced))
            {
                return null;
            }

            Thing ingredient = comp.FindBestIngredientFor(pawn, forced);
            if (ingredient == null)
            {
                JobFailReason.Is("No reachable guts or meat.");
                return null;
            }

            Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_LoadStewpotIngredient, ingredient, pot);
            job.count = comp.CountCanLoad(ingredient);
            return job;
        }
    }

    public class JobDriver_LoadStewpotIngredient : JobDriver
    {
        private Thing Ingredient => job.GetTarget(TargetIndex.A).Thing;
        private Building_GoblinStewpot Pot => job.GetTarget(TargetIndex.B).Thing as Building_GoblinStewpot;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Ingredient, job, 1, job.count, null, errorOnFailed) && pawn.Reserve(Pot, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Pot == null || !Pot.CanAcceptIngredient(Ingredient));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, putRemainderInQueue: false, subtractNumTakenFromJobCount: true);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            Toil load = ToilMaker.MakeToil("LoadGoblinStewpotIngredient");
            load.initAction = delegate
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried != null && Pot != null)
                {
                    Pot.LoadIngredientFromThing(carried);
                    if (carried != null && !carried.Destroyed)
                    {
                        pawn.carryTracker.TryDropCarriedThing(Pot.Position, ThingPlaceMode.Near, out Thing _);
                    }
                }
            };
            load.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return load;
        }
    }

    public class JobDriver_DispenseStewpotMeal : JobDriver
    {
        private Building_GoblinStewpot Pot => job.GetTarget(TargetIndex.A).Thing as Building_GoblinStewpot;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Pot, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Pot == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil dispense = ToilMaker.MakeToil("DispenseGoblinStewpotMeal");
            dispense.initAction = delegate
            {
                Pot?.TryDispenseGutStew();
            };
            dispense.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dispense;
        }
    }
}
