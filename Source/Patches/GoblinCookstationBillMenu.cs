using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    public sealed class GoblinPrioritizedBillSelection : IExposable
    {
        public Pawn pawn;
        public Thing billGiver;
        public Bill bill;
        public WorkGiverDef workGiverDef;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref billGiver, "billGiver");
            Scribe_References.Look(ref bill, "bill");
            Scribe_Defs.Look(ref workGiverDef, "workGiverDef");
        }

        public bool IsValid => pawn != null
            && !pawn.Destroyed
            && billGiver != null
            && !billGiver.Destroyed
            && bill?.billStack?.billGiver == billGiver
            && workGiverDef != null;
    }

    public sealed class GameComponent_GoblinPrioritizedBills : GameComponent
    {
        private List<GoblinPrioritizedBillSelection> selections = new List<GoblinPrioritizedBillSelection>();

        [Unsaved(false)]
        private Dictionary<Pawn, GoblinPrioritizedBillSelection> selectionsByPawn =
            new Dictionary<Pawn, GoblinPrioritizedBillSelection>();

        public GameComponent_GoblinPrioritizedBills(Game game)
        {
        }

        public static GameComponent_GoblinPrioritizedBills Current =>
            Verse.Current.Game?.GetComponent<GameComponent_GoblinPrioritizedBills>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref selections, "mugbPrioritizedProductionBills", LookMode.Deep);
            if (selections == null)
            {
                selections = new List<GoblinPrioritizedBillSelection>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildLookup();
            }
        }

        public void Set(Pawn pawn, Thing billGiver, Bill bill, WorkGiverDef workGiverDef)
        {
            if (pawn == null || billGiver == null || bill == null || workGiverDef == null)
            {
                return;
            }

            Clear(pawn);
            GoblinPrioritizedBillSelection selection = new GoblinPrioritizedBillSelection
            {
                pawn = pawn,
                billGiver = billGiver,
                bill = bill,
                workGiverDef = workGiverDef
            };
            selections.Add(selection);
            selectionsByPawn[pawn] = selection;
        }

        public bool TryGet(Pawn pawn, Thing billGiver, WorkGiverDef workGiverDef, out Bill bill)
        {
            bill = null;
            if (pawn == null
                || !selectionsByPawn.TryGetValue(pawn, out GoblinPrioritizedBillSelection selection)
                || selection.billGiver != billGiver
                || selection.workGiverDef != workGiverDef)
            {
                return false;
            }

            bill = selection.bill;
            return true;
        }

        public void Clear(Pawn pawn)
        {
            if (pawn == null || !selectionsByPawn.TryGetValue(pawn, out GoblinPrioritizedBillSelection selection))
            {
                return;
            }

            selectionsByPawn.Remove(pawn);
            selections.Remove(selection);
        }

        private void RebuildLookup()
        {
            selectionsByPawn = new Dictionary<Pawn, GoblinPrioritizedBillSelection>();
            for (int i = selections.Count - 1; i >= 0; i--)
            {
                GoblinPrioritizedBillSelection selection = selections[i];
                if (selection == null || !selection.IsValid || selectionsByPawn.ContainsKey(selection.pawn))
                {
                    selections.RemoveAt(i);
                    continue;
                }

                selectionsByPawn.Add(selection.pawn, selection);
            }
        }
    }

    public sealed class FloatMenuOptionProvider_GoblinProductionBills : FloatMenuOptionProvider
    {
        private static readonly HashSet<string> GoblinProductionDefNames = new HashSet<string>
        {
            "MUGB_campfire",
            "MUGB_fireplace",
            "MUGB_cookstation",
            "MUGB_Basin",
            "MUGB_smithbench",
            "MUGB_druglab"
        };

        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        public override bool TargetThingValid(Thing thing, FloatMenuContext context)
        {
            return base.TargetThingValid(thing, context)
                && thing is IBillGiver
                && GoblinProductionDefNames.Contains(thing.def.defName);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (!(clickedThing is IBillGiver billGiver) || billGiver.BillStack == null || billGiver.BillStack.Count == 0)
            {
                yield break;
            }

            Pawn pawn = context.FirstSelectedPawn;
            Thing productionBuilding = clickedThing;
            yield return new FloatMenuOption(
                "MUGB_SelectCookstationBillToPrioritize".Translate(),
                delegate
                {
                    OpenBillMenu(pawn, productionBuilding, billGiver, context.ClickedCell);
                });
        }

        private static void OpenBillMenu(Pawn pawn, Thing productionBuilding, IBillGiver billGiver, IntVec3 clickedCell)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < billGiver.BillStack.Count; i++)
            {
                Bill bill = billGiver.BillStack[i];
                if (bill?.recipe == null)
                {
                    continue;
                }

                string label = "MUGB_PrioritizeSpecificCookstationBill".Translate(i + 1, bill.LabelCap);
                if (bill.suspended)
                {
                    options.Add(new FloatMenuOption(label + ": " + "MUGB_CookstationBillSuspended".Translate(), null));
                    continue;
                }

                if (!bill.ShouldDoNow())
                {
                    options.Add(new FloatMenuOption(label + ": " + "MUGB_CookstationBillNotNeeded".Translate(), null));
                    continue;
                }

                Bill selectedBill = bill;
                options.Add(new FloatMenuOption(label, delegate
                {
                    TryPrioritizeBill(pawn, productionBuilding, billGiver, selectedBill, clickedCell);
                }));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("MUGB_NoCookstationBillsAvailable".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void TryPrioritizeBill(
            Pawn pawn,
            Thing productionBuilding,
            IBillGiver billGiver,
            Bill bill,
            IntVec3 clickedCell)
        {
            WorkGiver_DoBill worker = GoblinPrioritizedBillUtility.FindWorkGiver(productionBuilding?.def, bill);
            if (!GoblinPrioritizedBillUtility.TryCreateJob(
                    pawn,
                    productionBuilding,
                    billGiver,
                    bill,
                    worker,
                    showMessages: true,
                    out Job job))
            {
                return;
            }

            if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, worker, clickedCell))
            {
                GameComponent_GoblinPrioritizedBills.Current?.Set(
                    pawn,
                    productionBuilding,
                    bill,
                    worker.def);
            }
        }
    }

    internal static class GoblinPrioritizedBillUtility
    {
        [ThreadStatic]
        private static bool invokingVanillaWorkGiver;

        public static bool InvokingVanillaWorkGiver => invokingVanillaWorkGiver;

        public static WorkGiver_DoBill FindWorkGiver(ThingDef buildingDef, Bill bill)
        {
            if (buildingDef == null || bill?.recipe == null)
            {
                return null;
            }

            List<WorkGiverDef> workGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef def = workGivers[i];
                if (def.fixedBillGiverDefs?.Contains(buildingDef) != true
                    || !(def.Worker is WorkGiver_DoBill worker)
                    || (bill.recipe.requiredGiverWorkType != null
                        && bill.recipe.requiredGiverWorkType != def.workType))
                {
                    continue;
                }

                return worker;
            }

            return null;
        }

        public static bool TryCreateJob(
            Pawn pawn,
            Thing productionBuilding,
            IBillGiver billGiver,
            Bill bill,
            WorkGiver_DoBill worker,
            bool showMessages,
            out Job job)
        {
            job = null;
            if (pawn == null
                || productionBuilding == null
                || !productionBuilding.Spawned
                || bill?.billStack != billGiver?.BillStack
                || bill.DeletedOrDereferenced
                || !bill.CompletableEver)
            {
                Reject("MUGB_CookstationBillNoLongerAvailable", productionBuilding, showMessages);
                return false;
            }

            WorkGiverDef workGiverDef = worker?.def;
            if (workGiverDef == null)
            {
                Reject("MUGB_CookstationBillUnavailable", productionBuilding, showMessages);
                return false;
            }

            if (bill.suspended || !bill.ShouldDoNow())
            {
                Reject("MUGB_CookstationBillUnavailable", productionBuilding, showMessages);
                return false;
            }

            BillStack stack = billGiver.BillStack;
            List<Bill> bills = stack.Bills;
            List<Bill> originalOrder = new List<Bill>(bills);
            try
            {
                bills.Clear();
                bills.Add(bill);
                invokingVanillaWorkGiver = true;
                job = worker.JobOnThing(pawn, productionBuilding, forced: true);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce("[MUGB] Failed to create a prioritized goblin production bill job: " + exception, 178643921);
            }
            finally
            {
                invokingVanillaWorkGiver = false;
                bills.Clear();
                bills.AddRange(originalOrder);
            }

            if (job == null)
            {
                Reject("MUGB_CookstationBillUnavailable", productionBuilding, showMessages);
                return false;
            }

            job.workGiverDef = workGiverDef;
            return true;
        }

        private static void Reject(string key, Thing target, bool showMessages)
        {
            if (showMessages)
            {
                Messages.Message(key.Translate(), target, MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    internal static class WorkGiver_DoBill_GoblinPrioritizedBillPatch
    {
        private static bool Prefix(
            WorkGiver_DoBill __instance,
            Pawn pawn,
            Thing thing,
            ref Job __result)
        {
            if (GoblinPrioritizedBillUtility.InvokingVanillaWorkGiver)
            {
                return true;
            }

            GameComponent_GoblinPrioritizedBills component = GameComponent_GoblinPrioritizedBills.Current;
            if (component == null
                || !component.TryGet(pawn, thing, __instance.def, out Bill bill))
            {
                return true;
            }

            PriorityWork priorityWork = pawn.mindState?.priorityWork;
            if (priorityWork == null
                || !priorityWork.IsPrioritized
                || priorityWork.WorkGiver != __instance.def)
            {
                component.Clear(pawn);
                return true;
            }

            if (GoblinPrioritizedBillUtility.TryCreateJob(
                    pawn,
                    thing,
                    thing as IBillGiver,
                    bill,
                    __instance,
                    showMessages: false,
                    out __result))
            {
                return false;
            }

            component.Clear(pawn);
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(PriorityWork), nameof(PriorityWork.Set))]
    internal static class PriorityWork_Set_ClearGoblinBillPatch
    {
        private static void Prefix(Pawn ___pawn)
        {
            GameComponent_GoblinPrioritizedBills.Current?.Clear(___pawn);
        }
    }

    [HarmonyPatch(typeof(PriorityWork), nameof(PriorityWork.Clear))]
    internal static class PriorityWork_Clear_ClearGoblinBillPatch
    {
        private static void Prefix(Pawn ___pawn)
        {
            GameComponent_GoblinPrioritizedBills.Current?.Clear(___pawn);
        }
    }
}
