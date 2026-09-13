# Zipper 日志模块设计（ZLogger）

> 状态：**v1.4 草稿，待审阅**
> 定位：`Zipper.Core` 的日志模块 v1 设计定稿（取代 `core-design.md` §3 早期形态）。**只给设计思路与接口形态，不含实现代码**。
> **实施归属**：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：**`docs/architecture/bootstrap-design.md`（启动 / Bootstrap 机制——已从本文迁出）**、`docs/architecture/core-design.md`（Core 总览）、`docs/architecture/pool-manager-design.md`、`LocalNotes/logging-implementation-guide.md`（本地实现教程，不入库）
> 变更记录：v1.0 初稿；v1.1 落实第二轮决策；v1.2 启动顺序改硬；v1.3 Bootstrap 位置约定；**v1.4 Bootstrap 机制整体迁出至 `bootstrap-design.md`**（本文只保留"日志模块如何接入启动"）。

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
| 7 | 编译期剥离：**放弃**（v1 只做运行时过滤 + 前置判定） | §2.3 |
| 8 | 主线程驱动：**驱动对象** | §6.3 |
| 9 | **Bootstrap 机制**：模块 Bootstrap + 组装层总 Bootstrap（机制细节见 `bootstrap-design.md`） | §5 |
| 10 | 文件**不轮转、不清理**，但**按日期切分**（跨零点写新文件） | §7.2 |
| 11 | **不做去重折叠**（Unity Console 自带 Collapse） | §8 |
| 12 | 池侧**用构造参数**接收 `IZLogger`（不污染 `ZPoolOptions<T>`） | §9 |

---

## 2. 级别与过滤策略

### 2.1 五个级别

| 级别 | 谁需要看 | 判断标准 | 例子 | Release 保留？ |
|---|---|---|---|---|
| **Debug** | 开发期的我 | 比 Info 细、上线无价值 | `对象池扩容到 32` | ❌（不保留，见 §2.3） |
| **Info** | 想知道"系统走到哪了" | 正常运行的关键里程碑 | `Addressables 初始化完成，耗时 320ms` | ❌（同上） |
| **Warning** | 上线后 | **能继续跑，但不对劲**：兜底、降级、用了默认值 | `句柄未释放，已兜底释放` | ✅ |
| **Error** | 上线后 | **本次操作失败，程序还能继续** | `加载 enemy_basic 失败` | ✅ |
| **Fatal** | 上线后 | **整体走不下去**（通常伴随抛异常/退出） | `catalog 加载失败，无法继续` | ✅ |

**定级三问**：程序还能继续吗（能且正常 `Info` / 能但不正常 `Warning` / 本次失败 `Error` / 整体不行 `Fatal`）；谁需要看；多久一次。
**Warning 与 Error 的分界（最易错）**：`Warning` = 系统自己扛过去了（兜底/重试成功）；`Error` = 这次没扛住，调用方要知道。

### 2.2 过滤机制（v1 采用）

| 机制 | 采用 | 说明 |
|---|---|---|
| **运行时级别过滤** | ✅ 主要手段 | Router 持有**全局阈值**；低于阈值的调用直接返回 |
| **前置判定（零分配）** | ✅ **纪律要求** | 热路径必须先 `IsEnabled(level)` 再拼字符串 |
| 编译期剥离 | ❌ 不用（§2.3） | — |
| 条件编译 `#if` | ⚪ 可选 | 极少数热点可用（侵入业务代码，按需） |

### 2.3 编译期剥离：**定案 = 放弃**

因为不做静态门面（约束 #6），而 `[Conditional]` **只对静态调用生效**，所以 **v1 没有编译期剥离**：

- Release 构建里 `Debug/Info` 调用**仍会执行**（一次接口调用 + 一次阈值比较，纳秒级）
- 但**不会有字符串分配**——前提是守住纪律：先 `IsEnabled` 再拼串

```csharp
if (_logger.IsEnabled(ZLogLevel.Debug))            // 先判定
    _logger.Debug($"耗时 {ms}", nameof(MyClass));  // 后拼接（关闭时不分配）
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

**典型调用**：

```
_logger.Info("加载完成", nameof(MyClass));                        // 自动带 成员名:行号
_logger.Warning("句柄未释放", nameof(PoolManager), context: this);
_logger.Error("加载失败", ex, nameof(ResourceService));
```

**输出前缀格式**：`[14:32:07.123] [MyClass.SetupAsync:128] 加载完成`

### 3.2 caller 三件套

| 参数 | 来源 | 成本 | 说明 |
|---|---|---|---|
| `className` | **调用方显式传 `nameof(类名)`** | 零（编译期常量） | 类名靠显式传（不用 `CallerFilePath` 硬解析）；忘了传就是 `null`（前缀缺类名） |
| `member` | `[CallerMemberName]` 自动 | 零 | 在 lambda / 局部函数里会得到编译器生成名（如 `<SetupAsync>b__0`）——预期行为 |
| `line` | `[CallerLineNumber]` 自动 | 零 | 定位到具体行，配合 member 足够精确 |

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

### 4.1 `context` 参数怎么用

`context` 就是 Unity 原生 `Debug.Log(message, context)` 的第二个参数——**一个 `UnityEngine.Object`**：

| 作用 | 说明 |
|---|---|
| **在 Console 里关联对象** | 每条日志右侧会出现该对象的引用；**双击日志会 ping/高亮那个对象**（Hierarchy 或 Project 面板） |
| 传什么 | MonoBehaviour 里传 `this`；或 `gameObject`、某个组件、`ScriptableObject`、被加载的资源 |
| 不传 | 默认 `null` → 没有对象关联（不影响文本输出） |
| 对象已销毁 | Console 显示 Missing（无害） |

```
_logger.Warning("对象池已满", nameof(Spawner), context: this);
_logger.Info($"加载 {prefab.name} 完成", nameof(Loader), context: prefab);
```

---

## 5. 日志模块的启动接入

> **启动 / Bootstrap 机制已抽到独立文档：`docs/architecture/bootstrap-design.md`**（模块 Bootstrap 契约、总 Bootstrap 编排、阶段顺序、位置约定、`CancellationToken` 用法）。本节只写**日志模块自己**如何接入。

| 项 | 内容 |
|---|---|
| Bootstrap 类 | `ZLoggerBootstrap`，实现 `IZModuleBootstrap` |
| 阶段 | **`ZBootPhase.Logging`（最先执行）**——其它模块初始化时都要打日志 |
| 职责 | ① 装配 `ZLogRouter` 与 Sink（Console / File）；② 创建 `ZMainThreadDispatcher` 与驱动对象（§6）；③ 注册退出 / `OnApplicationPause` flush 回调 |
| 收尾 | `Dispose()`：**Flush 全部 Sink → 停后台线程 → 销毁驱动对象**；⚠️ **收尾不绑 `CancellationToken`**（Scope 取消时不跳过收尾，否则尾部日志丢失） |
| 位置 | `Assets/Zipper/Core/Logging/`（模块程序集内——需要访问模块 `internal` 类型） |
| 不负责 | 容器注册（`builder.Register<IZLogger, ZLogger>()` 与 `.As<IZModuleBootstrap>()` 都在组装层 `GameLifetimeScope`） |
| 与资源模块的关系 | 资源 Bootstrap 在 `ZBootPhase.Resources`（晚于 Logging）→ 它注入到的 `IZLogger` 必然已就绪 |

**装配顺序与容器边界（路由器的 dispatcher 从哪来）**：

```
InitializeAsync:
    _dispatcher = new ZMainThreadDispatcher();                   // ① 必须【主线程】创建（构造时记录主线程 ID）
    _router     = new ZLogRouter(_dispatcher, sinks, level);      // ② 构造注入 dispatcher ← 路由器不查容器
    var driver  = CreateDriverObject(); driver.Init(_dispatcher); // ③ 驱动对象持 dispatcher
    _logger     = new ZLogger(_router);                           // ④ 日志器持 router
```

| 组件 | 进容器？ | 理由 |
|---|---|---|
| `IZLogger`（→ `ZLogger`） | ✅ **必须** | 其它模块唯一的日志入口 |
| `ZLoggerBootstrap`（`IZModuleBootstrap`） | ✅ **必须** | 由总 Bootstrap 按阶段驱动 |
| `ZMainThreadDispatcher` | ❌ **不进** | ① 构造必须在**主线程**（否则记录错主线程 ID）；② **Bootstrap 在容器 Build 之后运行**，此时无法再 `RegisterInstance`；③ 它是内部协作者 |
| `ZLogRouter` / Sink（Console/File） | ❌ **不进** | 内部实现；注册等于给外部"绕过 `IZLogger` 改 Sink / 绕开级别控制"的越权口子 |

> **原则**：**容器只装"对外服务"，不装"内部协作者"。**
> **例外**：若将来**多个模块**都需要"把回调派发到主线程"，应抽成独立服务 `IZMainThreadDispatcher`（放 Core，惰性单例注册并在工厂里断言主线程），而不是让别的模块去拿日志的 dispatcher。现在只有一个使用者 → YAGNI，先不抽。
> **可测性**：构造函数注入使 Router 可脱离容器单测（`new ZLogRouter(new FakeDispatcher(), sinks, ZLogLevel.Debug)`）。

---

## 6. 主线程分发

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

### 6.3 驱动方式：**驱动对象**（方案 a）

```
ZLoggerBootstrap.InitializeAsync：
    1) 创建 dispatcher 实例
    2) new GameObject("[Zipper]MainThreadDispatcher") + AddComponent<ZMainThreadDispatcherDriver>()
       + DontDestroyOnLoad
    3) driver.Update() → dispatcher.Pump(maxPerFrame)
    Dispose：销毁该对象
```

- `Internal/ZMainThreadDispatcherDriver.cs` 是 Core 内**唯一**的 MonoBehaviour（Core 不引 VContainer / Addressables / R3；仅引 UniTask 作为异步契约基础）
- 备选（记录备查）：PlayerLoop 注入（无额外对象、代码更复杂）；外部驱动（依赖场景对象，顺序易错）

**IL2CPP / 脚本后端兼容性（澄清）**：

| 概念 | 说明 |
|---|---|
| `MonoBehaviour` | Unity 的**运行时基类**（挂 GameObject 的脚本都继承它） |
| `Mono` / `IL2CPP` | Unity 的两种**脚本后端**——IL2CPP 构建里 MonoBehaviour 照常工作 |

- 本设计用到的都是**编译期确定类型**的路径：`new GameObject(...)`、`AddComponent<ZMainThreadDispatcherDriver>()`（泛型、AOT 静态实例化）、`DontDestroyOnLoad`、引擎回调 `Update()`——**均非反射**，IL2CPP 下无需任何额外配置。
- 需要避免的是反射式替代写法：`AddComponent(Type)`（运行期类型）、`MakeGenericMethod/MakeGenericType`、`System.Reflection.Emit`——本设计**均未使用**（Core 也不引任何 Emit 库）。
- **主线程派发与脚本后端无关**：`Debug.Log` 的主线程约束来自 Unity API 的线程模型，Mono 与 IL2CPP 相同。
- 若不想新增隐藏对象（可选替代）：① PlayerLoop 注入（无 MonoBehaviour，但要重建 `PlayerLoopSystem` 并处理域重载，调试更难）；② 由已有 MonoBehaviour（如 `GameLifetimeScope`）在 `Update()` 中代跑 `Pump()`（零新增对象，但把日志输出绑在该对象存活上）；③ 上层改用 UniTask/R3 的 PlayerLoop 工具（Core 不引这些库，故不适用）。**v1 仍采用驱动对象**（自包含、简单、可控每帧上限）。

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

### 7.2 文件按日期切分（不做轮转/清理）

| 要点 | 做法 |
|---|---|
| 文件名 | `zipper-yyyyMMdd.log`（如 `zipper-20260910.log`），**不带时分秒** |
| 归属规则 | 按**写入时刻**的本地日期（`DateTime.Now.Date`）——简单、可预期；不做"按每条日志时间戳归属" |
| 切分时机 | **后台写线程每批 Drain 之前**检查一次日期：与当前文件日期不同 → 走切分流程（开销可忽略） |
| 切分流程 | ① 先把队列剩余条目 Drain 进**旧文件**；② `Flush` + `Close` 旧 writer；③ 以新日期命名打开新文件（**append**）；④ 更新"当前文件日期" |
| 同日重启 | **append** 追加到同一天同一文件（不新开） |
| 系统时间被改/时区变化 | 不特殊处理（最坏情况多切一个文件） |
| 不做的事 | ❌ 大小轮转、❌ 旧文件清理（**既定决策**）→ 日志目录会随时间增长，需定期手工清理 |
| 验收 | 模拟跨零点后写日志 → 新日期文件出现、旧文件保留、无条目丢失 |

---

## 8. 并发模型

| 角色 | 线程 | 只做什么 |
|---|---|---|
| 生产者 | 任意线程 | 判级别 → 格式化 → **入队**（无锁、不做 IO） |
| 主线程泵 | 主线程（驱动对象 `Update`） | 出队 → `ZConsoleSink` |
| 后台消费者 | 单个后台线程 | 批量出队 → `ZFileSink`（含日期切分与刷盘判定） |

纪律：**不 lock**（`ConcurrentQueue`）；写文件单线程；Console 只在主线程；热路径先 `IsEnabled`；**队列有上限、满则丢最旧并如实记录丢弃条数**（防 OOM/卡帧）。

**关于"去重折叠"**：**v1 不做**——Unity Console 自带 **Collapse** 已覆盖"显示层折叠"。
> 注意两点：① Console 的 Collapse 只影响显示，**文件里仍会保留全部重复行**；② 若真出现"同一警告每帧刷"的日志风暴，首选在**调用点**解决（提高级别、加 `IsEnabled` 判断），而不是给日志系统加限流。确需限流时列为 v2 可选。

---

## 9. 与其它模块的衔接

| 模块 | 衔接方式 |
|---|---|
| **对象池** | `ZObjectPoolManager` 构造注入 `IZLogger` ✓；池内部（`ZObjectPool<T>`）要打日志时，**由管理器在建池时通过构造参数传入**（约束 #12，不污染 `ZPoolOptions<T>`）→ 池构造签名变为 `internal ZObjectPool(in ZPoolOptions<T> options, IZLogger logger)`；池内两处 `//TODO 使用框架自带日志库输出` 即可接上（同步见 `pool-manager-design.md` §9） |
| 资源管理器 | 构造注入 `IZLogger`；初始化耗时、加载失败、句柄兜底释放等走日志 |
| 事件总线 | 独立；总线自身异常（订阅者抛异常）打 `Warning` |
| 组装层 | 注册 `IZLogger → ZLogger`；`ZLoggerBootstrap` 注册为 `IZModuleBootstrap`（`Phase = Logging`）——详见 `bootstrap-design.md` §6 |

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
| 启动接入 | 容器启动 | `ZLoggerBootstrap` 在 `ZBootPhase.Logging` 执行；资源 Bootstrap 注入到的 `IZLogger` 已就绪（机制与验收见 `bootstrap-design.md` §11） |

---

## 11. 待决项

| 项 | 内容 | 建议 |
|---|---|---|
| 日志风暴限流 | 是否要"同来源每秒最多 N 条" | v1 不做（先靠级别 + 调用点纪律）；确需时 v2 加 |
| PlayerLoop 驱动 | 是否用 PlayerLoop 替换驱动对象 | 保持驱动对象（v1）；除非将来要去掉额外 GameObject |
| `className` 是否强制 | 忘了传就缺类名 | 靠代码规范约束（每次传 `nameof(类型)`），不强制 |
| 文件阈值/保留 | 大小轮转、旧文件清理 | **不做**（既定决策）；仅提醒目录会增长 |
| 调试模式 | 内存中保留最近 N 条（供监视器/面板查看） | 需要时再加（可选 Sink） |

---

## 12. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v1.4.1 | 2026-09-12 | 措辞同步：Core 的依赖边界更正为"**仅依赖 Unity + UniTask**（不引 VContainer / Addressables / R3）"——因为启动契约 `IZModuleBootstrap.InitializeAsync` 返回 `UniTask`（详见 `core-design.md` v0.9.3、`roadmap` v0.10） |
| v1.4 | 2026-09-10 | **Bootstrap 机制整体迁出**：原文 §5「Bootstrap 分层」的全部内容（契约 `IZModuleBootstrap`/`ZBootPhase`、总 Bootstrap 阶段编排与四方案对照、位置约定、容器注册、`CancellationToken` 用法、失败策略、惰性初始化备选）迁移并扩展为独立文档 **`docs/architecture/bootstrap-design.md`（v1.0）**；本文 §5 仅保留"日志模块的启动接入"（阶段 = `Logging`、职责、收尾不绑 ct、位置、与资源模块的先后关系），其余章节编号不变 |
| v1.3 | 2026-09-10 | 新增 §5.5 Bootstrap 位置约定（契约归 Core、模块 Bootstrap 归模块程序集、总编排与注册归组装层）※ 该节已随 v1.4 迁出 |
| v1.2 | 2026-09-10 | **启动顺序机制改硬**：`int Order` → 语义化 `ZBootPhase`；总 Bootstrap 把阶段顺序显式写在代码里，阶段内才遍历集合 ※ 该节已随 v1.4 迁出 |
| v1.1 | 2026-09-10 | 落实第二轮决策：剥离方案 a、驱动方案 a、caller 三件套、`context` 用法说明、Bootstrap 分层、文件按日期切分、去掉去重折叠、池侧用构造参数接收 `IZLogger` |
| v1.0 | 2026-09-10 | 初稿：按使用者 6 条约束重做日志设计（Bootstrap 实例化、主线程分发器实例化、5 级、caller、取消 `ZLogModule`、取消静态门面） |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
