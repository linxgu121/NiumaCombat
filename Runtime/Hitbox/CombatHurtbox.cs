using System;
using System.Collections.Generic;
using UnityEngine;

namespace NiumaCombat.Hitbox
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CombatHurtbox : MonoBehaviour
    {
        [Header("归属")]
        [Tooltip("所属角色身份。通常拖同一角色根节点上的 CombatActorIdentity；留空时 Awake/OnValidate 会向父节点自动查找。")]
        [SerializeField] private CombatActorIdentity owner;

        [Header("受击部位")]
        [Tooltip("受击盒 ID。例：body、head、leg。命中结果会写入 CombatDamageRequest.HurtboxId。")]
        [SerializeField] private string hurtboxId = "body";

        [Tooltip("部位 ID。用于显示和后续表现层区分部位；第一版主要供策划识别。")]
        [SerializeField] private string bodyPartId = "body";

        [Tooltip("部位伤害倍率。头部可填 2，腿部可填 0.8；最终会乘入 CombatDamageRequest.BodyPartMultiplier。")]
        [SerializeField] private float partDamageMultiplier = 1f;

        [Tooltip("部位标签。例：weakpoint、armored、shield。会和 CombatActorIdentity.ActorTags 合并后参与命中标签过滤。")]
        [SerializeField] private string[] hurtboxTags = Array.Empty<string>();

        private string[] _targetTagsSnapshot;
        private bool _targetTagsDirty = true;

        public CombatActorIdentity Owner => owner;
        public string ActorId => owner != null ? owner.ActorId : string.Empty;
        public string FactionId => owner != null ? owner.FactionId : string.Empty;
        public string HurtboxId => hurtboxId;
        public string BodyPartId => bodyPartId;
        public float PartDamageMultiplier => Mathf.Max(0f, partDamageMultiplier);
        public string[] HurtboxTags => hurtboxTags ?? Array.Empty<string>();

        public bool IsValid => owner != null
            && !string.IsNullOrWhiteSpace(owner.ActorId)
            && !string.IsNullOrWhiteSpace(hurtboxId);

        public string[] GetTargetTagsSnapshot()
        {
            if (!_targetTagsDirty && _targetTagsSnapshot != null)
            {
                return _targetTagsSnapshot;
            }

            var merged = new List<string>();
            AddTags(merged, owner != null ? owner.ActorTags : null);
            AddTags(merged, hurtboxTags);
            _targetTagsSnapshot = merged.Count > 0 ? merged.ToArray() : Array.Empty<string>();
            _targetTagsDirty = false;
            return _targetTagsSnapshot;
        }

        private void Reset()
        {
            AutoResolveOwner();
        }

        private void Awake()
        {
            AutoResolveOwner();
        }

        private void OnValidate()
        {
            AutoResolveOwner();
            hurtboxId = string.IsNullOrWhiteSpace(hurtboxId) ? "body" : hurtboxId.Trim();
            bodyPartId = string.IsNullOrWhiteSpace(bodyPartId) ? hurtboxId : bodyPartId.Trim();
            partDamageMultiplier = Mathf.Max(0f, partDamageMultiplier);
            _targetTagsDirty = true;
            WarnIfColliderIsNotTrigger();
        }

        private void AutoResolveOwner()
        {
            if (owner == null)
            {
                var resolvedOwner = GetComponentInParent<CombatActorIdentity>();
                if (owner != resolvedOwner)
                {
                    owner = resolvedOwner;
                    _targetTagsDirty = true;
                }
            }
        }

        private void WarnIfColliderIsNotTrigger()
        {
            var hurtboxCollider = GetComponent<Collider>();
            if (hurtboxCollider != null && !hurtboxCollider.isTrigger)
            {
                Debug.LogWarning("[CombatHurtbox] 建议把同物体 Collider 勾选 Is Trigger。否则 CombatHitboxDriver 的 Trigger 检测可能无法稳定发现该 Hurtbox。", this);
            }
        }

        private static void AddTags(List<string> output, string[] tags)
        {
            if (output == null || tags == null)
            {
                return;
            }

            for (var i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                tag = tag.Trim();
                var exists = false;
                for (var j = 0; j < output.Count; j++)
                {
                    if (string.Equals(output[j], tag, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    output.Add(tag);
                }
            }
        }
    }
}
