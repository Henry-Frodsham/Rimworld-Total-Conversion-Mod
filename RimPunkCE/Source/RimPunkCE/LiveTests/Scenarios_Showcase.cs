using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>Not a test: puts one of each Arasaka grunt on the map and holds them there so the art can be looked at.
    /// Only runs when named explicitly (--filter showcase), never in a full run.</summary>
    public partial class GameComponent_RimPunkLiveTests
    {
        private static readonly string[] ArasakaShowcaseKinds =
        {
            "RPC_Arasaka_Grunt", "RPC_Arasaka_Guard", "RPC_Arasaka_Enforcer", "RPC_Arasaka_Netrunner",
            "RPC_Arasaka_Sniper", "RPC_Arasaka_Boss", "RPC_Arasaka_Trader",
        };

        [LiveScenario("showcase")]
        private IEnumerable<int> Scenario_ArasakaShowcase()
        {
            var faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def.defName == "RPC_Faction_Arasaka");
            Check(faction != null, "arasaka faction exists");
            if (faction == null) yield break;

            Find.CameraDriver.JumpToCurrentMapLoc(Center);
            for (int i = 0; i < ArasakaShowcaseKinds.Length; i++)
            {
                var kind = DefDatabase<PawnKindDef>.GetNamed(ArasakaShowcaseKinds[i], errorOnFail: false);
                if (kind == null) { Check(false, $"pawn kind {ArasakaShowcaseKinds[i]} exists"); continue; }
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true));
                GenSpawn.Spawn(p, Center + new IntVec3(-9 + i * 3, 0, 4), map);
                spawned.Add(p);
                // stunned in place so they stand still for the look; the stun outlasts the hold
                p.stances.stunner.StunFor(10000000, null, addBattleLog: false, showMote: false);
            }
            Check(spawned.Count == ArasakaShowcaseKinds.Length, $"spawned {spawned.Count} arasaka pawns");

            // about 10 in-game days at top speed; the game quits when the run ends
            yield return 600000;
        }
    }
}
