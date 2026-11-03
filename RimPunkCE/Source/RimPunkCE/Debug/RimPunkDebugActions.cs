using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>Dev-mode tools (Debug actions menu, "RimPunk CE" category).</summary>
    public static class RimPunkDebugActions
    {
        private const string Category = "RimPunk CE";

        [DebugAction(Category, "Install cyberware...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> InstallCyberware()
        {
            var nodes = new List<DebugActionNode>();
            foreach (var hd in CyberwareDefCache.AllCyberware)
            {
                var ext = hd.Ext();
                for (var tier = ext.minTier; tier <= ext.maxTier; tier++)
                {
                    var t = tier;
                    nodes.Add(new DebugActionNode(hd.defName + " " + t, DebugActionType.ToolMapForPawns)
                    {
                        pawnAction = p => DebugInstall(p, hd, t),
                    });
                }
            }
            return nodes;
        }

        private static void DebugInstall(Pawn pawn, HediffDef def, CyberTier tier)
        {
            var ext = def.Ext();
            BodyPartRecord part = null;
            if (!ext.slot.WholeBody)
            {
                part = CyberSurgeryUtility.CandidateParts(pawn, ext.slot).FirstOrDefault(p => CyberUtility.CanInstall(pawn, def, p, out _));
                if (part == null)
                {
                    Messages.Message("No free part / slot full", MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }
            else if (!CyberUtility.CanInstall(pawn, def, null, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var h = (Hediff_Cyberware)HediffMaker.MakeHediff(def, pawn, part);
            h.tier = tier;
            pawn.health.AddHediff(h, part);
        }

        [DebugAction(Category, "Remove all cyberware", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RemoveAll(Pawn p)
        {
            foreach (var c in p.InstalledCyberware().ToList()) p.health.RemoveHediff(c);
        }

        [DebugAction(Category, "Upgrade cyberware tier", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void UpgradeAll(Pawn p)
        {
            foreach (var c in p.InstalledCyberware().ToList()) c.SetTier(c.tier + 1);
        }

        [DebugAction(Category, "Force cyberpsychosis", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceCyberpsychosis(Pawn p) => CyberpsychosisUtility.TryStart(p, "debug");

        [DebugAction(Category, "Roll cyberpsychosis event (took damage)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RollEvent(Pawn p) => CyberpsychosisUtility.Notify_Event(p, CyberpsychosisEvent.TookDamage);

        [DebugAction(Category, "Refill RAM and stamina", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Refill(Pawn p)
        {
            var t = p.GetCyberTracker();
            t?.GainRam(9999f);
            var s = t?.Stamina;
            if (s != null) s.CurLevel = s.MaxLevel;
        }

        [DebugAction(Category, "Drain stamina", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DrainStamina(Pawn p)
        {
            var s = p.GetCyberTracker()?.Stamina;
            if (s == null) return;
            s.CurLevel = 0f;
            if (!s.Exhausted) p.health.AddHediff(RPDefOf.RPC_Exhausted);
        }

        [DebugAction(Category, "Reset cyberware cooldowns", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ResetCooldowns(Pawn p)
        {
            foreach (var a in p.CyberAbilities()) a.ResetCooldown();
        }

        [DebugAction(Category, "Log capacity", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogCapacity(Pawn p)
        {
            var t = p.GetCyberTracker();
            if (t == null) return;
            Log.Message($"[RimPunk CE] {p}: base {t.BaseCapacity}, used {t.CapacityUsed}, max {t.CapacityMax}, usage {t.UsageRatio:P0}, burnout {CyberpsychosisUtility.HasBurnout(p)}");
        }
    }
}
