# Zipper 日志模块设计（ZLogger）

> 状态：**v1.0 草稿，待审阅**
> 定位：`Zipper.Core` 的日志模块 v1 设计定稿（取代 `core-design.md` §3 早期形态）。**只给设计思路与接口形态，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/architecture/core-design.md`（Core 总览）、`docs/architecture/pool-manager-design.md`（池模块要接日志）、`LocalNotes/logging-implementation-guide.md`（本地实现教程，不入库）
> 变更记录：v1.0 初稿——按使用者 6 条约束重做设计（见 §1.2）。

---

## 1. 定位与既定约束

### 1.1 目标

Core 的日志模块，提供：**分级输出、运行时级别控制、Console + 文件双输出、线程安全**；被其它模块（资源/池/UI/音频）通过**依赖注入**使用。

### 1.2 使用者既定约束（v1 设计前提，逐条落实）

| # | 约束 | 落实位置 |
|---|---|---|
| 1 | **`ZLoggerBootstrap` 不是静态类**，与 `ResourcesBootstrapper` 一样是**容器管理的入口点**；全局尽量不用静态类 | §5 装配 |
| 2 | **主线程分发器同样实例化**（不用静态类） | §6 主线程分发 |
| 3 | 级别只实现 **Debug / Info / Warning / Error / Fatal**（5 级，无 Trace） | §2 级别 |
| 4 | 输出接口带 **caller 参数**（自动填充） | §3 接口形态 |
| 5 | **不要 `ZLogModule`**（模块枚举废弃）——来源由 caller 体现 | §3、§4 |
| 6 | **不做静态门面 `ZLog`**，只保留 `IZLogger` / `ZLogger` 实例 | §3 |

> ⚠️ **由 #6 引出的连锁后果（重要，待确认）**：`[Conditional]` **只对静态调用生效**，因此"不用静态门面"= **放弃编译期剥离能力**（Release 里日志调用仍会执行）。替代手段与取舍见 §2.3，需使用者拍板。

---

## 2. 级别与过滤策略

### 2.1 五个级别

| 级别 | 谁需要看 | 判断标准 | 例子 | Release 保留？ |
|---|---|---|---|---|
| **Debug** | 开发期的我 | 比 Info 细、上线无价值 | `对象池扩容到 32` | ❌（见 §2.3） |
| **Info** | 想知道"系统走到哪了" | 正常运行的**关键里程碑** | `Addressables 初始化完成，耗时 320ms` | ❌（见 §2.3） |
| **Warning** | 上线后 | **能继续跑，但不对劲**：兜底路径、降级、用了默认值 | `句柄未释放，已兜底释放` | ✅ 永远保留 |
| **Error** | 上线后 | **本次操作失败，程序还能继续** | `加载 enemy_basic 失败` | ✅ |
| **Fatal** | 上线后 | **整体走不下去**（通常伴随抛异常/退出） | `catalog 加载失败，无法继续` | ✅ |

**定级三问**：程序还能继续吗（能且正常 `Info` / 能但不正常 `Warning` / 本次失败 `Error` / 整体不行 `Fatal`）；谁需要看（只有排查时 `Debug`；上线也要 `Info` 以上）；多久一次。

**Warning 与 Error 的分界（最易错）**：`Warning` = 系统**自己扛过去了**（兜底/重试成功）；`Error` = **这次没扛住，调用方要知道**。

### 2.2 过滤机制（v1 实际生效的手段）

| 机制 | 是否采用 | 说明 |
|---|---|---|
| **运行时级别过滤** | ✅ **v1 主要手段** | Router 持有全局阈值；低于阈值的调用直接返回 |
| **前置判定（零分配）** | ✅ **纪律要求** | 热路径必须先 `IsEnabled(level)` 再拼字符串，否则插值先分配、白费 |
| 编译期剥离（`[Conditional]`） | ❌ **v1 不用**（因不用静态门面，见 §2.3） | — |
| 条件编译（`#if`） | ⚪ 可选手段 | 对极热路径可用 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` 包住调用（侵入业务代码，按需） |

### 2.3 ⚠️ 编译期剥离的取舍（待使用者拍板）

| 方案 | 做法 | 代价 |
|---|---|---|
| **(a) v1 放弃剥离（默认建议）** | 只靠运行时过滤 + `IsEnabled` 前置判定 | Release 里 `Info/Debug` 调用仍执行（一次接口调用 + 一次比较，纳秒级）；**无字符串分配**（因前置判定） |
| (b) 局部 `#if` | 对少量热点调用点用 `#if` 包住 | 侵入业务代码、可读性下降；但零成本 |
| (c) 加一个极小静态访问点 | 只加 `static class ZLog { public static IZLogger Logger; }`，不做整套 `[Conditional]` 门面 | 与"尽量不用静态类"原则冲突（多容器/测试会串） |
| (d) 将来真需要 | 再加静态门面（`[Conditional]`），调用点小改 | 就是被 #6 排除的方案 |

> **建议**：v1 用 (a)——你的场景（毕设 Demo / PC 为主）日志量可控，剥离收益很小；把"是否引入门面"留到确有性能或包体压力时再定。

---

## 3. 接口形态

### 3.1 `IZLogger`（实例，注入用）

```
public interface IZLogger
{
    bool IsEnabled(ZLogLevel level);                       // 热路径前置判定

    void Debug  (string message, UnityEngine.Object context = null, [CallerMemberName] string caller = null);
    void Info   (string message, UnityEngine.Object context = null, [CallerMemberName] string caller = null);
    void Warning(string message, UnityEngine.Object context = null, [CallerMemberName] string caller = null);
    void Error  (string message, Exception ex = null, UnityEngine.Object context = null, [CallerMemberName] string caller = null);
    void Fatal  (string message, Exception ex = null, UnityEngine.Object context = null, [CallerMemberName] string caller = null);

    void SetGlobalLevel(ZLogLevel level);                  // 运行时调级
}
```

要点：
- **`caller` 一律放最后 + 默认值**（`[CallerMemberName]` 要求是可选的），调用方无需传，编译器在**调用点**自动填入（接口调用同样有效）。
- `context`（`UnityEngine.Object`）可选：传给 Console 后支持**双击跳转**；不需要可去掉。
- `Error`/`Fatal` 带 `Exception` 参数（放在 `context`/`caller` 之前，便于 `Error("msg", ex)` 直接调用）。
- **只保留全局阈值**（去掉按模块控制，见 #5）。若日后需要"只看某来源"，用 `caller` 做 Sink 级谓词即可（不必回到枚举）。

### 3.2 caller 参数的取舍（必读）

| 参数 | 得到什么 | 成本 | 注意 |
|---|---|---|---|
| `[CallerMemberName]` | **方法名**（如 `SetupAsync`） | **零**（编译期字符串常量，无分配） | 在 lambda / 局部函数里会得到编译器生成名（如 `<SetupAsync>b__0`） |
| `[CallerLineNumber]`（可选加） | 行号（int） | **零** | 与 caller 组合，定位更准（`SetupAsync:128`） |
| `[CallerFilePath]` | 完整路径 | 零填充，但**太长**（暴露本机路径），通常要截断成文件名 → 有字符串处理成本 | 想要"类名"时才会考虑 |

**建议 v1**：`caller`（方法名）+ 可选 `line`（行号）。若你更想要"类名"，做法是调用方显式传 `nameof(类名)`（零成本、可控），而不是靠 `CallerFilePath` 硬解析。

### 3.3 `ZLogger`（`IZLogger` 的实现）

- 由容器注册为单例；内部持有 Router 引用（实例，非静态）。
- 可用构造参数定制：默认级别、是否附加 caller 前缀等（`ZLoggerOptions`，见 §5.3）。

---

## 4. 日志条目与数据

```
ZLogLevel  : Debug | Info | Warning | Error | Fatal          // 5 级，无 Trace、无 Module

ZLogEntry（只读结构，跨线程传递的就是它）
    ZLogLevel Level;
    string    Message;
    string    Caller;            // 调用方成员名（自动）
    int       Line;              // 可选：行号
    DateTime  Time;              // 或预格式化时间串
    int       ThreadId;
    UnityEngine.Object Context;  // 可选：Console 双击跳转
    Exception Exception;          // Error/Fatal 用
```

> **没有 Module 字段**：来源信息由 `Caller`（+ 可选 `Line`）承担（约束 #5）。

---

## 5. 装配与生命周期

### 5.1 `ZLoggerBootstrap`（**实例**，容器管理的入口点）

与 `ResourcesBootstrapper` 同构——**普通 C# 类**，由 VContainer 装配与调用：

```
public class ZLoggerBootstrap : IInitializable, IDisposable
{
    public ZLoggerBootstrap(IZLogger logger, /* 或直接持有 Router/Sink 依赖 */ ...) { ... }

    public void Initialize()      // 装配：建 Router/Sink、创建主线程分发器与驱动、注册退出回调
    public void Dispose()         // 收尾：Flush 全部 Sink → 停后台线程 → 销毁驱动对象
}
```

要点：
- **接口选择**：日志装配是同步的 → 用 **`IInitializable`**（VContainer 生命周期里最早的一档），保证**早于** `ResourcesBootstrapper`（`IAsyncStartable`）执行。若 VContainer 版本对执行顺序不保证，就让使用方（其它模块的 Bootstrap）在开始工作前**显式依赖日志已就绪**（构造函数注入 `IZLogger` 天然保证"装配先于使用"）。
- **`Dispose()` 必须实现**：容器销毁 Singleton 时会调用（VContainer 对注册为 Singleton 且实现 `IDisposable` 的类型会自动 Dispose）→ 在这里 **Flush + 停线程 + 回收驱动对象**。
- **不再有静态 `ZLogBootstrap.Initialize(...)`**（约束 #1）。

### 5.2 容器注册（`GameLifetimeScope`）

```
builder.Register<IZLogger, ZLogger>(Lifetime.Singleton);
builder.RegisterEntryPoint<ZLoggerBootstrap>(Lifetime.Singleton);   // 或 Register + As<IInitializable>
// …其余模块（资源/池/事件总线）照旧
```

> ⚠️ 不再注册 `IZLogger → ZLog`（静态门面已取消，约束 #6）。

### 5.3 配置（`ZLoggerOptions`，实例）

```
ZLoggerOptions
    ZLogLevel ConsoleMinimumLevel = ZLogLevel.Info     // 屏幕门槛
    ZLogLevel FileMinimumLevel    = ZLogLevel.Debug    // 文件门槛（更全，便于事后排查）
    bool      EnableFileSink      = true
    string    LogDirectory        = null               // 默认 persistentDataPath/ZipperLogs
    int       FlushIntervalMs     = 500                // 时间触发
    int       FlushBytes          = 16 * 1024          // 大小触发
    int       MaxQueuedLines      = 8192               // 队列上限（溢出丢最旧 + 计数）
    // 可选：轮转阈值、保留文件数、调试模式（记录最近 N 条）
```

---

## 6. 主线程分发（实例化，约束 #2）

### 6.1 为什么需要它

`Debug.Log` **只能在主线程调用**（Unity API 约束）。任意线程产生日志时，Console 输出必须回到主线程。

### 6.2 实例化形态

```
public sealed class ZMainThreadDispatcher : IDisposable
{
    readonly ConcurrentQueue<Action> _pending;
    int _mainThreadId;

    public bool IsMainThread { get; }        // 记录主线程 ID 后比较
    public void Enqueue(Action a);           // 任意线程调用：无锁入队
    public void Pump(int maxPerFrame = 256); // 主线程每帧调用：限量出队执行
    public void Dispose();
}
```

- **不再静态**：由 `ZLoggerBootstrap` 创建并持有（可注入、可替换、测试隔离）。
- 命名建议 `ZMainThreadDispatcher`（职责是"把回调派发到主线程"）；若你想保留 `MainThreadPump` 这个名字也一致（只要它是实例）。

### 6.3 驱动方式（三选一，**待你定**）

| 方案 | 做法 | 评价 |
|---|---|---|
| **(a) 驱动对象（推荐）** | Bootstrap `new GameObject("[Zipper]MainThread")` + `AddComponent<ZMainThreadDispatcherDriver>()` + `DontDestroyOnLoad`，`Update()` 里 `Pump()` | 自包含、简单、可控每帧上限；代价：多一个隐藏对象 |
| (b) PlayerLoop 注入 | 仿 UniTask/R3 的 `PlayerLoopHelper`，把自己的更新插进 PlayerLoop | 无额外 GameObject；代码复杂（重建 PlayerLoopSystem），Core 要写更多底层代码 |
| (c) 外部驱动 | 由场景引导对象每帧调 `dispatcher.Pump()` | 最省代码；但把"日志能否输出"绑在外部对象上（依赖顺序易错） |

> 建议 v1 用 (a)：`Internal/ZMainThreadDispatcherDriver.cs` 是 Core 内**唯一**的 MonoBehaviour，不引 UniTask/VContainer（Core 零框架依赖不变）。

---

## 7. Router 与 Sink

```
ZLogRouter（实例）
    状态：全局阈值 + Sink 列表（每 Sink 自带门槛）
    Dispatch(entry)：
        1) 运行时过滤：entry.Level < 全局阈值 → 返回
        2) 逐 Sink：entry.Level < sink.MinimumLevel → 跳过
                     sink.RequiresMainThread 且当前非主线程 → dispatcher.Enqueue(...)
                     否则 → try { sink.Write(entry) } catch { /* 单个 Sink 故障不影响其它 */ }
```

| Sink | RequiresMainThread | 职责 |
|---|---|---|
| `ZConsoleSink` | **true** | 映射 `Debug.Log` / `LogWarning` / `LogError`；文本前缀 `[时间][caller]`；`context` 支持双击跳转 |
| `ZFileSink` | false | 后台线程批量写；时间(500ms)/大小(16KB)双触发刷盘；`Error/Fatal` 立即刷；单文件轮转 + 溢出丢最旧并**如实记录丢弃条数** |

**必须 flush 的时机**（漏一个就丢日志）：`Application.quitting`、**`OnApplicationPause(true)`**（移动端被杀不走 quitting）、Scope `Dispose()`、`AppDomain.UnhandledException`（尽力而为）。

---

## 8. 并发模型（沿用 core-design §3.11 结论）

| 角色 | 线程 | 只做什么 |
|---|---|---|
| 生产者 | 任意线程 | 判级别 → 格式化 → **入队**（无锁、不做 IO） |
| 主线程泵 | 主线程（驱动对象 `Update`） | 出队 → `ZConsoleSink` |
| 后台消费者 | 单个后台线程 | 批量出队 → `ZFileSink` 写盘 |

纪律：不 lock（`ConcurrentQueue`）；写文件单线程；Console 只在主线程；热路径先 `IsEnabled`；队列有上限、满则丢最旧 + 计数；加去重折叠（相同消息连续出现压成"×N"）防日志风暴。

---

## 9. 与其它模块的衔接

| 模块 | 衔接方式 |
|---|---|
| **对象池** | `ZObjectPoolManager` 已注入 `IZLogger` ✓；池内部（`ZObjectPool<T>`）要打日志时，**由管理器在建池时把 `IZLogger` 传入**（构造参数或 `ZPoolOptions<T>` 字段）——池的两处 `//TODO 使用框架自带日志库输出` 即可接上 |
| 资源管理器 | 同理：`ZResourceManager` 注入 `IZLogger`；启动耗时、加载失败、句柄兜底释放等走日志 |
| 事件总线 | 独立；总线自身异常（订阅者抛异常）打 `Warning` |
| 组装层 | `GameLifetimeScope` 注册 `IZLogger → ZLogger` + `ZLoggerBootstrap` 入口点（§5.2） |

---

## 10. 验收标准与测试点

| 验收点 | 场景 | 断言 |
|---|---|---|
| 运行时过滤 | 设全局 `Warning` 后调 `Info` / `Warning` | `Info` 不输出、`Warning` 输出 |
| 前置判定零分配 | 关闭 `Debug`，热路径按纪律先 `IsEnabled` | 无字符串分配（Profiler 验证） |
| caller 自动填充 | 在方法内调 `Info("x")` | 输出含该方法名；lambda 内为编译器生成名（预期行为） |
| 双 Sink | 写 100 条后退出 | Console 有（按门槛）、文件有（按门槛，含时间与 caller） |
| 时间触发刷盘 | 写 1 条后等 1 秒（不退出） | 文件里已出现 |
| Error 立即刷 | 写 `Error` 后强杀进程 | 文件里有该条 |
| 并发安全 | 5 线程各写 1000 条 | 不抛异常；Console 在主线程输出；总数相符或明确记录丢弃条数 |
| 溢出策略 | 灌爆队列 | 丢最旧 + 文件里如实写"丢弃 N 条" |
| 退出不丢 | 写日志后立即退出 | 文件含尾部日志 |
| 组装层 | 容器启动 | `ZLoggerBootstrap` 自动装配；`ResourcesBootstrapper` 能正常注入 `IZLogger` |

---

## 11. 待决项

| 项 | 内容 | 建议 |
|---|---|---|
| **编译期剥离** | 不用静态类 = 无 `[Conditional]`；是否接受 | v1 用"运行时过滤 + 前置判定"（§2.3 方案 a） |
| **主线程驱动方式** | 驱动对象 / PlayerLoop 注入 / 外部驱动 | v1 用驱动对象（§6.3 方案 a） |
| caller 粒度 | 只要方法名，还是要"类名+方法名+行号" | v1 方法名 + 可选行号；要类名由调用方传 `nameof` |
| `context` 参数 | 是否保留 `UnityEngine.Object`（Console 双击跳转） | 建议保留（调试体验提升明显） |
| 文件轮转/保留 | 单文件阈值与保留份数 | 实现时定，默认单文件 2MB / 保留最近若干 |
| 去重折叠 | 是否 v1 就做 | 建议做（防日志风暴的成本极低） |
| 与池的参数传递 | `IZLogger` 传进池的方式：构造参数 vs `ZPoolOptions<T>` 字段 | 建议 `ZPoolOptions<T>` 加一个可选 `IZLogger` 字段（不改池构造签名） |

---

## 12. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v1.0 | 2026-09-10 | 初稿：按使用者 6 条约束重做日志设计——Bootstrap 实例化（容器入口点）、主线程分发器实例化、5 级（Debug/Info/Warning/Error/Fatal）、接口带 `[CallerMemberName]` caller、**取消 `ZLogModule`**、**取消静态门面 `ZLog`**；同步记录"放弃编译期剥离"的连锁后果与替代方案；补齐装配/驱动/Sink/并发/验收/待决项 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
