# Zipper.Core 设计 — 基础设施与日志系统

> 状态：**v0.2 草稿，待审阅**
> 定位：`docs/planning/technical-roadmap.md` §4.2 中 `Zipper.Core`（基础设施：日志、事件、扩展、公共工具）的展开设计稿。**含伪代码，不含可编译实现**。
> **实施归属（v0.2 明确）**：本文件是**设计文档**——AI 只负责架构设计与少量伪代码（供设计参考，**非实现交付物**）；**代码实现由使用者完成**。依据：`docs/standards/agent-role.md`。
> 已定范围（用户决策 2026-09-06）：日志 Sink = **Console + 文件输出**；Release **剥离 Info 及以下**；**支持运行时动态改级别**。
> 关联：`docs/architecture/resource-manager-design.md`（资源管理器，v0.2）、`docs/standards/git-workflow.md`、`docs/standards/agent-role.md`

---

## 1. Zipper.Core 的定位与边界

**是什么**：框架最底层程序集，提供所有上层模块（Pool / Resources / Audio / UI）都要用的基础设施。它**不认识任何业务概念**，也不认识其它 Zipper 模块。

**依赖原则（重要，与 roadmap §4.2 有一处刻意偏差）**：

| 依赖 | 是否允许 | 说明 |
|---|---|---|
| Unity 引擎 | ✅ 必须 | 日志走 Unity `Debug`、文件走 `Application.persistentDataPath` |
| UniTask | ⚠️ 可选 | v1 **不引**：日志设计为同步门面 + 自建后台泵，不必依赖 UniTask；将来若需要再评估 |
| VContainer / Addressables / DOTween / R3 | ❌ 禁止 | 保持零框架耦合，装配由上层 `Zipper.Runtime` 负责 |

> roadmap §4.2 写的是「所有 Zipper.* 运行时程序集需引用 UniTask / VContainer」——**Core 是例外**：它是最底层，引 DI 容器会反过来让所有模块被迫传递依赖，破坏"地基可独立裁剪"的目标。此处偏差建议记入 roadmap 修订项。

**管什么**：
1. **Logging**：分级日志、模块 tag、运行时开关、编译期剥离、Console + 文件双输出（本文重点，§3）
2. **Assert / Guard**：`ZAssert` 断言（编辑器中断 + Release 降级为 Error 日志）
3. **Extensions**：Unity 对象空判断、Transform/GameObject 便捷方法（**按需加，禁止一次性堆砌**）
4. **工具**：框架版本常量、时间/随机/路径小工具

**不管什么**：
- ❌ 不做事件总线——roadmap 已定 UI 绑定用 **R3**，自造 EventBus 会与其重复；等 R3 vendor 后再决定是否需要一层薄封装
- ❌ 不做资源/池/UI 相关任何逻辑
- ❌ 不做下载进度、场景流程等业务编排

---

## 2. 模块总览

```
Zipper.Core
 ├─ Logging/
 │    ├─ ZLog（静态门面，可编译期剥离）
 │    ├─ IZLogger / ZLogger（模块化日志器，可注入、可测）
 │    ├─ IZLogSink → ConsoleSink / FileSink（可插拔输出）
 │    └─ ZLogEntry / ZLogLevel / ZLogModule（数据与枚举）
 ├─ Assert/          ZAssert（编辑器断言 + Release 降级日志）
 ├─ Extensions/      UnityObjectExtensions 等（按需增补）
 └─ Utils/            ZipperVersion、小工具（克制）
```

---

## 3. 日志系统设计（核心）

### 3.1 需求与约束

| 需求 | 决策 |
|---|---|
| 分级 | `Trace / Debug / Info / Warn / Error / Fatal` 六级 |
| 模块归属 | 每个日志带 tag（`Resources`/`Pool`/`UI`/`Audio`/`Core`），Console 前缀 `[Zipper.Resources]` |
| 输出目标 | **Console + 文件**双 Sink（文件：`persistentDataPath/ZipperLogs/`） |
| Release 开销 | **Trace/Debug/Info 编译期剥离**；Warn/Error/Fatal 始终保留 |
| 运行时控制 | 全局 + 按模块动态改级别（设置面板/控制台命令可调） |
| 线程安全 | 任意线程可调用；**Console 输出必须主线程**、文件写入可后台 |
| 性能 | 关闭级别时**零字符串分配**；文件 IO 不阻塞主线程 |
| 健壮性 | 日志系统自身异常不得影响游戏；退出时 flush 不丢尾日志 |

### 3.2 架构

```
   调用点                                  输出
┌──────────────────┐   ┌──────────────┐   ┌────────────────┐
│ ZLog.Info(...)   │   │              │   │ ConsoleSink    │→ Unity Console
│ （静态门面，      │──▶│  ZLogRouter  │──▶│（主线程）       │   (Debug.Log*)
│  可编译期剥离）   │   │  分级/开关    │   └────────────────┘
├──────────────────┤   │  模块过滤     │   ┌────────────────┐
│ IZLogger（注入） │──▶│  分发到 Sink  │──▶│ FileSink       │→ zipper-*.log
│ （模块化、可测）  │   └──────────────┘   │（后台线程+缓冲） │   (persistentDataPath)
└──────────────────┘                      └────────────────┘
```

**双入口**（关键设计，二者不可互相替代）：

| 入口 | 用于 | 能否编译期剥离 | 特点 |
|---|---|---|---|
| `ZLog`（静态门面） | 框架与业务普通调用点、热路径 | ✅ 能（`[Conditional]` 只对静态调用生效） | 零依赖、随处可用 |
| `IZLogger`（注入） | 模块化日志、需要 mock 的单测、需要携带模块上下文的对象 | ❌ 不能（接口调用运行时才解析） | 走 `IsEnabled` 前置判定 + 运行时过滤 |

> 因此：**热路径用 `ZLog` + 前置判定；需要注入/测试的场合用 `IZLogger`**。两者最终汇入同一 Router 与 Sink 链，行为一致。

### 3.3 分级策略（剥离 × 运行时开关）

| 级别 | 编译期（Release） | 编译期（Editor/Development） | 运行时默认 | 典型用途 |
|---|---|---|---|---|
| Trace | 剥离 | 保留（`[Conditional("ZIPPER_LOG_VERBOSE")]`） | 关 | 逐帧/逐次调用的细节 |
| Debug | 剥离 | 保留（同上） | 关 | 开发期调试 |
| Info | 剥离 | 保留（同上） | 开 | 关键流程节点 |
| Warn | **保留** | 保留 | 开 | 可恢复的异常（如句柄泄漏兜底） |
| Error | **保留** | 保留 | 开 | 失败路径 |
| Fatal | **保留** | 保留 | 开 | 不可继续（伴随抛异常/退出） |

- `ZIPPER_LOG_VERBOSE` 由 Core 的 asmdef `versionDefines`/平台规则控制：编辑器与 Development Build 定义，正式构建不定义。
- **两套机制的关系说清楚**：编译期剥离是"连调用都不存在"（参数不求值、零成本）；运行时开关是"编译进来了但被过滤"。剥离掉的级别在 Release 里**运行时也调不出来**——这是性能换可观测性的取舍，已确认接受。

### 3.4 API 设计

```csharp
namespace Zipper.Core.Logging
{
    public enum ZLogLevel { Trace, Debug, Info, Warn, Error, Fatal }

    public enum ZLogModule { Core, Pool, Resources, Audio, UI, Game }   // 可扩展

    public readonly struct ZLogEntry
    {
        public readonly ZLogLevel Level;
        public readonly ZLogModule Module;
        public readonly string Message;
        public readonly string TimeStamp;      // HH:mm:ss.fff
        public readonly int ThreadId;
        public readonly UnityEngine.Object Context;   // Console 双击跳转用（可空）
    }

    public interface IZLogSink : System.IDisposable
    {
        bool RequiresMainThread { get; }       // ConsoleSink=true（Debug.Log 仅主线程）；FileSink=false
        void Write(in ZLogEntry entry);        // 实现自行保证线程语义
        void Flush();
    }

    public interface IZLogger                  // 模块化日志器（可注入、可 mock）
    {
        ZLogModule Module { get; }
        bool IsEnabled(ZLogLevel level);
        void Log(ZLogLevel level, string message, UnityEngine.Object context = null);
        void Info(string message);
        void Warn(string message);
        void Error(string message, System.Exception ex = null);
    }

    public static class ZLog                   // 静态门面（可编译期剥离）
    {
        // 运行时控制
        public static void SetGlobalLevel(ZLogLevel level);
        public static void SetModuleLevel(ZLogModule module, ZLogLevel level);
        public static bool IsEnabled(ZLogLevel level, ZLogModule module = ZLogModule.Core);

        // 输出（Info 及以下带 [Conditional]，见 §3.5 伪代码）
        public static void Trace(string message, UnityEngine.Object context = null);
        public static void Debug(string message, UnityEngine.Object context = null);
        public static void Info(string message, UnityEngine.Object context = null);
        public static void Warn(string message, UnityEngine.Object context = null);
        public static void Error(string message, System.Exception ex = null);
        public static void Fatal(string message, System.Exception ex = null);

        public static IZLogger For(ZLogModule module);   // 取模块日志器（等价注入版）
    }
}
```

### 3.5 核心伪代码（设计级，供参考、非实现交付）

> 以下伪代码用于表达设计意图（接口形态、调用链、线程与剥离语义），**不追求可直接编译**；正式实现由使用者编写，命名与拆分可自行取舍。

#### F1 门面与分级判定（编译期剥离 + 前置判定）

```csharp
using System.Diagnostics;   // Conditional

public static class ZLog
{
    // Router 由初始化时装配；未初始化时退化为"仅 Console、Info 级"
    static ZLogRouter _router = ZLogRouter.CreateDefault();

    // ---- 可剥离级别：Release 构建里连调用带参数一起消失 ----
    [Conditional("ZIPPER_LOG_VERBOSE")]
    public static void Trace(string message, UnityEngine.Object context = null)
        => _router?.Dispatch(ZLogLevel.Trace, ZLogModule.Core, message, context);

    [Conditional("ZIPPER_LOG_VERBOSE")]
    public static void Debug(string message, UnityEngine.Object context = null)
        => _router?.Dispatch(ZLogLevel.Debug, ZLogModule.Core, message, context);

    [Conditional("ZIPPER_LOG_VERBOSE")]
    public static void Info(string message, UnityEngine.Object context = null)
        => _router?.Dispatch(ZLogLevel.Info, ZLogModule.Core, message, context);

    // ---- 始终保留级别 ----
    public static void Warn(string message, UnityEngine.Object context = null)
        => _router?.Dispatch(ZLogLevel.Warn, ZLogModule.Core, message, context);

    public static void Error(string message, System.Exception ex = null)
        => _router?.Dispatch(ZLogLevel.Error, ZLogModule.Core,
                             ex == null ? message : $"{message}\n{ex}", null);

    public static void Fatal(string message, System.Exception ex = null)
        => _router?.Dispatch(ZLogLevel.Fatal, ZLogModule.Core,
                             ex == null ? message : $"{message}\n{ex}", null);

    // ---- 热路径前置判定（避免字符串插值分配）----
    public static bool IsEnabled(ZLogLevel level, ZLogModule module = ZLogModule.Core)
        => _router != null && _router.IsEnabled(level, module);

    // ---- 运行时控制 ----
    public static void SetGlobalLevel(ZLogLevel level) => _router.SetGlobalLevel(level);
    public static void SetModuleLevel(ZLogModule module, ZLogLevel level)
        => _router.SetModuleLevel(module, level);

    public static IZLogger For(ZLogModule module) => new ZLogger(_router, module);
}
```

使用纪律（写进编码规范）：

```csharp
// ❌ 关闭级别时仍然分配了插值字符串
ZLog.Debug($"加载耗时 {elapsed}ms, 地址 {address}");

// ✅ 先判定后拼接（热路径必须这样写）
if (ZLog.IsEnabled(ZLogLevel.Debug, ZLogModule.Resources))
    ZLog.Debug($"加载耗时 {elapsed}ms, 地址 {address}");
```

#### F2 Router 与 Console Sink

```csharp
sealed class ZLogRouter
{
    readonly ZLogLevel[] _moduleLevels;         // 每模块当前级别阈值
    ZLogLevel _globalLevel;
    readonly List<IZLogSink> _sinks;

    public bool IsEnabled(ZLogLevel level, ZLogModule module)
        => level >= _globalLevel && level >= _moduleLevels[(int)module];

    public void Dispatch(ZLogLevel level, ZLogModule module, string message,
                         UnityEngine.Object context)
    {
        if (!IsEnabled(level, module)) return;   // 运行时过滤（编译期已剥离的不在此列）

        var entry = new ZLogEntry(level, module, message, NowStamp(), ThreadId(), context);

        foreach (var sink in _sinks)
        {
            if (sink.RequiresMainThread && !IsMainThread())
                MainThreadPump.Enqueue(() => sink.Write(entry));   // 见 §3.6 线程安全
            else
                sink.Write(entry);
        }
    }
}

sealed class ConsoleSink : IZLogSink
{
    public bool RequiresMainThread => true;      // Debug.Log 只允许主线程

    public void Write(in ZLogEntry e)
    {
        // 富文本着色 + 模块 tag；context 支持双击跳转
        var text = $"<color={ColorOf(e.Level)}>[{e.TimeStamp}] [Zipper.{e.Module}] {e.Message}</color>";
        switch (e.Level)
        {
            case ZLogLevel.Warn:  UnityEngine.Debug.LogWarning(text, e.Context);  break;
            case ZLogLevel.Error:
            case ZLogLevel.Fatal: UnityEngine.Debug.LogError(text, e.Context);    break;
            default:              UnityEngine.Debug.Log(text, e.Context);         break;
        }
    }
}
```

#### F3 File Sink（后台线程 + 缓冲 + 轮转）

```csharp
sealed class FileSink : IZLogSink
{
    public bool RequiresMainThread => false;     // 任意线程可入队，IO 在后台

    readonly System.Collections.Concurrent.ConcurrentQueue<string> _queue = new();
    readonly System.Threading.AutoResetEvent _signal = new(false);
    System.Threading.Thread _worker;
    System.IO.StreamWriter _writer;
    long _writtenBytes;
    const long MaxBytesPerFile = 2 * 1024 * 1024;   // 2MB 轮转
    const int  MaxQueuedLines = 4096;               // 队列上限，超出丢最旧（防日志爆炸拖垮游戏）

    public void Write(in ZLogEntry e)
    {
        if (_queue.Count >= MaxQueuedLines) _queue.TryDequeue(out _);  // 丢弃最旧
        _queue.Enqueue(Format(e));
        _signal.Set();
    }

    void WorkerLoop()                             // 后台线程：批量写，不阻塞主线程
    {
        var buffer = new System.Text.StringBuilder(8192);
        while (!_shutdown)
        {
            _signal.WaitOne(500);                 // 最多等 500ms 或攒批
            while (buffer.Length < 8192 && _queue.TryDequeue(out var line))
                buffer.AppendLine(line);
            if (buffer.Length == 0) continue;

            _writer.Write(buffer);
            _writtenBytes += buffer.Length;
            buffer.Clear();

            if (_writtenBytes >= MaxBytesPerFile) Rotate();   // 轮转 + 清理旧文件
        }
        FlushInternal();
    }

    public void Flush() { /* 请求后台刷盘并等待（退出时可调） */ }
}
```

#### F4 线程安全与主线程泵

```csharp
static class MainThreadPump       // 保证 ConsoleSink 始终在主线程写
{
    static readonly ConcurrentQueue<System.Action> _pending = new();
    static int _mainThreadId = -1;

    public static void Install()   // 初始化时由 ZLogBootstrap 调用（挂一个隐藏不销毁对象驱动 Update）
    { _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId; }

    public static bool IsMainThread() => System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;
    public static void Enqueue(System.Action a) => _pending.Enqueue(a);
    public static void Pump()      // 每帧由驱动对象调用
    { while (_pending.TryDequeue(out var a)) a(); }
}
```

- **主线程泵的驱动**：`Zipper.Core` 内放一个极小的 `MonoBehaviour` 引导对象（`DontDestroyOnLoad`），在 `Update` 里 `Pump()`；不引 UniTask、不引 VContainer，保持 Core 零框架依赖。
- **非主线程调用约定**：文件写入任意线程可用；Console 输出经泵延迟到主线程（日志顺序仍按入队顺序，不丢不乱）。

#### F5 运行时级别控制

```csharp
// 设置面板 / 调试命令使用
ZLog.SetGlobalLevel(ZLogLevel.Warn);                          // 全局只看 Warn 以上
ZLog.SetModuleLevel(ZLogModule.Resources, ZLogLevel.Debug);   // 但 Resources 模块看 Debug 以上
// 注意：Release 构建里 Info 及以下已被编译期剥离，SetLevel 对它们无效（预期行为）
```

#### F6 初始化与关闭

```csharp
static class ZLogBootstrap
{
    public static void Initialize(ZLogOptions options)
    {
        MainThreadPump.Install();
        var router = new ZLogRouter(options.GlobalLevel, options.Sinks);
        if (options.EnableFileSink) router.AddSink(new FileSink(options.LogDirectory));
        ZLog.Attach(router);                       // 门面接入
        Application.quitting += () => router.Flush();   // 退出前冲掉尾部日志（防丢）
    }
}
```

> VContainer 集成：由上层 `Zipper.Runtime` 在 Scope 中调用 `ZLogBootstrap.Initialize(...)`，并把 `IZLogger`（`ZLog.For(module)`）注册进容器供各模块注入 —— **Core 自身不引容器**。

### 3.6 关键坑（实施时逐条对照）

| # | 坑 | 规避 |
|---|---|---|
| 1 | `$"..."` 插值在判定前就分配 | 热路径先 `IsEnabled` 再拼接（§3.5 F1） |
| 2 | `[Conditional]` 只对**静态调用**生效 | 注入路径（`IZLogger`）不能剥离，必须靠运行时判定 |
| 3 | 后台线程调 `Debug.Log` 行为未定义 | Console 走主线程泵（§3.5 F4） |
| 4 | 文件 IO 阻塞主线程 | 后台线程 + 队列 + 批量写（§3.5 F3） |
| 5 | 日志爆炸拖垮游戏 | 队列上限丢最旧 + 单文件 2MB 轮转 + 旧文件数量上限 |
| 6 | 日志系统自身抛异常 | Sink 内部全 try/catch 吞掉并降级（绝不让日志搞崩游戏） |
| 7 | 退出丢尾日志 | `Application.quitting` + Scope Dispose 双保险 Flush |
| 8 | 移动平台写盘路径/权限 | 只用 `persistentDataPath`；启动时做一次写入自检失败则禁用 FileSink |
| 9 | Fatal 后继续运行 | `Fatal` 记录后由调用方决定抛异常/退出（日志不代劳） |

### 3.7 验收标准与测试点

| 验收点 | 测试场景 | 断言 |
|---|---|---|
| 分级过滤 | 设 global=Warn 后调 Info/Warn | Info 不输出、Warn 输出 |
| 模块级别 | 全局 Warn，但 Resources 设 Debug | Resources 的 Debug 输出，Pool 的不输出 |
| 编译期剥离 | Release 构建调 Trace/Debug/Info | 调用点与字符串分配在 IL 中不存在（Editor 用 Development 构建对比） |
| 文件输出 | 写 100 条日志后 Flush | 文件含 100 行、格式与时间戳正确 |
| 线程安全 | 后台线程写 1000 条 | 不抛异常、条数不丢（或按上限策略丢弃最旧）、Console 在主线程输出 |
| 轮转 | 写入超过 2MB | 生成新文件、旧文件按上限清理 |
| 退出不丢 | 写日志后立即退出 | 退出后文件包含尾部日志 |
| 健壮性 | 目录只读/磁盘满 | 不抛异常，FileSink 自禁用并 Warn 一次 |

---

## 4. 其它模块（简述，按需落地）

### 4.1 ZAssert（断言/守卫）

```csharp
public static class ZAssert
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void IsNotNull(object value, string message) { /* 编辑器断言 + 日志 */ }
}
```
- 编辑器/开发构建：触发断言（定位快）；Release：降级为 `ZLog.Error`，不中断玩家。

### 4.2 Extensions（按需增补，禁止堆砌）

- `UnityObjectExtensions`：`IsNull()/IsNotNull()`（正确处理 Unity 的"假 null"）
- `TransformExtensions` / `GameObjectExtensions`：常用便捷操作，用到再加
- 集合/字符串扩展：确有复用再加

### 4.3 事件（暂缓）

roadmap 已定 UI 绑定层用 **R3**；事件总线是否自研待 R3 vendor 后评估——避免现在造一套、将来与 R3 重复。

### 4.4 工具

- `ZipperVersion`（框架版本常量，随包分发便于排查）
- 时间/随机/路径小工具：**按需**

---

## 5. 工程接入要素（v0.2 新增）

### 5.1 目录与文件布局（建议）

```
Assets/Zipper/Core/
├── Zipper.Core.asmdef
├── Logging/
│   ├── ZLog.cs                       静态门面（可编译期剥离）
│   ├── ZLogLevel.cs / ZLogModule.cs / ZLogEntry.cs
│   ├── IZLogger.cs / ZLogger.cs      模块化日志器（可注入、可 mock）
│   ├── ZLogRouter.cs                 分级 / 模块过滤 / 分发
│   ├── IZLogSink.cs
│   ├── Sinks/ConsoleSink.cs
│   ├── Sinks/FileSink.cs
│   ├── ZLogOptions.cs / ZLogBootstrap.cs
│   └── Internal/MainThreadPump.cs
│       Internal/MainThreadPumpDriver.cs（Core 内唯一 MonoBehaviour）
├── Assert/ZAssert.cs
├── Extensions/（按需增补）
└── Utils/ZipperVersion.cs
```

- 命名空间：`Zipper.Core`，Logging 子目录用 `Zipper.Core.Logging`；**目录与命名空间保持一致**（与工程既有约定一致）。

### 5.2 asmdef 配置要点

| 项 | 取值 | 说明 |
|---|---|---|
| name | `Zipper.Core` | 已存在（当前仅 `{"name": "Zipper.Core"}`） |
| references | **空** | 只依赖引擎；不引 UniTask / VContainer / Addressables（§1 依赖原则） |
| autoReferenced | true（默认） | 让 Assembly-CSharp（业务/DI 层）可直接使用 |
| includePlatforms | 全平台 | 编辑器专用代码（如日志 Viewer）不放这里，将来放 `Zipper.Editor` |
| allowUnsafeCode | false | 无需 |

> 若将来确实要引 UniTask（如异步 flush），再补 references，同时更新 §1 的 roadmap 偏差记录。

### 5.3 初始化与关闭时序

```
游戏启动
 └─ GameLifetimeScope.Awake（或将来的 Zipper.Runtime 引导组件）
      ├─ 1) ZLogBootstrap.Initialize(ZLogOptions)     ← 越早越好，先于任何业务日志
      │       ├─ MainThreadPump.Install()（记录主线程 ID + 创建驱动对象 DontDestroyOnLoad）
      │       ├─ 装配 Router 与 Sink（ConsoleSink 必装；FileSink 按配置）
      │       └─ ZLog.Attach(router)（门面接入）
      ├─ 2) Application.quitting += Shutdown            ← 退出前 Flush，防丢尾日志
      └─ 3) Configure(IContainerBuilder)：
              builder.RegisterInstance<IZLogger>(ZLog.For(ZLogModule.Core));   // 可选

Scope 关闭 / 应用退出
 └─ ZLogBootstrap.Shutdown() → Flush 全部 Sink → 停止后台线程（Join 带超时）→ 移除驱动对象
```

要点：
- **初始化必须早于任何业务日志**：推荐放在 Scope 的 `Awake`/引导入口，早于 `Configure` 内的注册。
- **驱动对象**：`MainThreadPumpDriver` 运行时 `Instantiate` + `DontDestroyOnLoad`，不入场景；编辑器下依赖 domain reload 自动重建（可用 `[RuntimeInitializeOnLoadMethod]` 兜底）。
- **未初始化时的降级**：门面持有默认 Router（仅 Console、Info 级），保证任何时刻调用日志都不报错。
- **退出不卡死**：后台线程 Join 设超时（如 1s），超时即放弃剩余缓冲。

### 5.4 与 GameLifetimeScope 的接入关系

```
GameLifetimeScope（Assembly-CSharp，现状）
 ├─ Awake
 │    └─ ZLogBootstrap.Initialize(...)                 ← v1 阶段手工调用
 ├─ Configure
 │    ├─ builder.RegisterInstance<IZLogger>(ZLog.For(ZLogModule.Core));
 │    ├─ builder.Register<IZResourceManager, ZResourceManager>(Lifetime.Singleton);
 │    └─ builder.RegisterEntryPoint<ResourcesBootstrapper>(Lifetime.Singleton);
 └─ 将来：改由 Zipper.Runtime 的引导组件统一装配（业务工程零改动）
```

- **模块取 tag**：各模块用 `ZLog.For(ZLogModule.Resources)` 或门面 `ZLog.Warn(msg, context)`；**框架内部普通调用建议用门面**（可编译期剥离），**需要 mock 的单测用注入的 `IZLogger`**。
- **第一个真实用例**：`ResourcesBootstrapper.InitializeAsync` 前后打点，记录 Addressables 初始化耗时（验证日志系统同时给资源管理器提供可观测性）。

### 5.5 裁剪与共存

- Core 无框架依赖 → 任何层次都可引用；某模块不用日志时无需改动（无强制引用）。
- 与 Unity 自带 `Debug.Log` 共存：框架代码统一走 `ZLog`，第三方/Demo 代码可直接用 `Debug.Log`。
- 现有调用点衔接：`ZResourceManager` 的 `Debug.LogWarning` 与两处 TODO，**由使用者实现日志系统时**自行替换（AI 不代改代码）。

---

## 6. 开放问题与待办

| 项 | 内容 | 建议 |
|---|---|---|
| 文件保留策略 | 单文件上限 2MB 之外的"保留最近 N 个文件/按天切分" | 实现时定，默认保留最近 5 个 |
| 编辑器 Viewer | 是否需要 Console 之外的独立日志窗口 | 毕设演示需要再加（内存 Sink 可扩展） |
| 日志格式 | 是否需要 JSON 结构化行（便于脚本分析） | 默认纯文本，需要时加 Sink 变体 |
| R3 事件 | 事件总线是否自研 | 等 R3 vendor 后评估 |
| roadmap 偏差 | Core 不引 VContainer/UniTask 与 roadmap §4.2 冲突 | 记入 roadmap 修订项 |
| 现有调用点替换 | Resources 的 Debug.LogWarning/TODO | 由使用者实现日志系统时统一替换 |
| MainThreadPump 驱动方式 | MonoBehaviour 驱动 vs 自定义 PlayerLoop 注入 vs 引 UniTask | v1 选 MonoBehaviour（Core 零依赖）；若日后引入 UniTask 可改为 PlayerLoop |
| 编译期剥离符号 | 用单一 `ZIPPER_LOG_VERBOSE` 还是按级别多符号 | v1 单符号（Editor/Development 定义）；需要更细粒度再拆 |

---

## 7. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.2 | 2026-09-06 | ① 对齐协作设定：明确实施归属（AI 只做设计+伪代码，代码由使用者实现）、§3.5 伪代码加定位说明；② 新增 §5「工程接入要素」（目录布局 / asmdef / 初始化与关闭时序 / 与 GameLifetimeScope 接入 / 裁剪共存）；③ §6 补两条待办 |
| v0.1 | 2026-09-06 | 初稿：Core 定位与依赖原则 + 日志系统（双 Sink/剥离/运行时级别/线程安全/伪代码）+ Assert/Extensions 简述 + 验收 |

> 审批：本文件为设计草稿，不含代码实现；由使用者据其自行实现，AI 不代写代码（见 `docs/standards/agent-role.md`）。
