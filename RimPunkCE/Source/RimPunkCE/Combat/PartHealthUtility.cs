using System;
using HarmonyLib;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Body part max health. With Elite Bionics Framework loaded its endpoint is used (EBF flags direct GetMaxHealth
    /// calls and its bionics change part max health); otherwise vanilla. Soft dependency, resolved once by reflection.
    /// </summary>
    public static class PartHealthUtility
    {
        private static Func<BodyPartRecord, Pawn, bool, float> ebf;
        private static bool resolved;

        public static float MaxHealth(BodyPartRecord part, Pawn pawn)
        {
            if (!resolved)
            {
                resolved = true;
                if (ModsConfig.IsActive("V1024.EBFramework"))
                {
                    try
                    {
                        var m = AccessTools.Method("EBF.EBFEndpoints:GetMaxHealthWithEBF", new[] { typeof(BodyPartRecord), typeof(Pawn), typeof(bool) });
                        if (m != null) ebf = (Func<BodyPartRecord, Pawn, bool, float>)Delegate.CreateDelegate(typeof(Func<BodyPartRecord, Pawn, bool, float>), m);
                    }
                    catch (Exception e)
                    {
                        RPLog.Debug("EBF endpoint not usable, falling back to vanilla max health: " + e.Message);
                    }
                }
            }
            return ebf != null ? ebf(part, pawn, true) : part.def.GetMaxHealth(pawn);
        }
    }
}
