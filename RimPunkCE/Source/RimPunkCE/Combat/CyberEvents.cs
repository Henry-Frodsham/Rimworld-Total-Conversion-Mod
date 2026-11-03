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
            ForEachReceiver(attacker, r => r.Notify_DealtDamage(victim, dinfo, damageDealt));
        }

        /// <summary>"Neutralize" = downing or killing an enemy (terms.md).</summary>
        public static void Notify_Neutralized(Pawn attacker, Pawn victim, bool killed)
        {
            if (attacker == null || victim == null || attacker == victim) return;
            if (attacker.GetCyberTracker() == null) return;
            foreach (var active in attacker.ActiveEffects()) active.neutralizedWhileActive++;
            ForEachReceiver(attacker, r => r.Notify_Neutralized(victim, killed));
            CyberpsychosisUtility.Notify_Event(attacker, killed ? CyberpsychosisEvent.Killed : CyberpsychosisEvent.Downed);
        }

        public static void Notify_DamageTaken(Pawn pawn, DamageInfo dinfo, float damageTaken)
        {
            if (damageTaken <= 0f) return;
            ForEachReceiver(pawn, r => r.Notify_TookDamage(dinfo, damageTaken));
            CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.TookDamage);
        }

        public static void Notify_EnteredCombat(Pawn pawn) => CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.EnteredCombat);

        public static void Notify_RamChanged(Pawn pawn, CompCyberTracker tracker) { }

        public static void Notify_AbilityUsed(Pawn pawn, CyberwareAbility ability) =>
            CyberpsychosisUtility.Notify_Event(pawn, CyberpsychosisEvent.UsedCyberwareAbility);

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
