using System.Linq;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Implemented by HediffComps on cyberware / active effects that react to combat events
    /// ("after damaging an enemy", "on neutralizing", ...). The event sources are a few Harmony patches
    /// and the tracker comp; receivers never patch anything themselves.
    /// </summary>
    public interface ICyberEventReceiver
    {
        void Notify_DealtDamage(Thing victim, DamageInfo dinfo, float damageDealt);
        void Notify_Neutralized(Pawn victim, bool killed);
        void Notify_TookDamage(DamageInfo dinfo, float damageTaken);
    }

    public static class CyberEvents
    {
        /// <summary>From Patch_Thing_TakeDamage: any damage a pawn deals to anything.</summary>
        public static void Notify_DamageDealt(Pawn attacker, Thing victim, DamageInfo dinfo, float damageDealt)
        {
            if (attacker == null || attacker == victim || damageDealt <= 0f) return;
            if (!dinfo.Def.ExternalViolenceFor(victim)) return;
            var tracker = attacker.GetCyberTracker();
            if (tracker == null) return;
            tracker.Notify_CombatAction();
            Diag.Count("damage-dealt-by-tracked-pawns");
            if (victim is Pawn vp && vp.HostileTo(attacker)) tracker.Notify_DamageForAdrenaline(dinfo, damageDealt, dealt: true);
            ForEachReceiver(attacker, r => r.Notify_DealtDamage(victim, dinfo, damageDealt));
        }

        /// <summary>Damage info of the neutralizing hit (set by the patches just before Notify_Neutralized).</summary>
        public static DamageInfo? LastNeutralizeDinfo;

        /// <summary>"Neutralize" = downing or killing an enemy (terms.md).</summary>
        public static void Notify_Neutralized(Pawn attacker, Pawn victim, bool killed)
        {
            if (attacker == null || victim == null || attacker == victim) return;
            if (attacker.GetCyberTracker() == null) return;
            foreach (var active in attacker.ActiveEffects().ToList()) active.Notify_Neutralized();
            ForEachReceiver(attacker, r => r.Notify_Neutralized(victim, killed));
            CyberpsychosisUtility.Notify_Event(attacker, killed ? CyberpsychosisEvent.Killed : CyberpsychosisEvent.Downed);
        }

        public static void Notify_DamageTaken(Pawn pawn, DamageInfo dinfo, float damageTaken)
        {
            if (damageTaken <= 0f) return;
            if (dinfo.Def.ExternalViolenceFor(pawn)) pawn.GetCyberTracker()?.Notify_DamageForAdrenaline(dinfo, damageTaken, dealt: false);
            ForEachReceiver(pawn, r => r.Notify_TookDamage(dinfo, damageTaken));
            CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.TookDamage);
        }

        public static void Notify_EnteredCombat(Pawn pawn)
        {
            FireTriggers(pawn, CyberTriggerKind.EnteredCombat, CyberTriggerContext.None);
            CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.EnteredCombat);
        }

        /// <summary>From Patch_Verb_MeleeAttackCE_SoundDodge: <paramref name="pawn"/> dodged a melee attack.</summary>
        public static void Notify_Dodged(Pawn pawn)
        {
            FireTriggers(pawn, CyberTriggerKind.Dodged, CyberTriggerContext.None);
            pawn.GetCyberTracker()?.Notify_EvasiveAction();
        }

        public static void Notify_ActiveEnded(Pawn pawn, HediffDef def) =>
            FireTriggers(pawn, CyberTriggerKind.ActiveEnded, new CyberTriggerContext { activeDef = def });

        public static void Notify_UsedGrenade(Pawn pawn) => FireTriggers(pawn, CyberTriggerKind.UsedGrenade, CyberTriggerContext.None);

        public static void FireTriggers(Pawn pawn, CyberTriggerKind kind, CyberTriggerContext ctx)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (i >= hediffs.Count) continue;
                if (hediffs[i] is HediffWithComps hwc && hwc.TryGetComp<HediffComp_CyberTriggers>() is HediffComp_CyberTriggers c)
                    c.Fire(kind, ctx);
            }
        }

        public static void Notify_RamChanged(Pawn pawn, CompCyberTracker tracker) { }

        public static void Notify_AbilityUsed(Pawn pawn, CyberwareAbility ability)
        {
            FireTriggers(pawn, CyberTriggerKind.OtherAbilityUsed, new CyberTriggerContext { ability = ability });
            pawn.GetCyberTracker()?.Notify_EvasiveAction(); // dashes count for Nano-plating
            if (ability.Source?.Ext?.osType is CyberOsType.Sandevistan or CyberOsType.Berserk)
                CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.UsedCyberwareAbility);
        }

        private static void ForEachReceiver(Pawn pawn, System.Action<ICyberEventReceiver> action)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return;
            // index loop over a snapshot count: receivers may add/remove hediffs
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (i >= hediffs.Count) continue;
                if (hediffs[i] is not HediffWithComps hwc) continue;
                var comps = hwc.comps;
                if (comps == null) continue;
                for (int j = 0; j < comps.Count; j++)
                    if (comps[j] is ICyberEventReceiver r) action(r);
            }
        }
    }
}
