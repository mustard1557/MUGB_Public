using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MUGB.Patches;
using Verse;

namespace MUGB
{
    [StaticConstructorOnStartup]
    public static class MUGB_HumanlikeSurgeryRecipeRegistrar
    {
        private static readonly string[] SurgeryDefNames =
        {
            "MUGB_ExtractBrain",
            "MUGB_ExtractHeart",
            "MUGB_ExtractFleshChunks",
            "MUGB_ExtractGoblinReproductiveSac",
            "MUGB_AdministerGoblinReproductiveSac",
            "MUGB_ImplantGoblinEmbryo",
            "MUGB_NosePickLobotomy"
        };

        static MUGB_HumanlikeSurgeryRecipeRegistrar()
        {
            LongEventHandler.ExecuteWhenFinished(Apply);
        }

        private static void Apply()
        {
            List<RecipeDef> surgeries = SurgeryDefNames
                .Select(DefDatabase<RecipeDef>.GetNamedSilentFail)
                .Where(recipe => recipe != null)
                .ToList();

            if (surgeries.Count == 0)
            {
                return;
            }

            foreach (ThingDef raceDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (raceDef?.category != ThingCategory.Pawn || raceDef.race?.Humanlike != true)
                {
                    continue;
                }

                raceDef.recipes ??= new List<RecipeDef>();
                foreach (RecipeDef surgery in surgeries)
                {
                    if (!raceDef.recipes.Contains(surgery))
                    {
                        raceDef.recipes.Add(surgery);
                    }
                }
            }
        }
    }

    public class CompProperties_GoblinReproductiveSac : CompProperties
    {
        public CompProperties_GoblinReproductiveSac()
        {
            compClass = typeof(CompGoblinReproductiveSac);
        }
    }

    public class CompGoblinReproductiveSac : ThingComp
    {
        private string donorName;
        private string donorKind;
        private string donorXenotype;
        private List<string> donorGenes;
        private List<string> donorVariantGenes;

        public bool HasDonorData => !donorXenotype.NullOrEmpty();
        public bool DonorIsHobgoblin => donorKind == "Hobgoblin";

        public IReadOnlyList<string> DonorGenes => donorGenes ?? (IReadOnlyList<string>)new List<string>();
        public IReadOnlyList<string> DonorVariantGenes => donorVariantGenes ?? (IReadOnlyList<string>)new List<string>();

        public void InitializeFromDonor(Pawn donor)
        {
            donorName = donor?.Name?.ToStringFull ?? donor?.LabelShort;
            donorKind = GoblinUtility.IsHobgoblin(donor) ? "Hobgoblin" : "ThinGoblin";
            donorXenotype = donor?.genes?.Xenotype?.defName;
            donorGenes = donor?.genes?.GenesListForReading
                .Select(gene => gene?.def?.defName)
                .Where(defName => !defName.NullOrEmpty())
                .Distinct()
                .OrderBy(defName => defName)
                .ToList() ?? new List<string>();
            donorVariantGenes = donorGenes
                .Where(IsGoblinVariantGene)
                .ToList();
        }

        public void CopyFrom(CompGoblinReproductiveSac source)
        {
            if (source == null)
            {
                return;
            }

            donorName = source.donorName;
            donorKind = source.donorKind;
            donorXenotype = source.donorXenotype;
            donorGenes = source.donorGenes != null ? new List<string>(source.donorGenes) : new List<string>();
            donorVariantGenes = source.donorVariantGenes != null ? new List<string>(source.donorVariantGenes) : new List<string>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref donorName, "donorName");
            Scribe_Values.Look(ref donorKind, "donorKind");
            Scribe_Values.Look(ref donorXenotype, "donorXenotype");
            Scribe_Collections.Look(ref donorGenes, "donorGenes", LookMode.Value);
            Scribe_Collections.Look(ref donorVariantGenes, "donorVariantGenes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                donorGenes ??= new List<string>();
                donorVariantGenes ??= new List<string>();
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!HasDonorData)
            {
                return "MUGB_DonorDataNone".Translate();
            }

            StringBuilder builder = new StringBuilder();
            string unknown = "MUGB_Unknown".Translate();
            builder.AppendLine("MUGB_DonorName".Translate(donorName ?? unknown));
            builder.AppendLine("MUGB_DonorType".Translate(donorKind ?? unknown));
            builder.AppendLine("MUGB_DonorXenotype".Translate(donorXenotype ?? unknown));
            builder.Append("MUGB_DonorStoredGenes".Translate(donorGenes?.Count ?? 0));
            if (!donorVariantGenes.NullOrEmpty())
            {
                builder.AppendLine();
                builder.Append("MUGB_DonorVariantGenes".Translate(string.Join(", ", donorVariantGenes)));
            }
            return builder.ToString();
        }

        private static bool IsGoblinVariantGene(string defName)
        {
            return defName == "MUGB_Gene_CrossEyed"
                || defName == "MUGB_Gene_GoblinStableCellMetabolism"
                || defName == "MUGB_Gene_GoblinUnstableCellMetabolism";
        }
    }

    public class Recipe_ExtractGoblinReproductiveSac : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.gender != Gender.Male || !GoblinUtility.IsGoblin(pawn))
            {
                yield break;
            }

            if (MUGBDefOf.MUGB_GoblinReproductiveSacDepleted != null && pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_GoblinReproductiveSacDepleted))
            {
                yield break;
            }

            if (HasSterilityProcedure(pawn))
            {
                yield break;
            }

            BodyPartRecord torso = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(part => part?.def?.defName == "Torso");
            if (torso != null)
            {
                yield return torso;
            }
        }

        private static bool HasSterilityProcedure(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.hediffs?.Any(hediff =>
            {
                string defName = hediff?.def?.defName;
                return defName == "Vasectomy" || defName == "Sterilized";
            }) == true;
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || MUGBDefOf.MUGB_GBroodSac == null)
            {
                return;
            }

            Thing sac = ThingMaker.MakeThing(MUGBDefOf.MUGB_GBroodSac);
            sac.TryGetComp<CompGoblinReproductiveSac>()?.InitializeFromDonor(pawn);
            GenPlace.TryPlaceThing(sac, pawn.Position, pawn.Map, ThingPlaceMode.Near);

            if (MUGBDefOf.MUGB_GoblinReproductiveSacDepleted != null && !pawn.health.hediffSet.HasHediff(MUGBDefOf.MUGB_GoblinReproductiveSacDepleted))
            {
                pawn.health.AddHediff(MUGBDefOf.MUGB_GoblinReproductiveSacDepleted);
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -20);
            }
        }
    }

    public class Recipe_AdministerGoblinReproductiveSac : Recipe_Surgery
    {
        private const float ConditioningGain = 12f;

        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.Dead || GoblinUtility.IsGoblin(pawn))
            {
                yield break;
            }

            if (GoblinPheromonePreferenceUtility.HasPreference(pawn))
            {
                yield break;
            }

            BodyPartRecord torso = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(part => part?.def?.defName == "Torso");
            if (torso != null)
            {
                yield return torso;
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || GoblinUtility.IsGoblin(pawn))
            {
                return;
            }

            GoblinPheromonePreferenceUtility.ForceGainConditioning(pawn, ConditioningGain);
            if (pawn.needs?.mood != null && MUGBDefOf.MUGB_GoblinSacNausea != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_GoblinSacNausea, billDoer);
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -15);
            }
        }
    }

    public class Recipe_MakeGoblinEmbryo : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            if (billDoer?.Map == null || MUGBDefOf.MUGB_Gfetus == null)
            {
                return;
            }

            CompGoblinReproductiveSac sacData = ingredients
                .FirstOrDefault(thing => thing?.def == MUGBDefOf.MUGB_GBroodSac)
                ?.TryGetComp<CompGoblinReproductiveSac>();
            Thing embryo = ThingMaker.MakeThing(MUGBDefOf.MUGB_Gfetus);
            embryo.TryGetComp<CompGoblinReproductiveSac>()?.CopyFrom(sacData);
            GenPlace.TryPlaceThing(embryo, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);
        }
    }

    public class Recipe_ImplantGoblinEmbryo : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.gender != Gender.Female || IsPregnant(pawn))
            {
                yield break;
            }

            BodyPartRecord torso = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Torso");
            if (torso != null)
            {
                yield return torso;
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || IsPregnant(pawn))
            {
                return;
            }

            HediffDef pregnantHuman = DefDatabase<HediffDef>.GetNamedSilentFail("PregnantHuman");
            if (pregnantHuman == null)
            {
                Messages.Message("MUGB_HumanPregnancyUnavailable".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            CompGoblinReproductiveSac embryoData = ingredients
                .FirstOrDefault(thing => thing?.def == MUGBDefOf.MUGB_Gfetus)
                ?.TryGetComp<CompGoblinReproductiveSac>();

            HediffWithComps pregnancy = HediffMaker.MakeHediff(pregnantHuman, pawn) as HediffWithComps;
            if (pregnancy == null)
            {
                return;
            }

            pawn.health.AddHediff(pregnancy);
            HediffComp_MUGBGoblinPregnancyPlan plan = pregnancy.TryGetComp<HediffComp_MUGBGoblinPregnancyPlan>();
            if (plan != null)
            {
                plan.InitializeFromEmbryo(pawn, embryoData?.DonorIsHobgoblin == true);
            }
            else
            {
                Log.Warning("[MUGB Goblin] Implanted a goblin embryo, but PregnantHuman does not have the MUGB pregnancy plan comp.");
            }

            if (billDoer != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            if (IsViolationOnPawn(pawn, part, Faction.OfPlayerSilentFail))
            {
                ReportViolation(pawn, billDoer, pawn.HomeFaction, -30);
            }
        }

        private static bool IsPregnant(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.hediffs?.Any(hediff => hediff?.def?.pregnant == true) == true;
        }
    }
}
