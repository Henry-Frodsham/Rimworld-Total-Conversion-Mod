using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Worker for every RimPunk pawn stat (RPC_PawnStatBase, category RPC_Cyberware). Vanilla StatWorker.ShouldShowFor
    /// only knows its own stat categories: a custom one hit "Unhandled case: RPC_..., Human" and the stats were hidden
    /// from the info card. Show them on humanlike pawns (and the humanlike race def card) only.
    /// </summary>
    public class StatWorker_CyberPawn : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide) return false;
            if (req.Thing != null) return req.Thing is Pawn p && p.RaceProps?.Humanlike == true;
            return req.Def is ThingDef td && td.race?.Humanlike == true;
        }
    }
}
