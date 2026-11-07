using System.Collections.Generic;
using System.Reflection;
using CombatExtended;
using CombatExtended.Compatibility;
using Verse;

namespace RimPunkCE.Compat
{
    /// <summary>
    /// General CE fix for other mods' implants. CE's melee code reads ToolCE data on every hit, but CE only converts
    /// weapon and race tools: an implant whose melee attack (HediffCompProperties_VerbGiver.tools) is a plain vanilla
    /// Tool throws a NullReferenceException in Verb_MeleeAttackCE.DamageInfosToApply on every sharp hit, so it never
    /// deals damage (found with Bionic modularity's plasma blade module, 2026-11-04). At startup every such tool is
    /// replaced by a ToolCE: all vanilla fields copied, armour penetration converted with CE's own race curves
    /// (vanilla AP, or vanilla's power-based default, mapped to mm sharp / MPa blunt).
    /// </summary>
    public static class ImplantToolsCE
    {
        private static readonly FieldInfo[] ToolFields = typeof(Tool).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        public static void ConvertAll()
        {
            int tools = 0;
            var defs = new List<string>();
            foreach (var hd in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                if (hd.comps == null) continue;
                bool changed = false;
                foreach (var cp in hd.comps)
                {
                    if (!(cp is HediffCompProperties_VerbGiver vg) || vg.tools == null) continue;
                    for (int i = 0; i < vg.tools.Count; i++)
                    {
                        var t = vg.tools[i];
                        if (t == null || t is ToolCE) continue;
                        vg.tools[i] = Convert(t);
                        tools++;
                        changed = true;
                    }
                }
                if (changed) defs.Add(hd.defName);
            }
            if (tools > 0)
                Diag.Event("startup", $"CE: converted {tools} implant melee tool(s) to ToolCE on {defs.Count} hediff(s): {string.Join(", ", defs)}");
        }

        private static ToolCE Convert(Tool tool)
        {
            var ce = new ToolCE();
            foreach (var f in ToolFields) f.SetValue(ce, f.GetValue(tool));
            // vanilla Tool.armorPenetration < 0 means "derive from power" (vanilla: power * 0.015)
            float vanillaAp = tool.armorPenetration >= 0f ? tool.armorPenetration : tool.power * 0.015f;
            ce.armorPenetrationSharp = RaceUtil.SharpCurve.Evaluate(vanillaAp);
            ce.armorPenetrationBlunt = RaceUtil.BluntCurve.Evaluate(vanillaAp);
            if (ce.cooldownTime <= 0f) ce.cooldownTime = 2f;
            return ce;
        }
    }
}
