using NiumaAttribute.Controller;
using NiumaAttribute.Service;
using NiumaCombat.Enum;
using NiumaCombat.Service;
using NiumaCore.Event;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaCombat.Controller
{
    [DisallowMultipleComponent]
    public sealed class NiumaCombatController : MonoBehaviour, IGameModule, ICombatRuntimeServiceProvider
    {
        [Header("依赖：Attribute")]
        [Tooltip("属性模块控制器。通常拖核心场景 GameplayServicesRoot 上的 NiumaAttributeController；留空时可从 GameContext 自动解析 IAttributeQuery/IAttributeCommand。")]
        [SerializeField] private NiumaAttributeController attributeController;

        [Tooltip("初始化时是否优先从 GameContext 查找 IAttributeQuery/IAttributeCommand。核心场景已注册 Attribute 服务时建议开启。")]
        [SerializeField] private bool resolveAttributeFromContext = true;

        [Header("依赖：阵营过滤")]
        [Tooltip("阵营关系解析器。拖实现 ICombatFactionResolver 的组件；留空时第一版只阻止攻击自己，其它目标默认允许，正式战斗建议补上。")]
        [SerializeField] private MonoBehaviour factionResolverProvider;

        [Tooltip("初始化时是否从 GameContext 查找 ICombatFactionResolver。全局阵营系统注册后建议开启。")]
        [SerializeField] private bool resolveFactionFromContext = true;

        [Header("战斗配置")]
        [Tooltip("生命资源 ID。必须和 NiumaAttribute 的 ResourceDefinition.ResourceId 一致；默认 hp。")]
        [SerializeField] private string hpResourceId = "hp";

        [Tooltip("全局 CombatResult 缓存上限。用于调试、事件桥接和 UI 轮询；过小会丢较早结果。")]
        [SerializeField] private int maxGlobalResults = 128;

        [Tooltip("每个 Actor 的最近结果缓存上限。UI 飘字和调试面板通常读取这里。")]
        [SerializeField] private int maxActorResults = 32;

        [Header("模块启动")]
        [Tooltip("Awake 时自动初始化战斗模块。仅在能解析 Attribute 依赖时执行；核心场景手动统一 Initialize(context) 时可关闭。")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("OnEnable 时自动启动模块。核心场景手动统一 StartModule 时可关闭。")]
        [SerializeField] private bool startOnEnable = true;

        [Tooltip("初始化时是否把 ICombatService 和 ICombatHitboxService 注册进 GameContext。需要 Query/Command 时从 ICombatService 使用。核心场景建议开启。")]
        [SerializeField] private bool registerServiceToContext = true;

        [Tooltip("是否由本控制器 Update 自动驱动 Tick。若已有统一模块启动器调用 IGameModule.Tick，请关闭，避免 Hitbox 超时计时重复推进。")]
        [SerializeField] private bool driveTickInUpdate = true;

        [Header("Inspector 调试")]
        [Tooltip("调试用攻击者 ActorId。用于右键菜单检查 CanTarget 和最近输出结果；正式逻辑不会读取这里。")]
        [SerializeField] private string debugSourceActorId;

        [Tooltip("调试用目标 ActorId。用于右键菜单检查存活、CanTarget 和最近承受结果；正式逻辑不会读取这里。")]
        [SerializeField] private string debugTargetActorId;

        [Tooltip("右键菜单“手动 Tick 一次”的推进秒数。用于调试 Hitbox ActiveSeconds 超时关闭。")]
        [SerializeField] private float debugTickSeconds = 0.1f;

        private CombatService _combatService;
        private CombatHitboxService _hitboxService;
        private ICombatConfigurationService _configurationService;
        private GameContext _context;
        private IAttributeQuery _attributeQuery;
        private IAttributeCommand _attributeCommand;
        private ICombatFactionResolver _factionResolver;
        private IAttributeQuery _externalAttributeQuery;
        private IAttributeCommand _externalAttributeCommand;
        private ICombatFactionResolver _externalFactionResolver;
        private IEventBus _externalEventBus;
        private bool _hasExternalEventBus;
        private bool _warnedMissingAwakeContext;

        public string ModuleName => "NiumaCombat";
        public ICombatService CombatService => _combatService;
        public ICombatQuery CombatQuery => _combatService;
        public ICombatCommand CombatCommand => _combatService;
        public ICombatHitboxService CombatHitboxService => _hitboxService;
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public long CombatRevision => _combatService?.Revision ?? 0L;

        private void Awake()
        {
            if (initializeOnAwake && !IsInitialized)
            {
                if (!CanInitializeWithCurrentInputs(out var message))
                {
                    WarnMissingAwakeContext(message);
                    return;
                }

                Initialize(_context);
            }
        }

        private void OnEnable()
        {
            if (startOnEnable && IsInitialized && !IsRunning)
            {
                StartModule();
            }
        }

        private void Update()
        {
            if (driveTickInUpdate && IsRunning)
            {
                Tick(Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            if (IsRunning)
            {
                StopModule();
            }
        }

        private void OnDestroy()
        {
            if (_context != null && registerServiceToContext)
            {
                UnregisterServicesFromContext(_context);
            }
        }

        private void OnValidate()
        {
            hpResourceId = string.IsNullOrWhiteSpace(hpResourceId) ? "hp" : hpResourceId.Trim();
            maxGlobalResults = Mathf.Max(1, maxGlobalResults);
            maxActorResults = Mathf.Max(1, maxActorResults);
            debugSourceActorId = debugSourceActorId != null ? debugSourceActorId.Trim() : string.Empty;
            debugTargetActorId = debugTargetActorId != null ? debugTargetActorId.Trim() : string.Empty;
            debugTickSeconds = Mathf.Max(0f, debugTickSeconds);

            if (factionResolverProvider != null && !(factionResolverProvider is ICombatFactionResolver))
            {
                Debug.LogWarning("[NiumaCombatController] Faction Resolver Provider 绑定的脚本没有实现 ICombatFactionResolver，运行时会被忽略。", this);
            }
        }

        public void Initialize(GameContext context)
        {
            var previousService = _combatService;
            var previousHitboxService = _hitboxService;
            var previousConfiguration = _configurationService;
            var previousContext = _context;
            var previousInitialized = IsInitialized;
            var previousRunning = IsRunning;

            ICombatService oldCombatService = null;
            ICombatHitboxService oldHitboxService = null;

            if (context != null)
            {
                context.TryGetService(out oldCombatService);
                context.TryGetService(out oldHitboxService);
            }

            try
            {
                _context = context;
                ResolveAttributeService(context);
                if (_attributeQuery == null || _attributeCommand == null)
                {
                    throw new System.InvalidOperationException("NiumaCombat requires IAttributeQuery and IAttributeCommand before initialization can complete.");
                }

                ResolveFactionResolver(context);

                var resolvedEventBus = _hasExternalEventBus ? _externalEventBus : context?.EventBus;
                _combatService = new CombatService(_attributeQuery, _attributeCommand, _factionResolver, resolvedEventBus, hpResourceId, maxGlobalResults, maxActorResults);
                _hitboxService = new CombatHitboxService(_combatService, () => Time.time);
                _combatService.SetHitboxService(_hitboxService);
                _configurationService = _combatService;

                if (registerServiceToContext && context != null)
                {
                    RegisterServicesToContext(context);
                }

                IsInitialized = true;
                IsRunning = false;
            }
            catch (System.Exception ex)
            {
                _combatService = previousService;
                _hitboxService = previousHitboxService;
                _configurationService = previousConfiguration;
                _context = previousContext;
                IsInitialized = previousInitialized;
                IsRunning = previousRunning;

                if (context != null && registerServiceToContext)
                {
                    RestoreRegisteredServices(context, oldCombatService, oldHitboxService);
                }

                Debug.LogError($"[NiumaCombatController] 初始化失败，已回滚：{ex.Message}", this);
                throw;
            }
        }

        public void StartModule()
        {
            if (!IsInitialized)
            {
                if (!CanInitializeWithCurrentInputs(out var message))
                {
                    WarnMissingAwakeContext(message);
                    return;
                }

                Initialize(_context);
            }

            IsRunning = true;
        }

        public void StopModule()
        {
            IsRunning = false;
        }

        public void Tick(float deltaTime)
        {
            if (!IsInitialized || !IsRunning)
            {
                return;
            }

            _combatService?.Tick(deltaTime);
        }

        public void SetAttributeService(IAttributeQuery query, IAttributeCommand command)
        {
            _externalAttributeQuery = query;
            _externalAttributeCommand = command;
            _attributeQuery = query;
            _attributeCommand = command;
            _configurationService?.SetAttributeService(query, command);
        }

        public void SetFactionResolver(ICombatFactionResolver resolver)
        {
            _externalFactionResolver = resolver;
            _factionResolver = resolver;
            _configurationService?.SetFactionResolver(resolver);
        }

        public void SetEventBus(IEventBus eventBus)
        {
            _externalEventBus = eventBus;
            _hasExternalEventBus = eventBus != null;
            _configurationService?.SetEventBus(_hasExternalEventBus ? _externalEventBus : _context?.EventBus);
        }

        private void ResolveAttributeService(GameContext context)
        {
            _attributeQuery = _externalAttributeQuery;
            _attributeCommand = _externalAttributeCommand;

            if (resolveAttributeFromContext && context != null)
            {
                if (_attributeQuery == null)
                {
                    context.TryGetService(out _attributeQuery);
                }

                if (_attributeCommand == null)
                {
                    context.TryGetService(out _attributeCommand);
                }
            }

            if ((_attributeQuery == null || _attributeCommand == null) && attributeController != null)
            {
                _attributeQuery ??= attributeController.AttributeQuery;
                _attributeCommand ??= attributeController.AttributeCommand;
            }
        }

        private void ResolveFactionResolver(GameContext context)
        {
            _factionResolver = _externalFactionResolver;

            if (_factionResolver == null && resolveFactionFromContext && context != null)
            {
                context.TryGetService(out _factionResolver);
            }

            if (_factionResolver == null && factionResolverProvider is ICombatFactionResolver resolver)
            {
                _factionResolver = resolver;
            }
        }

        private bool CanInitializeWithCurrentInputs(out string message)
        {
            if (attributeController != null
                || (_externalAttributeQuery != null && _externalAttributeCommand != null)
                || (_context != null && resolveAttributeFromContext))
            {
                message = null;
                return true;
            }

            message = "Cannot initialize NiumaCombat: missing GameContext Attribute service, NiumaAttributeController binding, or externally injected Attribute query/command.";
            return false;
        }

        private void WarnMissingAwakeContext(string message)
        {
            if (_warnedMissingAwakeContext)
            {
                return;
            }

            _warnedMissingAwakeContext = true;
            Debug.LogWarning($"[NiumaCombatController] {message}", this);
        }

        private void RegisterServicesToContext(GameContext context)
        {
            context.RegisterService<ICombatService>(_combatService);
            context.RegisterService<ICombatHitboxService>(_hitboxService);
        }

        private void UnregisterServicesFromContext(GameContext context)
        {
            if (ReferenceEquals(context.GetService<ICombatService>(), _combatService))
            {
                context.UnregisterService<ICombatService>();
            }

            if (ReferenceEquals(context.GetService<ICombatHitboxService>(), _hitboxService))
            {
                context.UnregisterService<ICombatHitboxService>();
            }
        }

        private static void RestoreRegisteredServices(
            GameContext context,
            ICombatService oldCombatService,
            ICombatHitboxService oldHitboxService)
        {
            RestoreService(context, oldCombatService);
            RestoreService(context, oldHitboxService);
        }

        private static void RestoreService<T>(GameContext context, T service) where T : class
        {
            if (service != null)
            {
                context.RegisterService(service);
            }
            else
            {
                context.UnregisterService<T>();
            }
        }

        [ContextMenu("NiumaCombat/调试/重新初始化模块")]
        private void DebugReinitialize()
        {
            Initialize(_context);
            Debug.Log($"[NiumaCombat] 重新初始化完成：Initialized={IsInitialized}, Running={IsRunning}, Revision={CombatRevision}", this);
        }

        [ContextMenu("NiumaCombat/调试/启动模块")]
        private void DebugStartModule()
        {
            StartModule();
            Debug.Log($"[NiumaCombat] 启动模块：Initialized={IsInitialized}, Running={IsRunning}", this);
        }

        [ContextMenu("NiumaCombat/调试/停止模块")]
        private void DebugStopModule()
        {
            StopModule();
            Debug.Log("[NiumaCombat] 已停止模块 Tick。", this);
        }

        [ContextMenu("NiumaCombat/调试/手动 Tick 一次")]
        private void DebugTickOnce()
        {
            if (!EnsureDebugService())
            {
                return;
            }

            _combatService.Tick(debugTickSeconds);
            Debug.Log($"[NiumaCombat] 手动 Tick：Delta={debugTickSeconds:0.###}, Revision={CombatRevision}", this);
        }

        [ContextMenu("NiumaCombat/调试/打印模块状态")]
        private void DebugPrintStatus()
        {
            Debug.Log(
                $"[NiumaCombat] Initialized={IsInitialized}, Running={IsRunning}, Revision={CombatRevision}, HasService={_combatService != null}, HasHitboxService={_hitboxService != null}, HasAttributeQuery={_attributeQuery != null}, HasAttributeCommand={_attributeCommand != null}, HasFactionResolver={_factionResolver != null}, HpResourceId={hpResourceId}",
                this);
        }

        [ContextMenu("NiumaCombat/调试/检查目标存活")]
        private void DebugCheckTargetAlive()
        {
            if (!EnsureDebugService())
            {
                return;
            }

            Debug.Log($"[NiumaCombat] IsActorAlive：ActorId={debugTargetActorId}, Alive={_combatService.IsActorAlive(debugTargetActorId)}", this);
        }

        [ContextMenu("NiumaCombat/调试/检查 CanTarget")]
        private void DebugCheckCanTarget()
        {
            if (!EnsureDebugService())
            {
                return;
            }

            Debug.Log($"[NiumaCombat] CanTarget：Source={debugSourceActorId}, Target={debugTargetActorId}, Result={_combatService.CanTarget(debugSourceActorId, debugTargetActorId)}", this);
        }

        [ContextMenu("NiumaCombat/调试/打印最近输出结果")]
        private void DebugPrintRecentOutgoing()
        {
            PrintRecentResults(debugSourceActorId, CombatResultActorRole.Source);
        }

        [ContextMenu("NiumaCombat/调试/打印最近承受结果")]
        private void DebugPrintRecentIncoming()
        {
            PrintRecentResults(debugTargetActorId, CombatResultActorRole.Target);
        }

        private bool EnsureDebugService()
        {
            if (_combatService != null)
            {
                return true;
            }

            Debug.LogWarning("[NiumaCombat] 战斗服务尚未初始化。请先由模块启动器调用 Initialize(context)，或在 Inspector 绑定 Attribute Controller 后使用“重新初始化模块”。", this);
            return false;
        }

        private void PrintRecentResults(string actorId, CombatResultActorRole role)
        {
            if (!EnsureDebugService())
            {
                return;
            }

            var results = _combatService.GetRecentResults(actorId, role, 8);
            Debug.Log($"[NiumaCombat] 最近结果：ActorId={actorId}, Role={role}, Count={results.Length}", this);
            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i];
                if (result == null)
                {
                    continue;
                }

                Debug.Log(
                    $"[NiumaCombat] #{i} Type={result.ResultType}, Failure={result.FailureReason}, Source={result.SourceActorId}, Target={result.TargetActorId}, Final={result.FinalValue:0.##}, Hp={result.TargetHpBefore:0.##}->{result.TargetHpAfter:0.##}, Killed={result.IsKilled}",
                    this);
            }
        }
    }
}
