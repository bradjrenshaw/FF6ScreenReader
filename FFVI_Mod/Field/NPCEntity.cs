using System;
using System.Collections.Generic;
using Il2Cpp;
using FFVI_ScreenReader.Audio;
using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Field
{
    /// <summary>
    /// Represents an NPC entity
    /// </summary>
    public class NPCEntity : NavigableEntity
    {
        /// <summary>
        /// Asset name used by the game (e.g., "P002" for Locke)
        /// </summary>
        public string AssetName => GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyNpc>()?.AssetName ?? "";

        /// <summary>
        /// Whether this NPC is a shop
        /// </summary>
        public bool IsShop => GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyNpc>()?.ProductGroupId > 0;

        /// <summary>
        /// NPC movement behavior
        /// </summary>
        public FieldEntityConstants.MoveType MovementType =>
            GameEntity?.Property?.TryCast<Il2CppLast.Map.PropertyNpc>()?.MoveType ?? FieldEntityConstants.MoveType.None;

        /// <summary>
        /// Character name if this is a playable character NPC
        /// </summary>
        public string CharacterName => GetCharacterName(AssetName);

        public override EntityCategory Category => EntityCategory.NPCs;

        public override int Priority => 4;

        public override bool BlocksPathing => true;

        public override SonarInfo SonarInfo => SonarInfo.Continuous("npc.wav", 5f);

        /// <summary>
        /// Gets friendly character name from asset name.
        /// Checks P-codes for playable characters, then queries NPC master data.
        /// </summary>
        public static string GetCharacterName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;

            // Check P-codes for playable characters
            var characterMap = new Dictionary<string, string>
            {
                { "P001", "Terra" },
                { "P002", "Locke" },
                { "P003", "Cyan" },
                { "P004", "Shadow" },
                { "P005", "Edgar" },
                { "P006", "Sabin" },
                { "P007", "Celes" },
                { "P008", "Strago" },
                { "P009", "Relm" },
                { "P010", "Setzer" },
                { "P011", "Mog" },
                { "P012", "Gau" },
                { "P013", "Gogo" },
                { "P014", "Umaro" }
            };

            // Check if asset name contains a P-code
            foreach (var kvp in characterMap)
            {
                if (assetName.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            // Try NPC master data
            try
            {
                var npcTemplateList = Il2CppLast.Data.Master.Npc.templateList;
                if (npcTemplateList != null && npcTemplateList.Count > 0)
                {
                    foreach (var kvp in npcTemplateList)
                    {
                        if (kvp.Value == null) continue;

                        var npcData = kvp.Value.TryCast<Il2CppLast.Data.Master.Npc>();
                        if (npcData != null &&
                            !string.IsNullOrEmpty(npcData.AssetName) &&
                            npcData.AssetName == assetName)
                        {
                            if (!string.IsNullOrEmpty(npcData.NpcName))
                            {
                                return npcData.NpcName;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Master data not available yet
            }

            return null;
        }

        protected override string GetDisplayName()
        {
            var details = new List<string>();

            // Add character name if available (recalculate from asset name if not set)
            string characterName = CharacterName;
            if (string.IsNullOrEmpty(characterName) && !string.IsNullOrEmpty(AssetName))
            {
                characterName = GetCharacterName(AssetName);
            }

            if (!string.IsNullOrEmpty(characterName))
            {
                details.Add(characterName);
            }
            else if (!string.IsNullOrEmpty(AssetName) && AssetName != Name)
            {
                details.Add(AssetName);
            }

            // Add shop indicator
            if (IsShop)
            {
                details.Add("shop");
            }

            // Add movement type
            if (MovementType == FieldEntityConstants.MoveType.None)
            {
                details.Add("stationary");
            }
            else if (MovementType == FieldEntityConstants.MoveType.Stamp)
            {
                details.Add("wandering");
            }
            else if (MovementType == FieldEntityConstants.MoveType.Area ||
                     MovementType == FieldEntityConstants.MoveType.Route)
            {
                details.Add("patrolling");
            }

            string detailStr = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";
            return $"{Name}{detailStr}";
        }

        protected override string GetEntityTypeName()
        {
            return "NPC";
        }
    }
}
