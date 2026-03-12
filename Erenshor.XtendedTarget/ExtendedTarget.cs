using System.Collections.Generic;
using UnityEngine;

namespace Erenshor.XTarget
{

    internal class XTargetSlot
    {
        public NPC    Source;
        public string Name;
        public float  HpPct;
        public bool   TargetingPlayer;
        public string TargetName;
        public int    PlayerHateRank;
    }

    internal static class ExtendedTarget
    {
        internal static readonly List<XTargetSlot> Slots = new List<XTargetSlot>();

        private static readonly List<AggroSlot>  _sortScratch    = new List<AggroSlot>();
        private static readonly HashSet<Character> _groupMembers  = new HashSet<Character>();

        internal static void BuildSlotList()
        {
            Slots.Clear();

            if (GameData.PlayerControl == null || GameData.PlayerStats == null)
                return;

            int       max    = Mathf.Clamp(XTargetPlugin.MaxSlots.Value, 1, 20);
            Character player = GameData.PlayerControl.Myself;

            BuildGroupMemberSet();

            foreach (NPC npc in NPCTable.LiveNPCs)
            {
                if (Slots.Count >= max) break;
                if (!IsRelevant(npc, player)) continue;
                Slots.Add(BuildSlot(npc, player));
            }
        }

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