using System.Collections.Generic;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Mantis blades: extend/retract toggle. Retracted blades can't be used (Patch_Verb_IsStillUsableBy).
    /// Extended blades replace every other melee attack, so a pawn holding a gun stabs instead of
    /// pistol-whipping (Patch_Pawn_MeleeVerbs_GetUpdatedAvailableVerbsList).
    /// </summary>
    public class HediffCompProperties_MantisBlades : HediffCompProperties
    {
        public string extendIconPath = "UI/RimPunk/Placeholder_Ability";

        public HediffCompProperties_MantisBlades() => compClass = typeof(HediffComp_MantisBlades);
    }

    public class HediffComp_MantisBlades : HediffComp
    {
        public bool extended = true;

        public HediffCompProperties_MantisBlades Props => (HediffCompProperties_MantisBlades)props;

        public override string CompLabelInBracketsExtra => extended ? "RPC_Extended".Translate() : "RPC_Retracted".Translate();

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            if (Pawn.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "RPC_ToggleBlades".Translate(),
                defaultDesc = "RPC_ToggleBladesDesc".Translate(),
                icon = ContentFinder<UnityEngine.Texture2D>.Get(Props.extendIconPath),
                isActive = () => extended,
                toggleAction = () => SetExtended(Pawn, !extended),
            };
        }

        /// <summary>Synced in multiplayer: all blades on the pawn extend/retract together.</summary>
        [SyncMethod]
        public static void SetExtended(Pawn pawn, bool value)
        {
            foreach (var h in pawn.health.hediffSet.hediffs)
                if (h is HediffWithComps hwc && hwc.TryGetComp<HediffComp_MantisBlades>() is HediffComp_MantisBlades c)
                    c.extended = value;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref extended, "extended", true);
        }
    }

    /// <summary>
    /// Instantly reduces all cyberware ability cooldowns by a tiered fraction after the pawn damages an enemy
    /// (Newton Module) or neutralizes one (Axolotl). Value keys: "cooldownReductionOnDamage",
    /// "cooldownReductionOnNeutralize" (fractions, 0.01 = 1%).
    /// </summary>
    public class HediffCompProperties_CooldownOnCombat : HediffCompProperties
    {
        public HediffCompProperties_CooldownOnCombat() => compClass = typeof(HediffComp_CooldownOnCombat);
    }

    public class HediffComp_CooldownOnCombat : HediffComp, ICyberEventReceiver
    {
        private Hediff_Cyberware Cyber => parent as Hediff_Cyberware;

        public void Notify_DealtDamage(Thing victim, DamageInfo dinfo, float damageDealt)
        {
            float f = Cyber?.GetValue("cooldownReductionOnDamage") ?? 0f;
            if (f > 0f && victim is Pawn p && p.HostileTo(Pawn)) ReduceAll(f);
        }

        public void Notify_Neutralized(Pawn victim, bool killed)
        {
            float f = Cyber?.GetValue("cooldownReductionOnNeutralize") ?? 0f;
            if (f > 0f) ReduceAll(f);
        }

        public void Notify_TookDamage(DamageInfo dinfo, float damageTaken) { }

        private void ReduceAll(float fraction)
        {
            foreach (var a in Pawn.CyberAbilities()) a.ReduceCooldownFraction(fraction);
        }
    }
}
