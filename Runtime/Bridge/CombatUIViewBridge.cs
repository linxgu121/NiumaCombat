using System;
using System.Collections.Generic;
using NiumaCombat.Controller;
using NiumaCombat.Data;
using NiumaCombat.Enum;
using NiumaCombat.Event;
using NiumaCombat.Service;
using NiumaCombat.ViewData;
using NiumaCore.Event;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaCombat.Bridge
{
    /// <summary>
    /// Combat 到 UI 的数据桥接层。
    /// 只把 CombatResult 转成 ViewData，不制作具体 UI，也不修改战斗事实。
    /// </summary>
    public sealed class CombatUIViewBridge : MonoBehaviour, IGameModule, ICombatReactionReceiver
    {
        [Header("模块引用")]
        [Tooltip("战斗模块控制器。拖 GameplayServicesRoot 上的 NiumaCombatController；为空时可按配置自动查找。")]
        [SerializeField] private NiumaCombatController combatController;

        [Tooltip("Combat UI 接收脚本。使用 UI Toolkit 时拖 CombatToolkitReceiver；自定义 UI 时拖实现 ICombatUIReceiver 的组件。")]
        [SerializeField] private MonoBehaviour combatUIReceiverProvider;

        [Header("自动查找")]
        [Tooltip("没有手动绑定 NiumaCombatController 时，是否在场景中自动查找。正式场景建议手动绑定。")]
        [SerializeField] private bool autoFindCombatController = true;

        [Header("刷新策略")]
        [Tooltip("启用桥接层时是否立即刷新一次 Combat 面板。")]
        [SerializeField] private bool refreshOnEnable = true;

        [Tooltip("是否在 LateUpdate 中按 Combat Revision 自动刷新最近结果。关闭后可由外部手动调用 RefreshCombatPanel。")]
        [SerializeField] private bool refreshInLateUpdate = true;

        [Tooltip("收到 Combat 事件时是否立即推送单条结果。关闭后只依赖 Revision 轮询刷新。")]
        [SerializeField] private bool pushResultEvents = true;

        [Tooltip("是否接收 CombatResultRejectedEvent。开启后可显示免疫、过滤、失败等提示；关闭时只显示已确认命中和已应用结果。")]
        [SerializeField] private bool includeRejectedResults = true;

        [Tooltip("没有 Combat 服务、ActorId 为空或没有结果时，是否发送 Cleared 更新给 UI 接收器。")]
        [SerializeField] private bool notifyWhenCleared = true;

        [Header("显示对象")]
        [Tooltip("当前要显示战斗结果的 ActorId。玩家可填 player；NPC、召唤物或远端玩家请填写对应稳定 ActorId。")]
        [SerializeField] private string actorId = "player";

        [Tooltip("读取该 Actor 的哪类结果：Source=作为攻击者，Target=作为受击者，Either=两者都显示。")]
        [SerializeField] private CombatResultActorRole resultRole = CombatResultActorRole.Target;

        [Tooltip("最近结果最多显示多少条。")]
        [SerializeField] private int maxRecentResults = 16;

        [Tooltip("飘字缓存最多保留多少条。UI 可只取最新一条播放，也可显示调试列表。")]
        [SerializeField] private int maxFloatingTexts = 12;

        [Header("日志")]
        [Tooltip("缺少必要引用、Receiver 类型错误或 UI 更新失败时是否输出警告。")]
        [SerializeField] private bool logWarnings = true;

        private readonly List<CombatFloatingTextViewData> _floatingBuffer = new List<CombatFloatingTextViewData>();
        private GameContext _context;
        private IEventBus _eventBus;
        private ICombatQuery _combatQuery;
        private ICombatUIReceiver _receiver;
        private long _observedRevision = -1L;
        private bool _subscribed;
        private bool _isApplyingUpdate;

        public string ModuleName => "NiumaCombat.UIBridge";

        private void Reset()
        {
            ResolveReferences(false);
        }

        private void OnEnable()
        {
            ResolveReferences(true);

            if (_context != null)
            {
                Subscribe(_context.EventBus);
            }

            if (refreshOnEnable)
            {
                RefreshCombatPanel();
            }
        }

        private void OnDisable()
        {
            _isApplyingUpdate = false;
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (!refreshInLateUpdate || _isApplyingUpdate || !EnsureCombatQuery())
            {
                return;
            }

            if (_observedRevision == _combatQuery.Revision)
            {
                return;
            }

            RefreshCombatPanel();
        }

        public void Initialize(GameContext context)
        {
            _context = context;
            ResolveReferences(true);

            if (_combatQuery == null && context != null)
            {
                ResolveCombatQueryFromContext(context);
            }

            Subscribe(context?.EventBus);
        }

        public void StartModule()
        {
            ResolveReferences(true);
            if (refreshOnEnable)
            {
                RefreshCombatPanel();
            }
        }

        public void StopModule()
        {
            Unsubscribe();
        }

        public void Tick(float deltaTime)
        {
            // 第一版由 LateUpdate 轮询 Revision；统一模块启动器可只调用生命周期，不需要驱动 Tick。
        }

        public void SetActorId(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.Equals(actorId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            actorId = normalized;
            _floatingBuffer.Clear();
            _observedRevision = -1L;
            RefreshCombatPanel();
        }

        public void RefreshCombatPanel()
        {
            if (!EnsureCombatQuery())
            {
                ApplyClearUpdate();
                return;
            }

            if (string.IsNullOrWhiteSpace(actorId))
            {
                ApplyClearUpdate();
                return;
            }

            var panel = BuildPanelViewData(null);
            if (panel == null || (panel.RecentResults.Length == 0 && panel.FloatingTexts.Length == 0))
            {
                ApplyClearUpdate();
                return;
            }

            ApplyUpdate(new CombatUIUpdate(CombatUIUpdateType.Refresh, _combatQuery.Revision, panel, panel.LastResult));
            _observedRevision = _combatQuery.Revision;
        }

        public void ApplyCombatResult(CombatResult result)
        {
            if (result == null || !ShouldHandleResult(result))
            {
                return;
            }

            if (!EnsureCombatQuery())
            {
                return;
            }

            var resultViewData = CombatResultViewData.FromResult(result);
            AppendFloatingText(BuildFloatingText(result, resultViewData));
            var panel = BuildPanelViewData(resultViewData);
            ApplyUpdate(new CombatUIUpdate(CombatUIUpdateType.Result, panel?.Revision ?? _combatQuery.Revision, panel, resultViewData));
            _observedRevision = _combatQuery.Revision;
        }

        private void ResolveReferences(bool logMissing)
        {
            if (combatController == null && autoFindCombatController)
            {
                combatController = FindSceneObject<NiumaCombatController>();
            }

            if (combatController != null)
            {
                _combatQuery = combatController.CombatQuery;
            }

            _receiver = combatUIReceiverProvider as ICombatUIReceiver;
            if (combatUIReceiverProvider == null)
            {
                _receiver = FindReceiverOnCurrentObject();
            }

            if (logMissing && combatUIReceiverProvider != null && _receiver == null)
            {
                Warn("Combat UI Receiver Provider 没有实现 ICombatUIReceiver。使用 UI Toolkit 时请拖 CombatToolkitReceiver。");
            }
        }

        private bool EnsureCombatQuery()
        {
            ResolveReferences(false);
            if (_combatQuery != null)
            {
                return true;
            }

            if (_context != null)
            {
                ResolveCombatQueryFromContext(_context);
            }

            if (_combatQuery != null)
            {
                return true;
            }

            Warn("未找到 ICombatQuery，无法刷新 Combat UI。请绑定 NiumaCombatController，或确保 NiumaCombatController 已注册到 GameContext。");
            return false;
        }

        private void Subscribe(IEventBus eventBus)
        {
            if (_subscribed || eventBus == null)
            {
                return;
            }

            _eventBus = eventBus;
            _eventBus.Subscribe<CombatDamageAppliedEvent>(HandleDamageApplied);
            _eventBus.Subscribe<CombatHealedEvent>(HandleHealed);
            _eventBus.Subscribe<CombatKilledEvent>(HandleKilled);
            _eventBus.Subscribe<CombatHitConfirmedEvent>(HandleHitConfirmed);
            _eventBus.Subscribe<CombatResultRejectedEvent>(HandleRejected);
            _subscribed = true;
        }

        private void ResolveCombatQueryFromContext(GameContext context)
        {
            if (context == null)
            {
                return;
            }

            ICombatService combatService;
            if (context.TryGetService(out combatService) && combatService != null)
            {
                _combatQuery = combatService;
                return;
            }

            ICombatQuery combatQuery;
            if (context.TryGetService(out combatQuery))
            {
                _combatQuery = combatQuery;
            }
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
            _eventBus.Unsubscribe<CombatHealedEvent>(HandleHealed);
            _eventBus.Unsubscribe<CombatKilledEvent>(HandleKilled);
            _eventBus.Unsubscribe<CombatHitConfirmedEvent>(HandleHitConfirmed);
            _eventBus.Unsubscribe<CombatResultRejectedEvent>(HandleRejected);
            _subscribed = false;
            _eventBus = null;
        }

        private void HandleDamageApplied(CombatDamageAppliedEvent evt)
        {
            if (!pushResultEvents || evt.Result == null)
            {
                return;
            }

            if (evt.Result.IsKilled)
            {
                return;
            }

            ApplyCombatResult(evt.Result);
        }

        private void HandleHealed(CombatHealedEvent evt)
        {
            if (pushResultEvents)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private void HandleKilled(CombatKilledEvent evt)
        {
            if (pushResultEvents)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private void HandleHitConfirmed(CombatHitConfirmedEvent evt)
        {
            if (!pushResultEvents || evt.Result == null)
            {
                return;
            }

            if (evt.Result.ResultType == CombatResultType.Blocked)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private void HandleRejected(CombatResultRejectedEvent evt)
        {
            if (pushResultEvents && includeRejectedResults)
            {
                ApplyCombatResult(evt.Result);
            }
        }

        private bool ShouldHandleResult(CombatResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            switch (resultRole)
            {
                case CombatResultActorRole.Source:
                    return string.Equals(actorId, result.SourceActorId, StringComparison.Ordinal);
                case CombatResultActorRole.Target:
                    return string.Equals(actorId, result.TargetActorId, StringComparison.Ordinal);
                default:
                    return string.Equals(actorId, result.SourceActorId, StringComparison.Ordinal)
                        || string.Equals(actorId, result.TargetActorId, StringComparison.Ordinal);
            }
        }

        private CombatPanelViewData BuildPanelViewData(CombatResultViewData latestResult)
        {
            var revision = _combatQuery?.Revision ?? 0L;
            var recent = _combatQuery != null && !string.IsNullOrWhiteSpace(actorId)
                ? _combatQuery.GetRecentResults(actorId, resultRole, maxRecentResults)
                : Array.Empty<CombatResult>();

            var recentViewData = new CombatResultViewData[recent.Length];
            for (var i = 0; i < recent.Length; i++)
            {
                recentViewData[i] = CombatResultViewData.FromResult(recent[i]);
            }

            return new CombatPanelViewData
            {
                ActorId = actorId,
                Revision = revision,
                LastResult = latestResult ?? (recentViewData.Length > 0 ? recentViewData[0] : null),
                RecentResults = recentViewData,
                FloatingTexts = _floatingBuffer.ToArray()
            };
        }

        private void AppendFloatingText(CombatFloatingTextViewData floatingText)
        {
            if (floatingText == null)
            {
                return;
            }

            _floatingBuffer.Insert(0, floatingText);
            var limit = Mathf.Max(1, maxFloatingTexts);
            if (_floatingBuffer.Count > limit)
            {
                _floatingBuffer.RemoveRange(limit, _floatingBuffer.Count - limit);
            }
        }

        private static CombatFloatingTextViewData BuildFloatingText(CombatResult result, CombatResultViewData resultViewData)
        {
            if (result == null || resultViewData == null)
            {
                return null;
            }

            return new CombatFloatingTextViewData
            {
                RequestId = result.RequestId,
                TargetActorId = result.TargetActorId,
                ResultType = result.ResultType,
                Value = result.FinalValue,
                IsCritical = result.IsCritical,
                IsKilled = result.IsKilled,
                WorldPosition = result.HitPoint,
                Text = resultViewData.Message,
                StyleKey = resultViewData.StyleKey
            };
        }

        private void ApplyClearUpdate()
        {
            if (!notifyWhenCleared)
            {
                return;
            }

            _floatingBuffer.Clear();
            ApplyUpdate(new CombatUIUpdate(CombatUIUpdateType.Cleared, _combatQuery?.Revision ?? 0L, null, null));
            _observedRevision = _combatQuery?.Revision ?? -1L;
        }

        private void ApplyUpdate(CombatUIUpdate update)
        {
            ResolveReferences(false);
            if (_receiver == null)
            {
                Warn("未绑定 Combat UI Receiver。使用 UI Toolkit 时请在 UIRoot/UIBridges 上挂 CombatToolkitReceiver，并拖到本字段。");
                return;
            }

            try
            {
                _isApplyingUpdate = true;
                _receiver.ApplyCombatUpdate(update?.Clone());
            }
            finally
            {
                _isApplyingUpdate = false;
            }
        }

        private void Warn(string message)
        {
            if (logWarnings && !string.IsNullOrWhiteSpace(message))
            {
                Debug.LogWarning($"[NiumaCombatUIBridge] {message}", this);
            }
        }

        private ICombatUIReceiver FindReceiverOnCurrentObject()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatUIReceiver receiver)
                {
                    return receiver;
                }
            }

            return null;
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }
    }
}
