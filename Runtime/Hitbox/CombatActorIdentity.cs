using System;
using UnityEngine;

namespace NiumaCombat.Hitbox
{
    [DisallowMultipleComponent]
    public sealed class CombatActorIdentity : MonoBehaviour
    {
        [Header("角色身份")]
        [Tooltip("战斗 ActorId。建议填写角色运行时唯一 ID；Hurtbox 会从这里读取目标身份。")]
        [SerializeField] private string actorId;

        [Tooltip("阵营 ID。第一版 CombatService 不直接读取该字段，正式阵营过滤请由 ICombatFactionResolver 消费它或其它角色状态后返回关系。")]
        [SerializeField] private string factionId;

        [Tooltip("目标标签。例：player、enemy、boss、flying。CombatHitboxDriver 会合并这些标签，用于 RequiredTargetTags / RejectedTargetTags 过滤。")]
        [SerializeField] private string[] actorTags = Array.Empty<string>();

        public string ActorId => actorId;
        public string FactionId => factionId;
        public string[] ActorTags => actorTags ?? Array.Empty<string>();

        public void SetActorId(string value)
        {
            actorId = value;
        }

        public void SetFactionId(string value)
        {
            factionId = value;
        }

        public string[] GetTagsSnapshot()
        {
            return actorTags != null ? (string[])actorTags.Clone() : Array.Empty<string>();
        }

        private void OnValidate()
        {
            actorId = actorId != null ? actorId.Trim() : string.Empty;
            factionId = factionId != null ? factionId.Trim() : string.Empty;
        }
    }
}
