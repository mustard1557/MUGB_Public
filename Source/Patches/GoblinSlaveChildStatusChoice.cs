using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MUGB.Patches
{
    public static class GoblinSlaveChildStatusUtility
    {
        public static void TryIssueStatusChoice(Pawn pawn, GoblinRapidMaturationComponent component)
        {
            if (pawn == null || component == null)
            {
                return;
            }

            bool recordedAtBirth = component.ConsumeCaptiveBornGoblin(pawn);
            if (!recordedAtBirth && !HasCaptiveMother(pawn))
            {
                return;
            }

            if (pawn.Dead || pawn.Destroyed || !pawn.IsSlaveOfColony)
            {
                return;
            }

            LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("MUGB_GoblinSlaveChildStatusChoice");
            if (letterDef == null)
            {
                Log.ErrorOnce("[MUGB] Missing MUGB_GoblinSlaveChildStatusChoice LetterDef.", 197310421);
                return;
            }

            ChoiceLetter_GoblinSlaveChildStatus letter = (ChoiceLetter_GoblinSlaveChildStatus)LetterMaker.MakeLetter(
                "MUGB_GoblinSlaveChildStatusLetterLabel".Translate(pawn.Named("PAWN")),
                "MUGB_GoblinSlaveChildStatusLetterText".Translate(pawn.Named("PAWN")),
                letterDef,
                pawn);
            letter.pawn = pawn;
            Find.LetterStack.ReceiveLetter(letter);
        }

        private static bool HasCaptiveMother(Pawn pawn)
        {
            List<DirectPawnRelation> relations = pawn.relations?.DirectRelations;
            if (relations == null)
            {
                return false;
            }

            return relations.Any(relation =>
                relation.def == PawnRelationDefOf.Parent
                && relation.otherPawn?.gender == Gender.Female
                && (relation.otherPawn.IsSlaveOfColony || relation.otherPawn.IsPrisonerOfColony));
        }

        public static bool CanAcceptAsColonist(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.IsSlaveOfColony;
        }

        public static void AcceptAsColonist(Pawn pawn)
        {
            if (!CanAcceptAsColonist(pawn))
            {
                return;
            }

            pawn.guest.SetGuestStatus(null);
            pawn.guest.Notify_PawnRecruited();
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            pawn.apparel?.UnlockAll();
        }

        public static void Release(Pawn pawn)
        {
            if (!CanAcceptAsColonist(pawn))
            {
                return;
            }

            PawnBanishUtility.Banish(pawn, giveThoughts: false);
        }
    }

    public class ChoiceLetter_GoblinSlaveChildStatus : ChoiceLetter
    {
        public Pawn pawn;

        public override bool CanDismissWithRightClick => false;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }

                if (!GoblinSlaveChildStatusUtility.CanAcceptAsColonist(pawn))
                {
                    yield return new DiaOption("Close".Translate())
                    {
                        action = () => Find.LetterStack.RemoveLetter(this),
                        resolveTree = true
                    };
                    yield break;
                }

                yield return new DiaOption("MUGB_GoblinSlaveChildKeepSlave".Translate(pawn.Named("PAWN")))
                {
                    action = delegate
                    {
                        Find.LetterStack.RemoveLetter(this);
                        Messages.Message(
                            "MUGB_GoblinSlaveChildKeptSlaveMessage".Translate(pawn.Named("PAWN")),
                            pawn,
                            MessageTypeDefOf.NeutralEvent,
                            historical: false);
                    },
                    resolveTree = true
                };

                yield return new DiaOption("MUGB_GoblinSlaveChildAcceptColonist".Translate(pawn.Named("PAWN")))
                {
                    action = delegate
                    {
                        GoblinSlaveChildStatusUtility.AcceptAsColonist(pawn);
                        Find.LetterStack.RemoveLetter(this);
                        Messages.Message(
                            "MUGB_GoblinSlaveChildAcceptedMessage".Translate(pawn.Named("PAWN")),
                            pawn,
                            MessageTypeDefOf.PositiveEvent,
                            historical: false);
                    },
                    resolveTree = true
                };

                yield return new DiaOption("MUGB_GoblinSlaveChildRelease".Translate(pawn.Named("PAWN")))
                {
                    action = delegate
                    {
                        Dialog_MessageBox confirmation = Dialog_MessageBox.CreateConfirmation(
                            "MUGB_GoblinSlaveChildReleaseConfirm".Translate(pawn.Named("PAWN")),
                            delegate
                            {
                                GoblinSlaveChildStatusUtility.Release(pawn);
                                Find.LetterStack.RemoveLetter(this);
                                Messages.Message(
                                    "MUGB_GoblinSlaveChildReleasedMessage".Translate(pawn.Named("PAWN")),
                                    pawn,
                                    MessageTypeDefOf.NeutralEvent,
                                    historical: false);
                            },
                            destructive: true);
                        Find.WindowStack.Add(confirmation);
                    },
                    resolveTree = true
                };

                if (lookTargets.IsValid())
                {
                    yield return Option_JumpToLocationAndPostpone;
                }
                yield return Option_Postpone;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pawn, "pawn");
        }
    }
}
