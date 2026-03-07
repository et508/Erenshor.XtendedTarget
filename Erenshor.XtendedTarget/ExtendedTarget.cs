using System.Collections.Generic;
using UnityEngine;

namespace Erenshor.XTarget
{
    // ─────────────────────────────────────────────────────────────────────────
    // XTargetSlot
    // Data snapshot for one row — built each frame by ExtendedTarget.BuildSlotList()
    // so the UI never needs to touch game objects directly.
    // ─────────────────────────────────────────────────────────────────────────
    internal class XTargetSlot
    {
        public NPC    Source;           // live reference used for click-to-target
        public string Name;
        public float  HpPct;            // 0–1
        public bool   TargetingPlayer;  // true = NPC's CurrentAggroTarget == player
        public string TargetName;       // name of who this NPC is currently attacking
        public int    PlayerHateRank;   // player's position on this NPC's hate table (1 = top, 0 = not on table)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ExtendedTarget
    // All game-logic: aggro scanning, hate ranking, targeting.
    // Called once per Update tick by XTargetUIController.
    //
    // SOURCE OF TRUTH: NPCTable.LiveNPCs + NPC.AggroTable
    //
    // Why not GameData.AttackingPlayer / GroupMatesInCombat?
    //   - AttackingPlayer removes an NPC the moment it switches target away from
    //     the player (NPC.cs ~line 6511). Mob taunted off you? Gone from the list.
    //   - GroupMatesInCombat removes when CurrentAggroTarget == null (leashing etc.)
    //   - Neither list filters out SimPlayer NPCs (group member AI).
    //
    // Instead we walk NPCTable.LiveNPCs each frame (~dozens of entries max) and
    // include any real (non-SimPlayer) NPC whose AggroTable contains the player
    // or a current group member. AggroTable entries persist until the character
    // dies or goes inactive, giving us stable, correct EQ-style tracking.
    // ─────────────────────────────────────────────────────────────────────────
    internal static class ExtendedTarget
    {
        internal static readonly List<XTargetSlot> Slots = new List<XTargetSlot>();

        // Scratch collections — reused each frame to avoid GC alloc
        private static readonly List<AggroSlot>  _sortScratch    = new List<AggroSlot>();
        private static readonly HashSet<Character> _groupMembers  = new HashSet<Character>();

        // ─────────────────────────────────────────────────────────────────────
        // BuildSlotList — call once per Update from the UI controller
        // ─────────────────────────────────────────────────────────────────────
        internal static void BuildSlotList()
        {
            Slots.Clear();

            if (GameData.PlayerControl == null || GameData.PlayerStats == null)
                return;

            int       max    = Mathf.Clamp(XTargetPlugin.MaxSlots.Value, 1, 20);
            Character player = GameData.PlayerControl.Myself;

            // Build a quick set of group member Characters for O(1) lookup
            BuildGroupMemberSet();

            foreach (NPC npc in NPCTable.LiveNPCs)
            {
                if (Slots.Count >= max) break;
                if (!IsRelevant(npc, player)) continue;
                Slots.Add(BuildSlot(npc, player));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // TargetSlot — called when the player clicks a row
        // Mirrors the exact pattern used by PlayerControl tab-targeting.
        // ─────────────────────────────────────────────────────────────────────
        internal static void TargetSlot(XTargetSlot slot)
        {
            if (slot?.Source == null) return;

            Character ch = slot.Source.GetChar();
            if (ch == null || !ch.Alive) return;

            Character current = GameData.PlayerControl.CurrentTarget;
            if (current != null)
                current.UntargetMe();

            GameData.PlayerControl.CurrentTarget = ch;
            ch.TargetMe();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        // True if this NPC should appear in the window:
        //   - Not a SimPlayer (AI group member)
        //   - Alive and active
        //   - Has the player or a group member on its AggroTable
        private static bool IsRelevant(NPC npc, Character player)
        {
            if (npc == null || npc.SimPlayer) return false;

            Character ch = npc.GetChar();
            if (ch == null || !ch.Alive || !ch.gameObject.activeSelf) return false;

            if (npc.AggroTable == null || npc.AggroTable.Count == 0) return false;

            foreach (AggroSlot slot in npc.AggroTable)
            {
                if (slot.Player == null) continue;
                if (slot.Player == player)          return true;
                if (_groupMembers.Contains(slot.Player)) return true;
            }

            return false;
        }

        private static XTargetSlot BuildSlot(NPC npc, Character player)
        {
            Character ch      = npc.GetChar();
            Stats     st      = ch.MyStats;
            bool      onPlayer = (npc.CurrentAggroTarget == player);

            string targetName;
            if (npc.CurrentAggroTarget == null)
                targetName = "";
            else if (onPlayer)
                targetName = "YOU";
            else
                targetName = npc.CurrentAggroTarget.MyStats.MyName;

            return new XTargetSlot
            {
                Source          = npc,
                Name            = st.MyName,
                HpPct           = Mathf.Clamp01((float)st.CurrentHP / Mathf.Max(1, st.CurrentMaxHP)),
                TargetingPlayer = onPlayer,
                TargetName      = targetName,
                PlayerHateRank  = GetPlayerHateRank(npc, player),
            };
        }

        // Populate _groupMembers with the Character of each current group member
        private static void BuildGroupMemberSet()
        {
            _groupMembers.Clear();
            SimPlayerTracking[] members = GameData.GroupMembers;
            if (members == null) return;

            for (int i = 0; i < members.Length; i++)
            {
                SimPlayerTracking spt = members[i];
                if (spt?.MyStats?.Myself == null) continue;
                Character ch = spt.MyStats.Myself;
                if (ch.Alive)
                    _groupMembers.Add(ch);
            }
        }

        // Returns the player's position in this NPC's hate table (1 = highest).
        // Returns 0 if the player isn't on the table.
        private static int GetPlayerHateRank(NPC npc, Character player)
        {
            if (player == null || npc.AggroTable == null || npc.AggroTable.Count == 0)
                return 0;

            _sortScratch.Clear();
            _sortScratch.AddRange(npc.AggroTable);
            _sortScratch.Sort((a, b) => b.Hate.CompareTo(a.Hate));

            for (int i = 0; i < _sortScratch.Count; i++)
            {
                if (_sortScratch[i].Player == player)
                    return i + 1;
            }
            return 0;
        }
    }
}