using Verse;

namespace MUGB
{
    public class Gene_GoblinVoice : Gene
    {
        public override bool Active => base.Active && GoblinUtility.HasGoblinCoreMarker(pawn);
    }
}
