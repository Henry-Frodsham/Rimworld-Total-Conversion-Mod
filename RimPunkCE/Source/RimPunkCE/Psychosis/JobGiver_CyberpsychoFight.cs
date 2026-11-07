using RimWorld;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>
    /// Fight AI for cyberpsychos (ThinkTree_Cyberpsychosis.xml). Vanilla JobGiver_AIFightEnemy gives colonists
    /// nothing unless their hostility response is Attack, and never lets colonists use abilities, so a colonist
    /// cyberpsycho just stood around. Here colonists fight like any other psycho: abilities first (Mantis Dash
    /// only fires when the gun is empty, see CompAbilityEffect_DashStrike), then guns, melee as a fallback.
    /// Targets come from MentalState_Cyberpsychosis.ForceHostileTo, so everyone counts, own faction included.
    /// </summary>
    public class JobGiver_CyberpsychoFight : JobGiver_AIFightEnemies
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var settings = pawn.playerSettings;
            if (settings == null || (!pawn.IsColonist && !pawn.IsColonySubhuman)) return base.TryGiveJob(pawn);

            UpdateEnemyTarget(pawn);
            var enemy = pawn.mindState.enemyTarget;
            if (enemy == null) return null;
            if (!(enemy is Pawn ep && ep.IsPsychologicallyInvisible()))
            {
                var abilityJob = GetAbilityJob(pawn, enemy);
                if (abilityJob != null) return abilityJob;
            }

            // vanilla bails for colonists not set to Attack: force it for this call only (the setting is untouched)
            var old = settings.hostilityResponse;
            settings.hostilityResponse = HostilityResponseMode.Attack;
            try
            {
                return base.TryGiveJob(pawn);
            }
            finally
            {
                settings.hostilityResponse = old;
            }
        }
    }
}
