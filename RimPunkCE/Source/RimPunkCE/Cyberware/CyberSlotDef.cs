using System.Collections.Generic;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// A cyberware slot (operating system, arms, prefrontal cortex, ...). All slot rules are data:
    /// edit Defs/CyberSlotDefs.xml to change limits.
    /// </summary>
    public class CyberSlotDef : Def
    {
        /// <summary>Max installed in this slot per pawn (across all body parts).</summary>
        public int maxInstalled = 1;

        /// <summary>Body parts this slot installs into. Empty = whole-body system (no vanilla part taken).</summary>
        public List<BodyPartDef> bodyParts = new List<BodyPartDef>();

        /// <summary>At most one of this slot's cyberware per body part (arms: one per arm).</summary>
        public bool onePerPart = true;

        /// <summary>Legs: every installed cyberware in this slot must be the same def.</summary>
        public bool allMustMatch;

        /// <summary>Legs: a full matching set (maxInstalled of the same def) doubles all its effects.</summary>
        public bool matchingSetDoublesEffects;

        public bool WholeBody => bodyParts.NullOrEmpty();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (maxInstalled < 1) yield return "maxInstalled must be >= 1";
        }
    }
}
