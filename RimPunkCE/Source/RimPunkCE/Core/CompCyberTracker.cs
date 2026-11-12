using System.Linq;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public class CompProperties_CyberTracker : CompProperties
    {
        public CompProperties_CyberTracker() => compClass = typeof(CompCyberTracker);
    }

    /// <summary>
    /// Per-pawn cyberware state, added at startup to every humanlike race (so other mods' races are covered).
    /// Holds rolled capacity, RAM, adrenaline and combat bookkeeping. Capacity max is a stat
    /// (RPC_CyberwareCapacity); this comp only contributes the rolled base via GetStatOffset.
    /// </summary>
    public class CompCyberTracker : ThingComp
    {
        public const float BaseCapacityMin = 50f;
        public const float BaseCapacityMax = 150f;

        public float instability;
        public int nextPsychosisTick;
        private float baseCapacity = -1f;
        private float ram;
        public int lastCombatTick = -99999;
        public int lastHighTierQuickhackTick = -999999;
        public int lastKiteTick = -999999;
        public int lastHackThinkTick = -999999;

        /// <summary>Neural shards in the pawn's ports (cycle 4, Shards/Shards.cs). Their effects are read from the defs.</summary>
        public List<ThingDef> shards = new List<ThingDef>();

        /// <summary>Recruit resistance / slave will of the person an engram wrote into this body (-1: none, the pawn kind's rolls apply).</summary>
        public float engramResistance = -1f;
        public float engramWill = -1f;

        private bool cacheDirty = true;
        private float cachedUsed;
        private readonly List<Hediff_Cyberware> cachedCyberware = new List<Hediff_Cyberware>();
        private readonly List<Hediff> cachedForeign = new List<Hediff>();

        /// <summary>Other mods' implants counted against capacity (ForeignCyberwareUtility).</summary>
        public IReadOnlyList<Hediff> ForeignImplants
        {
            get
            {
                RecacheIfDirty();
                return cachedForeign;
            }
        }

        /// <summary>Null when the parent is a corpse (corpse defs inherit the race's comps).</summary>
        public Pawn Pawn => parent as Pawn;

        public float BaseCapacity
        {
            get
            {
                EnsureBaseCapacity();
                return baseCapacity;
            }
        }

        // ---------- capacity ----------

        public IReadOnlyList<Hediff_Cyberware> Cyberware
        {
            get
            {
                RecacheIfDirty();
                return cachedCyberware;
            }
        }

        public bool HasCyberware => Cyberware.Count > 0;

        public float CapacityUsed
        {
            get
            {
                RecacheIfDirty();
                return cachedUsed;
            }
        }

        public float CapacityMax => Mathf.Max(1f, Pawn.GetStatValue(RPStatDefOf.RPC_CyberwareCapacity));

        /// <summary>Capacity usage as a fraction of max (trauma team strips chrome down to the safe fraction).</summary>
        public float UsageRatio => CapacityUsed / CapacityMax;

        public override float GetStatOffset(StatDef stat)
        {
            if (stat == RPStatDefOf.RPC_CyberwareCapacity && Pawn != null) return BaseCapacity;
            float sum = 0f;
            for (int i = 0; i < shards.Count; i++)
            {
                var offsets = shards[i].ShardExt()?.statOffsets;
                if (offsets != null) sum += offsets.GetStatOffsetFromList(stat);
            }
            return sum;
        }

        public override float GetStatFactor(StatDef stat)
        {
            float f = 1f;
            for (int i = 0; i < shards.Count; i++)
            {
                var factors = shards[i].ShardExt()?.statFactors;
                if (factors != null) f *= factors.GetStatFactorFromList(stat);
            }
            return f;
        }

        public override void GetStatsExplanation(StatDef stat, System.Text.StringBuilder sb, string whitespace = "")
        {
            foreach (var shard in shards)
            {
                var ext = shard.ShardExt();
                if (ext == null) continue;
                float off = ext.statOffsets?.GetStatOffsetFromList(stat) ?? 0f;
                float fac = ext.statFactors?.GetStatFactorFromList(stat) ?? 1f;
                if (off != 0f) sb.AppendLine(whitespace + shard.LabelCap + ": " + stat.Worker.ValueToString(off, false, ToStringNumberSense.Offset));
                if (fac != 1f) sb.AppendLine(whitespace + shard.LabelCap + ": " + stat.Worker.ValueToString(fac, false, ToStringNumberSense.Factor));
            }
        }

        public void Notify_CyberwareChanged()
        {
            cacheDirty = true;
            if (Pawn?.health?.hediffSet == null) return;
            if (Pawn.abilities != null) SyncQuickhacks();
            RecacheIfDirty();
            CyberUtility.UpdateMatchingSets(Pawn, cachedCyberware);
            Pawn.health.capacities?.Notify_CapacityLevelsDirty();
        }

        private void RecacheIfDirty()
        {
            if (!cacheDirty) return;
            cacheDirty = false;
            cachedCyberware.Clear();
            cachedForeign.Clear();
            cachedUsed = 0f;
            var hediffs = Pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Cyberware c)
                {
                    cachedCyberware.Add(c);
                    cachedUsed += c.CapacityCost;
                }
                else if (ForeignCyberwareUtility.IsForeignImplant(hediffs[i]))
                {
                    cachedForeign.Add(hediffs[i]);
                    cachedUsed += ForeignCyberwareUtility.CostOf(hediffs[i]);
                }
            }
        }

        /// <summary>Dev tools: override the rolled base capacity.</summary>
        public void Debug_SetBaseCapacity(float value)
        {
            baseCapacity = UnityEngine.Mathf.Max(0f, value);
            Notify_CyberwareChanged();
        }

        private void EnsureBaseCapacity()
        {
            if (baseCapacity >= 0f) return;
            // seeded per pawn: identical on every multiplayer client
            baseCapacity = Mathf.Round(CyberUtility.SeededRange(Gen.HashCombineInt(parent.thingIDNumber, 0x52504345), BaseCapacityMin, BaseCapacityMax));
        }

        // ---------- RAM ----------

        /// <summary>RAM is a cyberdeck resource: the bar only shows with a cyberdeck installed (netrunner trait, RAM implants alone don't).</summary>
        public bool HasCyberdeck => Pawn.InstalledCyberware().Any(c => c.Ext?.osType == CyberOsType.Cyberdeck);

        public float RamMax => Pawn.GetStatValue(RPStatDefOf.RPC_RamMax);
        public float Ram => Mathf.Min(ram, RamMax);

        public bool TryUseRam(float amount)
        {
            if (Ram < amount) return false;
            ram = Ram - amount;
            CyberEvents.Notify_RamChanged(Pawn, this);
            return true;
        }

        public void GainRam(float amount) => ram = Mathf.Clamp(Ram + amount, 0f, RamMax);

        /// <summary>Spend RAM; returns how much was missing (paid in health under Overclock, see QuickhackUtility.SpendRam).</summary>
        public float SpendRamAllowingDebt(float amount)
        {
            float have = Ram;
            if (have >= amount)
            {
                ram = have - amount;
                return 0f;
            }
            ram = 0f;
            return amount - have;
        }

        // ---------- quickhacks ----------

        private Dictionary<AbilityDef, CyberTier> quickhacks = new Dictionary<AbilityDef, CyberTier>();
        private List<AbilityDef> qhKeys;
        private List<CyberTier> qhValues;

        public IReadOnlyDictionary<AbilityDef, CyberTier> LearnedQuickhacks => quickhacks;

        /// <summary>Learned tier; Blackwall Gateway is always Iconic.</summary>
        public CyberTier QuickhackTier(AbilityDef def)
        {
            if (def == QuickhackDefOf.RPC_Quickhack_BlackwallGateway) return CyberTier.Iconic;
            return quickhacks.TryGetValue(def, out var t) ? t : def.GetModExtension<QuickhackExtension>()?.minTier ?? CyberTier.T1;
        }

        /// <summary>Temporary RAM cost reductions (t5 quickhack perks). Not used by the current quickhacks yet.</summary>
        public float QuickhackRamDiscount(AbilityDef def) => 0f;

        /// <summary>Learn a quickhack, or raise it to <paramref name="tier"/>. Returns false if already known at that tier or higher.</summary>
        public bool LearnQuickhack(AbilityDef def, CyberTier tier)
        {
            var ext = def?.GetModExtension<QuickhackExtension>();
            if (ext == null) return false;
            tier = TierUtility.Clamp(tier, ext.minTier, ext.maxTier);
            if (quickhacks.TryGetValue(def, out var have) && have >= tier) return false;
            quickhacks[def] = tier;
            SyncQuickhacks();
            return true;
        }

        public void ForgetQuickhack(AbilityDef def)
        {
            quickhacks.Remove(def);
            SyncQuickhacks();
        }

        /// <summary>
        /// Quickhack abilities follow the cyberdeck: with one installed every learned quickhack is granted (installing a
        /// deck teaches the preinstalled Short Circuit and Ping), without one they're hidden but stay learned. An Iconic
        /// Canto Mk.6 adds Blackwall Gateway. Old saves: abilities loaded as a different class are re-granted.
        /// </summary>
        public void SyncQuickhacks()
        {
            var pawn = Pawn;
            var abilities = pawn?.abilities;
            if (abilities == null) return;
            bool deck = WorldFeatures.Quickhacks && HasCyberdeck;
            if (deck)
            {
                foreach (var pre in new[] { QuickhackDefOf.RPC_Quickhack_ShortCircuit, QuickhackDefOf.RPC_Quickhack_Ping })
                    if (pre != null && !quickhacks.ContainsKey(pre)) quickhacks[pre] = CyberTier.T1;
            }
            var wanted = new HashSet<AbilityDef>();
            if (deck)
            {
                foreach (var d in quickhacks.Keys) wanted.Add(d);
                if (QuickhackDefOf.RPC_Quickhack_BlackwallGateway != null && QuickhackUtility.DeckValue(pawn, "blackwallGateway") > 0f)
                    wanted.Add(QuickhackDefOf.RPC_Quickhack_BlackwallGateway);
            }
            foreach (var a in abilities.AllAbilitiesForReading.ToList())
            {
                if (!a.def.HasModExtension<QuickhackExtension>()) continue;
                if (!wanted.Contains(a.def) || !(a is QuickhackAbility)) abilities.RemoveAbility(a.def);
            }
            foreach (var d in wanted)
                if (abilities.GetAbility(d) == null) abilities.GainAbility(d);
        }

        // ---------- adrenaline ----------

        private float adrenaline;

        public float AdrenalineMax => Mathf.Max(1f, Pawn.GetStatValue(RPStatDefOf.RPC_AdrenalineMax));
        public float Adrenaline => Mathf.Min(adrenaline, AdrenalineMax);
        public float AdrenalineFraction => Adrenaline / AdrenalineMax;

        /// <summary>Flat gain, already scaled by the caller. Nothing is gained with the system switched off.</summary>
        public void GainAdrenaline(float amount)
        {
            if (amount <= 0f || !RimPunkMod.Settings.adrenalineEnabled) return;
            adrenaline = Mathf.Min(AdrenalineMax, Adrenaline + amount);
        }

        /// <summary>Gain a fraction of max ("+22% adrenaline").</summary>
        public void GainAdrenalineFraction(float fraction) => GainAdrenaline(fraction * AdrenalineMax);

        public void SetAdrenaline(float amount) => adrenaline = Mathf.Clamp(amount, 0f, AdrenalineMax);

        /// <summary>True when the pawn can pay; with the system switched off everything is free.</summary>
        public bool HasAdrenaline(float cost) => !RimPunkMod.Settings.adrenalineEnabled || Adrenaline >= cost;

        public void SpendAdrenaline(float cost)
        {
            if (!RimPunkMod.Settings.adrenalineEnabled) return;
            adrenaline = Mathf.Max(0f, Adrenaline - cost);
        }

        /// <summary>From CyberEvents: damage dealt (<paramref name="dealt"/>) or taken builds adrenaline.</summary>
        public void Notify_DamageForAdrenaline(DamageInfo dinfo, float amount, bool dealt)
        {
            if (amount <= 0f || !RimPunkMod.Settings.adrenalineEnabled) return;
            var tuning = RPDefOf.RPC_AdrenalineTuning;
            float gain = amount * (dealt ? tuning.perDamageDealt : tuning.perDamageTaken) * Pawn.GetStatValue(RPStatDefOf.RPC_AdrenalineGain);
            if (dealt)
            {
                bool melee = dinfo.Weapon == null || dinfo.Weapon.IsMeleeWeapon;
                gain *= Pawn.GetStatValue(melee ? RPStatDefOf.RPC_AdrenalineGainMelee : RPStatDefOf.RPC_AdrenalineGainRanged);
            }
            foreach (var active in Pawn.ActiveEffects())
                if (active.Ext != null) gain *= active.Ext.adrenalineGainFactor;
            GainAdrenaline(gain);
            Diag.Count(dealt ? "adrenaline-dealt" : "adrenaline-taken");
        }

        /// <summary>Decays out of combat, and keeps the surge hediff in step with the level. Every 15 ticks.</summary>
        private void TickAdrenaline()
        {
            var pawn = Pawn;
            var tuning = RPDefOf.RPC_AdrenalineTuning;
            if (adrenaline > 0f && !InCombat)
                adrenaline = Mathf.Max(0f, adrenaline - tuning.decayPerSecond * pawn.GetStatValue(RPStatDefOf.RPC_AdrenalineDecay) * 0.25f);
            bool surging = !pawn.Downed && RimPunkMod.Settings.adrenalineEnabled && AdrenalineFraction >= tuning.surgeFraction;
            var surge = pawn.health.hediffSet.GetFirstHediffOfDef(RPDefOf.RPC_AdrenalineSurge);
            if (surging)
            {
                if (surge == null) surge = pawn.health.AddHediff(RPDefOf.RPC_AdrenalineSurge);
                surge.Severity = AdrenalineFraction;
            }
            else if (surge != null) pawn.health.RemoveHediff(surge);
        }

        /// <summary>Last dodge / dash / jump (Nano-plating bonus).</summary>
        public int lastEvasiveTick = -999999;

        public void Notify_EvasiveAction() => lastEvasiveTick = Find.TickManager.TicksGame;

        // Painducer: part of incoming damage is held back and applied as small hits over time
        private float delayedPool;
        private int delayedTicksLeft;
        public static bool ApplyingDelayed;

        public void AddDelayed(float amount, int overTicks)
        {
            delayedPool += amount;
            delayedTicksLeft = Mathf.Max(delayedTicksLeft, overTicks);
        }

        public float DelayedDamagePending => delayedPool;

        /// <summary>Drops the held-back Painducer damage (Trauma Team treatment).</summary>
        public void ClearDelayed()
        {
            delayedPool = 0f;
            delayedTicksLeft = 0;
        }

        private void TickDelayed(int interval)
        {
            if (delayedPool <= 0f || Pawn == null || !Pawn.Spawned) return;
            // the drip only spreads out what the pawn took on their feet: once down, the rest is dropped (it would
            // otherwise keep hitting, and finally kill, a pawn lying helpless)
            if (Pawn.Downed)
            {
                Diag.Event("cyberware", $"{Diag.Describe(Pawn)} downed: {delayedPool:0.#} delayed damage dropped");
                ClearDelayed();
                return;
            }
            int steps = Mathf.Max(1, delayedTicksLeft / interval);
            float hit = delayedPool / steps;
            delayedPool -= hit;
            delayedTicksLeft = Mathf.Max(0, delayedTicksLeft - interval);
            if (hit < 0.05f) { if (delayedTicksLeft <= 0) delayedPool = 0f; return; }
            ApplyingDelayed = true;
            try { Pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, hit, 999f, -1f, null, DelayedDamagePart(Pawn), null, DamageInfo.SourceCategory.ThingOrUnknown)); }
            finally { ApplyingDelayed = false; }
        }

        /// <summary>Torso or a limb, never the head (and so never the brain or eyes): delayed damage shouldn't be a headshot.</summary>
        private static BodyPartRecord DelayedDamagePart(Pawn pawn)
        {
            var head = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def == BodyPartDefOf.Head);
            var parts = pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
                .Where(p => p.coverageAbs > 0f && (head == null || !CyberSurgeryUtility.IsUnder(p, head))).ToList();
            return parts.Count > 0 ? parts.RandomElementByWeight(p => p.coverageAbs) : pawn.RaceProps.body.corePart;
        }

        public bool InCombat => Find.TickManager.TicksGame - lastCombatTick < RPDefOf.RPC_AdrenalineTuning.outOfCombatTicks;

        public void Notify_CombatAction()
        {
            bool wasInCombat = InCombat;
            lastCombatTick = Find.TickManager.TicksGame;
            if (wasInCombat) return;
            GainAdrenaline(RPDefOf.RPC_AdrenalineTuning.enteredCombatBurst); // the first rush: enough for one activation
            CyberEvents.Notify_EnteredCombat(Pawn);
        }

        // ---------- lifecycle ----------

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureBaseCapacity();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (Pawn != null) SyncQuickhacks();
            if (Pawn == null) return;
            if (!respawningAfterLoad && ram <= 0f) ram = RamMax; // arrive with a full cyberdeck (raiders can quickhack straight away)
            EnsureBaseCapacity();
            AptitudeUtility.EnsureAptitudeTrait(Pawn);
            cacheDirty = true;
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            if (!parent.IsHashIntervalTick(15, delta)) return;
            var pawn = Pawn;
            if (pawn == null || pawn.Dead) return;

            float ramMax = RamMax;
            if (ramMax > 0f && ram < ramMax)
                ram = Mathf.Min(ramMax, ram + pawn.GetStatValue(RPStatDefOf.RPC_RamRegen) * 15f / 60f);

            TickAdrenaline();
            if (parent.IsHashIntervalTick(60, delta))
            {
                TickDelayed(60);
                CyberpsychosisUtility.TickInstability(pawn);
                if (RimPunkMod.Settings.contextualAI) ContextualCombat.TickInterval(pawn, this);
            }
            if (parent.IsHashIntervalTick(250, delta)) CyberpsychosisUtility.ClearOrphanedRage(pawn);
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            var pawn = Pawn;
            if (pawn == null || pawn.Dead || !dinfo.Def.harmsHealth) return;
            CombatDamageUtility.PreApplyDamage(pawn, this, ref dinfo, out absorbed);
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            if (Pawn == null) return;
            if (dinfo.Def.ExternalViolenceFor(Pawn)) Notify_CombatAction();
            if (totalDamageDealt > 0f) Verb_Quickhack.CancelUpload(Pawn);
            CyberEvents.Notify_DamageTaken(Pawn, dinfo, totalDamageDealt);
        }

        public override void Notify_UsedVerb(Pawn pawn, Verb verb)
        {
            base.Notify_UsedVerb(pawn, verb);
            if (verb is Verb_CastAbilityJump) Notify_EvasiveAction();
            if (verb is Verb_CastAbility) return;
            MapComponent_CyberAnimations.Notify_VerbUsed(pawn, verb); // mantis / gorilla / monowire swing
            Notify_CombatAction();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;
            var pawn = Pawn;
            if (pawn == null) yield break;
            if (!(pawn.Faction?.IsPlayer ?? false) && !DebugSettings.ShowDevGizmos) yield break;
            if (WorldFeatures.Quickhacks && HasCyberdeck && RamMax > 0f) yield return new Gizmo_CyberBar(this, CyberBarKind.Ram);
            if (RimPunkMod.Settings.adrenalineEnabled && (pawn.Drafted || Adrenaline > 0f)) yield return new Gizmo_CyberBar(this, CyberBarKind.Adrenaline);
            if (pawn.IsColonistPlayerControlled && !GameComponent_RimPunk.IsCovered(pawn) && GameComponent_RimPunk.Tuning is TraumaTeamTuningDef tt
                && CyberFactionUtility.FirstOfKind(CyberFactionKind.TraumaTeam) != null)
            {
                var buy = new Command_Action
                {
                    defaultLabel = "RPC_BuyTraumaTeam".Translate(tt.coverageCostSilver),
                    defaultDesc = "RPC_BuyTraumaTeamDesc".Translate(tt.coverageCostSilver, tt.coverageDays.ToString("0"),
                        tt.feePerHediff, tt.combatFee, tt.combatFeePerHostile, tt.cyberpsychosisFee),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/RimPunk/TraumaTeam", false) ?? BaseContent.BadTex,
                    action = () => GameComponent_RimPunk.BuyCoverage(pawn),
                };
                if (GameComponent_RimPunk.TraumaHostileToPlayer)
                    buy.Disable("RPC_TraumaRefusedCoverageHostile".Translate(GameComponent_RimPunk.DaysUntilTraumaForgives.ToString("0.#")));
                else if (GameComponent_RimPunk.PlayerInDebt)
                    buy.Disable("RPC_TraumaRefusedCoverage".Translate(UnityEngine.Mathf.CeilToInt(GameComponent_RimPunk.Instance.TraumaDebt)));
                yield return buy;
            }
            // Trauma Team debt: pay what the colony can (on every colonist, like the coverage button)
            if (pawn.IsColonistPlayerControlled && pawn.Map != null && GameComponent_RimPunk.PlayerInDebt)
            {
                var inst = GameComponent_RimPunk.Instance;
                int owed = UnityEngine.Mathf.CeilToInt(inst.TraumaDebt);
                var t = GameComponent_RimPunk.Tuning;
                var pay = new Command_Action
                {
                    defaultLabel = "RPC_PayTraumaDebt".Translate(owed),
                    defaultDesc = "RPC_PayTraumaDebtDesc".Translate(owed, GameComponent_RimPunk.DaysUntilCollection.ToString("0.0"),
                        ((t?.debtDailyInterest ?? 0.02f) * 100f).ToString("0.#"), ((t?.minPaymentFraction ?? 0.25f) * 100f).ToString("0"),
                        SilverUtility.CountSilver(pawn.Map)),
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/RimPunk/TraumaTeam", false) ?? BaseContent.BadTex,
                    action = () => GameComponent_RimPunk.PayDebt(pawn.Map),
                };
                if (SilverUtility.CountSilver(pawn.Map) <= 0) pay.Disable("RPC_TraumaDebtNoSilver".Translate());
                yield return pay;
            }
        }

        public override string CompInspectStringExtra()
        {
            // every humanlike shows it, chrome or not (an unchromed pawn's capacity matters when planning installs)
            if (Pawn == null || Pawn.Dead || Pawn.RaceProps?.Humanlike != true) return null;
            return "RPC_CapacityUsage".Translate(CapacityUsed.ToString("0"), CapacityMax.ToString("0"), UsageRatio.ToStringPercent());
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref instability, "rpcInstability");
            Scribe_Values.Look(ref nextPsychosisTick, "rpcNextPsychosisTick");
            Scribe_Values.Look(ref baseCapacity, "baseCapacity", -1f);
            Scribe_Values.Look(ref ram, "ram");
            Scribe_Values.Look(ref adrenaline, "adrenaline");
            Scribe_Collections.Look(ref quickhacks, "quickhacks", LookMode.Def, LookMode.Value, ref qhKeys, ref qhValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) quickhacks ??= new Dictionary<AbilityDef, CyberTier>();
            Scribe_Values.Look(ref lastCombatTick, "lastCombatTick", -99999);
            Scribe_Values.Look(ref lastHighTierQuickhackTick, "lastHighTierQuickhackTick", -999999);
            Scribe_Values.Look(ref lastKiteTick, "lastKiteTick", -999999);
            Scribe_Values.Look(ref lastHackThinkTick, "lastHackThinkTick", -999999);
            Scribe_Values.Look(ref lastEvasiveTick, "lastEvasiveTick", -999999);
            Scribe_Collections.Look(ref shards, "shards", LookMode.Def);
            Scribe_Values.Look(ref engramResistance, "engramResistance", -1f);
            Scribe_Values.Look(ref engramWill, "engramWill", -1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                shards ??= new List<ThingDef>();
                shards.RemoveAll(d => d == null);
            }
            Scribe_Values.Look(ref delayedPool, "delayedPool");
            Scribe_Values.Look(ref delayedTicksLeft, "delayedTicksLeft");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) cacheDirty = true;
        }
    }
}
