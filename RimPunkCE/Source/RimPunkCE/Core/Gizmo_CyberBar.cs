using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    public enum CyberBarKind
    {
        Ram,
        Stamina,
    }

    /// <summary>Resource bar gizmo for RAM (cyberdeck users) and stamina (while drafted).</summary>
    [StaticConstructorOnStartup]
    public class Gizmo_CyberBar : Gizmo
    {
        private static readonly Texture2D RamFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.65f, 0.95f));
        private static readonly Texture2D StaminaFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.95f, 0.8f, 0.2f));
        private static readonly Texture2D ExhaustedFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.6f, 0.2f, 0.2f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.08f, 0.08f, 0.08f));

        private readonly CompCyberTracker tracker;
        private readonly CyberBarKind kind;

        public Gizmo_CyberBar(CompCyberTracker tracker, CyberBarKind kind)
        {
            this.tracker = tracker;
            this.kind = kind;
            Order = -90f;
        }

        public override float GetWidth(float maxWidth) => 140f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);
            var inner = rect.ContractedBy(6f);

            float cur, max;
            string label;
            Texture2D fill;
            if (kind == CyberBarKind.Ram)
            {
                cur = tracker.Ram;
                max = tracker.RamMax;
                label = "RPC_Ram".Translate();
                fill = RamFill;
            }
            else
            {
                var need = tracker.Stamina;
                cur = need?.CurLevel ?? 0f;
                max = need?.MaxLevel ?? 1f;
                label = "RPC_Stamina".Translate();
                fill = need != null && need.Exhausted ? ExhaustedFill : StaminaFill;
            }

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 20f), label);
            var bar = new Rect(inner.x, inner.y + 24f, inner.width, 22f);
            Widgets.FillableBar(bar, max > 0f ? Mathf.Clamp01(cur / max) : 0f, fill, Empty, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, cur.ToString("0.#") + " / " + max.ToString("0.#"));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return new GizmoResult(GizmoState.Clear);
        }
    }
}
