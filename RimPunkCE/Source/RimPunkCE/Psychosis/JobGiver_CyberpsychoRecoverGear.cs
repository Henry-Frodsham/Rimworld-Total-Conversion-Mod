using RimWorld;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>
    /// A cyberpsycho back on its feet after being downed grabs its weapon, then the inventory (CE ammo etc.) it
    /// dropped (recorded by MentalState_Cyberpsychosis.Notify_GearDropped). Inventory is matched by def near the
    /// drop spot because dropped stacks can merge with others on the ground. Forbidden is ignored; nothing further
    /// than MaxDistance is fetched, and whatever is gone (looted, burnt) is forgotten.
    /// </summary>
    public class JobGiver_CyberpsychoRecoverGear : ThinkNode_JobGiver
    {
        private const float MaxDistance = 12f;
        private const float SearchRadius = 4f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn.MentalState is MentalState_Cyberpsychosis psycho) || pawn.Downed || pawn.Map == null) return null;

            var weapon = psycho.droppedWeapon;
            if (weapon != null)
            {
                if (pawn.equipment?.Primary == null && weapon.Spawned && weapon.Map == pawn.Map && !weapon.Destroyed
                    && weapon.Position.InHorDistOf(pawn.Position, MaxDistance))
                {
                    var job = JobGiver_PickupDroppedWeapon.PickupWeaponJob(pawn, weapon, ignoreForbidden: true);
                    if (job != null)
                    {
                        psycho.droppedWeapon = null; // one attempt: if it fails (stolen mid-way) don't loop
                        if (pawn.mindState != null) pawn.mindState.droppedWeapon = null;
                        Diag.Event("psychosis", $"{Diag.Describe(pawn)} recovering weapon {weapon.def.defName}");
                        return job;
                    }
                }
                psycho.droppedWeapon = null;
            }

            var list = psycho.droppedInventory;
            if (list.Count == 0) return null;
            var center = psycho.droppedAt.IsValid ? psycho.droppedAt : pawn.Position;
            if (!center.InHorDistOf(pawn.Position, MaxDistance))
            {
                list.Clear();
                return null;
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];
                var thing = GenClosest.ClosestThingReachable(center, pawn.Map, ThingRequest.ForDef(entry.thingDef), PathEndMode.Touch,
                    TraverseParms.For(pawn, Danger.Deadly), SearchRadius,
                    t => t.Spawned && !t.IsBurning() && pawn.CanReserve(t));
                list.RemoveAt(i);
                if (thing == null) continue;
                int count = System.Math.Min(entry.count, thing.stackCount);
                if (count < entry.count) list.Add(new ThingDefCountClass(entry.thingDef, entry.count - count)); // rest may be in another stack
                var job = JobMaker.MakeJob(JobDefOf.TakeInventory, thing);
                job.count = count;
                job.ignoreForbidden = true;
                Diag.Event("psychosis", $"{Diag.Describe(pawn)} recovering {entry.thingDef.defName} x{count}");
                return job;
            }
            return null;
        }
    }
}
