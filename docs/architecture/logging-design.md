# Zipper 日志模块设计（ZLogger）

> 状态：**v1.1 草稿，待审阅**
> 定位：`Zipper.Core` 的日志模块 v1 设计定稿（取代 `core-design.md` §3 早期形态）。**只给设计思路与接口形态，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/architecture/core-design.md`（Core 总览）、`docs/architecture/pool-manager-design.md`（池要接日志）、`LocalNotes/logging-implementation-guide.md`（本地实现教程，不入库）
> 变更记录：v1.0 初稿；**v1.1** 落实第二轮决策——剥离方案 a、驱动方案 a、caller 三件套、context 保留并说明用法、**Bootstrap 分层（模块 Bootstrap + DI 总 Bootstrap）**、**文件按日期切分**、**去掉去重折叠**、池侧用构造参数传 logger。

---

## 1. 定位与既定约束

### 1.1 目标

Core 的日志模块，提供：**分级输出、运行时级别控制、Console + 文件双输出、线程安全**；被其它模块（资源/池/UI/音频）通过**依赖注入**使用。

### 1.2 既定约束（v1 设计前提，逐条落实）

| # | 约束 | 落实位置 |
|---|---|---|
| 1 | **`ZLoggerBootstrap` 不是静态类**，与 `ResourcesBootstrapper` 一样是**容器管理的入口点**；全局尽量不用静态类 | §5 |
| 2 | **主线程分发器同样实例化**（不用静态类） | §6 |
| 3 | 级别只实现 **Debug / Info / Warning / Error / Fatal**（5 级，无 Trace） | §2 |
| 4 | 输出接口带 **caller 参数**（自动填充 + 类名显式传） | §3 |
| 5 | **不要 `ZLogModule`**（模块枚举废弃）——来源由 caller 体现 | §3、§4 |
| 6 | **不做静态门面 `ZLog`**，只保留 `IZLogger` / `ZLogger` 实例 | §3 |
| 7 | **编译期剥离：选方案 a**（v1 放弃剥离，只做运行时过滤 + 前置判定） | §2.3 |
| 8 | **主线程驱动：选方案 a**（驱动对象） | §6.3 |
| 9 | **Bootstrap 分层**：各模块可自带 Bootstrap，`Zipper.DI` 有一个**总 Bootstrap**，从容器取出各模块 Bootstrap 并依次调用 | §5 |
| 10 | **文件不轮转、不清理**，但**按日期切分**（跨零点写新文件） | §7.2 |
| 11 | **不做去重折叠**（Unity Console 自带 Collapse） | §8 |
| 12 | 池侧 **用构造参数**接收 `IZLogger`（不污染 `ZPoolOptions<T>`） | §9 |

---

## 2. 级别与过滤策略

### 2.1 五个级别

| 级别 | 谁需要看 | 判断标准 | 例子 | Release 保留？ |
|---|---|---|---|---|
| **Debug** | 开发期的我 | 比 Info 细、上线无价值 | `对象池扩容到 32` | ❌（不保留，但见 §2.3：不是编译期剥离） |
| **Info** | 想知道"系统走到哪了" | 正常运行的关键里程碑 | `Addressables 初始化完成，耗时 320ms` | ❌（同上） |
| **Warning** | 上线后 | **能继续跑，但不对劲**：兜底、降级、用了默认值 | `句柄未释放，已兜底释放` | ✅ |
| **Error** | 上线后 | **本次操作失败，程序还能继续** | `加载 enemy_basic 失败` | ✅ |
| **Fatal** | 上线后 | **整体走不下去**（通常伴随抛异常/退出） | `catalog 加载失败，无法继续` | ✅ |

**定级三问**：程序还能继续吗（能且正常 `Info` / 能但不正常 `Warning` / 本次失败 `Error` / 整体不行 `Fatal`）；谁需要看；多久一次一次。
**Warning 与 Error 的分界（最易错）**：`Warning` = 系统自己扛过去了（兜底/重试成功）；`Error` = 这次没扛住，调用方要知道。

### 2.2 过滤机制（v1 采用）

| 机制 | 采用 | 说明 |
|---|---|---|
| **运行时级别过滤** | ✅ 主要手段 | Router 持有**全局阈值**；低于阈值的调用直接返回 |
| **前置判定（零分配）** | ✅ **纪律要求** | 热路径必须先 `IsEnabled(level)` 再拼字符串 |
| 编译期剥离 | ❌ 不用（§2.3） | — |
| 条件编译 `#if` | ⚪ 可选 | 极少数热点可用（侵入业务代码，按需） |

### 2.3 编译期剥离：**定案 = 放弃**（使用者决策：方案 a）

因为不做静态门面（约束 #6），而 `[Conditional]` **只对静态调用生效**，所以 **v1 没有编译期剥离**：

- Release 构建里 `Debug/Info` 调用**仍会执行**（一次接口调用 + 一次阈值比较，纳秒级）
- 但**不会有字符串分配**——前提是守住纪律：先 `IsEnabled` 再拼串

```csharp
if (_logger.IsEnabled(ZLogLevel.Debug))     // 先判定
    _logger.Debug($"耗时 {ms}", nameof(MyClass));   // 后拼接（关闭时不分配）
```

**代价与收益**：放弃"IL 里连调用都不存在"的零痕迹；换来"全框架无静态类"的一致性与可测试性。适用于日志量可控的项目（本工程毕设 Demo 场景）。若将来确有性能/包体压力，再单独评估引入静态门面（或对个别热点用 `#if`）。

---

## 3. 接口形态

### 3.1 `IZLogger`（实例，注入用）

```
public interface IZLogger
{
    bool IsEnabled(ZLogLevel level);                       // 热路径前置判定
    void SetGlobalLevel(ZLogLevel level);                  // 运行时调级

    void Debug  (string message, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
    void Info   (string message, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
    void Warning(string message, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
    void Error  (string message, Exception ex = null, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
    void Fatal  (string message, Exception ex = null, string className = null, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0, UnityEngine.Object context = null);
}
```

**典型调用**（三种位置参数，其余用命名参数）：

```
_logger.Info("加载完成", nameof(MyClass));                       // 自动带 成员名:行号
_logger.Warning("句柄未释放", nameof(PoolManager), context: this);
_logger.Error("加载失败", ex, nameof(ResourceService));
```

**输出前缀格式**（Console 侧）：

```
[14:32:07.123] [MyClass.SetupAsync:128] 加载完成
```

### 3.2 caller 三件套（使用者决策）

| 参数 | 来源 | 成本 | 说明 |
|---|---|---|---|
| `className` | **调用方显式传 `nameof(类名)`** | 零（编译期常量） | 类名靠显式传（不用 `CallerFilePath` 硬解析）；忘了传就是 `null`（前缀缺类名） |
| `member` | `[CallerMemberName]` 自动 | 零 | 在 lambda / 局部函数里会得到编译器生成名（如 `<SetupAsync>b__0`）——预期行为 |
| `line` | `[CallerLineNumber]` 自动 | 零 | 定位到具体行，配合 member 足够精确定位 |

---

## 4. 日志条目与数据

```
ZLogLevel  : Debug | Info | Warning | Error | Fatal          // 5 级，无 Trace、无 Module

ZLogEntry（只读结构，跨线程传递的就是它）
    ZLogLevel Level;
    string    Message;
    string    ClassName;         // 调用方显式传的类名（可为空）
    string    Member;            // 调用方成员名（自动）
    int       Line;              // 行号（自动）
    DateTime  Time;
    int       ThreadId;
    UnityEngine.Object Context;  // 可选：Console 对象引用（见 §4.1）
    Exception Exception;         // Error/Fatal 用
```

> **没有 Module 字段**：来源由 `ClassName` + `Member` + `Line` 承担（约束 #5）。

### 4.1 `context` 参数怎么用（使用者问）

`context` 就是 Unity 原生 `Debug.Log(message, context)` 的第二个参数——**一个 `UnityEngine.Object`**：

| 作用 | 说明 |
|---|---|
| **在 Console 里关联对象** | 每条日志右侧会出现该对象的引用；**双击日志会 ping/高亮那个对象**（Hierarchy 或 Project 面板） |
| 传什么 | 在 MonoBehaviour 里传 `this`；或传 `gameObject`、某个组件、`ScriptableObject`、被加载的资源对象 |
| 不传 | 默认 `null` → Console 里没有对象关联（不影响输出） |
| 对象已销毁 | Console 显示 Missing（无害，不影响日志文本） |

**建议用法**：凡是"与某个对象强相关"的日志都带上它，排查时能直接从日志跳到对象：

```
_logger.Warning("对象池已满", nameof(Spawner), context: this);
_logger.Info($"加载 {prefab.name} 完成", nameof(Loader), context: prefab);
```

---

## 5. Bootstrap 分层（约束 #1、#9）

### 5.1 结构

```
Zipper.DI（组装层）
 └─ ZipperBootstrapper            ← 总 Bootstrap（容器入口点）
      │ 从容器取出所有 IZModuleBootstrap，按 Order 依次调用
      ├─ ZLoggerBootstrap         Order = 0    （日志最先，其它模块依赖它）
      ├─ ZEventBusBootstrap?      Order = 10   （事件总线若需初始化）
      ├─ ZResourceBootstrap       Order = 20   （加载 catalog / 预下载）
      └─ ZObjectPoolBootstrap?    Order = 30   （池管理器若需预热）
```

### 5.2 模块 Bootstrap 契约（`IZModuleBootstrap`）

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

- 模块各自实现它（`ZLoggerBootstrap`、`ZResourceBootstrap`……）；**Bootstrap 只做"装配/初始化"，不含业务逻辑**
- 统一 `UniTask` + `CancellationToken`（roadmap 已定：对外异步一律 UniTask）
- 失败策略：**抛异常 → 启动失败**（可见、可定位）；若某模块允许降级，由该模块自行 catch 并记录（例如文件 Sink 不可用 → 降级为仅 Console）

**`InitializeAsync(CancellationToken ct)` 的 ct 从哪来（补充说明）**：

| 来源 | 何时用 |
|---|---|
| **VContainer 注入的 Scope 取消令牌**（`IAsyncStartable.StartAsync(ct)` 由容器传入，Scope 销毁即取消） | ✅ **默认路径**：总 Bootstrap 收到后**一路透传**给各模块，**不需要自己创建** |
| `default` / `CancellationToken.None` | 调用方无取消需求时（零成本、无分配） |
| **自己创建 `CancellationTokenSource`** | 仅两种场景：① **超时**——`CreateLinkedTokenSource(ct)` + `CancelAfter(...)`（**必须链接上游 ct**，用完 `using`/`Dispose`）；② **主动取消**（用户取消 / 切场景）——由**发起取消的一方**持有，`Cancel()` + `Dispose()` |

**三个反模式**：接收方在方法内部 `new CancellationTokenSource()`（无意义且易泄漏）；创建了不 `Dispose`；拿到 ct 却不检查（`ThrowIfCancellationRequested`）也不透传给下游 async API。

**日志模块的两点注意**：① 初始化几乎不等待（建 Sink / 开文件 / 创建驱动对象），ct 实际可能用不上，但**接口保留 ct 是框架统一约定**；② ⚠️ **收尾不得绑 ct**——`ZLoggerBootstrap.Dispose()` 的 Flush 与"停后台线程"必须走**不依赖 ct** 的路径，否则 Scope 取消时线程被中断、**尾部日志丢失**。

### 5.3 总 Bootstrap（`ZipperBootstrapper`）：**阶段顺序显式写在代码里**

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

**为什么这样写（关键结论）**：

| 做法 | 顺序可控性 | 新增模块 | 评价 |
|---|---|---|---|
| 遍历集合直接 await | ❌ 不可控（枚举顺序未知） | 不用改代码 | 你担心的那种——**不要用** |
| 按 `int Order` 排序后 await | ⚠️ **运行期约定**：忘写/重复/写错 → 静默错序，无编译期保障 | 不用改代码 | 能用，但把"顺序正确性"押在每个模块自觉上 |
| **阶段顺序显式写死 + 阶段内遍历**（✅ 采纳） | ✅ **完全可控**：阶段顺序在代码里，改动集中一处、一眼看清 | 阶段内新增不用改代码；**新阶段**才加一行 | 兼顾"可控"与"低维护" |
| 完全显式逐个调用（`await _logBootstrap…; await _resBootstrap…`） | ✅ 最硬（编译期强制） | 每加模块改一行 | 模块很少（4~6 个）时也可接受 |

- 用 `IAsyncStartable`（VContainer 的异步启动，`StartAsync` 返回类型随工程组合变化，本工程为 UniTask）
- 阶段内如果也想稳定（避免同阶段两个模块互相依赖），可在 `_bootstraps` 里再按类型名排序——**但请优先让同阶段模块彼此独立**（真正的依赖应该跨阶段，而不是靠顺序）

> **为什么这段编排不用 R3（边界说明）**：它是"一次性、有顺序、必须 await"的启动流程，属于**流程编排**，不是"时间轴上的多条流"——R3 的 `Where/Select/Concat` 只是换写法，不会让顺序更可控，反而带来三个代价：可读性下降、异常栈穿过操作符更难定位、异常语义（`OnErrorResume` 不终止流）与"初始化失败即启动失败"相冲突。
> 更合适的优化只有两条：① 写法简洁化 `foreach (var b in _bootstraps.Where(x => x.Phase == phase)) await b.InitializeAsync(ct);`；② 若同阶段模块彼此独立要并行 → `await UniTask.WhenAll(...)`。
> R3 在这条路径上唯一有意义的场景是**初始化进度上报**（加载条），那属于"进度事件流"，与本节编排无关。

### 5.4 容器注册（`GameLifetimeScope`）

```
builder.Register<IZLogger, ZLogger>(Lifetime.Singleton);

// 各模块 Bootstrap：注册为 IZModuleBootstrap 集合的一员
builder.Register<ZLoggerBootstrap>(Lifetime.Singleton).As<IZModuleBootstrap>();
builder.Register<ZResourceBootstrap>(Lifetime.Singleton).As<IZModuleBootstrap>();
// …

// 总 Bootstrap：唯一入口点
builder.RegisterEntryPoint<ZipperBootstrapper>(Lifetime.Singleton);
```

> ⚠️ **与现状的差异**：现在 `ResourcesBootstrapper` 是**直接** `RegisterEntryPoint`（自己启动）；改为实现 `IZModuleBootstrap`、注册进集合，由总 Bootstrap 统一调用。日志模块同理（不再有静态 `ZLogBootstrap.Initialize`）。
> **为何要分层**：① 启动顺序**显式可控**（日志阶段先于资源阶段，写在总 Bootstrap 里）；② 阶段内新增模块不动总 Bootstrap；③ 模块可裁剪（不注册就不启动）。

### 5.5 补充：能否让"顺序"根本不需要管？

有一个更彻底的方向（**可选，v1 不做**）：让日志"**注入即就绪**"——把 Router/Sink 的装配放在 `ZLogger` 的构造（或一个惰性初始化器）里，**主线程驱动对象改为首次需要 Console 输出时惰性创建**。这样任何模块拿到 `IZLogger` 就能直接用，**不再依赖"日志先启动"这个阶段顺序**。

- 好处：顺序约束从"框架约定"降级为"根本不存在"
- 代价：日志装配时机变得隐式（构造里做 IO/创建 GameObject 需要主线程与时机保证），出错时更难定位；且驱动对象惰性创建本身也依赖"第一次调用发生在主线程"
- **结论**：v1 采用 §5.3 的"显式阶段"（简单、可控、易调试）；惰性方案留作将来简化顺序依赖的备选

---

## 6. 主线程分发（约束 #2、#8）

### 6.1 为什么需要

`Debug.Log` **只能在主线程调用**。任意线程产生日志时，Console 输出必须回到主线程。

### 6.2 实例化形态

```
public sealed class ZMainThreadDispatcher : IDisposable
{
    readonly ConcurrentQueue<Action> _pending;
    int _mainThreadId;
    public bool IsMainThread { get; }
    public void Enqueue(Action action);            // 任意线程：无锁入队
    public void Pump(int maxPerFrame = 256);       // 主线程每帧：限量出队执行
    public void Dispose();
}
```

- 由 `ZLoggerBootstrap` 创建并持有（**非静态**：可注入、可替换、测试隔离）

### 6.3 驱动方式：**定案 = 驱动对象**（使用者决策：方案 a）

```
ZLoggerBootstrap.InitializeAsync：
    1) 创建 dispatcher 实例
    2) new GameObject("[Zipper]MainThreadDispatcher") + AddComponent<ZMainThreadDispatcherDriver>()
       + DontDestroyOnLoad
    3) driver.Update() → dispatcher.Pump(maxPerFrame)
    Dispose：销毁该对象
```

- `Internal/ZMainThreadDispatcherDriver.cs` 是 Core 内**唯一**的 MonoBehaviour（不引 UniTask / VContainer，Core 零框架依赖不变）
- 备选（记录备查）：PlayerLoop 注入（无额外对象、代码更复杂）；外部驱动（依赖场景对象，顺序易错）

---

## 7. Router 与 Sink

```
ZLogRouter（实例）
    状态：全局阈值 + Sink 列表（每 Sink 自带门槛）
    Dispatch(entry)：
        1) entry.Level < 全局阈值 → 返回
        2) 逐 Sink：entry.Level < sink.MinimumLevel → 跳过
                     sink.RequiresMainThread 且非主线程 → dispatcher.Enqueue(...)
                     否则 → try { sink.Write(entry) } catch { /* 单 Sink 故障不影响其它 Sink */ }
```

| Sink | RequiresMainThread | 职责 |
|---|---|---|
| `ZConsoleSink` | **true** | 映射 `Debug.Log/LogWarning/LogError`；前缀 `[时间] [类名.成员:行]`；传 `context` 支持双击跳转 |
| `ZFileSink` | false | 后台线程批量写；时间(500ms)/大小(16KB)双触发；`Error/Fatal` 立即刷；**按日期切分**（§7.2） |

### 7.1 必 flush 的时机（漏一个就丢日志）

`Application.quitting`、**`OnApplicationPause(true)`**（移动端被杀不走 quitting）、Scope `Dispose()`、`AppDomain.UnhandledException`（尽力而为）。

### 7.2 文件按日期切分（使用者要求；不做轮转/清理）

**目标**：文件名按日期，跨零点自动写新文件。

| 要点 | 做法 |
|---|---|
| 文件名 | `zipper-yyyyMMdd.log`（如 `zipper-20260910.log`），**不带时分秒** |
| 归属规则 | 按**写入时刻**的本地日期（`DateTime.Now.Date`）——简单、可预期；不做"按每条日志时间戳归属"（跨零点瞬间的条目归属旧文件，可接受） |
| 切分时机 | **后台写线程每批 Drain 之前**检查一次日期：与当前文件日期不同 → 走切分流程（开销：一次时间比较，可忽略） |
| 切分流程 | ① 先把队列里剩余条目 Drain 进**旧文件**；② `Flush` + `Close` 旧 writer；③ 以新日期命名打开新文件（**append 模式**）；④ 更新"当前文件日期" |
| 同日重启 | 用 **append** 追加到同一天的同一文件（不新开）——文件里可能有多段启动记录，可接受（或写一行分隔标记） |
| 系统时间被改/时区变化 | 不特殊处理（可能出现日期"回退"，此时按"与当前文件日期不同即切"处理，最坏情况多切一个文件） |
| 不做的事 | ❌ 不做大小轮转、❌ 不做旧文件清理（**既定决策**）→ 注意：日志目录会随时间增长，需定期手工清理 |
| 验收 | 跨零点（或改系统时间模拟）后写日志 → 新日期文件出现、旧文件保留、不丢条目 |

---

## 8. 并发模型

| 角色 | 线程 | 只做什么 |
|---|---|---|
| 生产者 | 任意线程 | 判级别 → 格式化 → **入队**（无锁、不做 IO） |
| 主线程泵 | 主线程（驱动对象 `Update`） | 出队 → `ZConsoleSink` |
| 后台消费者 | 单个后台线程 | 批量出队 → `ZFileSink`（含日期切分与刷盘判定） |

纪律：**不 lock**（`ConcurrentQueue`）；写文件单线程；Console 只在主线程；热路径先 `IsEnabled`；**队列有上限、满则丢最旧并如实记录丢弃条数**（防 OOM/卡帧）。

**关于"去重折叠"（约束 #11）**：**v1 不做**——Unity Console 自带 **Collapse** 已覆盖"显示层折叠"的需求。
> 注意两点：① Console 的 Collapse 只影响显示，**文件里仍会保留全部重复行**；② 若真出现"同一警告每帧刷"的日志风暴，首选在**调用点**解决（提高级别、加 `IsEnabled` 判断），而不是给日志系统加限流。若将来判定需要，再评估"同 `类名+成员+消息` 每秒最多 N 条"的限流（列为 v2 可选）。

---

## 9. 与其它模块的衔接

| 模块 | 衔接方式 |
|---|---|
| **对象池** | `ZObjectPoolManager` 构造注入 `IZLogger` ✓；池内部（`ZObjectPool<T>`）要打日志时，**由管理器在建池时通过构造参数传入**（约束 #12，不污染 `ZPoolOptions<T>`）→ 池构造签名变为 `internal ZObjectPool(in ZPoolOptions<T> options, IZLogger logger)`；池内两处 `//TODO 使用框架自带日志库输出` 即可接上（同步见 `pool-manager-design.md` §9） |
| 资源管理器 | 构造注入 `IZLogger`；初始化耗时、加载失败、句柄兜底释放等走日志 |
| 事件总线 | 独立；总线自身异常（订阅者抛异常）打 `Warning` |
| 组装层 | 按 §5.4 注册：`IZLogger → ZLogger` + 各模块 Bootstrap 注册为 `IZModuleBootstrap` + 总 `ZipperBootstrapper` 作为唯一入口点 |

---

## 10. 验收标准与测试点

| 验收点 | 场景 | 断言 |
|---|---|---|
| 运行时过滤 | 设全局 `Warning` 后调 `Info` / `Warning` | `Info` 不输出、`Warning` 输出 |
| 前置判定零分配 | 关闭 `Debug`，调用点按纪律先 `IsEnabled` | Profiler 无字符串分配 |
| caller 前缀 | 调 `Info("x", nameof(MyClass))` | 输出含 `MyClass.方法名:行号` |
| context 关联 | 带 `context: this` 输出 | Console 该条右侧有对象引用，双击可 ping 到对象 |
| 双 Sink | 写 100 条后退出 | Console（按门槛）与文件（按门槛）都有，文件含时间与 caller 前缀 |
| 时间触发刷盘 | 写 1 条后等 1 秒（不退出） | 文件里已出现 |
| Error 立即刷 | 写 `Error` 后强杀进程 | 文件里有该条 |
| **跨日期切分** | 模拟跨零点（改系统时间）后写日志 | 生成 `zipper-<新日期>.log`；旧文件保留；无条目丢失 |
| 同日重启追加 | 同一天重启应用后写日志 | 追加到同一文件（不覆盖） |
| 并发安全 | 5 线程各写 1000 条 | 不抛异常；Console 在主线程输出；文件行数相符或明确记录丢弃条数 |
| 溢出策略 | 灌爆队列 | 丢最旧 + 文件里如实写"丢弃 N 条" |
| 退出不丢 | 写日志后立即退出 | 文件含尾部日志 |
| **启动分层** | 容器启动 | 总 Bootstrap 按 `Order` 依次初始化（日志先于资源）；`ResourcesBootstrapper` 能注入到已就绪的 `IZLogger` |

---

## 11. 待决项

| 项 | 内容 | 建议 |
|---|---|---|
| 日志风暴限流 | 是否要"同来源每秒最多 N 条" | v1 不做（先靠级别 + 调用点纪律）；确需时 v2 加 |
| PlayerLoop 驱动 | 是否用 PlayerLoop 替换驱动对象 | 保持驱动对象（v1 定案）；除非将来需要去掉额外 GameObject |
| `className` 是否强制 | 忘了传就缺类名 | 靠代码规范约束（每次传 `nameof(类型)`），不强制 |
| 文件阈值/保留 | 大小轮转、旧文件清理 | **不做**（既定决策）；仅提醒目录会增长 |
| 调试模式 | 内存中保留最近 N 条（供监视器/面板查看） | 需要时再加（可选 Sink） |

---

## 12. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v1.2 | 2026-09-10 | **启动顺序机制改硬**（回应"顺序不可控"的质疑）：`IZModuleBootstrap` 的 `int Order` 改为语义化 **`ZBootPhase` 枚举**；总 Bootstrap **把阶段顺序显式写在代码里**（`await RunPhase(Logging) → RunPhase(Events) → RunPhase(Resources) → RunPhase(Pools)`），阶段内才遍历集合 → 顺序**编译期可见、单点可控**，同时阶段内新增模块无需改代码；§5.3 补四种做法对照表；新增 §5.5"惰性初始化让顺序无关"的可选方向 |
| v1.1 | 2026-09-10 | 落实第二轮决策：① 编译期剥离**定案方案 a**（放弃剥离，运行时过滤 + `IsEnabled` 前置判定）；② 主线程驱动**定案方案 a**（驱动对象 + `ZMainThreadDispatcherDriver`）；③ caller 定为**三件套**（`nameof(类名)` 显式 + `[CallerMemberName]` + `[CallerLineNumber]`）；④ `context` 保留并新增 §4.1 **用法说明**（Console 对象关联与双击跳转）；⑤ **Bootstrap 分层**（§5：`IZModuleBootstrap` 契约 + 总 `ZipperBootstrapper` 从容器取集合、按 `Order` 依次调用；同步说明与现有 `ResourcesBootstrapper` 的差异）；⑥ 文件 Sink 新增 **§7.2 按日期切分**（跨零点写新文件；不轮转、不清理）；⑦ **去掉去重折叠**（§8：Console 自带 Collapse 已覆盖显示层）；⑧ 池侧改为**构造参数**接收 `IZLogger`（不污染 `ZPoolOptions<T>`） |
| v1.0 | 2026-09-10 | 初稿：按使用者 6 条约束重做日志设计——Bootstrap 实例化、主线程分发器实例化、5 级、接口带 caller、取消 `ZLogModule`、取消静态门面；记录"放弃编译期剥离"的连锁后果；补齐装配/驱动/Sink/并发/验收/待决项 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
