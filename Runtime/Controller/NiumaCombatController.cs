using NiumaAttribute.Controller;
using NiumaAttribute.Service;
using NiumaCombat.Service;
using NiumaCore.Module;
using UnityEngine;

namespace NiumaCombat.Controller
{
    [DisallowMultipleComponent]
    public sealed class NiumaCombatController : MonoBehaviour, IGameModule
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
        [Tooltip("Awake 时自动初始化战斗模块。核心场景手动统一初始化时可关闭。")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("OnEnable 时自动启动模块。核心场景手动统一 StartModule 时可关闭。")]
        [SerializeField] private bool startOnEnable = true;

        [Tooltip("初始化时是否把 ICombatService/ICombatQuery/ICombatCommand/ICombatHitboxService 注册进 GameContext。核心场景建议开启。")]
        [SerializeField] private bool registerServiceToContext = true;

        [Tooltip("是否由本控制器 Update 自动驱动 Tick。若已有统一模块启动器调用 IGameModule.Tick，请关闭，避免 Hitbox 超时计时重复推进。")]
        [SerializeField] private bool driveTickInUpdate = true;

        private CombatService _combatService;
        private CombatHitboxService _hitboxService;
        private ICombatConfigurationService _configurationService;
        private GameContext _context;
        private IAttributeQuery _attributeQuery;
        private IAttributeCommand _attributeCommand;
        private ICombatFactionResolver _factionResolver;
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
                if (_context == null && resolveAttributeFromContext && attributeController == null && !_warnedMissingAwakeContext)
                {
                    _warnedMissingAwakeContext = true;
                    Debug.LogWarning("[NiumaCombatController] Awake 自动初始化时没有 GameContext，也没有绑定 NiumaAttributeController。核心场景建议由模块启动器调用 Initialize(context)，或在 Inspector 绑定 Attribute Controller。", this);
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

        public void Initialize(GameContext context)
        {
            var previousService = _combatService;
            var previousHitboxService = _hitboxService;
            var previousConfiguration = _configurationService;
            var previousContext = _context;
            var previousInitialized = IsInitialized;
            var previousRunning = IsRunning;

            ICombatService oldCombatService = null;
            ICombatQuery oldCombatQuery = null;
            ICombatCommand oldCombatCommand = null;
            ICombatHitboxService oldHitboxService = null;
            ICombatConfigurationService oldConfigurationService = null;

            if (context != null)
            {
                context.TryGetService(out oldCombatService);
                context.TryGetService(out oldCombatQuery);
                context.TryGetService(out oldCombatCommand);
                context.TryGetService(out oldHitboxService);
                context.TryGetService(out oldConfigurationService);
            }

            try
            {
                _context = context;
                ResolveAttributeService(context);
                ResolveFactionResolver(context);

                _combatService = new CombatService(_attributeQuery, _attributeCommand, _factionResolver, context?.EventBus, hpResourceId, maxGlobalResults, maxActorResults);
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
                    RestoreRegisteredServices(context, oldCombatService, oldCombatQuery, oldCombatCommand, oldHitboxService, oldConfigurationService);
                }

                Debug.LogError($"[NiumaCombatController] 初始化失败，已回滚：{ex.Message}", this);
                throw;
            }
        }

        public void StartModule()
        {
            if (!IsInitialized)
            {
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
            _attributeQuery = query;
            _attributeCommand = command;
            _configurationService?.SetAttributeService(query, command);
        }

        public void SetFactionResolver(ICombatFactionResolver resolver)
        {
            _factionResolver = resolver;
            _configurationService?.SetFactionResolver(resolver);
        }

        private void ResolveAttributeService(GameContext context)
        {
            _attributeQuery = null;
            _attributeCommand = null;

            if (resolveAttributeFromContext && context != null)
            {
                context.TryGetService(out _attributeQuery);
                context.TryGetService(out _attributeCommand);
            }

            if ((_attributeQuery == null || _attributeCommand == null) && attributeController != null)
            {
                _attributeQuery ??= attributeController.AttributeQuery;
                _attributeCommand ??= attributeController.AttributeCommand;
            }
        }

        private void ResolveFactionResolver(GameContext context)
        {
            _factionResolver = null;

            if (resolveFactionFromContext && context != null)
            {
                context.TryGetService(out _factionResolver);
            }

            if (_factionResolver == null && factionResolverProvider is ICombatFactionResolver resolver)
            {
                _factionResolver = resolver;
            }
        }

        private void RegisterServicesToContext(GameContext context)
        {
            context.RegisterService<ICombatService>(_combatService);
            context.RegisterService<ICombatQuery>(_combatService);
            context.RegisterService<ICombatCommand>(_combatService);
            context.RegisterService<ICombatHitboxService>(_hitboxService);
            context.RegisterService<ICombatConfigurationService>(_configurationService);
        }

        private void UnregisterServicesFromContext(GameContext context)
        {
            if (ReferenceEquals(context.GetService<ICombatService>(), _combatService))
            {
                context.UnregisterService<ICombatService>();
            }

            if (ReferenceEquals(context.GetService<ICombatQuery>(), _combatService))
            {
                context.UnregisterService<ICombatQuery>();
            }

            if (ReferenceEquals(context.GetService<ICombatCommand>(), _combatService))
            {
                context.UnregisterService<ICombatCommand>();
            }

            if (ReferenceEquals(context.GetService<ICombatHitboxService>(), _hitboxService))
            {
                context.UnregisterService<ICombatHitboxService>();
            }

            if (ReferenceEquals(context.GetService<ICombatConfigurationService>(), _configurationService))
            {
                context.UnregisterService<ICombatConfigurationService>();
            }
        }

        private static void RestoreRegisteredServices(
            GameContext context,
            ICombatService oldCombatService,
            ICombatQuery oldCombatQuery,
            ICombatCommand oldCombatCommand,
            ICombatHitboxService oldHitboxService,
            ICombatConfigurationService oldConfigurationService)
        {
            RestoreService(context, oldCombatService);
            RestoreService(context, oldCombatQuery);
            RestoreService(context, oldCombatCommand);
            RestoreService(context, oldHitboxService);
            RestoreService(context, oldConfigurationService);
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
    }
}