# NiumaCombat

## 模块定位

NiumaCombat 是战斗事实层，负责命中合法性、伤害 / 治疗结算、HP 修改、死亡事实判断、Hitbox / Hurtbox 运行时协作和 CombatResult 输出。

它不负责攻击动画、连招树、技能冷却、受击表现、击退位移、掉落和奖励。动作编排交给后续 `NiumaAction`，动画播放与 HFSM 仲裁交给 `NiumaTPC`，Buff / Debuff 交给 `NiumaEffect`。

## 依赖模块

| 依赖 | 用途 |
| --- | --- |
| `NiumaCore.Runtime` | `GameContext`、`IGameModule`、事件总线 |
| `NiumaAttribute.Runtime` | 读取攻击 / 防御 / 抗性属性，扣除或恢复 `hp` 资源 |

程序集文件在 `Runtime/NiumaCombat.Runtime.asmdef`，不是模块根目录。它已引用以上两个 Runtime 程序集。检查文件时请递归扫描 `NiumaCombat/**/*.asmdef`，或直接查看：

```text
D:\zhizuo\sava\NiumaM\Assets\Game\Moudle\NiumaCombat\Runtime\NiumaCombat.Runtime.asmdef
```

## 核心场景挂载

推荐在核心场景中创建：

```text
CoreScene
└── BootstrapRoot
    └── GameplayServicesRoot
        └── CombatRoot
```

`CombatRoot` 挂载 `NiumaCombatController`。

### NiumaCombatController

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `Attribute Controller` | 拖核心场景里的 `NiumaAttributeController` | 可以 | 留空时会尝试从 `GameContext` 读取 `IAttributeQuery / IAttributeCommand` |
| `Resolve Attribute From Context` | 勾选 | 可以 | 核心场景统一注册 Attribute 服务时开启 |
| `Faction Resolver Provider` | 拖实现 `ICombatFactionResolver` 的阵营组件 | 可以 | 留空时只阻止攻击自己，其它目标默认允许 |
| `Resolve Faction From Context` | 有全局阵营服务时勾选 | 可以 | 从 `GameContext` 获取 `ICombatFactionResolver` |
| `Hp Resource Id` | 默认 `hp` | 不建议空 | 必须和 Attribute 的生命资源 ID 一致 |
| `Max Global Results` | 默认 128 | 不建议空 | CombatResult 全局缓存上限 |
| `Max Actor Results` | 默认 32 | 不建议空 | 单 Actor 最近战斗结果缓存上限 |
| `Initialize On Awake` | 核心场景统一启动时可关闭 | 可以 | 没有 `GameContext` 时会跳过自动初始化并给出警告 |
| `Start On Enable` | 单模块测试可开启 | 可以 | 核心场景统一启动时可关闭 |
| `Register Service To Context` | 开启 | 可以 | 只注册 `ICombatService` 和 `ICombatHitboxService` |
| `Drive Tick In Update` | 没有统一模块 Tick 时开启 | 可以 | 如果外部已经调用 `IGameModule.Tick`，需要关闭，避免 Hitbox 超时重复推进 |
| `Debug Source Actor Id` | 调试用攻击者 ActorId | 可以 | 只给右键调试菜单使用，正式战斗不会读取 |
| `Debug Target Actor Id` | 调试用目标 ActorId | 可以 | 用于检查目标存活、CanTarget 和最近承受结果 |
| `Debug Tick Seconds` | 默认 `0.1` | 可以 | 用于右键菜单手动推进一次 Combat Tick |

`GameContext` 中只注册：

- `ICombatService`
- `ICombatHitboxService`

需要查询或命令能力时，从 `ICombatService` 使用，因为它继承了 `ICombatQuery` 和 `ICombatCommand`。

程序也可以在初始化前后调用 `SetAttributeService`、`SetFactionResolver`、`SetEventBus` 注入依赖。显式注入优先于 `GameContext` 自动解析，适合单模块测试或特殊战斗场景。

`SetEventBus(null)` 表示取消外部事件总线覆盖，重新回退使用当前 `GameContext.EventBus`；如果当前没有 `GameContext`，则 Combat 暂时不发布事件，但仍会返回 `CombatResult` 并递增 Revision。

### Inspector 右键调试

在 `NiumaCombatController` 组件右上角菜单中可以使用：

| 菜单 | 作用 |
| --- | --- |
| `NiumaCombat/调试/重新初始化模块` | 重新创建 CombatService / CombatHitboxService，并按配置注册到 GameContext |
| `NiumaCombat/调试/启动模块` | 调用 `StartModule()` |
| `NiumaCombat/调试/停止模块` | 调用 `StopModule()`，停止 Tick |
| `NiumaCombat/调试/手动 Tick 一次` | 按 `Debug Tick Seconds` 推进一次 Combat Tick |
| `NiumaCombat/调试/打印模块状态` | 输出初始化、运行状态、依赖解析和 Revision |
| `NiumaCombat/调试/检查目标存活` | 用 `Debug Target Actor Id` 调用 `IsActorAlive` |
| `NiumaCombat/调试/检查 CanTarget` | 用 `Debug Source Actor Id` 和 `Debug Target Actor Id` 检查目标是否合法 |
| `NiumaCombat/调试/打印最近输出结果` | 打印 `Debug Source Actor Id` 作为攻击者的最近 CombatResult |
| `NiumaCombat/调试/打印最近承受结果` | 打印 `Debug Target Actor Id` 作为目标的最近 CombatResult |

注意：`重新初始化模块` 会重新创建 `CombatService` 和 `CombatHitboxService`，因此旧的命中记录、有效 Hitbox、最近结果缓存都会被清空。这是调试菜单的预期行为，正式流程应由核心场景启动器控制初始化时机。

## 角色场景挂载

推荐角色层级：

```text
ActorRoot
├── Model
├── Hurtboxes
│   ├── Hurtbox_Body
│   └── Hurtbox_Head
└── WeaponSocket
    └── WeaponHitbox
```

### CombatActorIdentity

挂在角色根节点 `ActorRoot`。

| 字段 | 建议填写 | 说明 |
| --- | --- | --- |
| `Actor Id` | 运行时唯一角色 ID | Hurtbox 和 Hitbox 都会用它识别角色 |
| `Faction Id` | 阵营 ID | 第一版 CombatService 不直接读取，正式阵营过滤由 `ICombatFactionResolver` 解释 |
| `Actor Tags` | 如 `player`、`enemy`、`boss` | 会和 Hurtbox 标签合并后参与目标过滤 |

### CombatHurtbox

挂在角色受击部位，例如身体、头部、腿部。这个物体必须有 `Collider`，建议勾选 `Is Trigger`。

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `Owner` | 拖角色根节点的 `CombatActorIdentity` | 可以 | 留空时自动向父节点查找 |
| `Hurtbox Id` | 如 `body`、`head` | 不建议空 | 进入 `CombatDamageRequest.HurtboxId` |
| `Body Part Id` | 如 `body`、`head` | 可以 | 留空时使用 `Hurtbox Id` |
| `Part Damage Multiplier` | 身体 1，头部 2，腿部 0.8 | 可以 | 会覆盖写入 `CombatDamageRequest.BodyPartMultiplier` |
| `Hurtbox Tags` | 如 `weakpoint`、`armored` | 可以 | 和 Actor Tags 合并参与过滤 |

如果同物体 Collider 没勾选 `Is Trigger`，`OnValidate` 会给出警告。

## Hitbox 使用

`CombatHitboxDriver` 挂在武器、攻击范围、技能触发体或调试攻击体上。这个物体通常也需要一个 Trigger Collider。

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `Combat Service Provider` | 拖核心场景 `CombatRoot` 上的 `NiumaCombatController` | 可以 | 它实现 `ICombatRuntimeServiceProvider`；留空并开启自动查找时会在场景中查找 |
| `Auto Find Combat Provider` | 单 CombatRoot 场景可开启 | 可以 | 自动查找失败会明确提示需要绑定什么 |
| `Owner Identity` | 拖攻击者根节点的 `CombatActorIdentity` | 可以 | 留空时向父节点查找 |
| `Owner Actor Id` | 调试时填写 | 可以 | 正式角色建议用 `Owner Identity` |
| `Definition` | 配置 Hitbox 和伤害模板 | 不建议空 | 包含 `HitboxId`、伤害模板、激活时间和重复命中规则 |
| `Hitbox Collider` | 拖当前武器或范围 Collider | 可以 | 留空时自动取当前物体 Collider |
| `Target Layer Mask` | 选择可命中的目标层 | 可以 | 不在 LayerMask 内的 Collider 不会提交 |
| `Use Trigger Callbacks` | 常规武器开启 | 可以 | 开启后收集 `OnTriggerEnter / OnTriggerStay` |
| `Submit On Trigger Stay` | 目标可能提前在范围内时开启 | 可以 | Driver 会在 `FixedUpdate` 按 Hurtbox 去重后批量提交 |
| `Open On Enable` | 只建议调试使用 | 可以 | 正式攻击应由动画事件或 ActionBridge 调用 `OpenHitbox` |
| `Log Warnings` | 开发期建议开启 | 可以 | 缺失服务、身份、Hurtbox 时输出警告 |

`DamageTemplate` 只填写伤害规则字段，例如：

- `DamageType`
- `BasePower`
- `AttackAttributeId`
- `AttackScale`
- `DefenseAttributeId`
- `DefenseScale`
- `ResistanceAttributeId`
- `DamageMultiplier`
- `CritRateOverride`
- `CritDamageOverride`
- `CanCrit`
- `IgnoreDefense`
- `IgnoreResistance`
- `Reaction`

不要在模板里填写运行时字段，例如 `RequestId`、`AttackInstanceId`、`SourceActorId`、`TargetActorId`、`HitboxId`、`HurtboxId`、`TargetTags`、`HitPoint`、`HitDirection`、`SourcePosition`。如果误填，Driver 会在打开 / 生成请求时警告并自动清空这些字段。

`Auto Find Combat Provider` 只是兜底方案。Driver 会缓存成功找到的 Provider，但正式场景仍建议手动把核心场景 `CombatRoot` 上的 `NiumaCombatController` 拖到 `Combat Service Provider`，这样配置更直观，也避免测试场景里误找到其它 CombatRoot。

## 基础流程

```text
TPC / Action 动画事件
-> CombatHitboxDriver.OpenHitbox()
-> Trigger / Overlap 检测 CombatHurtbox
-> FixedUpdate 批量提交 Hurtbox
-> CombatService.ApplyDamage()
-> NiumaAttribute 扣 hp
-> 输出 CombatResult / 事件
-> UI 或 TPC Bridge 播放飘字、受击、死亡表现
```

## TPC 表现桥接

`TPCCombatBridge` 位于 `Runtime/TPCBridge`，属于独立程序集 `NiumaCombat.TPCBridge`。它引用 `NiumaCombat.Runtime` 和 `NiumaTPC.Runtime`，但 `NiumaCombat.Runtime` 不反向引用 TPC。

建议挂载位置：

```text
PlayerRoot
├── PlayerModuleController
├── CombatActorIdentity
└── CombatBridge
    └── TPCCombatBridge
```

### TPCCombatBridge

| 字段 | 建议填写 | 可留空 | 说明 |
| --- | --- | --- | --- |
| `玩家模块控制器` | 拖 `PlayerRoot` 上的 `PlayerModuleController` | 不建议 | 留空且自动查找失败时无法播放 TPC 战斗表现 |
| `目标身份` | 拖当前角色根节点的 `CombatActorIdentity` | 不建议 | 用 `ActorId` 过滤 CombatResult，避免别人的受击结果触发当前角色动作 |
| `自动从父节点查找` | 建议开启 | 可以 | 桥接脚本挂在 `PlayerRoot/CombatBridge` 时会自动向父节点找控制器和身份 |
| `订阅 Combat 事件` | 使用统一 `GameContext` 时开启 | 可以 | 开启后 `Initialize(GameContext)` 会订阅 Combat 事件；关闭时只能由外部直接调用 `ApplyCombatResult` |
| `接收伤害事件` | 开启 | 可以 | `CombatDamageAppliedEvent` 到达时播放轻受击 / 重受击 / 击倒 |
| `接收死亡事件` | 开启 | 可以 | `CombatKilledEvent` 到达时播放死亡表现 |
| `接收格挡事件` | 开启 | 可以 | `CombatHitConfirmedEvent` 中 `Blocked` 会播放格挡表现；第一版 `Immune` 不走该事件路径 |
| `重受击伤害阈值` | 例如 `30` | 可以 | `FinalValue` 大于等于该值时映射为重受击 |
| `击倒力度阈值` | 例如 `8` | 可以 | `Reaction.KnockbackForce` 达到阈值或 `ForceKnockdown=true` 时映射为击倒 |
| `死亡强制冻结输入` | 建议开启 | 可以 | 死亡表现会覆盖请求并冻结输入 |
| `输出警告日志` | 开发期建议开启 | 可以 | 缺配置或播放失败时输出 Warning |

事件映射规则：

| CombatResult | TPC 表现 |
| --- | --- |
| `IsKilled=true` | `死亡` |
| `ResultType=Blocked` | `格挡 / 免伤` |
| `Reaction.ForceKnockdown=true` | `击倒` |
| `Reaction.KnockbackForce >= 击倒力度阈值` | `击倒` |
| `ResultType=Damage` 且 `FinalValue >= 重受击伤害阈值` | `重受击` |
| `ResultType=Damage` 且未达到重受击阈值 | `轻受击` |

注意：

- `TPCCombatBridge` 只把 Combat 结果翻译成 `TPCCombatReactionRequest`，不扣血、不判断命中、不切换 TPC 内部 HFSM。
- 真正播放动作由 `PlayerModuleController.TryPlayCombatReaction` 进入 TPC `ActionArbiter / OverrideState`。
- TPC 表现动画仍在 `TPCCombatReactionProfile` 中配置，动画数据使用 `MotionClipData`，可通过 TPC RootMotionBaker 烘焙。
- `TPCCombatBridge` 会把 `CombatResult.HitDirection` 作为 `Reaction.HitDirection` 为空时的兜底方向，并透传 `StaggerSeconds`、`KnockbackDistance`、`HitVfxCueId`、`HitAudioCueId` 到 `TPCCombatReactionRequest`。第一版 TPC 只保留这些字段，后续由受击状态、VFX 或 Audio 桥接消费。
- 第一版 `Immune` 是过滤 / 拒绝类结果，不会通过 `CombatHitConfirmedEvent` 驱动 TPC 格挡表现；如果后续需要免疫动作或免疫音效，应订阅 `CombatResultRejectedEvent` 或新增专门事件。

## 伤害公式

第一版公式：

```text
AttackPart = BasePower + AttackAttribute * AttackScale
AfterDamageMultiplier = AttackPart * DamageMultiplier
AfterCritical = AfterDamageMultiplier * CriticalMultiplier
AfterDefense = IgnoreDefense ? AfterCritical : AfterCritical - DefenseAttribute * DefenseScale
AfterResistance = IgnoreResistance ? AfterDefense : AfterDefense * (1 - Resistance)
FinalValue = max(0, AfterResistance * BodyPartMultiplier)
```

`DamageMultiplier = 0` 表示本次伤害倍率为 0。`DefenseScale = 0` 表示本次防御减免为 0，不会回退为 1；如果想跳过防御读取，使用 `IgnoreDefense`。

## 常见问题

| 问题 | 处理 |
| --- | --- |
| Hitbox 打不开 | 检查 `Combat Service Provider`、`Owner Identity`、攻击者 `hp` 是否大于 0 |
| 打不到目标 | 检查目标 Layer、目标是否有 `CombatHurtbox`、Hurtbox Collider 是否勾选 `Is Trigger` |
| 同一目标重复刷拒绝结果 | 默认 `HitSameTargetOnce` 会在 Driver 和 Service 双层过滤；需要多段命中时关闭该项并配置冷却 |
| 阵营过滤不生效 | 需要实现并注入 `ICombatFactionResolver` |
| 飘字位置不对 | 检查 `CombatResult.HitPoint`，Driver 会优先使用命中 Collider 的 ClosestPoint |

## 后续扩展

- `NiumaAction` 接入连招树、动作窗口、武器来源。
- `NiumaTPC` Bridge 接收 CombatResult 播放受击、硬直、击退、死亡表现。
- `NiumaSkill` 后续可添加 `CombatOutputs`，命中后调用 Combat。
- UI Toolkit Bridge 后续输出伤害数字、治疗数字、暴击、免疫、击杀提示。

## 阶段 4 修正补充

- `Initialize On Awake` 不是无条件初始化。它只有在能找到 Attribute 依赖时才会生效：已经有 `GameContext` 且启用 `Resolve Attribute From Context`，或 Inspector 绑定了 `NiumaAttributeController`，或代码已调用 `SetAttributeService(query, command)`。
- `StartModule()` 同样会先检查 Attribute 依赖路径。没有 `GameContext`、没有 `NiumaAttributeController`、也没有外部注入 Attribute 服务时，会输出警告并停止，不会创建一个后续必定结算失败的半成品 CombatService。
- `Initialize(context)` 完成前必须解析到 `IAttributeQuery` 和 `IAttributeCommand`；缺失时会回滚初始化并抛出错误，避免模块进入假初始化状态。
- 只要 `CombatDamageRequest.AttackInstanceId` 非空，`ApplyDamage` 会校验该 Hitbox 仍处于 active 状态，并校验请求的 `SourceActorId` 与 Hitbox 的 `OwnerActorId` 一致。已关闭 Hitbox 或伪造 Source 的请求会被拒绝。
- Skill 直接结算时，如果不需要 Hitbox 生命周期和防重复命中，请不要填写 `AttackInstanceId`。仅填写 `HitboxId` 作为日志或表现来源时，不要求 `HurtboxId`，也不会触发 active Hitbox 校验。
- `DamageTemplate` 中不要填写运行时上下文字段，范围包括 `RequestId`、`AttackInstanceId`、`SourceActorId`、`TargetActorId`、`SkillId`、`HitboxId`、`HurtboxId`、`TargetTags`、`HitPoint`、`HitDirection`、`SourcePosition`。

## 阶段 4.1 修正补充

- `ApplyDamage` 在发布 `CombatHitConfirmedEvent` 前会先预留命中记录和有效命中次数，防止同步事件订阅者在同一帧重入 `ApplyDamage` 时绕过 `MaxHitCount` 或重复命中过滤。若后续 Attribute 写入失败，会回滚这次预留。
- `TryRegisterHit` 会走同一套预留命中记录逻辑。传入不存在、已关闭或已超出 `MaxHitCount` 的 `AttackInstanceId` 时返回 `false`，不会写入命中记录。
- 如果直接 new `CombatService` 但没有注入 HitboxService，带 `AttackInstanceId` 的 `TryRegisterHit / ApplyDamage` 会按 `InternalError` 处理；只有 HitboxService 存在但实例已关闭或不存在时，才返回 `HitboxNotActive`。
- `MaxHitCount` 的最终判定集中在命中预留阶段；`ApplyDamage` 不再做多层重复预检查，避免状态判断分散。
- `ICombatHitboxService` 不再暴露 `Tick`。业务层只使用 `OpenHitbox`、`CloseHitbox`、`IsHitboxActive`；Hitbox 超时关闭由 `ICombatService.Tick` 内部驱动。
- `CombatResultType.Blocked` 表示伤害公式最终值 `FinalValue <= 0`，例如被防御、抗性或倍率压到 0。Blocked 会触发 `CombatHitConfirmedEvent`，不会触发 `CombatDamageAppliedEvent`，也不是 Rejected。
- `CombatResultType.Miss` 第一版保留为后续命中 / 闪避系统使用；当前 Runtime 不主动生成 Miss。技能命中率裁决仍由 Skill 层完成，未命中时不调用 Combat。
- `Reaction.HitDirection` 的最终兜底由 `CombatService` 处理；当 `Reaction` 为空时，`CombatResult.HitDirection` 会保留请求级方向，供 TPC / UI 表现桥接兜底使用。Driver 只写入请求级 `HitDirection`，不再直接改写 `Reaction`。
- `CombatHitboxDriver` 自动查找 `ICombatRuntimeServiceProvider` 只是兜底，并带全局 1 秒节流。正式场景仍建议把 `NiumaCombatController` 拖到 `Combat Service Provider`。
- `ApplyHeal` 第一版允许 `SourceActorId` 为空，不检查治疗来源是否存活；`TargetActorId` 必须存在且目标存活。

## Skill 接入

第五阶段开始，`NiumaSkill` 可通过 `SkillDefinition.CombatOutputs` 调用 Combat。

- Combat 数据层提供 `SkillCombatOutputData`，字段包括 `DamageTemplate`、`HealTemplate`、`ApplyToPrimaryTarget`、`ApplyToAllResolvedTargets`。
- `SkillCombatOutputData` 位于 `NiumaCombat.Runtime/Data`，这是第五阶段的有意设计：`NiumaSkill` 已经需要 `ICombatCommand`，所以直接引用 `NiumaCombat.Runtime`。第一版不额外拆 `SkillBridge`。
- `NiumaSkillController` 需要绑定 `NiumaCombatController`，或通过 `GameContext` 解析 `ICombatCommand`。
- Skill 命中成功后才调用 `ApplyDamage / ApplyHeal`；Skill 未命中不调用 Combat。
- Skill 直接结算 Combat 时不填写 `AttackInstanceId`，因此不参与 Hitbox 激活周期的防重复命中。
- `DamageTemplate` / `HealTemplate` 里不要手动填写 `RequestId`、`SourceActorId`、`TargetActorId`、`SkillId`、`AttackInstanceId` 等运行时字段。`SkillDefinition.OnValidate` 和 Skill 运行时生成请求时都会清理这些字段，再写入本次释放的上下文。
- CombatResult 会返回到 `SkillCastResult.CombatResults`，后续 UI / TPC / Audio 桥接以 CombatResult 为事实来源。
