using System;
using System.Collections.Generic;
using NiumaCombat.Data;
using NiumaCombat.Enum;
using NiumaCombat.Service;
using UnityEngine;

namespace NiumaCombat.Hitbox
{
    [DisallowMultipleComponent]
    public sealed class CombatHitboxDriver : MonoBehaviour
    {
        [Header("服务来源")]
        [Tooltip("战斗服务提供者。通常拖核心场景 CombatRoot 上的 NiumaCombatController（它实现 ICombatRuntimeServiceProvider）；留空且开启自动查找时，会在场景中查找实现该接口的组件。")]
        [SerializeField] private MonoBehaviour combatServiceProvider;

        [Tooltip("找不到服务提供者时是否自动在场景中查找实现 ICombatRuntimeServiceProvider 的组件。核心场景常驻 NiumaCombatController 时建议开启。")]
        [SerializeField] private bool autoFindCombatProvider = true;

        [Header("攻击者身份")]
        [Tooltip("攻击者身份。推荐拖攻击者根节点上的 CombatActorIdentity；留空时会向父节点自动查找。")]
        [SerializeField] private CombatActorIdentity ownerIdentity;

        [Tooltip("调试用攻击者 ActorId。Owner Identity 为空时才使用；正式角色建议通过 CombatActorIdentity 提供。")]
        [SerializeField] private string ownerActorId;

        [Header("命中配置")]
        [Tooltip("命中盒定义。配置 HitboxId、伤害模板、ActiveSeconds、重复命中和标签过滤规则。")]
        [SerializeField] private CombatHitboxDefinition definition = new CombatHitboxDefinition();

        [Tooltip("用于产生命中的 Collider。通常是武器或攻击范围上的 Trigger Collider；留空时自动取当前物体 Collider。")]
        [SerializeField] private Collider hitboxCollider;

        [Tooltip("可命中的目标 Layer。只有命中对象所在 Layer 包含在这里时才会尝试读取 CombatHurtbox。")]
        [SerializeField] private LayerMask targetLayerMask = ~0;

        [Tooltip("是否使用 OnTriggerEnter / OnTriggerStay 自动提交命中。关闭后可由外部调用 SubmitHurtbox 手动提交。")]
        [SerializeField] private bool useTriggerCallbacks = true;

        [Tooltip("开启后 OnTriggerStay 也会提交命中。适合 Hitbox 打开时目标已经在范围内的情况；重复过滤仍由 CombatService 兜底。")]
        [SerializeField] private bool submitOnTriggerStay = true;

        [Tooltip("启用物体时自动打开 Hitbox，仅用于调试或持续陷阱。正式攻击建议由动画事件或 ActionBridge 调用 OpenHitbox。")]
        [SerializeField] private bool openOnEnable;

        [Tooltip("开发期输出缺失服务、缺失身份、缺失 Hurtbox 等警告。")]
        [SerializeField] private bool logWarnings = true;

        private ICombatCommand _combatCommand;
        private ICombatQuery _combatQuery;
        private ICombatHitboxService _hitboxService;
        private string _activeAttackInstanceId;
        private readonly Dictionary<CombatHurtbox, Collider> _pendingHurtboxes = new Dictionary<CombatHurtbox, Collider>();
        private const float AutoFindProviderRetrySeconds = 1f;
        private static ICombatRuntimeServiceProvider _cachedServiceProvider;
        private static float _nextGlobalProviderScanTime;

        public bool IsOpen => !string.IsNullOrWhiteSpace(_activeAttackInstanceId)
            && _hitboxService != null
            && _hitboxService.IsHitboxActive(_activeAttackInstanceId);

        public string ActiveAttackInstanceId => _activeAttackInstanceId;
        public CombatHitboxDefinition Definition => definition;

        public void SetCombatServices(ICombatCommand combatCommand, ICombatHitboxService hitboxService)
        {
            SetCombatServices(combatCommand, hitboxService, combatCommand as ICombatQuery);
        }

        public void SetCombatServices(ICombatCommand combatCommand, ICombatHitboxService hitboxService, ICombatQuery combatQuery)
        {
            _combatCommand = combatCommand;
            _hitboxService = hitboxService;
            _combatQuery = combatQuery;
        }

        public string OpenHitbox()
        {
            if (!ResolveServices())
            {
                Warn("无法打开 Hitbox：未找到 ICombatCommand、ICombatQuery 或 ICombatHitboxService。请在核心场景放置并初始化 NiumaCombatController，或调用 SetCombatServices。");
                return null;
            }

            var sourceActorId = ResolveOwnerActorId();
            if (string.IsNullOrWhiteSpace(sourceActorId))
            {
                Warn("无法打开 Hitbox：攻击者 ActorId 为空。请绑定 Owner Identity 或填写调试用 Owner Actor Id。");
                return null;
            }

            if (!IsSourceActorAlive(sourceActorId))
            {
                Warn($"无法打开 Hitbox：攻击者 {sourceActorId} 已死亡或无法确认存活。");
                return null;
            }

            if (definition == null)
            {
                Warn("无法打开 Hitbox：Hitbox Definition 为空。");
                return null;
            }

            WarnIfDamageTemplateHasRuntimeFields();

            if (!string.IsNullOrWhiteSpace(_activeAttackInstanceId))
            {
                CloseHitbox();
            }

            _activeAttackInstanceId = _hitboxService.OpenHitbox(definition, sourceActorId);
            if (string.IsNullOrWhiteSpace(_activeAttackInstanceId))
            {
                Warn("打开 Hitbox 失败：HitboxService 返回空 AttackInstanceId。请检查 Definition 和 Owner ActorId。");
            }

            return _activeAttackInstanceId;
        }

        public void CloseHitbox()
        {
            if (string.IsNullOrWhiteSpace(_activeAttackInstanceId))
            {
                return;
            }

            ResolveServices();
            _hitboxService?.CloseHitbox(_activeAttackInstanceId);
            _activeAttackInstanceId = null;
        }

        public CombatResult SubmitHurtbox(CombatHurtbox hurtbox)
        {
            if (hurtbox == null)
            {
                return BuildDriverFailure(CombatFailureReason.InvalidRequest, "无法提交命中：Hurtbox 为空。", null);
            }

            return SubmitHurtbox(hurtbox, null);
        }

        private void Reset()
        {
            AutoResolveLocalReferences();
        }

        private void Awake()
        {
            AutoResolveLocalReferences();
            ResolveServices();
        }

        private void OnEnable()
        {
            if (openOnEnable)
            {
                OpenHitbox();
            }
        }

        private void OnDisable()
        {
            _pendingHurtboxes.Clear();
            CloseHitbox();
        }

        private void OnValidate()
        {
            AutoResolveLocalReferences();
            ownerActorId = ownerActorId != null ? ownerActorId.Trim() : string.Empty;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (useTriggerCallbacks)
            {
                QueueCollider(other);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (useTriggerCallbacks && submitOnTriggerStay)
            {
                QueueCollider(other);
            }
        }

        private void FixedUpdate()
        {
            if (_pendingHurtboxes.Count == 0)
            {
                return;
            }

            foreach (var pair in _pendingHurtboxes)
            {
                SubmitHurtbox(pair.Key, pair.Value);
            }

            _pendingHurtboxes.Clear();
        }

        private CombatResult SubmitHurtbox(CombatHurtbox hurtbox, Collider hitCollider)
        {
            if (!IsOpen)
            {
                return BuildDriverFailure(CombatFailureReason.HitboxNotActive, "Hitbox 未激活，无法提交命中。", hurtbox);
            }

            if (!ResolveServices())
            {
                return BuildDriverFailure(CombatFailureReason.InternalError, "无法提交命中：Combat 服务未解析。", hurtbox);
            }

            if (hurtbox == null || !hurtbox.isActiveAndEnabled)
            {
                return BuildDriverFailure(CombatFailureReason.InvalidRequest, "无法提交命中：Hurtbox 为空或未启用。", hurtbox);
            }

            if (!hurtbox.IsValid)
            {
                return BuildDriverFailure(CombatFailureReason.InvalidRequest, $"跳过 Hurtbox：{hurtbox.name} 缺少 Owner ActorId 或 HurtboxId。", hurtbox);
            }

            var sourceActorId = ResolveOwnerActorId();
            if (!IsSourceActorAlive(sourceActorId))
            {
                return BuildDriverFailure(CombatFailureReason.SourceDead, $"攻击者 {sourceActorId} 已死亡或无法确认存活，本次命中被过滤。", hurtbox);
            }

            var request = BuildDamageRequest(hurtbox, hitCollider);
            return _combatCommand.ApplyDamage(request);
        }

        private void QueueCollider(Collider other)
        {
            if (_combatQuery == null && !ResolveServices())
            {
                return;
            }

            if (other == null || !IsLayerAllowed(other.gameObject.layer))
            {
                return;
            }

            var hurtbox = other.GetComponentInParent<CombatHurtbox>();
            if (hurtbox == null)
            {
                return;
            }

            if (ShouldSkipQueuedHurtbox(hurtbox))
            {
                return;
            }

            _pendingHurtboxes[hurtbox] = other;
        }

        private bool ShouldSkipQueuedHurtbox(CombatHurtbox hurtbox)
        {
            if (hurtbox == null
                || string.IsNullOrWhiteSpace(_activeAttackInstanceId)
                || _combatQuery == null
                || string.IsNullOrWhiteSpace(hurtbox.ActorId))
            {
                return false;
            }

            if (definition != null && !definition.HitSameTargetOnce)
            {
                return false;
            }

            return _combatQuery.HasHit(_activeAttackInstanceId, hurtbox.ActorId);
        }

        private CombatDamageRequest BuildDamageRequest(CombatHurtbox hurtbox, Collider hitCollider)
        {
            var template = definition != null && definition.DamageTemplate != null
                ? definition.DamageTemplate.Clone()
                : new CombatDamageRequest();
            template.ClearRuntimeFields();

            var sourcePosition = ResolveSourcePosition();
            var hitPoint = ResolveHitPoint(hitCollider, hurtbox, sourcePosition);
            var hitDirection = ResolveHitDirection(sourcePosition, hitPoint, hurtbox);

            template.RequestId = Guid.NewGuid().ToString("N");
            template.AttackInstanceId = _activeAttackInstanceId;
            template.SourceActorId = ResolveOwnerActorId();
            template.TargetActorId = hurtbox.ActorId;
            template.HitboxId = !string.IsNullOrWhiteSpace(definition?.HitboxId) ? definition.HitboxId : template.HitboxId;
            template.HurtboxId = hurtbox.HurtboxId;
            template.TargetTags = hurtbox.GetTargetTagsSnapshot();
            template.HitPoint = hitPoint;
            template.HitDirection = hitDirection;
            template.SourcePosition = sourcePosition;
            template.BodyPartMultiplier = hurtbox.PartDamageMultiplier;

            return template;
        }

        private bool ResolveServices()
        {
            if (_combatCommand != null && _hitboxService != null && _combatQuery != null)
            {
                return true;
            }

            var provider = combatServiceProvider as ICombatRuntimeServiceProvider;
            if (provider == null && autoFindCombatProvider)
            {
                provider = FindServiceProviderInScene();
                combatServiceProvider = provider as MonoBehaviour;
                if (provider == null)
                {
                    Warn("自动查找战斗服务提供者失败：场景中没有找到实现 ICombatRuntimeServiceProvider 的组件。请把核心场景 CombatRoot 上的 NiumaCombatController 拖到 Combat Service Provider，或由桥接脚本调用 SetCombatServices。");
                }
            }

            if (provider != null)
            {
                _combatCommand ??= provider.CombatCommand;
                _combatQuery ??= provider.CombatQuery;
                _hitboxService ??= provider.CombatHitboxService;
            }

            _combatQuery ??= _combatCommand as ICombatQuery;
            return _combatCommand != null && _hitboxService != null && _combatQuery != null;
        }

        private void WarnIfDamageTemplateHasRuntimeFields()
        {
            if (definition?.DamageTemplate == null || !definition.DamageTemplate.HasRuntimeFields())
            {
                return;
            }

            Warn("Hitbox Definition 的 DamageTemplate 填写了运行时字段（如 RequestId、SkillId、ActorId、HitboxId、HurtboxId、HitPoint 或 TargetTags）。这些字段会在本次命中请求中自动清空，并由 Driver 根据实际命中重新写入。");
        }

        private void AutoResolveLocalReferences()
        {
            if (ownerIdentity == null)
            {
                ownerIdentity = GetComponentInParent<CombatActorIdentity>();
            }

            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponent<Collider>();
            }
        }

        private string ResolveOwnerActorId()
        {
            if (ownerIdentity != null && !string.IsNullOrWhiteSpace(ownerIdentity.ActorId))
            {
                return ownerIdentity.ActorId;
            }

            return ownerActorId;
        }

        private Vector3 ResolveSourcePosition()
        {
            if (ownerIdentity != null)
            {
                return ownerIdentity.transform.position;
            }

            if (hitboxCollider != null)
            {
                return hitboxCollider.bounds.center;
            }

            return transform.position;
        }

        private Vector3 ResolveHitPoint(Collider hitCollider, CombatHurtbox hurtbox, Vector3 sourcePosition)
        {
            if (hitCollider != null)
            {
                return hitCollider.ClosestPoint(sourcePosition);
            }

            var hurtboxCollider = hurtbox != null ? hurtbox.GetComponent<Collider>() : null;
            if (hurtboxCollider != null)
            {
                return hurtboxCollider.ClosestPoint(sourcePosition);
            }

            if (hitboxCollider != null)
            {
                return hitboxCollider.ClosestPoint(hurtbox.transform.position);
            }

            return hurtbox.transform.position;
        }

        private Vector3 ResolveHitDirection(Vector3 sourcePosition, Vector3 hitPoint, CombatHurtbox hurtbox)
        {
            var direction = hitPoint - sourcePosition;
            if (direction.sqrMagnitude <= 0.0001f && hurtbox != null)
            {
                direction = hurtbox.transform.position - sourcePosition;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = transform.forward;
            }

            return direction.normalized;
        }

        private bool IsLayerAllowed(int layer)
        {
            return (targetLayerMask.value & (1 << layer)) != 0;
        }

        private bool IsSourceActorAlive(string sourceActorId)
        {
            return _combatQuery != null
                && !string.IsNullOrWhiteSpace(sourceActorId)
                && _combatQuery.IsActorAlive(sourceActorId);
        }

        private CombatResult BuildDriverFailure(CombatFailureReason reason, string message, CombatHurtbox hurtbox)
        {
            Warn(message);
            return new CombatResult
            {
                RequestId = Guid.NewGuid().ToString("N"),
                AttackInstanceId = _activeAttackInstanceId,
                SourceActorId = ResolveOwnerActorId(),
                TargetActorId = hurtbox != null ? hurtbox.ActorId : null,
                HitboxId = definition != null ? definition.HitboxId : null,
                HurtboxId = hurtbox != null ? hurtbox.HurtboxId : null,
                ResultType = ToFailureResultType(reason),
                FailureReason = reason,
                HitPoint = hurtbox != null ? hurtbox.transform.position : transform.position,
                ResolvedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static CombatResultType ToFailureResultType(CombatFailureReason reason)
        {
            return reason == CombatFailureReason.HitboxNotActive
                || reason == CombatFailureReason.SourceDead
                ? CombatResultType.Filtered
                : CombatResultType.Failed;
        }

        private static ICombatRuntimeServiceProvider FindServiceProviderInScene()
        {
            var cachedBehaviour = _cachedServiceProvider as MonoBehaviour;
            if (cachedBehaviour != null)
            {
                return _cachedServiceProvider;
            }

            if (Time.unscaledTime < _nextGlobalProviderScanTime)
            {
                return null;
            }

            _nextGlobalProviderScanTime = Time.unscaledTime + AutoFindProviderRetrySeconds;
            _cachedServiceProvider = null;
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatRuntimeServiceProvider provider)
                {
                    _cachedServiceProvider = provider;
                    return provider;
                }
            }

            return null;
        }

        private void Warn(string message)
        {
            if (logWarnings)
            {
                Debug.LogWarning($"[CombatHitboxDriver] {message}", this);
            }
        }
    }
}
