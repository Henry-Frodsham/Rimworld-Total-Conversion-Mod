using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public static class CyberUtility
    {
        public static CompCyberTracker GetCyberTracker(this Pawn pawn) => pawn?.TryGetComp<CompCyberTracker>();

        public static IEnumerable<Hediff_Cyberware> InstalledCyberware(this Pawn pawn)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) yield break;
            for (int i = 0; i < hediffs.Count; i++)
                if (hediffs[i] is Hediff_Cyberware c) yield return c;
        }

        public static IEnumerable<Hediff_CyberActive> ActiveEffects(this Pawn pawn)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) yield break;
            for (int i = 0; i < hediffs.Count; i++)
                if (hediffs[i] is Hediff_CyberActive a) yield return a;
        }

        /// <summary>
        /// Slot rules (CyberSlotDef): max per pawn, one per body part, matching sets.
        /// Capacity never blocks an install (cyberpsychosis is the deterrent).
        /// </summary>
        public static bool CanInstall(Pawn pawn, HediffDef def, BodyPartRecord part, out string reason)
        {
            reason = null;
            var ext = def.Ext();
            if (ext?.slot == null) return true;
            var slot = ext.slot;
            int inSlot = 0;
            foreach (var c in pawn.InstalledCyberware())
            {
                var cext = c.Ext;
                if (cext?.slot != slot) continue;
                inSlot++;
                if (slot.onePerPart && part != null && c.Part == part)
                {
                    reason = "RPC_Reason_PartOccupied".Translate(part.LabelCap, c.LabelCap);
                    return false;
                }
                if (slot.allMustMatch && c.def != def)
                {
                    reason = "RPC_Reason_MustMatch".Translate(slot.label, c.def.label);
                    return false;
                }
            }
            if (inSlot >= slot.maxInstalled)
            {
                reason = "RPC_Reason_SlotFull".Translate(slot.label, slot.maxInstalled);
                return false;
            }
            return true;
        }

        /// <summary>Legs: a full set of identical cyberware doubles its effects.</summary>
        public static void UpdateMatchingSets(Pawn pawn, IReadOnlyList<Hediff_Cyberware> installed)
        {
            for (int i = 0; i < installed.Count; i++)
            {
                var slot = installed[i].Ext?.slot;
                bool doubled = false;
                if (slot != null && slot.matchingSetDoublesEffects)
                {
                    int same = 0;
                    for (int j = 0; j < installed.Count; j++)
                        if (installed[j].def == installed[i].def) same++;
                    doubled = same >= slot.maxInstalled && slot.maxInstalled > 1;
                }
                installed[i].SetDoubled(doubled);
            }
        }

        /// <summary>Heals every injury by a fraction of its severity ("+25% health").</summary>
        public static void HealFraction(Pawn pawn, float fraction)
        {
            if (fraction <= 0f || pawn?.health?.hediffSet == null) return;
            var injuries = new List<Hediff_Injury>();
            pawn.health.hediffSet.GetHediffs(ref injuries, h => h.CanHealNaturally() || h.CanHealFromTending());
            foreach (var inj in injuries)
                inj.Heal(inj.Severity * Mathf.Clamp01(fraction));
        }

        public static bool IsHumanlikeWithTracker(this Pawn pawn) => pawn?.RaceProps?.Humanlike == true && pawn.GetCyberTracker() != null;

        public static bool HasDeathless(Pawn pawn) =>
            ModsConfig.BiotechActive && RPDefOf.Deathless != null && pawn.genes != null && pawn.genes.HasActiveGene(RPDefOf.Deathless);

        public static Hediff_Cyberware SourceCyberwareFor(Pawn pawn, AbilityDef ability)
        {
            Hediff_Cyberware best = null;
            foreach (var c in pawn.InstalledCyberware())
            {
                var give = c.TryGetComp<HediffComp_GiveCyberAbilities>();
                if (give == null || !give.Props.abilities.Contains(ability)) continue;
                if (best == null || c.tier > best.tier) best = c;
            }
            return best;
        }

        public static IEnumerable<CyberwareAbility> CyberAbilities(this Pawn pawn)
        {
            var list = pawn?.abilities?.AllAbilitiesForReading;
            if (list == null) yield break;
            foreach (var a in list)
                if (a is CyberwareAbility ca) yield return ca;
        }

        public static bool IsMechanoidLike(Thing t) => t is Pawn p && p.RaceProps.IsMechanoid;

        public static int SecondsToTicks(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * 60f));

        public static string Joined(IEnumerable<string> parts) => string.Join(", ", parts.Where(p => !p.NullOrEmpty()));
    }
}
