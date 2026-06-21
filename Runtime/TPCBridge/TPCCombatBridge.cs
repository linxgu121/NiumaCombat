using NiumaCombat.Data;
using NiumaCombat.Enum;
using NiumaCombat.Event;
using NiumaCombat.Hitbox;
using NiumaCombat.Service;
using NiumaCore.Event;
using NiumaCore.Module;
using NiumaTPC.CombatReaction;
using NiumaTPC.Module;
using UnityEngine;

namespace NiumaCombat.TPCBridge
{
    /// <summary>
    /// 将 CombatResult 转成 TPC 战斗表现请求。
    /// 只负责表现桥接，不做伤害结算、不切换 TPC 内部 HFSM 状态。
    /// </summary>
    public sealed class TPCCombatBridge : MonoBehaviour, IGameModule, ICombatReactionReceiver
    {
        [Header("目标角色")]
        [InspectorName("玩家模块控制器")]
        [Tooltip("拖 PlayerRoot 上的 PlayerModuleController。桥接会调用它的 TryPlayCombatReaction 播放受击、格挡、击倒、死亡表现。")]
        [SerializeField] private PlayerModuleController playerController;

        [InspectorName("目标身份")]
        [Tooltip("拖当前角色根节点上的 CombatActorIdentity。用于过滤 CombatResult：只有 TargetActorId 等于该 ActorId 时才播放表现。")]
        [SerializeField] private CombatActorIdentity targetIdentity;

        [InspectorName("自动从父节点查找")]
        [Tooltip("开启后，Awake 会从当前物体和父节点查找 PlayerModuleController / CombatActorIdentity。建议桥接脚本挂在 PlayerRoot/CombatBridge。")]
        [SerializeField] private bool autoResolveFromParents = true;

        [Header("事件订阅")]
        [InspectorName("订阅 Combat 事件")]
        [Tooltip("开启后，Initialize(GameContext) 会订阅 CombatDamageAppliedEvent、CombatKilledEvent、CombatHitConfirmedEvent。没有统一 GameContext 时可关闭，并由外部直接调用 ApplyCombatResult。")]
        [SerializeField] private bool subscribeCombatEvents = true;

        [InspectorName("接收伤害事件")]
        [Tooltip("CombatDamageAppliedEvent 到达时播放受击表现。")]
        [SerializeField] private bool reactToDamageApplied = true;

        [InspectorName("接收死亡事件")]
        [Tooltip("CombatKilledEvent 到达时播放死亡表现。")]
        [SerializeField] private bool reactToKilled = true;

        [InspectorName("接收格挡事件")]
        [Tooltip("CombatHitConfirmedEvent 中 ResultType 为 Blocked 时播放格挡表现。第一版 Immune 不走 HitConfirmed。")]
        [SerializeField] private bool reactToBlockedConfirmed = true;

        [Header("表现映射")]
        [InspectorName("重受击伤害阈值")]
        [Tooltip("FinalValue 大于等于该值时，将普通 Damage 映射为重受击。小于该值时映射为轻受击。")]
        [SerializeField] private float heavyHitDamageThreshold = 30f;

        [InspectorName("击倒力度阈值")]
        [Tooltip("Reaction.KnockbackForce 大于等于该值，或 Reaction.ForceKnockdown 为 true 时，映射为击倒。小于等于 0 表示只看 ForceKnockdown。")]
        [SerializeField] private float knockdownForceThreshold = 8f;

        [InspectorName("死亡强制冻结输入")]
        [Tooltip("死亡表现请求会覆盖配置并冻结输入。")]
        [SerializeField] private bool forceBlockInputOnDeath = true;

        [InspectorName("输出警告日志")]
        [Tooltip("开启后，缺少 PlayerModuleController、缺少 ActorId、表现播放失败等情况会输出 Warning。")]
        [SerializeField] private bool logWarnings = true;

        private GameContext _context;
        private IEventBus _eventBus;
        private bool _subscribed;

        public string ModuleName => "NiumaCombat.TPCBridge";

        public void Initialize(GameContext context)
        {
            _context = context;
            ResolveReferences();

            if (subscribeCombatEvents)
            {
                Subscribe(context?.EventBus);
            }
        }

        public void StartModule()
        {
            ResolveReferences();
        }

        public void StopModule()
        {
            Unsubscribe();
        }

        public void Tick(float deltaTime)
        {
            // 第一版桥接无帧逻辑。战斗表现由 Combat 事件或 ApplyCombatResult 直接驱动。
        }

        public void SetGameContext(GameContext context)
        {
            if (ReferenceEquals(_context, context))
            {
                return;
            }

            Unsubscribe();
            Initialize(context);
        }

        public void ApplyCombatResult(CombatResult result)
        {
            if (result == null)
            {
                return;
            }

            if (!ShouldHandleResult(result))
            {
                return;
            }

            if (!TryMapReaction(result, out var request))
            {
                return;
            }

            if (playerController == null)
            {
                Warn("无法播放 Combat 表现：未绑定 PlayerModuleController。请把 PlayerRoot 上的 PlayerModuleController 拖到“玩家模块控制器”。");
                return;
            }

            if (!playerController.TryPlayCombatReaction(request))
            {
                Warn($"TPC 表现播放失败：Reaction={request.ReactionType}, RequestId={request.RequestId}");
            }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_context != null && subscribeCombatEvents)
            {
                Subscribe(_context.EventBus);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            if (!autoResolveFromParents)
            {
                return;
            }

            if (playerController == null)
            {
                playerController = GetComponentInParent<PlayerModuleController>();
            }

            if (targetIdentity == null)
            {
                targetIdentity = GetComponentInParent<CombatActorIdentity>();
            }
        }

        private void Subscribe(IEventBus eventBus)
        {
            if (_subscribed || eventBus == null)
            {
                return;
            }

            _eventBus = eventBus;
            _eventBus.Subscribe<CombatDamageAppliedEvent>(HandleDamageApplied);
            _eventBus.Subscribe<CombatKilledEvent>(HandleKilled);
            _eventBus.Subscribe<CombatHitConfirmedEvent>(HandleHitConfirmed);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _eventBus == null)
            {
                _subscribed = false;
                _eventBus = null;
                return;
            }

            _eventBus.Unsubscribe<CombatDamageAppliedEvent>(HandleDamageApplied);
            _eventBus.Unsubscribe<CombatKilledEvent>(HandleKilled);
            _eventBus.Unsubscribe<CombatHitConfirmedEvent>(HandleHitConfirmed);
            _subscribed = false;
            _eventBus = null;
        }

        private void HandleDamageApplied(CombatDamageAppliedEvent evt)
        {
            if (evt.Result != null && evt.Result.IsKilled && reactToKilled)
            {
                return;
            }

            if (reactToDamageApplied)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private void HandleKilled(CombatKilledEvent evt)
        {
            if (reactToKilled)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private void HandleHitConfirmed(CombatHitConfirmedEvent evt)
        {
            if (!reactToBlockedConfirmed || evt.Result == null)
            {
                return;
            }

            if (evt.Result.ResultType == CombatResultType.Blocked)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private bool ShouldHandleResult(CombatResult result)
        {
            var actorId = GetTargetActorId();
            if (string.IsNullOrWhiteSpace(actorId))
            {
                Warn("无法过滤 CombatResult：目标身份未配置 ActorId。请绑定 CombatActorIdentity，或在其 Actor Id 中填写当前角色 ID。");
                return false;
            }

            return string.Equals(actorId, result.TargetActorId, System.StringComparison.Ordinal);
        }

        private string GetTargetActorId()
        {
            return targetIdentity != null ? targetIdentity.ActorId : null;
        }

        private bool TryMapReaction(CombatResult result, out TPCCombatReactionRequest request)
        {
            request = default;

            var reactionType = MapReactionType(result);
            if (reactionType == TPCCombatReactionType.None)
            {
                return false;
            }

            request = TPCCombatReactionRequest.ForType(reactionType, result.RequestId);
            request.HitPoint = result.HitPoint;
            request.HitDirection = ResolveHitDirection(result);
            request.Force = result.Reaction != null ? result.Reaction.KnockbackForce : 0f;
            request.StaggerSeconds = result.Reaction != null ? result.Reaction.StaggerSeconds : 0f;
            request.KnockbackDistance = result.Reaction != null ? result.Reaction.KnockbackDistance : 0f;
            request.HitVfxCueId = result.Reaction != null ? result.Reaction.HitVfxCueId : null;
            request.HitAudioCueId = result.Reaction != null ? result.Reaction.HitAudioCueId : null;

            if (reactionType == TPCCombatReactionType.Death && forceBlockInputOnDeath)
            {
                request.OverrideBlockInput = true;
                request.BlockInput = true;
            }

            return true;
        }

        private TPCCombatReactionType MapReactionType(CombatResult result)
        {
            if (result.IsKilled)
            {
                return TPCCombatReactionType.Death;
            }

            if (result.ResultType == CombatResultType.Blocked)
            {
                return TPCCombatReactionType.Blocked;
            }

            if (result.ResultType != CombatResultType.Damage)
            {
                return TPCCombatReactionType.None;
            }

            var reaction = result.Reaction;
            if (reaction != null)
            {
                if (reaction.ForceKnockdown)
                {
                    return TPCCombatReactionType.Knockdown;
                }

                if (knockdownForceThreshold > 0f && reaction.KnockbackForce >= knockdownForceThreshold)
                {
                    return TPCCombatReactionType.Knockdown;
                }
            }

            return result.FinalValue >= heavyHitDamageThreshold
                ? TPCCombatReactionType.HeavyHit
                : TPCCombatReactionType.LightHit;
        }

        private static Vector3 ResolveHitDirection(CombatResult result)
        {
            if (result?.Reaction != null && result.Reaction.HitDirection.sqrMagnitude > 0.0001f)
            {
                return result.Reaction.HitDirection.normalized;
            }

            if (result != null && result.HitDirection.sqrMagnitude > 0.0001f)
            {
                return result.HitDirection.normalized;
            }

            return Vector3.zero;
        }

        private void Warn(string message)
        {
            if (logWarnings)
            {
                Debug.LogWarning($"[NiumaCombat.TPCBridge] {message}", this);
            }
        }
    }
}
