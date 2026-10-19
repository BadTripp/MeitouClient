using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;

namespace Meitou.Simulation.Dialogue;

public sealed partial class DialogueSystem
{
    /// <summary>
    /// DA_JOIN_SQUAD_WITH_EDIT (3) / DA_JOIN_SQUAD_FAST (18) with the player character as the target (docs/game/dialogue.md "Recruiting"): the speaker leaves its squad and joins the
    /// player's active squad (the selection's, else the player's squad, else the talker's) as a player character; when that squad already holds CONSTANTS <c>max squad size</c>
    /// members, a new squad of the "blank squad" template takes it. At the faction's head cap (<c>max faction size</c>) the conversation ends with "It looks like your squad is a
    /// little full." and the line's remaining effects are skipped (false). DA 3 then opens the character editor on the recruit (engine: the conversation ends for it).
    /// </summary>
    bool Recruit(World world, Conversation c, CharacterId me, bool withEditor)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(me, out int slot) || table.Cold(slot) is not { IsPlayer: false } mc || Cold(world, c.Player) is not { IsPlayer: true } tc) return true;
        var limits = data.Bodies.Constants;
        int heads = 0;
        for (int i = 0; i < table.Next.Length; i++)
            if (table.Next[i].Alive && table.Cold(i) is { IsPlayer: true, DiedTick: < 0 }) heads++;
        if (heads >= limits.MaxFactionSize)
        {
            world.Post("It looks like your squad is a little full.");
            c.Ended = true;
            return false;
        }
        var selected = world.Player.Selection.Select(id => Cold(world, id)).FirstOrDefault(s => s is { IsPlayer: true });
        if ((world.Squads.Find(selected?.SquadId ?? -1) ?? world.Squads.Find(world.Player.Squad) ?? world.Squads.Find(tc.SquadId)) is not { } squad) return true;
        if (squad.Members.Count >= limits.MaxSquadSize) squad = NewPlayerSquad(world, table.Next[slot].Position);

        if (world.Squads.Find(mc.SquadId) is { } old)
        {
            old.Members.Remove(me);
            if (old.Leader == me) old.Leader = old.Members.Count > 0 ? old.Members[0] : CharacterId.None;
        }
        mc.Faction = world.Player.Faction;
        mc.SquadId = squad.Id;
        mc.Role = (int)SquadRole.Squad1;
        mc.FormationOffset = Vector2.Zero;
        mc.IsPlayer = true;
        mc.NpcClass = FundamentalType.Adventurer;   // every player-faction character's class
        mc.Orders = Orders.PlayerDefaults;
        mc.Ai = null;
        mc.Engaged = false;
        mc.Fleeing = false;
        mc.Pending = null;
        mc.Jobs.Clear();   // an NPC's list, if any, does not carry over
        mc.Save = null;   // written as a new member of the player's platoon; the save's old slot of it is removed
        table.Next[slot].Mode = SpeedMode.Free;   // the original's speed mode 2
        MovementSystem.Stop(table, slot);
        squad.Members.Add(me);
        if (squad.Leader.IsNone) squad.Leader = me;

        if (withEditor)
        {
            c.Ended = true;
            request = new DialogueRequest(++requestId, DialogueRequestKind.RecruitEditor, me, me);
        }
        return true;
    }

    /// <summary>A new, empty squad of the player's faction at <paramref name="at"/>, of the SQUAD_TEMPLATE named "blank squad" (the original's fallback platoon).</summary>
    Squad NewPlayerSquad(World world, Vector3 at)
    {
        var template = data.Db.OfType(FcsRecordType.SQUAD_TEMPLATE).FirstOrDefault(r => r.Name == "blank squad");
        var squad = new Squad
        {
            Id = world.Squads.NextId(),
            TemplateId = template?.StringId ?? "blank squad",
            Faction = world.Player.Faction,
            Position = at,
            HomeCentre = new Vector2(at.X, at.Z),
            HomeRadius = 100,
        };
        world.Squads.Add(squad);
        return squad;
    }
}
