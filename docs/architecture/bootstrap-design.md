# Zipper 启动与装配设计（Bootstrap）

> 状态：**v1.0 草稿，待审阅**
> 定位：框架的**启动 / 装配机制**设计——模块如何被初始化、顺序如何保证、Bootstrap 代码放哪、`CancellationToken` 从哪来。**只给设计思路与接口形态，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/architecture/logging-design.md`（日志模块作为第一个 Bootstrap 使用者）、`docs/architecture/pool-manager-design.md`、`docs/planning/technical-roadmap.md` §5.5（组装层）
> 变更记录：v1.0 初稿（从 `logging-design.md` §5 迁出并扩展为通用机制）。

---

## 0. 这份文档怎么读

| 读者 | 建议路径 |
|---|---|
| 要立刻接线的人 | §3 契约 → §4 总 Bootstrap → §6 注册 |
| 要写某个模块 Bootstrap 的人 | §5 位置约定 → §10 各模块清单 |
| 要把关的人 | §2 分层理由 → §4 顺序可控性 → §11 验收 |

---

## 1. 目标与范围

### 1.1 要解决的问题

1. 各模块（日志 / 事件总线 / 资源 / 池 / 将来 UI、音频）**如何被初始化**
2. **初始化顺序**如何保证（例如：日志必须先于资源——资源初始化要打日志）
3. Bootstrap 代码**放哪**（不破坏程序集封装）
4. `CancellationToken` **从哪来**、什么时候才需要自己造

### 1.2 管什么 / 不管什么

**管**：模块 Bootstrap 契约、总 Bootstrap 编排、启动阶段顺序、位置约定、注册与初始化的分工、ct 传递。

**不管**：具体模块初始化内容（各模块自己的设计文档负责）；容器本身的装配语法（VContainer 负责）；场景切换后的二次初始化（暂不涉及）。

---

## 2. 为什么分层（模块 Bootstrap + 总 Bootstrap）

```
Zipper.DI（组装层）
 └─ ZipperBootstrapper               ← 总 Bootstrap：只做"按阶段编排"
      ├─ ZLoggerBootstrap   Phase=Logging     （日志最先）
      ├─ ZEventBusBootstrap Phase=Events
      ├─ ZResourceBootstrap Phase=Resources
      └─ ZObjectPoolBootstrap Phase=Pools
```

| 收益 | 说明 |
|---|---|
| **顺序可控** | 阶段顺序**显式写在总 Bootstrap 代码里**（编译期可见、单点控制），不依赖注册顺序或运行期约定 |
| **模块自包含** | 每个模块自己知道"怎么初始化我"，Bootstrap 与模块同程序集/同生命周期 |
| **可裁剪** | 不注册某模块的 Bootstrap → 它就不启动（模块可整体删除） |
| **职责分离** | 模块管"初始化自己"，组装层管"按什么顺序把你们串起来" |

---

## 3. 契约

```
public enum ZBootPhase          // 启动阶段（语义化，替代"魔法数字 Order"）
{
    Logging = 0, Events = 100, Resources = 200, Pools = 300, UI = 400,
}

public interface IZModuleBootstrap
{
    ZBootPhase Phase { get; }                              // 属于哪个启动阶段
    UniTask InitializeAsync(CancellationToken ct);          // 统一异步（资源加载天然异步）
}
```

- **契约放 `Zipper.Core`**（如 `Core/Boot/`）：底层契约层（Core 仅依赖 Unity + UniTask），所有模块都能实现，不反向依赖组装层
- 统一 `UniTask` + `CancellationToken`（roadmap：对外异步一律 UniTask）
- Bootstrap 只做**装配 / 初始化**，不含业务逻辑

---

## 4. 总 Bootstrap：阶段顺序显式写在代码里

```
public class ZipperBootstrapper : IAsyncStartable          // 由 VContainer 启动
{
    readonly IReadOnlyList<IZModuleBootstrap> _bootstraps;   // 集合注入（顺序不重要）

    public ZipperBootstrapper(IEnumerable<IZModuleBootstrap> bootstraps)
        => _bootstraps = bootstraps.ToList();

    public async UniTask StartAsync(CancellationToken ct)
    {
        // ↓↓↓ 顺序在这里一眼看清（编译期可见、单点控制），不依赖任何运行时约定
        await RunPhase(ZBootPhase.Logging,   ct);
        await RunPhase(ZBootPhase.Events,    ct);
        await RunPhase(ZBootPhase.Resources, ct);
        await RunPhase(ZBootPhase.Pools,     ct);
    }

    async UniTask RunPhase(ZBootPhase phase, CancellationToken ct)
    {
        foreach (var b in _bootstraps)                 // 阶段内顺序无关紧要
            if (b.Phase == phase)
                await b.InitializeAsync(ct);
    }
}
```

### 4.1 四种做法对照（为什么选"显式阶段 + 阶段内遍历"）

| 做法 | 顺序可控性 | 新增模块 | 评价 |
|---|---|---|---|
| 遍历集合直接 await | ❌ 不可控（枚举顺序未知） | 不用改代码 | **不要用** |
| 按 `int Order` 排序后 await | ⚠️ **运行期约定**：忘写/重复/写错 → 静默错序，无编译期保障 | 不用改代码 | 能用，但把"顺序正确性"押在每个模块自觉上 |
| **阶段顺序写死 + 阶段内遍历**（✅ 采纳） | ✅ **完全可控**：阶段顺序在代码里，一眼看清、改动集中一处 | 阶段内**不用改代码**；新阶段才加一行 | 兼顾"可控"与"低维护" |
| 完全显式逐个调用 | ✅ 最硬（编译期强制） | 每加模块改一行 | 模块很少（4~6 个）时也可接受 |

- **阶段内请让模块彼此独立**：真正的依赖应该跨阶段，而不是靠同阶段内的先后（后者没有保障）
- 若同阶段内也需稳定顺序，可再按类型名排序（但优先消除这种依赖）

### 4.2 为什么这段编排不用 R3（边界说明）

它是"一次性、有顺序、必须 await"的**流程编排**，不是"时间轴上的多条流"——`Where`/`Select`/`Concat` 只是换写法，不会让顺序更可控，反而带来三个代价：可读性下降、异常栈穿过操作符更难定位、异常语义（`OnErrorResume` 不终止流）与"初始化失败即启动失败"冲突。

更合适的优化只有两条：① 写法简洁化 `foreach (var b in _bootstraps.Where(x => x.Phase == phase)) await b.InitializeAsync(ct);`；② 同阶段模块彼此独立要并行 → `await UniTask.WhenAll(...)`。

（与 `core-design.md` §4.3 的 R3 用途边界一致：流程编排交给显式调用。）

---

## 5. 位置约定（Bootstrap 放哪）

| 角色 | 放哪 | 理由 |
|---|---|---|
| 契约 `IZModuleBootstrap` + `ZBootPhase` | **`Zipper.Core`**（`Core/Boot/`） | 底层契约层（Core 仅依赖 Unity + UniTask）；所有模块都能实现，不反向依赖组装层 |
| **各模块的 Bootstrap**（`ZLoggerBootstrap` / `ZResourceBootstrap` / `ZObjectPoolBootstrap`） | **各模块程序集内**（`Core/Logging/`、`Resources/`、`Pool/`） | ① **`internal` 可见性（硬约束）**：Bootstrap 常要访问模块内部类型（Router/Sink/dispatcher、池基类等），放组装层程序集看不到，只能改 public 或加 `InternalsVisibleTo`；② 模块自包含、可裁剪；③ 依赖方向干净（只依赖自身 + Core 契约） |
| **总 Bootstrap**（`ZipperBootstrapper`） | **组装层**（`Zipper.DI`，未来 `Zipper.Runtime`） | 装配与顺序编排正是组装层职责 |
| 容器注册代码 | 组装层（`GameLifetimeScope.Configure`） | 同上 |

**一条关键区分**：

| 事 | 归谁 |
|---|---|
| **注册**（`IContainerBuilder`：`builder.Register<...>()`） | 组装层 |
| **初始化**（运行时装配：开文件、加载 catalog、创建驱动对象） | 模块自己的 Bootstrap |

→ **模块 Bootstrap 不应持有 `IContainerBuilder`**（否则会成为"第二个注册入口"，职责混乱、顺序失控）。

**现状改动点**：`ResourcesBootstrapper` 目前在 `Assets/Zipper/DI/`（Assembly-CSharp 程序集）。按本约定应**迁到 `Assets/Zipper/Resources/`**（进 `Zipper.Resources` 程序集）并改名为 `ZResourceBootstrap`、实现 `IZModuleBootstrap`；若它只用 public 类型，留在 DI 也能跑，但会与其它模块的 Bootstrap 位置不一致。

---

## 6. 容器注册（组装层）

```
// 各模块服务（日志用"同一实例双暴露"写法：Bootstrap 需注入具体类型做两段式装配 Attach）
builder.Register<ZLogger>(Lifetime.Singleton).As<IZLogger>();
builder.Register<IZEventBus, ZEventBus>(Lifetime.Singleton);
builder.Register<IZResourceManager, ZResourceManager>(Lifetime.Singleton);
builder.Register<IZObjectPoolManager, ZObjectPoolManager>(Lifetime.Singleton);

// 各模块 Bootstrap：注册为 IZModuleBootstrap 集合的一员
builder.Register<ZLoggerBootstrap>(Lifetime.Singleton).As<IZModuleBootstrap>();
builder.Register<ZEventBusBootstrap>(Lifetime.Singleton).As<IZModuleBootstrap>();
builder.Register<ZResourceBootstrap>(Lifetime.Singleton).As<IZModuleBootstrap>();
builder.Register<ZObjectPoolBootstrap>(Lifetime.Singleton).As<IZModuleBootstrap>();

// 总 Bootstrap：唯一入口点
builder.RegisterEntryPoint<ZipperBootstrapper>(Lifetime.Singleton);
```

> VContainer 支持 `IEnumerable<T>` 集合注入 ✓（总 Bootstrap 用 `IEnumerable<IZModuleBootstrap>` 取全部）。

---

## 7. `CancellationToken` 从哪来、什么时候要自己造

| 来源 | 何时用 |
|---|---|
| **VContainer 注入的 Scope 取消令牌**（`IAsyncStartable.StartAsync(ct)` 由容器传入，Scope 销毁即取消） | ✅ **默认路径**：总 Bootstrap 收到后**一路透传**给各模块 `InitializeAsync(ct)`，**不需要自己创建** |
| `default` / `CancellationToken.None` | 调用方无取消需求时（零成本、无分配） |
| **自己创建 `CancellationTokenSource`** | 仅两种场景：① **超时**——`CreateLinkedTokenSource(ct)` + `CancelAfter(...)`（**必须链接上游 ct**，用完 `using`/`Dispose`）；② **主动取消**（用户取消 / 切场景）——由**发起取消的一方**持有，`Cancel()` + `Dispose()` |

**三个反模式**：接收方在方法内部 `new CancellationTokenSource()`（无意义且易泄漏）；创建了不 `Dispose`；拿到 ct 却不检查（`ThrowIfCancellationRequested`）也不透传给下游 async API。

⚠️ **收尾（Flush / 停线程 / 释放资源）不要绑 ct**：Scope 取消时 ct 会触发，若收尾依赖它，**尾部工作会被跳过**（例如日志尾部丢失）。收尾统一放各 Bootstrap 的 `Dispose()`。

---

## 8. 失败策略

- 模块初始化失败 → **抛异常 → 启动失败**（可见、可定位；总 Bootstrap 不吞异常）
- 允许降级的模块（如日志的文件 Sink 不可用）→ 由**该模块自行 catch 并记录**，然后以降级形态继续（例如仅 Console 输出）
- 总 Bootstrap 不提供"跳过失败模块继续启动"的选项（避免半初始化状态静默运行）

---

## 9. 备选方向：让顺序"根本不需要管"（v1 不做）

让模块"**注入即就绪**"——例如日志把 Router/Sink 装配放在 `ZLogger` 构造或惰性初始化里，主线程驱动对象改为首次需要 Console 输出时惰性创建 → 任何模块注入到 `IZLogger` 就能直接用，**不再依赖阶段顺序**。

- 好处：顺序约束从"框架约定"降级为"不存在"
- 代价：装配时机变隐式（构造里做 IO / 创建 GameObject 需要主线程与时机保证），出错时更难定位；惰性创建本身也依赖"第一次调用发生在主线程"
- **结论**：v1 采用 §4 的"显式阶段"（简单、可控、易调试）；本方向留作将来简化顺序依赖的备选

---

## 10. 各模块启动清单（Phase 与职责）

| Phase | 模块 | Bootstrap 做什么 | 依赖 |
|---|---|---|---|
| **Logging (0)** | 日志 | 建 Router 与 Sink（Console/File）；创建 `ZMainThreadDispatcher` 与驱动对象；注册退出 / `OnApplicationPause` flush | 无（最先） |
| **Events (100)** | 事件总线 | 若需要：装配总线实例 / 清理策略 | 日志（可选，用于告警） |
| **Resources (200)** | 资源管理器 | 初始化 Addressables（加载 catalog）；可选预下载 / 预热 | 日志 |
| **Pools (300)** | 对象池管理器 | 若需要：预热常用池；建立池与母本的登记 | 日志、资源（母本来源） |
| **UI (400)** | UI 管理器（将来） | 建 UI 根节点 / 层级、加载常驻面板 | 日志、资源、池、事件总线 |

> 顺序的"必要性"都来自**跨阶段依赖**：后一阶段的模块会用到前一阶段的服务（最典型：所有模块都要打日志 → 日志必须最先）。

---

## 11. 验收标准与测试点

| 验收点 | 场景 | 断言 |
|---|---|---|
| 顺序正确 | 容器启动 | 各模块 Bootstrap **按 Phase 顺序**执行（日志先于资源） |
| 依赖可用 | 资源 Bootstrap 内使用 `IZLogger` | 此时日志已就绪（能正常输出与落盘） |
| 可裁剪 | 不注册某模块 Bootstrap | 该模块不启动，其余不受影响 |
| 新增模块 | 加一个同 Phase 模块 | **无需改总 Bootstrap** |
| 失败可见 | 某模块初始化抛异常 | 启动失败并明确指向该模块（不静默跳过） |
| ct 透传 | Scope 销毁时正在初始化 | 初始化被取消（不残留） |
| 收尾不丢 | Scope 销毁 / 应用退出 | 各模块 `Dispose()` 完成收尾（例如日志尾部已落盘） |

---

## 12. 待决项

| 项 | 内容 | 建议 |
|---|---|---|
| `ZBootPhase` 取值粒度 | 是否需要更细阶段（如 `ResourcesPreload`） | 先按 §3 五档，出现真实需求再加 |
| 同阶段内排序 | 是否强制按类型名稳定排序 | 不强制；优先消除同阶段依赖 |
| 惰性初始化 | 是否改走"注入即就绪"（§9） | v1 不做 |
| 总 Bootstrap 与场景切换 | 多场景是否需要二次初始化 / 子 Scope | 延后（单场景 Demo 不需要） |
| 启动耗时统计 | 是否记录每个模块初始化耗时 | 建议做（日志一次 Info 即可，代价极低） |

---

## 13. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v1.0.1 | 2026-09-12 | 措辞同步：Core 依赖边界更正为"**仅依赖 Unity + UniTask**"（契约的 `UniTask` 返回类型所致），详见 `core-design.md` v0.9.3 / `roadmap` v0.10 |
| v1.0 | 2026-09-10 | 初稿：从 `logging-design.md` §5 迁出并扩展为通用机制——分层理由、`IZModuleBootstrap`/`ZBootPhase` 契约、总 Bootstrap 显式阶段编排（含四方案对照与"不用 R3"边界）、位置约定（注册 vs 初始化、现状改动点）、容器注册、`CancellationToken` 用法、失败策略、惰性初始化备选、各模块启动清单、验收与待决项 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
