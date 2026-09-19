# Zipper 事件总线设计（EventBus）

> 状态：**v0.5.1 草案，待审阅**
> 定位：`IZEventBus` / `ZEventBus`（`Zipper.Core.Events`）的**契约与机制设计**——订阅凭据、通道结构、分发语义、生命周期、装配、R3 桥。**只给设计思路与思路级伪代码，不含实现代码**。
> 实施归属：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/architecture/core-design.md` §4.3（决策来源：单实现 + R3 桥、「为什么不做 R3 版总线」五条依据）、`docs/architecture/logging-design.md`（`IZLogger`、主线程判定）、`docs/architecture/bootstrap-design.md`（装配与阶段）、`docs/standards/naming-convention.md`、`docs/planning/technical-roadmap.md`
> 变更记录：v0.1 初稿（把 `core-design.md` §4.3 的决策展开为可实现契约，并补齐 §4.3 未定的机制细节）

---

## 0. 这份文档怎么读

| 读者 | 建议路径 |
|---|---|
| 要立刻实现的人 | §1 事实 → §2 契约 → §3 机制 → §4 分发语义 |
| 要把关的人 | §2.3 与 §4.3 的差异 → §6 装配 → §9 验收 → §13 待决 |
| 做 UI / 要用操作符的人 | §8 R3 桥（含"今天能用什么、缺什么"） |
| 想知道"为什么不那样做"的人 | 附录 A（与 CommunityToolkit.Mvvm messenger 的对照）、附录 B（为什么不做 `this` 自动装配） |

---

## 1. 前置事实（已核对，2026-09-13）

### 1.1 代码现状

| 项 | 状态 |
|---|---|
| `Zipper.Core/Events/IZEventBus.cs` | 空接口壳（已提交） |
| `Zipper.Core/Events/ZEventBus.cs` | 空实现壳（已提交） |
| 容器注册 | `builder.Register<IZEventBus, ZEventBus>(Lifetime.Singleton)` 已在 `GameLifetimeScope` 中 |
| `ZBootPhase.Events` | 预留阶段，**当前无任何 Bootstrapper** |
| 文档 | 决策在 `core-design.md` §4.3；**本文是它的展开稿** |

### 1.2 R3 环境（已核对：官方要求的两步安装**都已完成**）

| 步骤 | 形态 | 位置 | 版本 |
|---|---|---|---|
| ① NuGetForUnity 装 `R3`（core） | 预编译 `R3.dll`（netstandard2.1） | `Assets/Packages/R3.1.3.1/` | 1.3.1 |
| ② UPM git URL 装 `R3.Unity`（Unity 集成） | UPM 包 `com.cysharp.r3` → 程序集 `R3.Unity`（+ `R3.Unity.Editor`） | `Library/PackageCache/com.cysharp.r3@fdfb36e3d5/`（来源见 `Packages/manifest.json`） | 1.3.1 |

> `R3.Unity` 的 asmdef 显式引用 core：`overrideReferences: true` + `precompiledReferences: [R3.dll, Microsoft.Bcl.TimeProvider.dll, Microsoft.Bcl.AsyncInterfaces.dll]`；两处版本一致（1.3.1），无版本错配。

**Unity 集成提供的能力（已具备）**：

| 能力 | 来源 | 说明 |
|---|---|---|
| `UnityProviderInitializer.SetDefaultObservableSystem` | `Runtime/UnityProviderInitializer.cs` | 带 `[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)]`，**启动自动执行**：把 `ObservableSystem.DefaultTimeProvider` / `DefaultFrameProvider` 设为 `UnityTimeProvider.Update` / `UnityFrameProvider.Update`；未处理异常默认交给 `UnityEngine.Debug.LogException` |
| `UnityTimeProvider` / `UnityFrameProvider` | 同目录 | PlayerLoop 各阶段（含 `*IgnoreTimeScale` 变体） |
| `AddTo(this GameObject)` / `AddTo(this Component)` | `Runtime/MonoBehaviourExtensions.cs` | 内部走 `RegisterTo(mb.destroyCancellationToken)`（Unity 2022.2+ 原生有该 token）；会按需给对象挂 `ObservableDestroyTrigger` |
| `UnityEvent.AsObservable(...)` | `Runtime/UnityEventExtensions.cs` | 与 UnityEvent 互操作 |
| Trigger 组件族、`SerializableReactiveProperty<T>` | `Runtime/Triggers/` 等 | Unity 侧常用能力（可序列化的 ReactiveProperty 等） |
| Editor 窗口 `ObservableTracker` | `Editor/ObservableTrackerWindow.cs` | **R3 订阅泄漏排查工具**（见 §10） |

**一条需要注意的边界（影响"Core 不引 R3"这条纪律）**：

core 是通过 NuGet 落在 `Assets/Packages/` 的**预编译 DLL**，而 `Zipper.Core.asmdef` 未打开 `overrideReferences` → **Unity 会把它自动引用给所有程序集**（`Library/Bee/artifacts/.../Zipper.Core.rsp` 里确实有 `-r:"Assets/Packages/R3.1.3.1/lib/netstandard2.1/R3.dll"`）。也就是说 **Core 里写 `R3.*` 现在也能编过**——"Core 不引 R3"目前只有纪律约束、没有编译期强制（`R3.Unity` 是 UPM 程序集，只有 asmdef 显式 `references` 才可见，那一层是硬隔离）。若要编译期强制，见 §13 D9。

---

## 2. 契约

### 2.1 接口形态（`IZEventBus`，定义在 `Zipper.Core`）

```
public interface IZEventBus : IDisposable
{
    // ① 匿名订阅：返回"可释放的订阅凭据"，凭据 Dispose 即退订（幂等）
    IDisposable Subscribe<T>(Action<T> action) where T : class;

    // ② 收件人订阅：subscriber 只用于"批量注销"记账，不改变 handler 签名
    IDisposable Subscribe<T>(object subscriber, Action<T> action) where T : class;

    // ③ 按委托取消：移除所有与该委托相等的订阅，返回是否移除了至少一个
    //    ⚠️ 要求"同一个委托实例"；两处分别重写的捕获型 lambda 匹配不上 → 记 Warning
    bool Unsubscribe<T>(Action<T> action) where T : class;

    // ④ 注销该收件人的全部订阅（幂等）；subscriber 为 null → 记 Warning 并忽略（防误清匿名订阅）
    void UnsubscribeAll(object subscriber);

    // 发布：同步、立即分发
    void Publish<T>(in T @event) where T : class;

    // 调试/监控：状态快照 DTO（照对象池 ZPoolState/ZPoolsState 的形状，见 §10）
    ZEventsState GetState();
}
```

> **v0.5 契约变更（对齐已实现形态）**：`recipient` 统一改称 **`subscriber`**（代码与文档同一个词，消除 B8 那类"一物两名"）；`UnregisterAll` 改名 **`UnsubscribeAll`**（更直白）；`int SubscriberCount<T>()` 由 **`ZEventsState GetState()`** 取代（照对象池的 DTO 风格，见 §10）。
>
> **`where T : class`（v0.5 定案：事件必须是引用类型）**：原稿"不加约束、载荷优先 `readonly struct`"**作废**。代价与随之而来的约定：① 每次发布都需要一个事件对象（构造/复用/池化由调用方负责；**总线自身仍然零分配**）；② 事件类型建议 `sealed` + 只读成员，避免"发布后被人改"；③ **struct 事件不支持**。

> **两条订阅路径的分工**（硬规则）：
> - **有生命周期的对象**（模块服务、MonoBehaviour 面板）→ 走 ②，注销靠 ④ 一句话；
> - **生命周期不由自己决定或很短的对象**（R3 桥接、测试、`using` 块）→ 走 ①，靠凭据 Dispose。
>
> 为什么 handler 签名里**不**回传 subscriber（工具包那种 `Action<object,T>`）：见附录 A。

> **三种取消方式**（别用错）：
>
> | 想做的事 | 用什么 | 可靠性 |
> |---|---|---|
> | 取消**某一个**订阅 | 任何 `Subscribe` 返回的**凭据** `Dispose()`（两条订阅路径的返回值都能用） | ✅ 结构上必然成立 |
> | 取消**某个对象的全部**订阅 | `UnsubscribeAll(subscriber)` | ✅ 结构上必然成立 |
> | 按 handler 取消（熟悉的 `-=` 手感） | `Unsubscribe<T>(action)` | ⚠️ 可靠的前提是"**同一个委托实例**"：方法组 / 静态方法 / 无捕获 lambda 每次求值都等价，没问题；**在订阅处和退订处各重写一遍的捕获型 lambda**（`m => OnX(m, _local)`）会新建闭包对象（Target 不同）→ **匹配不上 → 返回 false + 记 Warning**（不是静默失败）。把 lambda 存成变量、退订时传同一个实例也可以 |
>
> - `Unsubscribe` 语义：移除**所有**与该委托相等的订阅（同一 handler 注册两次不会留僵尸），与订阅路径无关（收件人订阅只要 handler 相等也会被移除）；**返回 `bool`**，未命中时记 Warning——因为"能不能退订"是调用方唯一能拿到的反馈；
> - 在**两个地方分别重写**捕获型 lambda 的订阅，请用**凭据**或**收件人**路径注销，不要用 `Unsubscribe`。

思路级伪代码（设计级，非最终实现）：

```
Subscribe<T>(subscriber, action):
    action 为 null → 抛 ArgumentNullException（编程错误，早失败）
      （否则 null handler 会进通道，之后每次发布都记一条 Error —— 错的地方离现场太远）
    subscriber 为 null = **匿名订阅**（合法；单参重载就是这么调它的）
    — 只有 UnsubscribeAll(null) 需要守卫（见下）
    非主线程 → 记 Error 并返回"空凭据"（**共享单例，不返回 null**，否则调用方 Dispose 时 NRE）
    已 Dispose → 返回"空凭据"（一个共享的单例，Dispose 空实现；不抛，关闭期宽容，见 §5.3）
    取（或创建）T 的通道 → 把 action 包装成 Subscription（记下 subscriber）加入通道
    → 返回该 Subscription（Dispose 时由通道完成"标记 + 摘除"）

UnsubscribeAll(subscriber):
    subscriber 为 null → 记 Warning 并直接返回（**不能**当成"清理匿名订阅"）
    非主线程 → 记 Error 并忽略
    遍历所有通道 → 用**引用相等**挑出 Subscriber 与之相同的 Subscription
      → 标记 IsDisposed + 从数组移除（copy-on-write，同 §3.2）
    （幂等：没有匹配项时就是空操作）

Unsubscribe<T>(action):
    action 为 null → 抛 ArgumentNullException
    非主线程 → 记 Error 并返回 false
    取 T 的通道：不存在 → 记 Warning 后返回 false
    → 用**委托相等**（Delegate.Equals：Target + Method）挑出全部匹配的 Subscription
      → 标记 IsDisposed + 从数组移除
    一个都没匹配到 → 记 Warning（"未找到匹配订阅：可能是两处分别重写的捕获型 lambda，或已注销"）并返回 false
    （语义：移除全部匹配项；与订阅路径无关）

Publish<T>(event):
    已 Dispose → 记一次 Debug 后返回
    非主线程 → 记 Error 后返回（§4.2）
    取 T 的通道：**不存在 → 直接返回，不记任何日志**（"没人订阅"是最常见的情况，不是异常；
      在这里刷 Warning 会破坏"零分配发布"这条验收，还会刷屏）
    对通道当前订阅者数组做遍历（数组即快照，见 §3.2）
      → 每个元素先查 IsDisposed（即时退订语义）
      → try/catch 调用；异常记 Error 后继续下一个（§3.4）
```

### 2.2 命名与类型约定

| 项 | 约定 | 依据 |
|---|---|---|
| 接口 / 实现 | `IZEventBus` / `ZEventBus` | 品牌 API 用 `IZ*` / `Z*`（`naming-convention.md` §1） |
| 凭据具体类型 | `Subscription`（**不加 Z 前缀**），`internal`，实现 `IDisposable` | 内部机制类型不加前缀；接口只暴露 `IDisposable`，调用方看不到该类型名 |
| 通道类型 | `EventChannelBase`（非泛型基类）+ `EventChannel<T>`，均 `internal` | 同上；基类只为"批量清空"提供入口 |
| 事件类型 | 框架自带事件：`Z*` + **过去式**（`ZResourceLoaded`）；**业务事件按业务命名，不加 Z** | `naming-convention.md` §1/§4（`*` 前缀是"框架对外概念"，业务事件不属于框架品牌 API） |
| 载荷形态 | **必须是引用类型**（`where T : class`）——建议 `sealed` + 只读成员、只放纯数据（id、枚举、数值、Unity 对象引用），不放模块接口/句柄；**struct 事件不支持**（v0.5 定案，见 §2.1） | 引用类型便于复用/池化；只读成员避免"发布后发布者继续改对象"的共享可变状态 |
| 接口泛型约束 | **`where T : class`**（v0.5 定案：仅支持引用类型事件） | 原稿"不加约束"作废；代价是每次发布需要一个事件对象（构造/复用由调用方负责，**总线自身仍零分配**） |
| 收件人 `subscriber` | **非 null 的引用类型**，传"这个订阅的生命周期归谁管"的那个对象（即会在 `OnDestroy`/`Dispose` 里清理它的对象）；**禁止值类型**——会被装箱，注册与 `UnsubscribeAll` 各装箱一次、引用不同 → **永远匹配不上、静默泄漏**；**不要求**等于 `handler.Target`（捕获型 lambda 的 Target 是编译器生成的闭包类） | `UnsubscribeAll` 靠**引用相等**匹配，键必须是稳定的对象身份；语义约定见 §5.1 |

### 2.3 本文与 `core-design.md` §4.3 的差异（**需同步或明确拒绝**）

| # | §4.3 原表述 | 本文建议 | 理由 |
|---|---|---|---|
| 1 | "线程：默认主线程发布；**后台线程发布经主线程泵**" | **v1 只允许主线程发布**；非主线程 → 记 Error 并丢弃。需要跨线程时由调用方自己 `SwitchToMainThread` 后再发布 | 若 Publish 自动 marshal，则"主线程发布=立即分发、后台线程发布=延迟分发"，**同一 API 两种时序语义**，订阅方无法推理；而日志可以这么做是因为它是 fire-and-forget，事件不是。见 §4.2 |
| 2 | "发布时对订阅者做**快照**" | 细化为**copy-on-write 数组 + 逐元素 `IsDisposed` 检查**（快照保证遍历安全，`IsDisposed` 保证**退订立即生效**） | 只做快照会出现"退订后本轮仍收到一次"的经典意外（对象已销毁仍被回调）；逐元素检查成本可忽略 |
| 3 | 未明确"回调内订阅"的可见性 | 明确：**回调内新订阅 → 本轮不收到，下轮起生效** | 需要一个确定规则，否则行为依赖实现细节 |
| 4 | "Scope 关闭统一释放（防泄漏兜底）" | v1 **不引入总线侧的 Scope 收集**：注销由持有方负责——收件人路径在 `Dispose`/`OnDestroy` 里 `UnsubscribeAll(this)`（§5.1），匿名路径靠凭据 `Dispose` | 总线替别人管生命周期会引入"谁负责"的模糊地带；项目已有"谁签发谁释放"的文化（`AssetHandle`）。采纳收件人路径后，"凭据集合类型"这个需求自动消解（§13.1） |
| 5 | 未涉及 | 补充**事件类型放哪**的两级规则（§9） | §4.3 §6 待办里的"事件类型集中放在哪个命名空间/目录" |
| 6 | 未涉及 | **新增"收件人维度"**（`Subscribe<T>(subscriber, action)` + `UnsubscribeAll`） | 纯增量能力，§4.3 无需修订；它把"订阅泄漏"从"靠记性保存凭据"变成"一句话注销"，理由见附录 A |
| 7 | 未涉及（§4.3 只提"v1 明确不做：优先级、粘性、异步分发……"） | 明确**不采纳** CommunityToolkit.Mvvm messenger 的 class 信封 / 弱引用 / 请求-应答 / MVVM 值变化语义 / `IRecipient` 声明式注册 | 见附录 A（逐条理由） |

> 若采纳本文 §4.2 与 §4.3 不同，`core-design.md` §4.3 需同步修订（或把"后台线程经泵"标为二期）。

---

## 3. 内部机制

### 3.1 通道与寻址

```
ZEventBus
 ├─ Dictionary<Type, EventChannelBase> _channels     // 事件类型 → 通道（唯一寻址入口）
 └─ bool _disposed

EventChannel<T> : EventChannelBase
 └─ Subscription[] _subscriptions                   // copy-on-write 数组（见 §3.2）
```

- **零反射**：寻址键是 `typeof(T)`（编译期静态类型），创建通道是 `new EventChannel<T>()`，取用是 `as EventChannel<T>` —— 全程无 `MakeGenericMethod` / `Activator.CreateInstance` / `Reflection.Emit`，IL2CPP 安全。
- **只按精确类型匹配**：`Publish<BaseEvent>` 不会送到 `Subscribe<DerivedEvent>`，反之亦然（§4.3 已定：避免隐式继承匹配）。
- 通道按需创建（首次订阅或发布时）；`Publish` 时通道不存在 = 无订阅者 = 直接返回，**不创建**。

### 3.2 copy-on-write 数组（零分配发布的关键）

```
Subscription（internal sealed, IDisposable）
 ├─ Action<T> Handler
 ├─ object Subscriber         // 收件人（匿名订阅为 null），只用于 UnsubscribeAll 记账
 ├─ EventChannel<T> _channel  // ← 反向边：所属通道（"我没了"时通知它）
 └─ bool IsDisposed           // 退订标记，发布时逐元素检查

Subscribe：新建 Subscription（记下 Subscriber + 通道）→ 数组整体复制 + 追加 → 替换 _subscriptions
退订（Dispose）：标记 IsDisposed=true → 调用 _channel.Remove(this)（**由通道完成摘除**）
UnsubscribeAll(subscriber)：所有通道里引用相等匹配的 Subscription → 标记 + 摘除（批量）
Unsubscribe<T>(action)：同一通道里**委托相等**（Target + Method）匹配的 Subscription（可能多个）→ 同上
Publish：直接遍历当前的 _subscriptions（**不分配、不复制**）
```

> **反向边是必需的，但要"指向通道、而不是直接改通道的存储"**（这是最容易被写错的一处）：
> `Subscription` 只声明"我不再有效"，**由通道负责**把它从数组里摘掉 —— 通道必须提供 `Remove(Subscription<T> target)`（按**身份**匹配）这个重载。
> ⚠️ 如果只提供 `Remove(object subscriber)`（按收件人匹配），`_channel.Remove(this)` 会被编译器绑到**那个重载**上 → 拿"订阅对象"去和"收件人对象"比 → **永远匹配不上，静默什么都删不掉**（表现为：退订后计数不减、数组只增不减）。

- 收益：**发布零分配**（项目对 GC 敏感：池/资源/日志都按这个标准设计）；订阅/退订是 O(n) 复制 —— 总线定位是"跨模块低频通知"，订阅者数量小，代价可接受。
- 同时满足两件事：遍历安全（数组引用不可变）+ 退订立即生效（`IsDisposed`）。
- `UnsubscribeAll` 是 O(通道数 × 订阅数) 的扫描；`Unsubscribe` 是单通道扫描。两者都是低频操作，**不建索引**（索引会引入第二份需要同步维护的状态）。
- **`Unsubscribe` 的匹配是 `Delegate.Equals`（Target + Method）**：同一个委托实例（或等价的方法组 / 静态方法 / 无捕获 lambda）能匹配上；**在两处分别重写的捕获型 lambda** 会新建闭包 → 匹配不上 → 返回 `false` + 记 Warning（§2.1 的取消方式表）。`UnsubscribeAll` 没有这个陷阱。
- **收件人比较必须用引用相等**（`ReferenceEquals`，或把字段声明为 `object` 后用 `==`）。**不要**把 `Subscriber` 声明成 `UnityEngine.Object` 再比较 —— Unity 重载的 `==` 会把"已销毁"的伪 null 参与进来，语义就不是"同一个对象"了。
- **增删都走"复制出新数组 → 替换字段"**（v0.5.1 更正）：`Array.Resize(ref _subscriptions, …)` 本身就是"分配新数组 + 复制 + 把新引用写回字段"，**语义上就是 copy-on-write**，用它没问题 —— 我 v0.5 初稿把它写成"原地改写、要避免"是**错的**（原地扩容在 .NET 里不存在）。真正要守住的只有一条：**不要在"正在被遍历"的那个数组上原地改元素**，否则"数组即快照"就被破坏。
- **`Remove(...)` 类方法要返回"移除了几个"**（v0.5.1 补）：否则上层 `Unsubscribe` 只能无条件 `return true` → **未命中却谎报成功**（比静默更糟）。约定：`EventChannel<T>.Remove(Action<T>)` 返回 `int`，`ZEventBus.Unsubscribe` 据此决定返回 `true` 还是"记 Warning + 返回 `false`"。

### 3.3 发布语义（精确规则）

| 场景 | 规则 |
|---|---|
| 订阅者顺序 | 按订阅先后调用（数组顺序即订阅顺序）；**不提供优先级** |
| 回调内 **订阅** 同类型事件 | 本轮不收到；下一轮（下次 Publish）起收到 |
| 回调内 **订阅** 其他类型事件 | 立即生效（该类型本轮尚无遍历在进行） |
| 回调内 **退订自己** | 立即生效（`IsDisposed` 检查），自己不再被本轮后续调用 |
| 回调内 **退订别人** | 立即生效；若该订阅者尚未被本轮调用，则**不再收到**本轮事件 |
| 回调内 **`UnsubscribeAll(自己)`** | 等价于退订自己的全部订阅：本轮后续不再收到（即使本类型还有别的 handler） |
| 回调内 **`UnsubscribeAll(别人)`** | 立即生效；对方尚未被本轮调用则不再收到本轮事件 |
| 回调内 **再发布**（重入） | 允许，深度优先、同步完成；顺序 = 内层先跑完再回到外层剩余订阅者 |
| 重入深度 | 不做限制；**风险提示**：A 事件的处理里发 A 事件 = 无限递归 → 纪律见 §4.5 |

### 3.4 异常隔离

| 项 | 约定 |
|---|---|
| 单个订阅者抛异常 | 捕获 → 记日志（`IZLogger.Error`，带事件类型 + 订阅者标识）→ **继续通知其余订阅者** |
| 事件类型 | `ZLogLevel.Error`（不是 Fatal：一条通知失败不该打断框架运行） |
| 订阅者标识 | 优先 `handler.Target?.GetType().Name` + `Method.Name`；若担心 IL2CPP 元数据裁剪，退化为"第 N 个订阅者" + 事件类型 |
| 是否可配置 | **v1 不可配置**（固定"隔离 + 记日志"）；需要 fail-fast 的场景不用总线（用接口方法调用） |
| 日志不可用时 | `IZLogger` 未装配（`_router == null`）会静默丢弃 —— 已知代价，见 `logging-design.md` §2 的兜底约定 |

---

## 4. 分发语义

### 4.1 同步、立即

`Publish` 返回时，所有订阅者（除异常者）都已执行完。**不做**：延迟到帧末、队列化、异步分发、按阶段分发。（延迟语义需要单独设计，见 §12。）

### 4.2 线程模型（v1）

| 调用 | 允许线程 | 违规时 |
|---|---|---|
| `Subscribe`（两种重载） | **仅主线程** | 记 Error 并忽略本次操作 |
| `UnsubscribeAll` / `Unsubscribe` / 凭据 `Dispose` | **仅主线程** | 记 Error 并忽略本次操作 |
| `Publish` | **仅主线程** | 记 Error 并**丢弃**该次发布 |
| `Dispose`（总线本身） | 容器释放（主线程） | — |

- **判定方式：总线自持线程 ID，不复用日志的 dispatcher。** `ZEventBus` 在构造时（容器在主线程构建）捕获 `Thread.CurrentThread.ManagedThreadId`，`Publish`/`Subscribe` 前比一次。不复用 `ZMainThreadDispatcher` 的理由（三条具体耦合）：
  1. **命名空间语义**：它是 `Zipper.Core.Logging` 里的类型，总线（同程序集）用它等于把"日志模块的内部机制"降格为全 Core 的基础设施，读代码的人会困惑；
  2. **所有权**：它现在归 `ZLoggerBootstrapper` 所有并在其 `Dispose` 里释放；日志引导器先于总线释放（创建顺序决定逆序释放）→ 总线会拿到一个已释放的 dispatcher，`Enqueue` 被静默短路 → **事件悄悄丢失**，比报错更难查；
  3. **启动顺序**：它在 **Logging 阶段**才创建，而总线在容器构建期就可用 → 存在"总线能用、设施未就绪"的窗口，要额外写降级分支。
  复用收益仅是"3 行判定逻辑"，不值得上面三条耦合。
- **这个校验不是可选的礼貌检查**：通道是 copy-on-write + 非原子数组替换，要求"发布与订阅/注销同线程"，否则数组替换与遍历并发会撕裂/丢更新。它是维护不变量的断言，属于总线自身。
- **为什么不在这里排队**：见 §2.3 差异 1。"跨线程发布"讨论过三条路，**v1 取 A**：

  | 方案 | 要 dispatcher 吗 | 代价 | 结论 |
  |---|---|---|---|
  | **A. 调用方自己回主线程**：后台线程干完活 `await UniTask.SwitchToMainThread()` 后再 `Publish` | ❌ | 无（UniTask 本就在用） | ✅ **v1 采用** |
  | **B. Core 提供显式异步入口** `UniTask PublishOnMainThreadAsync<T>(T evt)`（内部 `await SwitchToMainThread(); Publish(evt);`） | ❌ | 一个 API；**延迟写在返回类型上**，调用方看得见 | ⏸ 真有跨线程需求时**优先加这个** |
  | **C. `Publish` 内部排队自动 marshal** | ✅（复用日志的 dispatcher 需先中立化，见下条） | 同一 API 两种时序语义、关闭期队列语义、可观测量、共享设施所有权 | ❌ **不采纳** |

  - 不采纳 C 的关键理由：**改动可逆性不对称**——现在收紧为"仅主线程 + 显式异步入口"，将来要加自动 marshal 是**纯增量**；反过来则是破坏性变更。另外**日志可以自动 marshal 是因为它是 fire-and-forget**（延迟/合并/丢弃都无所谓），而事件是事实通知，订阅方可能依赖时序。
  - **结论：v1 不复用日志的主线程分发器**（只需自持线程 ID，见上一条）；只有采纳 C 时才需要下面的中立化。
- **将来若真要做"排队式跨线程发布"**：先把 `ZMainThreadDispatcher` + driver **中立化**（迁到 `Zipper.Core.Threading`、由组装层创建与持有、谁都不替它 `Dispose`、driver GameObject 只留一个），日志与总线共享同一实例——这是一次独立的小重构，**不要为了现在的 3 行判定就做**（见 §13 D13）。
  - 附带发现（属日志模块，记录备查）：`ZMainThreadDispatcher` 现为 `public sealed`，但实测**Core 之外零消费者**（13 处引用全在 `Zipper.Core/Logging/` 内）→ 按 `naming-convention.md` §4"`public` ≠ 品牌 API"，它本该是 `internal`；另外它的 `IsMainThread` 语义实为"**创建它的那个线程**"而非 Unity 主线程（当前两者恰好一致），中立化时需加"必须主线程创建"的断言或改名。

### 4.3 与日志模块的关系

- 总线只消费 `IZLogger`（异常隔离时记日志），**不向日志系统发事件** —— 防止"日志失败 → 发事件 → 又记日志"的环。
- 总线不承担日志的职责：诊断信息走日志，事实通知走总线。

### 4.4 关闭期语义

| 操作（总线 Dispose 之后） | 行为 |
|---|---|
| `Publish` | no-op（记一次 Debug 级痕迹，便于排查"为什么没反应"） |
| `Subscribe` | 返回一个"已释放的空凭据"（调用方后续 `Dispose` 它不会炸） |
| 旧凭据 `Dispose` | no-op（幂等） |
| `SubscriberCount<T>` | 0 |

> 选"宽容不抛"而不是 `ObjectDisposedException`：容器释放顺序决定了很多对象会在关闭路径里继续打日志/发通知，抛异常会把真正的关闭错误盖掉（日志模块在 FileSink `_disposed` 上已经踩过这个坑，见 `logging-design.md`）。

---

## 5. 生命周期

### 5.1 谁负责注销（两条路径 + 三类对象）

**原则**：有生命周期的对象走"收件人 + `UnsubscribeAll`"，其余走"凭据 `Dispose`"。

| 对象形态 | 订哪种 | 注销做法 | 额外成本 |
|---|---|---|---|
| **容器托管的单例服务**（资源/池/音频/事件） | 收件人 | 在自身 `Dispose()` 里 `UnsubscribeAll(this)` —— VContainer 本来就会调 `Dispose` | **零** |
| **场景对象**（MonoBehaviour 面板/View） | 收件人 | 在 `OnDestroy()` 里 `UnsubscribeAll(this)` | 一句 |
| **R3 桥 / 测试 / 临时逻辑** | 匿名 | 凭据 `Dispose` / `using`（R3 侧还可 `AddTo(this)`，见 §7.3） | 无 |

服务与场景对象的典型写法（**推荐形态**）：

```
// 模块服务
public sealed class ShopService : IDisposable
{
    readonly IZEventBus _bus;
    public ShopService(IZEventBus bus)
    {
        _bus = bus;
        _bus.Subscribe<ZResourceLoaded>(this, OnResourceLoaded);   // 实例方法组，一眼看出归属
    }
    void OnResourceLoaded(ZResourceLoaded e) { ... }
    public void Dispose() => _bus.UnsubscribeAll(this);            // 加多少订阅都覆盖
}

// MonoBehaviour 面板
void Awake()
{
    _bus.Subscribe<ZSceneChanged>(this, OnSceneChanged);
}
void OnDestroy() => _bus.UnsubscribeAll(this);
```

**只想取消其中一个订阅**（其余保留）：直接 `Dispose` 那个订阅返回的凭据 —— **两条订阅路径都一样**：

```
// 收件人路径：只取消这一个，该对象的其他订阅照旧
IDisposable hotkeySub = _bus.Subscribe<ZHotkeyPressed>(this, OnHotkey);
hotkeySub.Dispose();

// 匿名路径：同样靠凭据
IDisposable tempSub = _bus.Subscribe<ZResourceLoaded>(OnLoaded);
tempSub.Dispose();

// 混用安全：先批量再单个，都是幂等的
_bus.UnsubscribeAll(this);
hotkeySub.Dispose();

// 既要闭包、又想能批量注销 → 用收件人重载（subscriber 与 handler 形态无关）
_bus.Subscribe<ZResourceLoaded>(this, m => OnLoaded(m, _cache));
```

> 想在**退订点重写 handler**（连字段都不存）就用 `Unsubscribe<T>(handler)`——但只对方法组 / 静态方法 / 无捕获 lambda 可靠（§2.1 的取消方式表）。

**纪律（要写进编码纪律）**：凡是用了收件人订阅的类，**必须在 `Dispose`/`OnDestroy` 里 `UnsubscribeAll(this)`**——否则总线会通过 `Subscriber` 强引用钉住它（见 §5.2）。

**不做的自动化**（讨论结论，理由见附录 B）：`this` 的自动装配（基类 / 扩展方法 receiver / 反射发现）一律不做；`CancellationToken` 重载（配 `destroyCancellationToken` 自动注销）暂不做，留口，见 §13 D12。

### 5.2 泄漏的三种典型场景（文档要写进编码纪律）

1. **忘记 `UnsubscribeAll`（采用收件人路径后的头号坑）**：总线持有 `Subscriber` 强引用 → 该对象（含 MonoBehaviour）不会被 GC；若它还监听，销毁后仍会收到回调。**这是选定"强引用 + 显式注销"方案必须付的代价**（弱引用能掩盖这个问题，但代价是问题更难查，且 Unity 下本就不可靠，见附录 A）。
2. **匿名订阅忘记释放凭据**：等价的老问题；R3 桥那类由 R3 的 `IDisposable` 负责，一般不会漏。
3. **重复注销**：`UnsubscribeAll` 与凭据 `Dispose` 都必须幂等（前者无匹配项即空操作，后者标记 `IsDisposed` 后直接返回）。

### 5.3 总线自身的 Dispose

由容器释放（`ZEventBus : IDisposable`，VContainer 会 dispose 容器创建的单例）。`Dispose` 内容：遍历所有通道 → 清空订阅数组 → 标记总线 `_disposed`。**不**保证通知订阅方（见 §7.4 桥的 `OnCompleted` 待决）。

---

## 6. 装配

| 项 | 约定 |
|---|---|
| 容器注册 | `builder.Register<IZEventBus, ZEventBus>(Lifetime.Singleton)`（**唯一一条总线**，已在 `GameLifetimeScope`） |
| 依赖注入 | `ZEventBus(IZLogger logger)` —— 构造函数注入；日志器在容器构建期已可用，实际使用发生在发布期（那时日志必已 Attach） |
| Bootstrapper | **v1 不需要** `ZEventBusBootstrapper`：总线没有异步初始化内容。`ZBootPhase.Events` 因此**空转**，需在 `bootstrap-design.md` 注明"预留阶段，不注册 Bootstrapper 是预期行为" |
| R3 初始化 | `R3.Unity` 的 `UnityProviderInitializer` 会在启动时自动设好 Time/Frame Provider，但未处理异常默认走 `UnityEngine.Debug.LogException` → 要接进框架日志，需要一处启动调用 `SetDefaultObservableSystem(ex => logger.Error("R3 未处理异常", ex))`（**必须在日志阶段之后**：放 Events 阶段或组装层；届时引入 `ZR3Bootstrapper`） |
| 装配顺序依赖 | 总线无启动顺序要求；但**使用总线发事件的模块**必须晚于日志阶段（否则异常日志会静默丢失） |

---

## 7. R3 桥（订阅侧适配器）

### 7.1 定位与放置

- **本质**：一个扩展方法，把总线订阅变成 R3 可观察对象。**桥接唯一实现**，不是第二条总线（`core-design.md` §4.3）。
- **放在哪**：依赖 R3 的程序集 —— 独立桥接程序集（建议名 `Zipper.R3`）或 `Zipper.UI`；**绝不进 Core**（Core 只依赖 Unity + UniTask）。
- **谁需要引**：只想收事件的模块只注入 `IZEventBus`（不引 R3）；只有要用操作符的 UI 代码才引桥所在程序集。
- **"可见"≠"允许用"**：core DLL 被 Unity 自动引用给所有程序集，所以在 Core 里写 `R3.*` 也能编过（§1.2）——"绝不进 Core"要靠纪律与评审，或按 §13 D9 打开 `overrideReferences` 交给编译器强制。

### 7.2 形态（思路级伪代码）

```
// 桥程序集内
public static class ZEventBusObservableExtensions
{
    public static Observable<T> AsObservable<T>(this IZEventBus bus)
        => Observable.Create<T>(observer =>
               bus.Subscribe<T>(evt => observer.OnNext(evt)));   // 返回的 IDisposable = 总线凭据
}
```

- **`Observable.Create` 是 R3 core 的官方工厂**（本地 README §Factory 表已核对签名）：`Create(Func<Observer<T>, IDisposable> subscribe, bool rawObserver = false) → Observable<T>`。它要求的返回值正是"订阅的释放凭据"，与总线 `Subscribe` 的返回类型天然对齐。`rawObserver` 保持默认 `false`（走 R3 的观察者包装，`OnErrorResume` 语义由 R3 负责）。
- **备选**：`Observable.FromEvent<T>(Action<Action<T>> addHandler, Action<Action<T>> removeHandler, CancellationToken ct = default)` 也能桥（R3 官方文档化的另一种形态），但 `removeHandler` 只拿到委托、拿不到总线凭据，得额外维护"委托 → 凭据"的映射，比 `Create` 绕 —— **不推荐**。
- **不需要 `Synchronize`**：R3 明确要求"`OnNext` 必须在单一线程发出"，多线程源需要 `Synchronize` 包装；本总线已强制"只在主线程发布"（§4.2），所以桥无需额外同步算子。
- **退订联动**：R3 侧 `Subscribe` 返回的 `IDisposable` 就是总线凭据 → 释放它即同时退订（验收点，§9）。
- **与总线的收件人机制无关**：桥走的是**匿名订阅**那条路（它由 R3 的 `IDisposable` 管生命周期）；UI 若用 R3 自己的 `subscription.AddTo(this)` 自动退订，那是 **R3 的 API**，不是总线的 `this` 自动装配（§5.1/附录 B 讨论的是总线侧）。两者互不影响。
- **只发 `OnNext`**：总线没有异常出口（异常在总线内部被隔离并记日志）→ 桥不会产生 `OnErrorResume`；链上后续算子自己抛的异常按 **R3 语义**（`OnErrorResume`，**不终止链**）处理，最终由 `ObservableSystem` 的未处理处理器兜底。
- **未处理异常接日志**：`ObservableSystem.RegisterUnhandledExceptionHandler(ex => logger.Error("R3 未处理异常", ex))` —— **core R3 就有**，今天就能接（不依赖 `R3.Unity`）。
- **不隐式切线程**：桥不改调度；要 `ObserveOn` 由使用方显式指定（`UnityFrameProvider` 已具备：`Update`/`FixedUpdate`/`PostLateUpdate` 等）。
- **不引入粘性/当前值语义**：要"订阅即得当前值"用 `ReactiveProperty`（§4.3 已定）。

### 7.3 UI 侧可用能力（`R3.Unity` 1.3.1 已装，全部可用）

| 能力 | 说明 |
|---|---|
| 退订 | 显式保存凭据在 `OnDestroy`/`Dispose` 释放；或 `subscription.AddTo(this)` 随对象销毁自动退订 |
| 时间算子（`Debounce`/`ThrottleLast`/`Delay`） | 默认走 `ObservableSystem.DefaultTimeProvider = UnityTimeProvider.Update`（PlayerLoop + TimeScale）；要忽略 TimeScale 用 `*IgnoreTimeScale` 变体显式传入 |
| 帧算子（`DebounceFrame`/`ObserveOn(FrameProvider)`） | 默认 `UnityFrameProvider.Update`；也可显式选 `FixedUpdate`/`PostLateUpdate` 等 |
| UnityEvent 互操作 | `unityEvent.AsObservable(ct)` |
| 订阅泄漏排查 | Editor 窗口 `ObservableTracker`（见 §10） |

> `AddTo(this)` 的代价：当目标 GameObject **尚未激活**时，它会挂一个 `ObservableDestroyTrigger` 组件（R3 的刻意设计：未激活过的组件不触发 `OnDestroy`，`destroyCancellationToken` 也不会被取消）。UI 对象数量多时值得知道这一点。

### 7.4 待决：桥的 `OnCompleted`

总线 `Dispose`（应用关闭）时，桥接出的 Observable **没有**推送 `OnCompleted` 的机制（总线没有"我关了"的对外通知）。两条路：

- **(a) v1 不做**（推荐）：桥只保证 `OnNext` 与退订联动；UI 自己在 `OnDestroy` 释放订阅。
- **(b)** 给总线加关机通知（例如 `IZEventBus.Disposing` 事件或一条框架级 `ZAppQuitting` 事件），桥据此 `OnCompleted`，让 `Subscribe` 侧注册的清理逻辑跑起来。

（§4.3 曾提到"Dispose 时完成所有 Observable"，但没给机制；本文把它显式列为待决，而不是默认实现。）

---

## 8. 事件类型的组织（§4.3 §6 待办）

**两级规则**：

| 事件类别 | 放哪 | 例 |
|---|---|---|
| **跨模块通用事件**（谁都在用，载荷是纯数据） | `Zipper.Core/Events/`（Core 是所有模块的公共依赖） | `ZResourceLoaded`、`ZSceneChanged`、`ZAppQuitting` |
| **模块领域事件**（只有一个模块在用） | 用它的模块程序集内（`Zipper.UI/Events/`） | `ZUIPanelOpened` |

**理由**：把事件放 Core 能让"订阅方不必引用发布方程序集"（Core 人人都有）；但只放**纯数据**载荷，否则 Core 被迫引用各模块类型（例如事件里塞 `AssetHandle` 就把 Resources 拖进 Core）。领域事件留在模块里，避免 Core 变成"所有模块事件的大杂院"。

**目录/命名**：`Events/` 目录、文件名 = 类型名、过去式；事件类型**集中登记**（不要散落在业务目录里），这是 §4.3 里"隐式控制流"风险的唯一缓解手段。

---

## 9. 验收标准（EditMode 测试清单）

| # | 用例 | 期望 |
|---|---|---|
| 1 | 订阅 → 发布 | 收到一次；参数值正确 |
| 2 | 退订 → 发布 | 不再收到 |
| 3 | **回调内退订自己** → 同一轮后续 | 不再被调用（退订立即生效） |
| 4 | **回调内退订尚未被调用的订阅者** | 该订阅者本轮不收到 |
| 5 | **回调内订阅同类型** | 本轮不收到，下轮收到 |
| 6 | 回调内订阅其他类型并发布 | 立即收到 |
| 7 | 多订阅者，其中一个抛异常 | 其余仍收到；日志有 Error 记录 |
| 8 | 无订阅者时发布 | 不抛、**零堆分配**（Profiler/`GC.Alloc` 断言） |
| 9 | 连续发布 N 次（有订阅者） | 无每帧堆分配 |
| 10 | 回调内重入发布（同类型） | 不抛、顺序符合 §3.3 |
| 11 | 非主线程发布 | 记 Error、该次事件被丢弃、无异常 |
| 12 | `Dispose` 总线后发布/订阅 | no-op / 返回已释放凭据，均不抛 |
| 13 | 凭据重复 `Dispose` | 幂等，不抛 |
| 14 | `GetState()` 计数 | 与订阅数一致、退订后递减（`TotalSubscribers` 与按类型的 `SubscriberCount` 都要对） |
| 15 | **R3 桥**：`bus.AsObservable<T>()` 退订 | 同时释放总线凭据（订阅数归零） |
| 16 | **R3 桥**：桥后加算子（如 `Debounce`） | 不影响其他订阅者收到的原始事件序列 |
| 17 | `UnsubscribeAll(a)` | 只清 `a` 的全部订阅；**不影响他人**（含 handler 同名但收件人不同的订阅） |
| 18 | `UnsubscribeAll(a)` 重复调用 | 幂等，不抛 |
| 19 | `UnsubscribeAll(null)` | **记 Warning 并忽略**（不误清匿名订阅、不抛） |
| 20 | 匿名订阅 + `UnsubscribeAll(任意对象)` | 匿名订阅**不受影响**（仍是各自的凭据管） |
| 21 | 收件人订阅在回调里 `UnsubscribeAll(自己)` | 本轮后续不再收到（与 §3.3 一致） |
| 22 | `GetState()` 混合计数 | 两种路径混用时计数正确；`UnsubscribeAll` 后相应递减 |
| 23 | 单个凭据 `Dispose`（两条订阅路径各测一次） | **只取消自己**；同一收件人的其他订阅、以及他人的订阅都不受影响 |
| 24 | `Unsubscribe<T>(handler)`（方法组） | 移除成功、返回 `true`；**同一 handler 注册两次时全部移除**（不留僵尸） |
| 25 | `Unsubscribe<T>(捕获型 lambda)`（订阅与退订处**各重写一遍**） | 返回 `false` 且**记一条 Warning**（不静默） |
| 26 | **`Unsubscribe<T>` 未命中**（通道存在但 handler 不匹配） | 返回 `false` + Warning；**绝不能谎报 `true`**（`Remove` 要返回移除个数） |
| 27 | 值类型作为 `subscriber` | 被拒绝（调试期检查或直接抛），**不会**出现"注册成功但 `UnsubscribeAll` 永远匹配不上"的静默泄漏 |

---

## 10. 状态查询与可观测（`GetState()` 已提供；调试视图延后）

**v1 已采用：`ZEventsState GetState()`**（取代原计划的 `int SubscriberCount<T>()`），形状**照对象池的 `ZPoolState` / `ZPoolsState`**：

| 类型 | 成员 | 说明 |
|---|---|---|
| `ZEventsState`（`public`） | `int TotalEventTypes` / `int TotalSubscribers` / `List<ZEventState> EventStates` | 一次性快照；属性用 `{ get; private set; }`（与 `ZPoolsState` 一致） |
| `ZEventState`（`public`） | `Type Type` / `int SubscriberCount` | `Type` 用 `System.Type`（与 `ZPoolState.Type` 一致），**不要用 `string`** |

> ⚠️ 实现要点（照 `ZPoolsState` 的写法）：`TotalSubscribers` 必须在遍历里 **`+=` 累加**（`ZPoolsState` 是 `AllItems += poolState.CountAll;`）。漏掉累加 → 该字段恒为 0，而"按类型的 `SubscriberCount` 却是对的"——这种半个字段是错的，最难被发现。

**延后的部分**（原样保留）：

- 调试模式下记录**最近 N 条事件**（环形缓冲，类型 + 时间 + 订阅者数）；
- 建议与"对象池监视""资源簿记"合并成一个调试面板（同一批需求、同一套 UI），不要单独造。
- 事件历史建议用一个可选的 `IEventBusObserver`（默认不注册 = 零成本）。
- **已有一个现成工具**：`R3.Unity` 自带 Editor 窗口 `ObservableTracker`（可列出未释放的 R3 订阅）。排查"桥接订阅泄漏"先用它，不必自研。
- 排"总线订阅泄漏"（非 R3 侧）才需要自研视图 —— 这也是建议与池/资源监控面板合并的原因。

---

## 11. 分阶段实施建议

| 阶段 | 内容 | 可否独立验收 |
|---|---|---|
| **P1（最小可用）** | 契约 + 通道 + copy-on-write + 快照/退订语义 + 异常隔离 + 主线程校验 + Dispose 语义 + 验收 1–14 | ✅ 是（本文档 §9 就是它的验收单） |
| **P2（可选）** | 把 R3 未处理异常接进 `IZLogger`（`UnityProviderInitializer.SetDefaultObservableSystem(ex => logger.Error(...))`，需一处启动调用 + `ZR3Bootstrapper`） | ✅ 是 |
| **P3（UI 前）** | R3 桥 `AsObservable` + 验收 15–16 | ✅ 是（R3 环境已就绪，无需额外安装） |
| **P4（有真实需求再做）** | 调试视图、跨线程排队式发布（需要迁 dispatcher） | — |

---

## 12. 明确不做（v1）

继承 `core-design.md` §4.3 并补充：

| 不做 | 理由 |
|---|---|
| 事件继承匹配（只按精确类型） | 避免"基类事件被所有子类订阅"的隐式行为（§4.3 已定） |
| 优先级 / 粘性（回放）事件 / 谓词过滤订阅 | 用 `ReactiveProperty` 或订阅侧自过滤解决；总线只做路由 |
| 异步分发 / 延迟到帧末 / 事件队列 | 需要单独的时序设计；v1 保持"同步、立即、主线程"单一语义 |
| **弱引用订阅** | 会掩盖泄漏（订阅方不释放也"看起来正常"）；项目用显式 `Dispose` 文化 |
| 总线级 Scope 容器 / 自动收集凭据 | 见 §2.3 差异 4 |
| 跨进程 / 网络 / 持久化 | 不在框架范围 |
| 通道结构升级（链表、无锁高频） | copy-on-write 对"跨模块低频通知"足够；过早优化 |
| 按收件人分桶的注销索引 | `UnsubscribeAll` 是低频操作，扫描够用；分桶会引入第二份要同步维护的状态 |
| 总线自带重试 / 事务 / 回滚 | 事件是"已发生的事实"，不做补偿 |
| **CommunityToolkit.Mvvm 式 class 信封**（`MessageBase`/`ValueChangedMessage<T>`） | 与"零分配发布 + `readonly struct` 载荷"冲突；元数据需求由调试视图承担。详见附录 A |
| **弱引用订阅**（补考据） | 除"掩盖泄漏"外，Unity 下本就不可靠：弱引用跟踪**托管对象**，`Destroy` 后托管包装器常仍存活 → 清理迟到或永不触发。Unity 里管生命周期用 `destroyCancellationToken` 或显式注销 |
| **请求-应答消息**（`RequestMessage<T>`） | §4.3 已定：要结果走接口方法 + UniTask |
| **MVVM 值变化语义**（`ValueChangedMessage`/`PropertyChangedMessage`） | 值变化/当前值归 R3 的 `ReactiveProperty`（R3.Unity 还有 `SerializableReactiveProperty<T>`）；两套并行违反"同一件事只发布一条链路" |
| **`IRecipient<T>` 式声明注册**（`RegisterAll(this)` 自动发现） | 需枚举接口 + `MakeGenericMethod` 反射 → 与"零反射 / IL2CPP 安全"冲突（除非上代码生成，框架外） |
| 静态 `.Default` 单例总线 | 走容器注入（`IZEventBus` 单例），不做静态入口 |
| **`this` 的自动装配**（基类 / 扩展方法 / 反射） | 省的只是 `this,` 五个字符，真正痛点是"忘记注销"，已由 §5.1 解决；详见附录 B |
| 通道 token（同一事件类型多通道） | 延后：你已拒绝"带 key 注册两条总线"，token 是同一问题的另一解法；等出现真实需求再加（键变 `(Type, token)`） |
| 让 `Unsubscribe` 识别"两处分别重写的捕获型 lambda" | 语言层面做不到：委托相等按 Target + Method 比较，两处重写就是两个闭包对象。已用"未命中记 Warning + 文档标注"兜住，别指望框架能自动修复调用方写法 |

---

## 13. 本次讨论已定 / 仍待决策

### 13.1 已定（v0.3，本轮讨论结论）

| 事项 | 结论 |
|---|---|
| 收件人维度 | **采纳（简化形态）**：`Subscribe<T>(object subscriber, Action<T>)` + `UnsubscribeAll(object)`；`subscriber` 只作记账，**不改 handler 签名** |
| 处理器是否回传 subscriber（`Action<object,T>`） | **不采纳**：它服务的是"静态 lambda 零闭包"，收益是订阅期一个委托分配，代价是每个调用点都要转型 → 不值（附录 A 第 2 条） |
| CommunityToolkit.Mvvm 的 class 信封 / 弱引用 / 请求-应答 / MVVM 值变化 / 声明式注册 / 静态单例 | **一律不采纳**（逐条理由见附录 A） |
| `this` 的自动装配（基类 / 扩展方法 / 反射发现） | **不做**；留"扩展方法 + 空标记接口"作为将来的 10 行可选项（附录 B） |
| 通道 token | **延后**，等真实需求 |
| `Unsubscribe<T>(Action<T>)`（按委托取消） | **采纳（选项 A）**：移除**全部**相等项、返回是否有移除、**未命中记 Warning**（把"捕获型 lambda 匹配不上"从静默失效变成当场可诊断）；文档写明它只对方法组 / 静态方法 / 无捕获 lambda 可靠 |
| 凭据集合类型（`ZSubscriptionBag`） | **不再需要**——收件人路径下注销是一句话，匿名路径自己管 |
| 主线程判定 | **总线自持线程 ID**；不迁 dispatcher（§4.2） |

**v0.5 新增（使用者拍板）**

| 事项 | 结论 |
|---|---|
| 事件的类型约束 | **必须引用类型**（`where T : class`）：原稿"不加约束、载荷优先 `readonly struct`"作废；载荷建议 `sealed` + 只读成员；**struct 事件不支持**；代价是每次发布需一个事件对象（总线自身仍零分配） |
| 状态查询 | 采纳 **`ZEventsState GetState()`**，照对象池 DTO 风格（`Type` 而非 `string`、`{ get; private set; }`、`TotalSubscribers` 用 `+=` 累加）；弃用 `SubscriberCount<T>()` |
| 命名统一 | `recipient` → **`subscriber`**；`UnregisterAll` → **`UnsubscribeAll`**（代码与文档统一，消除"一物两名"） |
| 收件人为 null | `UnsubscribeAll(null)` → **记 Warning 并忽略**（不抛）：既防"误清匿名订阅"，又不给关闭期路径添异常 |
| `Subscription` 的反向边 | **持有所属通道**（`EventChannel<T>`）；Dispose 只标记，**摘除由通道完成** → 通道必须提供 `Remove(Subscription<T>)`（按身份）重载，否则 `Remove(this)` 会被绑到 `Remove(object)` 而静默失效（§3.2） |
| `Publish` 无人订阅 | **静默直接返回**（不记任何日志）：没人订阅是最常见情况，不是异常；在那里刷 Warning 会破坏"零分配发布"验收并刷屏 |

### 13.2 待决策项（需要使用者拍板）

| # | 决策 | 我的建议 | 影响 |
|---|---|---|---|
| ~~**D1**~~ | ~~是否补装 `R3.Unity`~~ | **已确认：两步安装都已完成**（core 1.3.1 NuGet + `com.cysharp.r3` 1.3.1 UPM，§1.2） | 无需动作 |
| **D2** | `Publish` 是否需要一个"是否有订阅者"的快速判断（如 `Publish` 返回 `int` 收到数，或保留 `void`） | **v1 保持 `void`** | 影响"无订阅者时是否值得构造事件对象"的优化写法；有真实需求再加 |
| **D3** | 跨线程发布语义 | **已定（v0.3）：v1 不排队、不 marshal、不复用 dispatcher**——只允许主线程 `Publish`，跨线程需求走"调用方 `SwitchToMainThread`"；将来若有真实需求，优先加显式异步入口 `PublishOnMainThreadAsync`（§4.2 表） | 残余动作：`core-design.md` §4.3 原表述"后台线程发布经主线程泵"需同步修订（或标注为二期） |
| ~~**D4**~~ | ~~是否提供凭据集合类型~~ | **已消解**：采纳收件人路径后无必要（§13.1） | — |
| **D5** | 桥的 `OnCompleted`（总线关闭时通知桥） | **v1 不做**（§7.4 (a)） | 决定是否需要"总线关机通知"这条框架级约定 |
| **D6** | `ZBootPhase.Events` 保留空转还是删除 | **保留并注明预留** | 纯文档约定；删掉则将来加 R3 Bootstrapper 要改枚举 |
| **D7** | 事件类型两级归属规则（§8） | 采纳 | 决定 `ZResourceLoaded` 这类事件放 Core 还是模块程序集 |
| **D8** | 调试视图（§10）何时做 | 与池/资源监控面板合并，**不做单独的** | 避免三套调试 UI |
| **D9** | 是否给 `Zipper.Core.asmdef` 打开 `overrideReferences`，把"Core 不引 R3 / VContainer / Addressables"从纪律变成**编译期强制** | 建议做（一次 asmdef 配置 + 重新导入） | §1.2：core DLL 目前被自动引用，Core 里误写 `R3.*` 也能编过；打开后需确认 UniTask 等 UPM asmdef 引用不受影响 |
| **D10** | 是否加**调试期**检查：`subscriber` 为**值类型**时拒绝或记 Warning | 建议加（仅编辑器/调试期，成本一次 `IsValueType` 判断） | 值类型会被装箱 → 注册与 `UnsubscribeAll` 各装箱一次 → 永远匹配不上（静默泄漏）；§2.2 已写死"禁止值类型 subscriber"，这条是把它变成当场可见 |
| **D11** | `Subscribe` 是否加 `CancellationToken` 重载（配 `destroyCancellationToken` 实现"销毁即自动注销"） | **暂不做**，留口 | 若做：给线程模型加一个"回调可能在非主线程触发"的例外入口，且每次订阅多一个 `CancellationTokenRegistration`（订阅期分配，发布期无影响） |
| **D12** | 是否将来给 `Subscribe` 加"扩展方法 + 空标记接口"的糖（`Subscribe(_bus, OnX)`，receiver 隐式） | **暂不做**；若实测嫌烦再补 | 10 行、不动契约；会引入一个空接口 `IZEventRecipient` |
| **D13** | 主线程设施中立化（`ZMainThreadDispatcher` 迁 `Zipper.Core.Threading` + 降 `internal` + 组装层持有） | **独立重构项**，等做排队式跨线程发布时一起 | 顺带解掉"该类型本应为 internal"和"`IsMainThread` 语义是创建线程"两个问题 |

---

## 14. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.5.1 | 2026-09-13 | **修复文档自身的两处错误 + 一次审查跟进**：① **更正 v0.5 的 `Array.Resize` 说法**——`Array.Resize` 本质就是"新数组 + 复制 + 替换字段"，语义上就是 copy-on-write，我 v0.5 写成"原地改写、要避免"是错的；真正要守的只有"不要在正在遍历的数组上原地改元素"。② **更正 `Subscribe` 的 null 校验**：v0.5 写"公开的两参重载 subscriber 为 null → 抛"与实现/设计都冲突——`subscriber = null` 就是**匿名订阅**（单参重载正是这么调的），合法；需要守卫的只有 `UnsubscribeAll(null)`。同时补"`action == null` 必须早失败"的理由（否则 null handler 进通道，发布时每条都记 Error）与非主线程 `Subscribe` **必须返回共享空凭据而非 null**（返回 null 会让调用方 Dispose 时 NRE）。③ 新增约定：**`EventChannel<T>.Remove(...)` 要返回"移除了几个"**，否则上层 `Unsubscribe` 只能无条件 `return true` → 未命中却**谎报成功**（§9 第 26 条据此改写）。④ 术语扫尾：§3.3/§4.2/§5/§9/§12/§13.1 里残留的 `UnregisterAll`→`UnsubscribeAll`、`recipient`/`Recipient`→`subscriber`/`Subscriber` 全部统一（变更记录与附录 A 中的历史/第三方 API 名称保留原样） |
| v0.5 | 2026-09-13 | **契约对齐已实现形态 + 三项使用者决策**：① **`where T : class` 定案**——事件必须是引用类型，原 §2.2"不加约束、载荷优先 `readonly struct`"**作废**，并写明代价（每次发布需一个事件对象；总线自身仍零分配）与"struct 事件不支持"；② **状态查询改为 `ZEventsState GetState()`**（照对象池 `ZPoolState/ZPoolsState` 的 DTO 形状：`Type` 而非 `string`、`{ get; private set; }`、`TotalSubscribers` 必须 `+=` 累加），弃用 `SubscriberCount<T>()`；③ **命名统一**：`recipient` → `subscriber`、`UnregisterAll` → `UnsubscribeAll`（消除一物两名）；④ 收件人为 null 改为"**记 Warning 并忽略**"（不抛），既防误清匿名订阅又不给关闭期添异常；⑤ §3.2 补"**反向边指向通道、摘除由通道负责**"，并显式警告"若通道只有 `Remove(object)`，`_channel.Remove(this)` 会静默匹配不上"；⑥ §2.1 伪代码补 `Publish` 无通道时**不记日志**（无人订阅不是异常，刷 Warning 会破坏零分配验收）与主线程/关闭期/异常隔离的落点。修订原因：代码审查（两轴）发现实现与 v0.4 有契约级偏离，逐条裁决后以本版为准 |
| v0.4 | 2026-09-13 | **采纳"按委托取消"`bool Unsubscribe<T>(Action<T>)`（选项 A，加进 `IZEventBus` 契约）**：移除**全部**相等项、返回是否有移除、**未命中记 Warning**（把"捕获型 lambda 匹配不上"从静默失效变成当场可诊断）；文档写明其可靠前提是"同一个委托实例"（方法组/静态/无捕获 lambda 天然满足，两处重写的捕获型 lambda 不可靠），并跟 `UnregisterAll`/凭据做"三种取消方式"对照表。**修正 v0.3 的两处错误**：§2.2 原写"`recipient` 必须与 `handler.Target` 引用相同"是过严且会造成误报——收件人只是记账键，改为"非 null 引用类型 + 生命周期归属语义 + **禁止值类型**（装箱导致 `UnregisterAll` 永远匹配不上）"；D10 的检查项相应由"Target 一致性"改为"值类型 recipient"。§3.2/§4.2/§5.1/§9/§12/§13.1 同步：新增 `Unsubscribe` 的机制与线程约束、补"单个取消"示例、验收增至 27 条、新增"语言层面做不到"的不做项 |
| v0.3 | 2026-09-13 | **采纳"收件人维度"（简化形态）**：契约新增 `Subscribe<T>(object recipient, Action<T>)` 与 `UnregisterAll(object)`（`recipient` 只作记账、**不回传进 handler 签名**、`null` 不受理、引用相等比较）；§3.2 `Subscription` 增 `Recipient` 字段与扫描式注销；§3.3 补"回调内 `UnregisterAll`"语义；§5 重写为"两条路径 + 三类对象"的注销清单与纪律（服务靠 `Dispose`、场景对象靠 `OnDestroy`、临时走凭据），§5.2 泄漏场景更新；§4.2 明确"**总线自持线程 ID、不复用日志 dispatcher**"（三条具体耦合理由）并把"主线程设施中立化"记为独立重构项（附两条日志模块备查发现）；§9 增 6 条验收；§12 增 9 条"明确不做"（含 Unity 语境下弱引用不可靠的考据）；§13 拆为"已定 / 待决"，D4 消解、新增 D10–D13；**§4.2 补"跨线程发布三条路"对照表并定案 v1 取"调用方自己 `SwitchToMainThread`"（不排队、不 marshal、不复用日志 dispatcher）**；**新增附录 A**（与 CommunityToolkit.Mvvm messenger 的逐条对照取舍）与**附录 B**（为什么不做 `this` 自动装配）|
| v0.2 | 2026-09-13 | **更正 §1.2 的 R3 环境结论（事实性修订）**：v0.1 称"只装了 core、缺 `R3.Unity`"是**错误**的——当时只核对了 NuGet 的 core DLL（`Assets/Packages/R3.1.3.1`），漏看了 UPM 包。实际 `Packages/manifest.json` 里 `com.cysharp.r3` 已指向 `https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity`，包在 `Library/PackageCache/com.cysharp.r3@fdfb36e3d5`，版本 1.3.1 与 core 一致。§1.2 重写为"两步安装均已完成"并列出 Unity 集成提供的全部能力（`UnityProviderInitializer` 自动初始化、PlayerLoop Time/Frame Provider、`AddTo(this)`、`UnityEvent.AsObservable`、Trigger 族、Editor 的 `ObservableTracker`）；§7.3 由"缺什么"改为"可用能力"表；D1 关闭；P2/P3 相应调整。新增 D9 与 §1.2 的边界说明：core DLL 因 asmdef 未开 `overrideReferences` 而被自动引用，**"Core 不引 R3"目前仅靠纪律**，可用 `overrideReferences` 交给编译器强制 |
| v0.1 | 2026-09-13 | 初稿：把 `core-design.md` §4.3 的决策展开为可实现契约（接口形态、凭据、copy-on-write 通道、快照与退订的精确语义、异常隔离、线程模型、关闭期语义、装配、R3 桥、事件类型组织、验收清单、分阶段实施）；列出与 §4.3 的五处差异（线程模型建议改为主线程限定、退订立即生效、回调内订阅可见性、Scope 兜底改为持有方负责、事件类型归属规则）待同步确认 |

---

## 附录 A：与 CommunityToolkit.Mvvm messenger 的对照与取舍记录

> 背景：使用者提出"做成 CommunityToolkit.Mvvm 那种、把事件包成信封（`ValueChangedMessage<T>`）"的方案。本附录记录逐条评估结论，避免以后重复讨论。
> 参考：`IMessenger.Register<TRecipient,TMessage,TToken>` / `Send<TMessage,TToken>` / `UnregisterAll(object)`、`WeakReferenceMessenger` / `StrongReferenceMessenger`（[Messenger 文档](https://learn.microsoft.com/zh-cn/dotnet/communitytoolkit/mvvm/messenger)、[WeakReferenceMessenger 源码](https://github.com/CommunityToolkit/dotnet/blob/main/src/CommunityToolkit.Mvvm/Messaging/WeakReferenceMessenger.cs)）。

它的能力其实是**五件正交的事**捆在一起——它自己把"收件人维度"和"弱引用"分开提供两种实现（`StrongReferenceMessenger` / `WeakReferenceMessenger`），说明这些特性本就可以拆开取用：

| # | 特性 | 它怎么做 | 我们的决定 | 理由 |
|---|---|---|---|---|
| 1 | **收件人维度** | 注册时传入 recipient，注册表按收件人记账，可 `UnregisterAll(recipient)` | ✅ **采纳（简化形态）** | 把"订阅泄漏"从"靠记性保存凭据"变成"一句话注销"，并让"这个订阅属于谁"在调用点肉眼可见（§5.1） |
| 2 | **handler 回传 recipient** | `MessageHandler<TRecipient,TMessage>`，写法 `static (r, m) => ((T)r).OnX(m)` | ❌ 不采纳 | 它服务的是"静态 lambda 零闭包"。算账：省的是**订阅期一个委托分配**（发布期完全无关），代价是**每个调用点**都要写转型。而简化形态用 `Subscribe<T>(this, OnX)`（实例方法组）同样"无 display class、归属可见"，还更短更直白 |
| 3 | **通道 token** | `(消息类型, token, 收件人)` 三者定位，同一消息类型可分多条通道 | ⏸ 延后 | 已拒绝"带 key 注册两条总线"，token 是同一问题的另一解法；等出现真实需求再加（键变 `(Type, token)`，值类型 token 在键里有一次装箱，仅订阅/发布路径） |
| 4 | **class 信封** | `MessageBase` → `ValueChangedMessage<T>` / `RequestMessage<T>` / `PropertyChangedMessage<T>`，值挂在 `Value` 属性上 | ❌ 不采纳 | ① 信封的价值来自**公共基类 + 元数据 + 多态** ⇒ 必须是 class（struct 不能有公共基类；改用接口装 struct 会装箱）；② 每次 `Send` 构造一个堆对象，与"发布零分配 + `readonly struct` 载荷"直接对撞；③ 若只做 `readonly struct` 信封，等于给载荷换个名字，收益≈0；④ 元数据需求已有出口：调试视图（§10）记录"类型/时间/订阅者数"本就是**总线侧**的事，不必让每个事件背信封。若将来确有个别事件需要元数据，再作为**可选**路径引入，绝不强制 |
| 5 | **弱引用默认** | 持有收件人的弱引用，收件人被 GC 后订阅自动消失，无需手动注销 | ❌ 不采纳 | ① 与已定"弱引用会掩盖泄漏"冲突；② **Unity 语境下本就不可靠**：弱引用跟踪的是**托管对象**，`Destroy` 掉 GameObject 后其托管包装器往往仍然存活（"伪 null"只是 `==` 重载的结果）→ 清理迟到甚至永不触发，"已销毁对象仍收回调"照样发生。Unity 里管生命周期应使用 `destroyCancellationToken` 或显式注销；③ 它同时证明特性 1 与 5 可以分开（强引用 + 收件人维度即可） |
| 6 | **请求-应答** | `RequestMessage<T>` / `AsyncRequestMessage<T>`，`Send` 返回消息对象、发送方读 `Response` | ❌ 不采纳 | `core-design.md` §4.3 已定"要对方做事并拿结果 → 接口方法 + UniTask 返回值"；破掉这条，总线会长成万能调用层 |
| 7 | **MVVM 值变化语义** | `ValueChangedMessage<T>` / `PropertyChangedMessage<T>` / `CollectionChangedMessage<T>` | ❌ 不采纳 | "值变化 / 当前值"在本框架已有归属：R3 的 `ReactiveProperty`（`R3.Unity` 还提供可序列化的 `SerializableReactiveProperty<T>`）。两套并行会直接违反 §4.3 的纪律"同一件事不允许既发总线事件、又发 R3 流" |
| 8 | **声明式注册** | `IRecipient<T>` + `ObservableRecipient` + `RegisterAll(this)` 自动发现 | ❌ 不采纳 | 需要枚举接口并 `MakeGenericMethod` 逐个注册 → 与"零反射 / IL2CPP 安全"冲突（除非上代码生成，属框架外）；另外它把"谁订了什么"藏进了基类，可读性反而下降 |
| 9 | **静态单例** | `WeakReferenceMessenger.Default` / `StrongReferenceMessenger.Default` | ❌ 不采纳 | 走容器注入 `IZEventBus` 单例，不做静态入口 |
| 10 | **发送过程中允许注销** | 文档明确 `Send` 期间可以 `Unregister` | ✅ 与本文一致 | 我们用"数组即快照 + 逐元素 `IsDisposed`"达成同样语义（§3.3），无需改动 |

**一句话**：只借它的**"收件人"这一维度**（而且只借记账那部分），其余一概不借——它的内核是"MVVM 的弱引用消息泵 + class 信封"，与本项目的四个前提（零反射、零分配、主线程、只发事实通知）都不同。

---

## 附录 B：为什么不做 `this` 的自动装配

C# **没有**"把调用者实例自动传给方法"的机制：`[CallerMemberName]` 只给方法名、不给实例；`[CallerArgumentExpression]` 是 C# 10 特性（本项目 C# 9）且给的是表达式文本。所以"自动装配 `this`"只有三条路：

| 路线 | 调用点长什么样 | 代价 | 决定 |
|---|---|---|---|
| A. 承载基类（如 `ZEventRecipientBehaviour`） | `Subscribe<X>(OnX);` | **占掉 Unity 唯一的基类位**（面板若已继承 UI 基类即冲突）；且要为 MonoBehaviour / 非 MonoBehaviour 各做一个 | ❌ 不做 |
| B. 扩展方法 + 空标记接口（如 `IZEventRecipient`） | `Subscribe(_bus, OnX);`（receiver 隐式即 `this`） | 一个空接口 + 一条扩展方法；**不进契约** | ❌ 暂不做，**留口**（若实测嫌烦，10 行可补，见 D12） |
| C. `IRecipient<T>` 反射式自动发现 | `_bus.RegisterAll(this);` | 反射 + `MakeGenericMethod` → AOT 风险，与零反射冲突 | ❌ 不做 |

判断依据：

1. 自动装配省下的只是 **`this,` 五个字符**；
2. 真正的痛点是**忘记注销**，而它已由 §5.1 的"三类对象清单 + 纪律"解决（服务靠 `Dispose`、场景对象靠 `OnDestroy`、临时走凭据）——**不需要语言技巧**；
3. 框架每加一层糖，就多一样"以后要维护、要解释"的东西。你在 CommunityToolkit.Mvvm 上已经体验过"高级功能大多用不上"，同一个道理。

若将来确实需要：用路线 B 补（10 行、不动契约），或走 D11 的 `CancellationToken` 重载（`destroyCancellationToken` → 销毁即自动注销，不需要继承）。
