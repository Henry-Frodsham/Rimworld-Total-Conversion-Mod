using RimWorld;
using Verse;

namespace RimPunkCE
{
    [DefOf]
    public static class RPStatDefOf
    {
        public static StatDef RPC_CyberwareCapacity;
        public static StatDef RPC_CyberwareDamageFactor;
        public static StatDef RPC_CyberwareCooldownFactor;
        public static StatDef RPC_RamMax;
        public static StatDef RPC_RamRegen;
        public static StatDef RPC_QuickhackUploadSpeed;
        public static StatDef RPC_QuickhackCritChance;
        public static StatDef RPC_StaminaMax;
        public static StatDef RPC_StaminaRegen;
        public static StatDef RPC_StaminaCostFactor;
        public static StatDef RPC_MitigationChance;
        public static StatDef RPC_MitigationStrength;
        public static StatDef RPC_RangedCritChance;
        public static StatDef RPC_CritDamageFactor;
        public static StatDef RPC_HeadshotDamageFactor;
        public static StatDef RPC_DamageVsMechanoids;
        public static StatDef RPC_ThermalResistance;
        public static StatDef RPC_ChemicalResistance;
        public static StatDef RPC_ElectricalResistance;
        public static StatDef RPC_IncomingDamageFactor;
        public static StatDef RPC_MaxHealthFactor;
        public static StatDef RPC_DamageFactorAll;
        public static StatDef RPC_DamageFactorTech;
        public static StatDef RPC_DamageFactorPower;
        public static StatDef RPC_DamageVsPoisoned;
        public static StatDef RPC_OrganDamageFactor;
        public static StatDef RPC_CritChanceGrenades;
        public static StatDef RPC_StaminaCostFactorMelee;
        public static StatDef RPC_StaminaCostFactorRanged;
        public static StatDef RPC_RecoilFactor;
        public static StatDef RPC_SpreadFactor;
        public static StatDef RPC_QuickhackDamageFactor;

        static RPStatDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RPStatDefOf));
    }

    [DefOf]
    public static class RPDefOf
    {
        public static NeedDef RPC_Stamina;
        public static HediffDef RPC_Exhausted;
        public static HediffDef RPC_CyberpsychosisBurnout;
        public static HediffDef RPC_CyberpsychosisRage;
        public static HediffDef RPC_WeaponThrown;
        public static HediffDef RPC_TraumaTeamCoverage;
        public static JobDef RPC_TraumaBoardShuttle;
        public static TraitDef RPC_CyberAptitude;
        public static TraitDef RPC_Netrunner;
        public static MentalStateDef RPC_Cyberpsychosis;
        public static DamageDef RPC_Electrical;
        public static DamageDef RPC_Toxic;
        public static DamageDef RPC_Thermal;
        public static DamageDef RPC_Shock;
        public static StaminaTuningDef RPC_StaminaTuning;
        public static CyberpsychosisTuningDef RPC_CyberpsychosisTuning;
        public static AbilityCategoryDef RPC_Cyberware;

        [MayRequireBiotech] public static GeneDef Deathless;

        static RPDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RPDefOf));
    }

    /// <summary>Capacity aptitude trait degrees (one TraitDef, five degrees; pawn_traits.md).</summary>
    public static class AptitudeDegree
    {
        public const int Gonk = -2;
        public const int Choom = -1;
        public const int Edgerunner = 0;
        public const int Dreamer = 1;
        public const int Legend = 2;
    }
}
