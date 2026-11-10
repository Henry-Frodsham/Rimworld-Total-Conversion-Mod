using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    public enum CyberFactionKind
    {
        Corporation,
        Gang,
        Nomads,
        TraumaTeam,
        NetWatch,
        MaxTac,
        /// <summary>Trauma Team's debt collection agency: only raids a colony with unpaid Trauma Team debt (TraumaBilling.cs).</summary>
        DebtCollector,
    }

    /// <summary>On every RimPunk FactionDef (prompts/factions.md).</summary>
    public class CyberFactionExtension : DefModExtension
    {
        public CyberFactionKind kind = CyberFactionKind.Gang;

        /// <summary>Factions this one is always hostile to (Arasaka vs Militech, ...).</summary>
        public List<FactionDef> rivals = new List<FactionDef>();

        /// <summary>Chance a member gets a street name as their nickname.</summary>
        public float streetNameChance = 0.7f;

        /// <summary>Corporations: days before a retaliation strike team arrives.</summary>
        public FloatRange retaliationDelayDays = new FloatRange(1f, 3f);

        /// <summary>Corporations: retaliation strength as a multiplier on normal raid points.</summary>
        public float retaliationPointsFactor = 1.5f;
    }

    /// <summary>
    /// Cyberware a pawn kind spawns with. Each option rolls once; one hediff is picked from its list
    /// (skipping ones whose slot is full). Tiers rolled in [minTier, maxTier] within each item's own range.
    /// </summary>
    public class CyberwareLoadoutExtension : DefModExtension
    {
        public CyberTier minTier = CyberTier.T1;
        public CyberTier maxTier = CyberTier.T2;
        public List<CyberwareLoadoutOption> options = new List<CyberwareLoadoutOption>();

        /// <summary>Force this capacity aptitude degree (rogue cyberpsycho: gonk, so the chrome overloads them).</summary>
        public int forceAptitudeDegree = -99;

        /// <summary>Plain hediffs on top of the cyberware (scrap plating on rogue cyberpsychos).</summary>
        public List<HediffDef> extraHediffs = new List<HediffDef>();

        /// <summary>Roll for extra armor implants (ChromePlatingUtility.AddGeneratedArmor).</summary>
        public bool addArmor = true;
    }

    public class CyberwareLoadoutOption
    {
        public float chance = 1f;
        public List<HediffDef> hediffs = new List<HediffDef>();
        public int count = 1;

        /// <summary>
        /// Optional families (all sandevistans, all mantis blades...). When present they replace <see cref="hediffs"/>: a family is
        /// picked with equal odds however many models it has, then a model from inside it, so adding models never shifts the odds.
        /// </summary>
        public List<CyberwareLoadoutGroup> groups = new List<CyberwareLoadoutGroup>();
    }

    public class CyberwareLoadoutGroup
    {
        public List<HediffDef> hediffs = new List<HediffDef>();
    }

    /// <summary>Street names used as nicknames for faction pawns. Original list, Defs/Factions/StreetNames.xml.</summary>
    public class StreetNamesDef : Def
    {
        public List<string> names = new List<string>();
    }

    public static class CyberFactionUtility
    {
        public static CyberFactionExtension Ext(this Faction f) => f?.def?.GetModExtension<CyberFactionExtension>();

        public static bool IsCorporation(this Faction f) => f.Ext()?.kind == CyberFactionKind.Corporation;

        public static bool IsTraumaTeam(this Faction f) => f.Ext()?.kind == CyberFactionKind.TraumaTeam;

        /// <summary>Trauma Team and MaxTac: goodwill with the player never drops (Patch_Faction_TryAffectGoodwillWith).</summary>
        public static bool IsNeverHostile(this Faction f) => f.Ext()?.kind is CyberFactionKind.TraumaTeam or CyberFactionKind.MaxTac;

        public static Faction FirstOfKind(CyberFactionKind kind)
        {
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
                if (!f.defeated && f.Ext()?.kind == kind) return f;
            return null;
        }

        /// <summary>
        /// Sets a relation that sticks. Factions with goodwill can't use SetRelationDirect (vanilla logs an error and
        /// goodwill would override it): with the player we move goodwill; between NPC factions we set both sides'
        /// base goodwill and kind directly.
        /// </summary>
        public static void ForceRelation(Faction a, Faction b, FactionRelationKind kind, string reason = null)
        {
            if (a == null || b == null || a == b) return;
            if (!(a.HasGoodwill && b.HasGoodwill))
            {
                if (a.RelationKindWith(b) != kind) a.SetRelationDirect(b, kind, false, reason);
                return;
            }
            int target = kind == FactionRelationKind.Hostile ? -100 : kind == FactionRelationKind.Ally ? 100 : 0;
            if (a.IsPlayer || b.IsPlayer)
            {
                var npc = a.IsPlayer ? b : a;
                int delta = target - npc.PlayerGoodwill;
                if (delta != 0) npc.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: false, canSendHostilityLetter: true);
                return;
            }
            foreach (var (x, y) in new[] { (a, b), (b, a) })
            {
                var rel = x.RelationWith(y, allowNull: true);
                if (rel == null) continue;
                rel.baseGoodwill = target;
                rel.kind = kind;
            }
        }

        /// <summary>Rivals hate each other (set once per game, kept hostile).</summary>
        public static void ApplyRivalries()
        {
            var all = Find.FactionManager.AllFactionsListForReading;
            foreach (var f in all)
            {
                var ext = f.Ext();
                if (ext == null || ext.rivals.NullOrEmpty()) continue;
                foreach (var other in all)
                {
                    if (other == f || other.IsPlayer || !ext.rivals.Contains(other.def)) continue;
                    if (f.RelationKindWith(other) != FactionRelationKind.Hostile)
                        ForceRelation(f, other, FactionRelationKind.Hostile);
                }
            }
        }
    }
}
