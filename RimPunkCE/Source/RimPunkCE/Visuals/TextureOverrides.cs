using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Drop-in art. At startup every RimPunk def looks for a texture under Textures/RimPunk/ by name and, if one exists,
    /// replaces its placeholder. No XML edits: save the PNG, restart. Lookup order (first file found wins):
    ///   cyberware item  RimPunk/Cyberware/&lt;item defName&gt;, RimPunk/Cyberware/_&lt;slot&gt;         (slot = Arm, Face, Legs, ...)
    ///   gun             RimPunk/Weapons/&lt;defName&gt;, RimPunk/Weapons/_&lt;Manu&gt;_&lt;Class&gt;, RimPunk/Weapons/_&lt;Class&gt;
    ///   melee           RimPunk/Weapons/&lt;defName&gt;, RimPunk/Weapons/_Melee_&lt;Class&gt;
    ///   ability         RimPunk/Abilities/&lt;defName&gt;
    ///   faction         RimPunk/Factions/&lt;defName&gt; (white, tinted by faction colour), RimPunk/Factions/&lt;defName&gt;_Settlement
    /// Source/Tools/gen_assets.py mirrors these rules to list what is missing.
    /// </summary>
    public static class TextureOverrides
    {
        private static readonly FieldInfo CachedGraphic = AccessTools.Field(typeof(GraphicData), "cachedGraphic");
        private static readonly MethodInfo ResolveIcon = AccessTools.Method(typeof(BuildableDef), "ResolveIcon");
        private static readonly FieldInfo FactionIcon = AccessTools.Field(typeof(FactionDef), "factionIcon");
        private static readonly FieldInfo SettlementTexture = AccessTools.Field(typeof(FactionDef), "settlementTexture");

        public static int Applied { get; private set; }

        public static void ApplyAll()
        {
            Applied = 0;

            var itemSlot = new Dictionary<ThingDef, string>();
            foreach (var h in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                var ext = h.GetModExtension<CyberwareExtension>();
                if (ext?.item != null && ext.slot != null) itemSlot[ext.item] = Short(ext.slot.defName, "RPC_Slot_");
            }

            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.graphicData == null || !d.defName.StartsWith("RPC_")) continue;
                if (itemSlot.TryGetValue(d, out var slot))
                    ApplyThing(d, true, "RimPunk/Cyberware/" + d.defName, "RimPunk/Cyberware/_" + slot);
                else if (d.defName.StartsWith("RPC_Gun_"))
                {
                    var manu = Tag(d, "RPC_Manu_");
                    var cls = Tag(d, "RPC_Class_");
                    ApplyThing(d, false, "RimPunk/Weapons/" + d.defName,
                        manu != null && cls != null ? $"RimPunk/Weapons/_{manu}_{cls}" : null,
                        cls != null ? "RimPunk/Weapons/_" + cls : null);
                }
                else if (d.defName.StartsWith("RPC_Melee_"))
                {
                    var cls = Tag(d, "RPC_MeleeClass_");
                    ApplyThing(d, false, "RimPunk/Weapons/" + d.defName, cls != null ? "RimPunk/Weapons/_Melee_" + cls : null);
                }
            }

            foreach (var a in DefDatabase<AbilityDef>.AllDefsListForReading)
            {
                if (!a.defName.StartsWith("RPC_")) continue;
                var tex = Find("RimPunk/Abilities/" + a.defName);
                if (tex == null) continue;
                a.iconPath = "RimPunk/Abilities/" + a.defName;
                a.uiIcon = tex;
                Applied++;
            }

            foreach (var f in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (!f.HasModExtension<CyberFactionExtension>()) continue;
                string icon = "RimPunk/Factions/" + f.defName, settlement = icon + "_Settlement";
                if (Find(icon) != null) { f.factionIconPath = icon; FactionIcon?.SetValue(f, null); Applied++; }
                if (Find(settlement) != null) { f.settlementTexturePath = settlement; SettlementTexture?.SetValue(f, null); Applied++; }
            }

            Diag.Event("startup", $"textures: {Applied} drop-in overrides applied");
        }

        private static void ApplyThing(ThingDef d, bool clearTint, params string[] candidates)
        {
            var path = candidates.FirstOrDefault(p => p != null && Find(p) != null);
            if (path == null) return;
            var g = d.graphicData;
            g.texPath = path;
            g.graphicClass = typeof(Graphic_Single);
            if (clearTint) g.color = Color.white;
            CachedGraphic?.SetValue(g, null);
            d.graphic = g.Graphic;
            ResolveIcon?.Invoke(d, null);
            Applied++;
        }

        private static Texture2D Find(string path) => ContentFinder<Texture2D>.Get(path, false);

        /// <summary>Gizmo icon from RimPunk/UI/&lt;name&gt;, falling back to the placeholder.</summary>
        public static Texture2D Gizmo(string name)
        {
            if (!GizmoCache.TryGetValue(name, out var tex))
                GizmoCache[name] = tex = Find("RimPunk/UI/" + name) ?? Find(name) ?? Find("UI/RimPunk/Placeholder_Ability") ?? BaseContent.BadTex;
            return tex;
        }

        private static readonly Dictionary<string, Texture2D> GizmoCache = new Dictionary<string, Texture2D>();

        private static string Tag(ThingDef d, string prefix)
        {
            var t = d.weaponTags?.FirstOrDefault(w => w.StartsWith(prefix));
            return t == null ? null : Short(t, prefix);
        }

        private static string Short(string s, string prefix) => s.StartsWith(prefix) ? s.Substring(prefix.Length) : s;
    }
}
