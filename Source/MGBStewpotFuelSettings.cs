using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MUGB
{
    public class CompProperties_GoblinFuelFilter : CompProperties
    {
        public bool showFuelFilterToggles = false;
        public bool allowWood = true;
        public bool allowCoal = true;

        public CompProperties_GoblinFuelFilter()
        {
            compClass = typeof(CompGoblinFuelFilter);
        }
    }

    public class CompGoblinFuelFilter : ThingComp
    {
        private ThingFilter allowedFuelFilter;
        private bool allowWood;
        private bool allowCoal;
        private CompRefuelable cachedRefuelable;
        private bool refuelableResolved;

        private CompProperties_GoblinFuelFilter Props => (CompProperties_GoblinFuelFilter)props;

        public ThingFilter AllowedFuelFilter => EnsureAllowedFuelFilter();

        /// <summary>
        /// 한국어 의도: 미디블 오버홀이 이 건물의 연료를 직접 관리하는지 알려줍니다.
        /// 미디블이 있으면 연료 필터와 "현재 연료" 고정 규칙을 미디블 쪽에 완전히 넘기고,
        /// 우리 연료 탭과 급유 패치는 물러납니다. 양쪽이 동시에 개입하면 유저가 미디블
        /// 연료 탭에서 고른 설정이 우리 패치에 덮여 무시되기 때문입니다.
        /// </summary>
        public bool MedievalFuelHandlerActive =>
            GoblinFuelFilterUtility.HasMedievalFuelHandler(parent);

        /// <summary>
        /// 한국어 의도: CompRefuelable을 한 번만 찾아 캐시합니다.
        /// AllowsFuel은 연료 후보를 훑는 동안 대상 하나마다 호출되므로,
        /// 그때마다 TryGetComp로 컴프 목록을 뒤지면 낭비가 큽니다.
        /// </summary>
        private CompRefuelable RefuelableComp
        {
            get
            {
                if (!refuelableResolved)
                {
                    refuelableResolved = true;
                    cachedRefuelable = parent?.TryGetComp<CompRefuelable>();
                }

                return cachedRefuelable;
            }
        }

        // 연료 후보 확장은 PostSpawnSetup에서 한 번만 수행합니다. 이 프로퍼티는 급유 탐색의
        // 최내곽에서 호출되므로 여기서는 아무 계산도 하지 않습니다.
        public ThingFilter ParentFuelFilter => RefuelableComp?.Props.fuelFilter;

        public bool CoalFuelAvailable => DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Coal") != null;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            allowWood = Props.allowWood;
            allowCoal = Props.allowCoal;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // 한국어 의도: 중세 연료 후보(석탄·숯·목질 재료 등)를 CompRefuelable 쪽 필터에
            // 미리 넓혀 둡니다. 미디블의 CompStoreFuelThing은 PostSpawnSetup에서 이 필터를
            // 복사해 가는데, 컴프 순서상 우리가 먼저 실행되므로 넓힌 결과가 그대로 반영됩니다.
            // 이 호출이 없으면 미디블 연료 탭에 나무와 석탄만 뜨게 됩니다.
            //
            // 순서가 중요합니다. 부모 필터를 먼저 넓힌 뒤에 우리 필터를 만들어야
            // 넓어진 후보가 그대로 담깁니다.
            GoblinFuelFilterUtility.EnsureMedievalFuelCandidates(ParentFuelFilter);
            EnsureAllowedFuelFilter();
            PruneAllowedFuelFilterToParent();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref allowedFuelFilter, "allowedFuelFilter");
            Scribe_Values.Look(ref allowWood, "allowWoodFuel", true);
            Scribe_Values.Look(ref allowCoal, "allowCoalFuel", true);

            // 여기서 필터를 미리 만들지 않습니다. 이 시점의 부모 필터는 아직 연료 후보가
            // 넓혀지기 전이라, 지금 복사하면 나무만 담긴 필터가 굳어버립니다.
            // 생성과 정리는 부모 필터를 넓힌 뒤 PostSpawnSetup에서 처리합니다.
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Props.showFuelFilterToggles)
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "MUGB_UseWoodFuel".Translate(),
                defaultDesc = "MUGB_UseWoodFuelDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/MGB_UseWoodFuel", reportFailure: false),
                isActive = () => allowWood,
                toggleAction = delegate
                {
                    allowWood = !allowWood;
                    if (allowWood)
                    {
                        GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
                    }
                    else
                    {
                        GoblinSlaveMarriageUtility.PlayCommandCanceledSound();
                    }
                }
            };

            if (DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Coal") != null)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "MUGB_UseCoalFuel".Translate(),
                    defaultDesc = "MUGB_UseCoalFuelDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Icons/MGB_UseWoodFuel", reportFailure: false),
                    isActive = () => allowCoal,
                    toggleAction = delegate
                    {
                        allowCoal = !allowCoal;
                        if (allowCoal)
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
        }

        public bool AllowsFuel(ThingDef fuelDef)
        {
            if (fuelDef == null)
            {
                return false;
            }

            ThingFilter parentFilter = ParentFuelFilter;
            return parentFilter != null && parentFilter.Allows(fuelDef) && AllowedFuelFilter.Allows(fuelDef);
        }

        private ThingFilter EnsureAllowedFuelFilter()
        {
            if (allowedFuelFilter == null)
            {
                allowedFuelFilter = new ThingFilter();
                ThingFilter parentFilter = ParentFuelFilter;
                if (parentFilter != null)
                {
                    allowedFuelFilter.CopyAllowancesFrom(parentFilter);
                }

                ApplyLegacyFuelToggles();
            }

            return allowedFuelFilter;
        }

        /// <summary>
        /// 한국어 의도: 저장본에 남아 있던 허용 항목 중 이제는 부모 필터가 허용하지 않는 것을 걷어냅니다.
        /// (모드 구성이 바뀌어 예전에 쓰던 연료가 사라진 경우 등)
        ///
        /// 예전에는 AllowedFuelFilter를 읽을 때마다 이 정리를 돌리면서 매번 리스트를 새로 할당했습니다.
        /// 그런데 이 프로퍼티는 급유 대상을 찾는 동안 후보 하나마다 호출되기 때문에, 지도에 나무가
        /// 쌓여 있을수록 쓰레기 할당이 폭증했습니다. 부모 필터는 게임 중에 바뀌지 않으므로
        /// 배치·불러오기 시점에 한 번만 정리하면 충분합니다.
        /// </summary>
        private void PruneAllowedFuelFilterToParent()
        {
            ThingFilter parentFilter = ParentFuelFilter;
            if (parentFilter == null || allowedFuelFilter == null)
            {
                return;
            }

            List<ThingDef> staleDefs = null;
            foreach (ThingDef allowedDef in allowedFuelFilter.AllowedThingDefs)
            {
                if (!parentFilter.Allows(allowedDef))
                {
                    if (staleDefs == null)
                    {
                        staleDefs = new List<ThingDef>();
                    }

                    staleDefs.Add(allowedDef);
                }
            }

            if (staleDefs == null)
            {
                return;
            }

            foreach (ThingDef staleDef in staleDefs)
            {
                allowedFuelFilter.SetAllow(staleDef, false);
            }
        }

        private void ApplyLegacyFuelToggles()
        {
            ThingDef wood = ThingDef.Named("WoodLog");
            if (wood != null)
            {
                allowedFuelFilter.SetAllow(wood, allowWood);
            }

            ThingDef coal = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Coal");
            if (coal != null)
            {
                allowedFuelFilter.SetAllow(coal, allowCoal);
            }
        }
    }

    public static class GoblinFuelFilterUtility
    {
        private static readonly HashSet<ThingFilter> PatchedFilters = new HashSet<ThingFilter>();

        private static bool medievalFuelTypeResolved;
        private static Type medievalFuelHandlerType;

        /// <summary>
        /// 한국어 의도: 미디블 오버홀의 연료 관리 컴프를 어셈블리 참조 없이 이름으로 찾습니다.
        /// 미디블이 없거나 클래스 이름이 바뀌면 null이 되어 기존 고블린 연료 로직이 그대로 유지됩니다.
        /// </summary>
        private static Type MedievalFuelHandlerType
        {
            get
            {
                if (!medievalFuelTypeResolved)
                {
                    medievalFuelTypeResolved = true;
                    medievalFuelHandlerType = AccessTools.TypeByName("MedievalOverhaul.CompStoreFuelThing");
                }

                return medievalFuelHandlerType;
            }
        }

        public static bool HasMedievalFuelHandler(ThingWithComps thing)
        {
            Type handlerType = MedievalFuelHandlerType;
            if (handlerType == null || thing == null)
            {
                return false;
            }

            List<ThingComp> comps = thing.AllComps;
            if (comps == null)
            {
                return false;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                if (handlerType.IsInstanceOfType(comps[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasMedievalFuelHandler(Thing thing)
        {
            return HasMedievalFuelHandler(thing as ThingWithComps);
        }

        private static List<ThingDef> cachedFuelCandidates;

        /// <summary>
        /// 한국어 의도: 연료로 쓸 만한 ThingDef 목록을 한 번만 추려 재사용합니다.
        /// 판정에 문자열 소문자 변환이 들어가는데, 예전에는 건물 필터마다 전체 Def를 다시
        /// 훑어서 같은 계산을 반복했습니다. 결과는 모든 필터에서 동일하므로 한 번이면 됩니다.
        /// </summary>
        private static List<ThingDef> FuelCandidates
        {
            get
            {
                if (cachedFuelCandidates == null)
                {
                    cachedFuelCandidates = new List<ThingDef>();
                    foreach (ThingDef fuelDef in DefDatabase<ThingDef>.AllDefsListForReading)
                    {
                        if (IsMedievalFuelCandidate(fuelDef))
                        {
                            cachedFuelCandidates.Add(fuelDef);
                        }
                    }
                }

                return cachedFuelCandidates;
            }
        }

        public static void EnsureMedievalFuelCandidates(ThingFilter filter)
        {
            if (filter == null || !PatchedFilters.Add(filter))
            {
                return;
            }

            List<ThingDef> candidates = FuelCandidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                filter.SetAllow(candidates[i], true);
            }
        }

        private static bool IsMedievalFuelCandidate(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Item)
            {
                return false;
            }

            if (def == ThingDefOf.WoodLog || def.defName == "WoodLog")
            {
                return true;
            }

            string defName = def.defName?.ToLowerInvariant() ?? string.Empty;
            string label = def.label?.ToLowerInvariant() ?? string.Empty;
            if (IsExcludedHighEnergyFuel(defName, label))
            {
                return false;
            }

            // 목질 재료는 스터프 카테고리라는 구조적 신호라서 이름 추측이 아닙니다.
            // 믿을 수 있으므로 원자재 조건 없이 그대로 통과시킵니다.
            if (IsWoodyStuff(def))
            {
                return true;
            }

            // 아래 이름 매칭은 오탐이 나기 쉬운 부분입니다. ("연료"가 이름에 들어간
            // 고에너지 아이템 등) 그래서 원자재로 한정해 그물을 좁힙니다.
            // 나무·석탄처럼 실제로 때는 연료는 전부 원자재로 분류되므로 손실이 없습니다.
            if (!IsRawResource(def))
            {
                return false;
            }

            return defName.Contains("coal") || label.Contains("coal")
                || defName.Contains("charcoal") || label.Contains("charcoal")
                || defName.Contains("firewood") || label.Contains("firewood")
                || defName.Contains("fuel") || label.Contains("fuel");
        }

        /// <summary>
        /// 한국어 의도: 원자재(ResourcesRaw) 계열인지 확인합니다.
        /// 모드들이 원자재 밑에 하위 카테고리를 만드는 경우가 많아 부모를 거슬러 올라가며 봅니다.
        /// </summary>
        private static bool IsRawResource(ThingDef def)
        {
            List<ThingCategoryDef> categories = def.thingCategories;
            ThingCategoryDef rawResources = ThingCategoryDefOf.ResourcesRaw;
            if (categories == null || rawResources == null)
            {
                return false;
            }

            for (int i = 0; i < categories.Count; i++)
            {
                for (ThingCategoryDef category = categories[i]; category != null; category = category.parent)
                {
                    if (category == rawResources)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsWoodyStuff(ThingDef def)
        {
            StuffCategoryDef woody = DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Woody");
            return woody != null && def.IsStuff && def.stuffProps?.categories?.Contains(woody) == true;
        }

        private static bool IsExcludedHighEnergyFuel(string defName, string label)
        {
            return defName.Contains("chemfuel") || label.Contains("chemfuel")
                || defName.Contains("chemical") || label.Contains("chemical")
                || defName.Contains("antimatter") || label.Contains("antimatter")
                || defName.Contains("anti_matter") || label.Contains("anti-matter")
                || defName.Contains("nuclear") || label.Contains("nuclear")
                || defName.Contains("uranium") || label.Contains("uranium")
                || defName.Contains("reactor") || label.Contains("reactor");
        }
    }

    /// <summary>
    /// 한국어 의도: 고블린 건물의 조사 탭 중복을 제거합니다.
    ///
    /// 미디블 오버홀의 Add_FuelFilter 패치는 CompRefuelable을 가진 모든 ThingDef에
    /// MedievalOverhaul.ITab_Fuel을 붙입니다. 고블린 화로는 부모(MUGB_GoblinCampfireBase)도
    /// CompRefuelable을 갖고 있어 부모와 자식 양쪽에 탭이 붙는데, inspectorTabs는 상속할 때
    /// 두 목록이 합쳐지므로 연료 탭이 두 개로 보입니다.
    ///
    /// XML 패치는 상속이 해석되기 전에 실행되어 합쳐진 결과를 볼 수 없습니다. 그래서 모드
    /// 로드 순서와 관계없이 확실하게 처리되도록 여기서 정리합니다.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GoblinInspectTabDeduplicator
    {
        static GoblinInspectTabDeduplicator()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.defName == null || !def.defName.StartsWith("MUGB_"))
                {
                    continue;
                }

                DeduplicateResolvedTabs(def);
            }
        }

        private static void DeduplicateResolvedTabs(ThingDef def)
        {
            List<InspectTabBase> tabs = def.inspectorTabsResolved;
            if (tabs == null || tabs.Count < 2)
            {
                return;
            }

            HashSet<Type> seenTypes = new HashSet<Type>();
            List<InspectTabBase> uniqueTabs = new List<InspectTabBase>(tabs.Count);

            foreach (InspectTabBase tab in tabs)
            {
                if (tab != null && seenTypes.Add(tab.GetType()))
                {
                    uniqueTabs.Add(tab);
                }
            }

            if (uniqueTabs.Count != tabs.Count)
            {
                def.inspectorTabsResolved = uniqueTabs;
            }
        }
    }

    public class ITab_GoblinFuel : ITab
    {
        private static readonly Vector2 WinSize = new Vector2(300f, 480f);

        private readonly ThingFilterUI.UIState thingFilterState = new ThingFilterUI.UIState();

        public ITab_GoblinFuel()
        {
            size = WinSize;
            labelKey = "MUGB_TabFuel";
            tutorTag = "MUGB_GoblinFuel";
        }

        private CompGoblinFuelFilter SelectedComp => SelThing?.TryGetComp<CompGoblinFuelFilter>();

        public override bool IsVisible
        {
            get
            {
                Thing thing = SelThing;
                CompGoblinFuelFilter comp = SelectedComp;

                // 미디블 오버홀이 이 건물의 연료를 맡으면 미디블 연료 탭 하나로 통합합니다.
                // Defs 패치가 우리 탭을 빼주지만, 패치가 적용되지 않은 경우에도 탭이 중복되지 않도록
                // 여기서 한 번 더 막습니다.
                if (comp == null || comp.MedievalFuelHandlerActive)
                {
                    return false;
                }

                return thing != null && (thing.Faction == null || thing.Faction == Faction.OfPlayer);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            thingFilterState.quickSearch.Reset();
        }

        protected override void FillTab()
        {
            CompGoblinFuelFilter comp = SelectedComp;
            ThingFilter parentFilter = comp?.ParentFuelFilter;
            if (comp == null || parentFilter == null)
            {
                return;
            }

            Rect rect = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);
            Widgets.BeginGroup(rect);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, 0f, rect.width, 22f), "MUGB_FuelTabHint".Translate());
            Text.Font = GameFont.Small;
            Rect filterRect = new Rect(0f, 26f, rect.width, rect.height - 26f);
            ThingFilterUI.DoThingFilterConfigWindow(filterRect, thingFilterState, comp.AllowedFuelFilter, parentFilter, 1, null, null, forceHideHitPointsConfig: true, forceHideQualityConfig: true);
            Widgets.EndGroup();
        }

        public override void Notify_ClickOutsideWindow()
        {
            base.Notify_ClickOutsideWindow();
            thingFilterState.quickSearch.Unfocus();
        }
    }

    [HarmonyPatch]
    public static class RefuelWorkGiverUtility_FindBestFuel_GoblinFuelFilter_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(RefuelWorkGiverUtility), "FindBestFuel");
        }

        public static void Postfix(Pawn pawn, Thing refuelable, ref Thing __result)
        {
            CompGoblinFuelFilter goblinFuelFilter = refuelable?.TryGetComp<CompGoblinFuelFilter>();
            if (goblinFuelFilter == null || goblinFuelFilter.MedievalFuelHandlerActive)
            {
                // 미디블이 이 건물의 연료를 맡고 있으면 그쪽 결정(연료 필터 + 현재 연료 고정)을
                // 그대로 둡니다. 여기서 다시 검색하면 미디블 연료 탭 설정이 무시됩니다.
                return;
            }

            if (__result != null && goblinFuelFilter.AllowsFuel(__result.def))
            {
                return;
            }

            CompRefuelable refuelableComp = refuelable.TryGetComp<CompRefuelable>();
            ThingFilter filter = refuelableComp?.Props.fuelFilter;
            if (filter == null)
            {
                __result = null;
                return;
            }

            __result = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, filter.BestThingRequest, PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, thing =>
            {
                if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing))
                {
                    return false;
                }

                return filter.Allows(thing) && goblinFuelFilter.AllowsFuel(thing.def);
            });
        }
    }

    [HarmonyPatch]
    public static class RefuelWorkGiverUtility_FindAllFuel_GoblinFuelFilter_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(RefuelWorkGiverUtility), "FindAllFuel");
        }

        public static void Postfix(Pawn pawn, Thing refuelable, ref List<Thing> __result)
        {
            CompGoblinFuelFilter goblinFuelFilter = refuelable?.TryGetComp<CompGoblinFuelFilter>();
            if (goblinFuelFilter == null || goblinFuelFilter.MedievalFuelHandlerActive)
            {
                return;
            }

            CompRefuelable refuelableComp = refuelable.TryGetComp<CompRefuelable>();
            ThingFilter filter = refuelableComp?.Props.fuelFilter;
            if (filter == null)
            {
                __result = null;
                return;
            }

            int fuelCountToFullyRefuel = refuelableComp.GetFuelCountToFullyRefuel();
            __result = RefuelWorkGiverUtility.FindEnoughReservableThings(
                pawn,
                refuelable.Position,
                new IntRange(fuelCountToFullyRefuel, fuelCountToFullyRefuel),
                thing => filter.Allows(thing) && goblinFuelFilter.AllowsFuel(thing.def));
        }
    }

    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.Refuel), new[] { typeof(List<Thing>) })]
    public static class CompRefuelable_Refuel_GoblinFuelFilter_Patch
    {
        public static void Prefix(CompRefuelable __instance, ref List<Thing> fuelThings)
        {
            CompGoblinFuelFilter goblinFuelFilter = __instance?.parent?.TryGetComp<CompGoblinFuelFilter>();
            if (goblinFuelFilter == null || fuelThings == null || goblinFuelFilter.MedievalFuelHandlerActive)
            {
                // 미디블이 맡은 건물은 건드리지 않습니다. 미디블은 이 메서드를 트랜스파일러로
                // 고쳐 목록에서 연료를 꺼내 쓰므로, 우리가 목록을 비우면 예외가 납니다.
                return;
            }

            List<Thing> allowedFuel = fuelThings
                .Where(thing => thing != null && goblinFuelFilter.AllowsFuel(thing.def))
                .ToList();

            // 필터 결과가 비면 원본을 그대로 둡니다. 빈 목록을 넘기면 급유가 조용히 실패합니다.
            if (allowedFuel.Count > 0)
            {
                fuelThings = allowedFuel;
            }
        }
    }
}
