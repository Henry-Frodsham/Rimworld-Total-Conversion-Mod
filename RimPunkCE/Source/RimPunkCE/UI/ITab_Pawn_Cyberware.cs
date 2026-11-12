using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Cyberware tab on every humanlike: a simplified Cyberpunk 2077 ripperdoc screen. Slot groups on both sides of the
    /// pawn (frontal cortex, operating system, face, arms, hands | skeleton, nervous, circulatory, integumentary, legs),
    /// plus limb replacement slots (vanilla / modded bionic arms and legs) under the portrait.
    /// Picking cyberware for a slot only queues the surgery (a medical bill, same as the health tab); the slot shows the
    /// queued item greyed out until a doctor performs it. Clicking a queued slot cancels the bill.
    /// </summary>
    [StaticConstructorOnStartup]
    public class ITab_Pawn_Cyberware : ITab
    {
        private static readonly string[] LeftSlots = { "RPC_Slot_PrefrontalCortex", "RPC_Slot_OperatingSystem", "RPC_Slot_Face", "RPC_Slot_Arm", "RPC_Slot_Hands" };
        private static readonly string[] RightSlots = { "RPC_Slot_Skeleton", "RPC_Slot_NervousSystem", "RPC_Slot_CirculatorySystem", "RPC_Slot_IntegumentarySystem", "RPC_Slot_Legs" };

        private const float Box = 46f, Gap = 6f, LabelH = 22f, GroupGap = 8f, ColW = 250f, LimbW = 96f;
        private static readonly Color Accent = new Color(0.35f, 0.9f, 0.95f);
        private static readonly Color BoxBg = new Color(0.07f, 0.09f, 0.11f);
        private static readonly Color EmptyBorder = new Color(0.3f, 0.36f, 0.4f);
        private static readonly Color QueuedBorder = new Color(1f, 0.8f, 0.25f);
        private static readonly Texture2D AccentTex = SolidColorMaterials.NewSolidColorTexture(Accent);
        private static readonly Texture2D QueuedTex = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 0.8f, 0.25f, 0.5f));
        private static readonly Texture2D BgTex = SolidColorMaterials.NewSolidColorTexture(BoxBg);

        private List<CyberSlotDef> leftSlots, rightSlots;
        private Vector2 scroll;
        /// <summary>Height the content needed last frame (the scroll view is sized from it).</summary>
        private float contentHeight = 600f;

        public ITab_Pawn_Cyberware()
        {
            size = new Vector2(800f, 580f);
            labelKey = "RPC_TabCyberware";
        }

        private Pawn Patient => SelPawn ?? (SelThing as Corpse)?.InnerPawn;

        public override bool IsVisible => Patient?.RaceProps?.Humanlike == true && Patient.GetCyberTracker() != null;

        private static bool CanOperate(Pawn p) =>
            !p.Dead && p.Spawned && (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || p.IsSlaveOfColony);

        private void ResolveSlots()
        {
            if (leftSlots != null) return;
            CyberSlotDef Get(string n) => DefDatabase<CyberSlotDef>.GetNamedSilentFail(n);
            leftSlots = LeftSlots.Select(Get).Where(s => s != null).ToList();
            rightSlots = RightSlots.Select(Get).Where(s => s != null).ToList();
            // slots added later (or by other content) go on the right
            foreach (var s in DefDatabase<CyberSlotDef>.AllDefsListForReading)
                if (!leftSlots.Contains(s) && !rightSlots.Contains(s)) rightSlots.Add(s);
        }

        // ================================================================================================ layout

        protected override void FillTab()
        {
            var p = Patient;
            if (p == null) return;
            ResolveSlots();
            bool canOp = CanOperate(p);
            var bills = p.BillStack?.Bills.OfType<Bill_Medical>().ToList() ?? new List<Bill_Medical>();
            var outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            float headerH = DrawHeader(new Rect(outer.x, outer.y, outer.width, 56f), p, bills, canOp);
            // the slots scroll under the fixed header
            var view = new Rect(outer.x, outer.y + headerH + 6f, outer.width, outer.height - headerH - 6f);
            var rect = new Rect(0f, 0f, view.width - 16f, Mathf.Max(contentHeight, view.height));
            Widgets.BeginScrollView(view, ref scroll, rect);
            var left = new Rect(rect.x, 0f, ColW, rect.height);
            var right = new Rect(rect.xMax - ColW, 0f, ColW, rect.height);
            var center = new Rect(left.xMax + 10f, 0f, right.x - left.xMax - 20f, rect.height);

            float y = 0f;
            foreach (var s in leftSlots) DrawSlotGroup(ref y, left.x, s, p, bills, canOp);
            float leftH = y;
            y = 0f;
            foreach (var s in rightSlots) DrawSlotGroup(ref y, right.x, s, p, bills, canOp);
            float rightH = y;
            float centerH = DrawCenter(center, p, bills, canOp);
            Widgets.EndScrollView();
            contentHeight = Mathf.Max(leftH, Mathf.Max(rightH, centerH)) + 8f;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private float DrawHeader(Rect r, Pawn p, List<Bill_Medical> bills, bool canOp)
        {
            var tracker = p.GetCyberTracker();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 30f), "RPC_UI_Title".Translate(p.LabelShortCap));
            Text.Font = GameFont.Small;
            float max = Mathf.Max(1f, tracker.CapacityMax);
            float queued = bills.Where(b => b.recipe.Worker is Recipe_InstallCyberware).Sum(b => b.recipe.addsHediff.Ext()?.capacityCost ?? 0f);
            var bar = new Rect(r.x, r.y + 32f, 300f, 18f);
            float now = tracker.CapacityUsed / max, after = (tracker.CapacityUsed + queued) / max;
            // queued operations in amber behind the current usage
            Widgets.FillableBar(bar, Mathf.Clamp01(after), QueuedTex, BgTex, false);
            Widgets.FillableBar(bar, Mathf.Clamp01(now), AccentTex, BaseContent.ClearTex, false);

            string text = "RPC_UI_Capacity".Translate(tracker.CapacityUsed.ToString("0"), max.ToString("0"), now.ToStringPercent());
            if (queued > 0f) text += "RPC_UI_CapacityQueued".Translate(after.ToStringPercent());
            Widgets.Label(new Rect(bar.xMax + 10f, bar.y - 2f, r.width - bar.width - 10f, 24f), text);
            TooltipHandler.TipRegion(bar, "RPC_Psychosis_Status".Translate(tracker.instability.ToString("0.#"), CyberpsychosisUtility.ChromeLoad(p).ToString("0.#")));
            if (!canOp)
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(r.xMax - 320f, r.y + 4f, 320f, 22f), "RPC_UI_ReadOnly".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            return r.height;
        }

        /// <summary>Portrait and limb slots. Returns the height used.</summary>
        private float DrawCenter(Rect r, Pawn p, List<Bill_Medical> bills, bool canOp)
        {
            float pw = Mathf.Min(r.width, 200f);
            var portrait = new Rect(r.center.x - pw / 2f, r.y, pw, 250f);
            Widgets.DrawBoxSolid(portrait, BoxBg);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.4f);
            Widgets.DrawBox(portrait);
            GUI.color = Color.white;
            if (!p.Dead) GUI.DrawTexture(portrait.ContractedBy(4f), PortraitsCache.Get(p, new Vector2(portrait.width - 8f, portrait.height - 8f), Rot4.South, default, 1.15f));

            // limb replacement: whole arm / leg (vanilla and modded bionic, archotech... limbs)
            float y = portrait.yMax + 12f;
            Text.Font = GameFont.Small;
            GUI.color = Accent;
            Widgets.Label(new Rect(r.x, y, r.width, LabelH), "RPC_UI_Limbs".Translate());
            GUI.color = Color.white;
            y += LabelH;
            var limbs = LimbParts(p);
            float x0 = r.center.x - (LimbW * 2f + Gap) / 2f;
            for (int i = 0; i < limbs.Count; i++)
            {
                var box = new Rect(x0 + (i % 2) * (LimbW + Gap), y + (i / 2) * (Box + LabelH + Gap), LimbW, Box);
                DrawLimb(box, p, limbs[i], bills, canOp);
            }
            y += ((limbs.Count + 1) / 2) * (Box + LabelH + Gap);
            y = DrawShards(r, y + 6f, p, bills, canOp);
            return y - r.y;
        }

        // ================================================================================================ neural shards

        private static bool SelfService(Pawn p) => p.Faction == Faction.OfPlayer && !p.IsPrisoner;

        /// <summary>
        /// Neural shards: two ports that open once basic cyberware (the comms implant) is in. Colonists slot and unslot their own shards
        /// directly (no surgery); prisoners and other patients get the forced surgeries as bills. Returns the new y.
        /// </summary>
        private float DrawShards(Rect r, float y, Pawn p, List<Bill_Medical> bills, bool canOp)
        {
            var slotted = ShardUtility.Slotted(p);
            bool port = ShardUtility.HasShardPort(p);
            var pending = bills.Where(b => b.recipe.Worker is Recipe_InstallShard).ToList();
            GUI.color = Accent;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, y, r.width, LabelH), "RPC_UI_Shards".Translate(slotted.Count, ShardUtility.MaxSlots));
            TooltipHandler.TipRegion(new Rect(r.x, y, r.width, LabelH), "RPC_UI_ShardsTip".Translate());
            GUI.color = Color.white;
            y += LabelH;
            float x0 = r.center.x - (LimbW * 2f + Gap) / 2f + (LimbW - Box) / 2f;
            for (int i = 0; i < ShardUtility.MaxSlots; i++)
            {
                var box = new Rect(x0 + i * (LimbW + Gap), y, Box, Box);
                if (i < slotted.Count)
                {
                    var shard = slotted[i];
                    var removal = bills.FirstOrDefault(b => b.recipe.Worker is Recipe_RemoveShard && b.recipe.GetModExtension<ShardRecipeExtension>()?.shard == shard);
                    string tip = shard.LabelCap + "\n\n" + shard.description;
                    DrawBox(box, shard.uiIcon, null, removal != null ? QueuedBorder : Accent, removal != null, removal != null ? "RPC_UI_RemovalQueued".Translate().ToString() : null, tip, () =>
                    {
                        if (!canOp) return;
                        if (removal != null) { Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("RPC_UI_Cancel".Translate(removal.LabelCap), () => p.BillStack.Delete(removal)) })); return; }
                        var opts = new List<FloatMenuOption>();
                        if (SelfService(p) && p.Spawned)
                            opts.Add(new FloatMenuOption("RPC_UI_ShardRemove".Translate(shard.LabelCap), () => ShardUtility.OrderRemove(p, shard)));
                        else
                        {
                            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_RemoveShard_" + shard.defName);
                            if (recipe != null) opts.Add(new FloatMenuOption("RPC_UI_ShardRemoveSurgery".Translate(shard.LabelCap), () => Queue(p, recipe, null, null)));
                        }
                        if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
                    });
                }
                else if (i - slotted.Count < pending.Count)
                {
                    var bill = pending[i - slotted.Count];
                    var shard = bill.recipe.GetModExtension<ShardRecipeExtension>()?.shard;
                    DrawBox(box, shard?.uiIcon, null, QueuedBorder, true, "RPC_UI_Queued".Translate(), "RPC_UI_QueuedTip".Translate(bill.LabelCap), () =>
                    {
                        if (canOp) Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("RPC_UI_Cancel".Translate(bill.LabelCap), () => p.BillStack.Delete(bill)) }));
                    });
                }
                else if (!port) DrawLocked(box, "RPC_UI_ShardNoPort".Translate());
                else DrawBox(box, null, null, EmptyBorder, false, null, "RPC_UI_ShardEmptyTip".Translate(),
                    canOp ? () => OpenShardMenu(p) : (Action)null, "+");
            }
            y += Box + 6f;

            // engrams are always an operation: the new person overwrites the old one
            if (canOp)
            {
                var engramRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_InsertEngram");
                var queued = bills.FirstOrDefault(b => b.recipe == engramRecipe);
                string label = queued != null ? "RPC_UI_EngramQueued".Translate().ToString() : "RPC_UI_EngramInsert".Translate().ToString();
                var btn = new Rect(r.center.x - 95f, y, 190f, 26f);
                if (Widgets.ButtonText(btn, label) && engramRecipe != null)
                {
                    if (queued != null) p.BillStack.Delete(queued);
                    else OpenEngramMenu(p, engramRecipe);
                }
                TooltipHandler.TipRegion(btn, "RPC_UI_EngramTip".Translate());
                y += 32f;
            }
            return y;
        }

        private static void OpenShardMenu(Pawn p)
        {
            var opts = new List<FloatMenuOption>();
            var stock = p.Map == null ? new List<Thing>() : ShardUtility.AllShards.SelectMany(d => p.Map.listerThings.ThingsOfDef(d))
                .Where(t => !t.IsForbidden(Faction.OfPlayer) && !ReservedByBill(t)).ToList();
            foreach (var group in stock.GroupBy(t => t.def).OrderBy(g => g.Key.label))
            {
                var def = group.Key;
                var thing = group.First();
                string name = def.LabelCap + " (" + "RPC_UI_InStock".Translate(group.Sum(t => t.stackCount)) + ")";
                if (!ShardUtility.CanSlot(p, def, out string reason))
                {
                    opts.Add(new FloatMenuOption(name + " - " + reason, null, def));
                    continue;
                }
                bool kills = def.ShardExt().soulkiller;
                if (SelfService(p) && p.Spawned)
                {
                    Action go = () => ShardUtility.OrderSlot(p, thing);
                    opts.Add(new FloatMenuOption(name, kills
                        ? () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RPC_UI_SoulkillerConfirm".Translate(p.LabelShortCap), go, true))
                        : go, def));
                }
                else
                {
                    var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_InsertShard_" + def.defName);
                    if (recipe != null) opts.Add(new FloatMenuOption(name + " - " + "RPC_UI_ShardSurgery".Translate(), () => Queue(p, recipe, null, thing), def));
                }
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("RPC_UI_NoShards".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void OpenEngramMenu(Pawn p, RecipeDef recipe)
        {
            var opts = new List<FloatMenuOption>();
            if (!EngramUtility.CanReceive(p, out string why)) opts.Add(new FloatMenuOption(why, null));
            else if (p.Map != null)
            {
                foreach (var t in p.Map.listerThings.AllThings.Where(t => t.TryGetComp<CompEngram>() != null && !t.IsForbidden(Faction.OfPlayer) && !ReservedByBill(t)))
                {
                    var thing = t;
                    opts.Add(new FloatMenuOption(thing.LabelCap, () => Queue(p, recipe, null, thing), thing.def));
                }
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("RPC_UI_NoEngrams".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // ================================================================================================ cyberware slots

        private void DrawSlotGroup(ref float y, float x, CyberSlotDef slot, Pawn p, List<Bill_Medical> bills, bool canOp)
        {
            var installed = p.InstalledCyberware().Where(c => c.Ext?.slot == slot).ToList();
            var pending = bills.Where(b => b.recipe.Worker is Recipe_InstallCyberware && b.recipe.addsHediff.Ext()?.slot == slot).ToList();
            GUI.color = Accent;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, y, ColW, LabelH), "RPC_UI_SlotLabel".Translate(slot.LabelCap, installed.Count + pending.Count, slot.maxInstalled));
            GUI.color = Color.white;
            if (!slot.description.NullOrEmpty()) TooltipHandler.TipRegion(new Rect(x, y, ColW, LabelH), slot.description);
            y += LabelH;
            float bx = x;
            bool full = installed.Count + pending.Count >= slot.maxInstalled;

            if (slot.onePerPart && !slot.WholeBody)
            {
                // one box per body part (left arm / right arm, each eye, each hand, each leg)
                foreach (var part in SlotParts(p, slot))
                {
                    var box = new Rect(bx, y, Box, Box);
                    // under a bionic limb the arm / hand is "missing" in vanilla terms: its cyberware sits on the limb's part
                    var target = CyberSurgeryUtility.EffectivePart(p, part);
                    var c = target == null ? null : installed.FirstOrDefault(h => h.Part == target);
                    var bill = target == null ? null : pending.FirstOrDefault(b => b.Part == target);
                    if (c != null) DrawInstalled(box, p, c, bills, canOp);
                    else if (bill != null) DrawQueued(box, p, bill, canOp);
                    else if (target == null) DrawLocked(box, "RPC_UI_PartMissing".Translate(part.LabelCap));
                    else if (full) DrawLocked(box, "RPC_UI_SlotFull".Translate(slot.label, slot.maxInstalled));
                    else DrawEmpty(box, p, slot, target, part.LabelCap, installed, pending, canOp);
                    bx += Box + Gap;
                }
            }
            else
            {
                // pooled: maxInstalled boxes (brain systems use the brain, whole-body systems no part)
                var part = slot.WholeBody ? null : CyberSurgeryUtility.CandidateParts(p, slot).FirstOrDefault();
                for (int i = 0; i < slot.maxInstalled; i++)
                {
                    var box = new Rect(bx, y, Box, Box);
                    if (i < installed.Count) DrawInstalled(box, p, installed[i], bills, canOp);
                    else if (i - installed.Count < pending.Count) DrawQueued(box, p, pending[i - installed.Count], canOp);
                    else if (!slot.WholeBody && part == null) DrawLocked(box, "RPC_UI_NoPart".Translate());
                    else DrawEmpty(box, p, slot, part, null, installed, pending, canOp);
                    bx += Box + Gap;
                }
            }
            y += Box + GroupGap;
        }

        private static IEnumerable<BodyPartRecord> SlotParts(Pawn p, CyberSlotDef slot) =>
            p.RaceProps.body.AllParts.Where(pt => slot.bodyParts.Contains(pt.def));

        private void DrawInstalled(Rect box, Pawn p, Hediff_Cyberware c, List<Bill_Medical> bills, bool canOp)
        {
            var ext = c.Ext;
            var change = bills.FirstOrDefault(b => b.Part == c.Part && (b.recipe.removesHediff == c.def || b.recipe.GetModExtension<UpgradeRecipeExtension>()?.cyberware == c.def));
            string overlay = change == null ? null : (change.recipe.removesHediff == c.def ? "RPC_UI_RemovalQueued" : "RPC_UI_UpgradeQueued").Translate().ToString();
            string tip = $"{c.def.LabelCap} ({c.tier.Label()})" + (c.Part != null ? $" - {c.Part.LabelCap}" : "") + "\n\n" + c.def.description
                         + "\n\n" + "RPC_UI_CostTip".Translate((ext?.capacityCost ?? 0f).ToString("0.#"));
            if (change != null) tip += "\n\n" + "RPC_UI_ChangeTip".Translate(change.LabelCap);
            DrawBox(box, ext?.item?.uiIcon, TierShort(c.tier), change != null ? QueuedBorder : TierColor(c.tier), change != null, overlay, tip, () =>
            {
                if (!canOp) return;
                var opts = new List<FloatMenuOption>();
                if (change != null)
                {
                    opts.Add(new FloatMenuOption("RPC_UI_Cancel".Translate(change.LabelCap), () => p.BillStack.Delete(change)));
                    Find.WindowStack.Add(new FloatMenu(opts));
                    return;
                }
                var remove = DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_Remove_" + c.def.defName);
                if (remove != null) opts.Add(new FloatMenuOption("RPC_UI_Remove".Translate(c.LabelCap), () => Queue(p, remove, c.Part, null)));
                if (ext != null && c.tier < ext.maxTier)
                {
                    var up = DefDatabase<RecipeDef>.GetNamedSilentFail("RPC_Upgrade_" + c.def.defName + "_" + (c.tier + 1));
                    if (up != null)
                    {
                        string label = "RPC_UI_UpgradeTo".Translate((c.tier + 1).Label()) + " (" + string.Join(", ", up.ingredients.Select(i => i.Summary)) + ")";
                        opts.Add(up.AvailableNow
                            ? new FloatMenuOption(label, () => Queue(p, up, c.Part, null))
                            : new FloatMenuOption(label + " - " + "RPC_UI_NeedsResearch".Translate(up.researchPrerequisite?.LabelCap ?? "?"), null));
                    }
                }
                if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
            });
        }

        private void DrawQueued(Rect box, Pawn p, Bill_Medical bill, bool canOp)
        {
            var ext = bill.recipe.addsHediff.Ext();
            var item = bill.uniqueRequiredIngredients?.FirstOrDefault();
            var tier = item?.TryGetComp<CompCyberwareTier>()?.tier;
            string tip = "RPC_UI_QueuedTip".Translate(bill.LabelCap) + (bill.Part != null ? $" ({bill.Part.LabelCap})" : "");
            DrawBox(box, ext?.item?.uiIcon, tier.HasValue ? TierShort(tier.Value) : null, QueuedBorder, true, "RPC_UI_Queued".Translate(), tip, () =>
            {
                if (canOp) Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("RPC_UI_Cancel".Translate(bill.LabelCap), () => p.BillStack.Delete(bill)) }));
            });
        }

        private void DrawLocked(Rect box, string tip) => DrawBox(box, null, null, EmptyBorder * 0.6f, true, null, tip, null, "×");

        private void DrawEmpty(Rect box, Pawn p, CyberSlotDef slot, BodyPartRecord part, string partLabel, List<Hediff_Cyberware> installed, List<Bill_Medical> pending, bool canOp)
        {
            string tip = "RPC_UI_EmptyTip".Translate(slot.label) + (partLabel != null ? $" ({partLabel})" : "");
            DrawBox(box, null, null, EmptyBorder, false, null, tip, canOp ? () => OpenInstallMenu(p, slot, part, installed, pending) : (Action)null, "+");
        }

        /// <summary>Every cyberware for this slot: one option per tier in stock (that exact item is reserved for the bill), or "none in stock" to queue and wait.</summary>
        private static void OpenInstallMenu(Pawn p, CyberSlotDef slot, BodyPartRecord part, List<Hediff_Cyberware> installed, List<Bill_Medical> pending)
        {
            var opts = new List<FloatMenuOption>();
            var recipes = p.def.AllRecipes.Where(r => r.Worker is Recipe_InstallCyberware && r.addsHediff.Ext()?.slot == slot).OrderBy(r => r.addsHediff.label);
            foreach (var recipe in recipes)
            {
                var def = recipe.addsHediff;
                var ext = def.Ext();
                string name = def.LabelCap;
                if (!recipe.AvailableNow)
                {
                    opts.Add(new FloatMenuOption(name + " - " + "RPC_UI_NeedsResearch".Translate(recipe.researchPrerequisite?.LabelCap ?? "?"), null));
                    continue;
                }
                if (!CyberUtility.CanInstall(p, def, null, out string reason))
                {
                    opts.Add(new FloatMenuOption(name + " - " + reason, null));
                    continue;
                }
                if ((slot.WholeBody || !slot.onePerPart) && pending.Any(b => b.recipe.addsHediff == def))
                {
                    opts.Add(new FloatMenuOption(name + " - " + "RPC_UI_AlreadyQueued".Translate(), null));
                    continue;
                }
                if (slot.allMustMatch && (installed.Any(c => c.def != def) || pending.Any(b => b.recipe.addsHediff != def)))
                {
                    var other = installed.Select(c => c.def).Concat(pending.Select(b => b.recipe.addsHediff)).First(d => d != def);
                    opts.Add(new FloatMenuOption(name + " - " + "RPC_Reason_MustMatch".Translate(slot.label, other.label), null));
                    continue;
                }
                if (recipe.targetsBodyPart && (part == null || !recipe.Worker.GetPartsToApplyOn(p, recipe).Contains(part)))
                {
                    opts.Add(new FloatMenuOption(name + " - " + "RPC_UI_NoPart".Translate(), null));
                    continue;
                }
                string usage = CyberSurgeryUtility.UsageSuffix(p, ext?.capacityCost ?? 0f);
                var stock = p.Map.listerThings.ThingsOfDef(ext.item).Where(t => !t.IsForbidden(Faction.OfPlayer) && !ReservedByBill(t))
                    .GroupBy(t => t.TryGetComp<CompCyberwareTier>()?.tier ?? ext.minTier).OrderByDescending(g => g.Key).ToList();
                var bp = recipe.targetsBodyPart ? part : null;
                if (stock.Count == 0)
                    opts.Add(new FloatMenuOption(name + usage, () => Queue(p, recipe, bp, null), ext.item));
                foreach (var g in stock)
                {
                    var item = g.First();
                    opts.Add(new FloatMenuOption($"{name} ({g.Key.Label()}, {"RPC_UI_InStock".Translate(g.Count())})" + usage, () => Queue(p, recipe, bp, item), ext.item));
                }
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("RPC_UI_NoOptions".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        /// <summary>Another queued surgery already reserved this exact item.</summary>
        private static bool ReservedByBill(Thing t)
        {
            foreach (var pawn in t.Map.mapPawns.AllPawnsSpawned)
            {
                var stack = pawn.BillStack;
                if (stack == null || stack.Count == 0) continue;
                foreach (var b in stack.Bills)
                    if (b is Bill_Medical m && m.uniqueRequiredIngredients != null && m.uniqueRequiredIngredients.Contains(t)) return true;
            }
            return false;
        }

        private static void Queue(Pawn p, RecipeDef recipe, BodyPartRecord part, Thing item) =>
            HealthCardUtility.CreateSurgeryBill(p, recipe, part, item != null ? new List<Thing> { item } : null);

        // ================================================================================================ limbs

        /// <summary>Whole-limb parts: shoulders (vanilla bionic / archotech arms attach there) and legs.</summary>
        private static List<BodyPartRecord> LimbParts(Pawn p) =>
            p.RaceProps.body.AllParts.Where(pt => pt.def.defName == "Shoulder" || pt.def == BodyPartDefOf.Leg).ToList();

        private static string LimbLabel(BodyPartRecord part)
        {
            // "left shoulder" reads as "left arm": the arm hangs off it
            var arm = part.def.defName == "Shoulder" ? part.parts.FirstOrDefault(c => c.def == BodyPartDefOf.Arm) : null;
            return (arm ?? part).LabelCap;
        }

        private static bool IsLimbRecipe(RecipeDef r) =>
            r.addsHediff != null && typeof(Hediff_AddedPart).IsAssignableFrom(r.addsHediff.hediffClass) && !(r.Worker is Recipe_InstallCyberware);

        private void DrawLimb(Rect box, Pawn p, BodyPartRecord part, List<Bill_Medical> bills, bool canOp)
        {
            var hs = p.health.hediffSet;
            var added = hs.hediffs.OfType<Hediff_AddedPart>().FirstOrDefault(h => h.Part == part);
            bool missing = added == null && hs.PartIsMissing(part);
            var bill = bills.FirstOrDefault(b => b.Part == part && (IsLimbRecipe(b.recipe) || b.recipe.Worker is Recipe_RemoveBodyPart));
            string state = added != null ? added.LabelBaseCap : (missing ? "RPC_UI_Missing".Translate() : "RPC_UI_Natural".Translate()).ToString();
            Texture2D icon = (bill != null ? bill.recipe.addsHediff?.spawnThingOnRemoved : added?.def.spawnThingOnRemoved)?.uiIcon;
            string tip = $"{LimbLabel(part)}: {state}";
            if (bill != null) tip += "\n\n" + "RPC_UI_QueuedTip".Translate(bill.LabelCap) + "\n" + "RPC_UI_ClickCancel".Translate();
            var border = bill != null ? QueuedBorder : added != null ? Accent : missing ? new Color(0.8f, 0.3f, 0.3f) : EmptyBorder;
            DrawBox(box, icon, null, border, bill != null, bill != null ? "RPC_UI_Queued".Translate().ToString() : null, tip, canOp ? () =>
            {
                if (bill != null)
                {
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("RPC_UI_Cancel".Translate(bill.LabelCap), () => p.BillStack.Delete(bill)) }));
                    return;
                }
                OpenLimbMenu(p, part, added);
            } : (Action)null, icon == null ? state : null);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(box.x - 6f, box.yMax + 1f, box.width + 12f, LabelH), LimbLabel(part));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void OpenLimbMenu(Pawn p, BodyPartRecord part, Hediff_AddedPart added)
        {
            var opts = new List<FloatMenuOption>();
            // replacing the limb drops cyberware on it (Patch_RestorePart_DropCyberware): say so
            bool chromed = p.InstalledCyberware().Any(c => c.Part != null && IsUnder(c.Part, part));
            string warn = chromed ? " - " + "RPC_UI_LimbWarn".Translate() : "";
            foreach (var r in p.def.AllRecipes.Where(IsLimbRecipe).OrderBy(r => r.label))
            {
                if (!r.AvailableNow || added?.def == r.addsHediff) continue;
                if (!r.Worker.GetPartsToApplyOn(p, r).Contains(part)) continue;
                var item = r.addsHediff.spawnThingOnRemoved;
                int stock = item == null ? 0 : p.Map.listerThings.ThingsOfDef(item).Count(t => !t.IsForbidden(Faction.OfPlayer));
                string label = r.LabelCap + (stock > 0 ? " (" + "RPC_UI_InStock".Translate(stock).ToString() + ")" : "") + warn;
                opts.Add(new FloatMenuOption(label, () => Queue(p, r, part, null), item));
            }
            if (added != null)
            {
                var remove = p.def.AllRecipes.FirstOrDefault(r => r.Worker is Recipe_RemoveBodyPart && r.AvailableNow && r.Worker.GetPartsToApplyOn(p, r).Contains(part));
                if (remove != null) opts.Add(new FloatMenuOption("RPC_UI_Remove".Translate(added.LabelBaseCap) + warn, () => Queue(p, remove, part, null)));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("RPC_UI_NoOptions".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static bool IsUnder(BodyPartRecord child, BodyPartRecord ancestor)
        {
            for (var x = child; x != null; x = x.parent)
                if (x == ancestor) return true;
            return false;
        }

        // ================================================================================================ drawing

        private static void DrawBox(Rect r, Texture2D icon, string corner, Color border, bool greyed, string overlay, string tip, Action onClick, string centerText = null)
        {
            Widgets.DrawBoxSolid(r, BoxBg);
            if (icon != null)
            {
                GUI.color = greyed ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
                GUI.DrawTexture(r.ContractedBy(5f), icon, ScaleMode.ScaleToFit);
            }
            else if (centerText != null)
            {
                GUI.color = greyed ? Color.gray : new Color(0.6f, 0.7f, 0.75f);
                Text.Font = centerText.Length > 2 ? GameFont.Tiny : GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, centerText);
            }
            GUI.color = border;
            Widgets.DrawBox(r, overlay != null || !greyed ? 2 : 1);
            Text.Font = GameFont.Tiny;
            if (corner != null)
            {
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = border;
                Widgets.Label(new Rect(r.x, r.y, r.width - 3f, r.height - 1f), corner);
            }
            if (overlay != null)
            {
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = QueuedBorder;
                Widgets.Label(new Rect(r.x - 4f, r.y + 1f, r.width + 8f, 16f), overlay);
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Widgets.DrawHighlightIfMouseover(r);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            if (onClick != null && Widgets.ButtonInvisible(r)) onClick();
        }

        private static string TierShort(CyberTier t) => t == CyberTier.Iconic ? "IC" : t.ToString();

        /// <summary>Cyberpunk 2077 rarity colours: common, uncommon, rare, epic, legendary, iconic.</summary>
        private static Color TierColor(CyberTier t) => t switch
        {
            CyberTier.T1 => new Color(0.75f, 0.75f, 0.75f),
            CyberTier.T2 => new Color(0.35f, 0.85f, 0.35f),
            CyberTier.T3 => new Color(0.3f, 0.6f, 1f),
            CyberTier.T4 => new Color(0.7f, 0.4f, 1f),
            CyberTier.T5 => new Color(1f, 0.75f, 0.2f),
            _ => new Color(1f, 0.3f, 0.35f),
        };
    }
}
