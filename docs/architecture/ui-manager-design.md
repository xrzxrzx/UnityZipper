# Zipper UI 管理器设计（Zipper.UI — uGUI + MVVM）

> 状态：**v0.2 草稿，待审阅**（D1–D7 已按使用者拍板落定，见 §13）
> 定位：UI 管理器 MVP 的**契约、机制与边界**——面板栈与生命周期、View 池化与解绑/重绑协议、最小 MVVM 绑定层、加载与装配。**只给设计思路与思路级伪代码，不含实现代码**。
> 实施归属：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/planning/technical-roadmap.md` §5.3（UI 技术路线）/§5.5（组装层）/§5.4.1（对象池）/§4.2（依赖链）、`docs/architecture/pool-manager-design.md`、`docs/architecture/resource-manager-design.md`、`docs/architecture/event-bus-design.md`、`docs/architecture/bootstrap-design.md`、`docs/standards/naming-convention.md`
> 变更记录：v0.1 初稿（S1 已获使用者批准：A 方案 = UI 管理器设计稿）

---

## 0. 这份文档怎么读

| 读者 | 建议路径 |
|---|---|
| 要立刻实现 MVP 的人 | §1 范围 → §3 分层 → §4 面板栈 → §5 池化与解绑协议 → §7 装配 |
| 要把关的人 | §2 前置事实 → §5.3 解绑协议（最容易泄漏处）→ §11 验收 → §13 待决 |
| 关心与既有模块边界的人 | §8 与其它模块的边界、§9 程序集 |

---

## 1. 目标与范围

### 1.1 要解决什么

1. **面板怎么开、怎么关、怎么返回**：栈语义、层级、遮罩与排序、异步打开与取消；
2. **View 怎么复用**：关闭不销毁 → 归还对象池 → 再开重绑；**复用不得残留上一次的绑定**（本设计最大的坑）；
3. **MVVM 最小可用**：View 无业务逻辑 / ViewModel 纯 C# 可单测 / 绑定层由 R3 驱动；
4. **UI 与既有模块怎么接**：面板预制体只经资源管理器、View 复用走对象池、跨模块通知走总线、VM 由容器构造。

### 1.2 管什么 / 不管什么

**管**：面板栈与生命周期契约、View 池化与绑定协议、最小绑定层约定、面板注册表、打开/关闭的异步与失败语义、UI 与其它模块的边界、验收标准。

**不管**（见 §12）：交互动画/转场、UI 性能优化（合批/图集）、本地化、UI Toolkit 路线、子 Scope 层级、声明式（特性/反射）自动绑定、通用 MVVM 框架。

---

## 2. 前置事实（已核对，2026-09-13）

### 2.1 既有模块的契约（本设计只能依赖这些）

| 来源 | 契约要点 |
|---|---|
| `IZResourceManager`（Resources） | `UniTask<PrefabAsset> LoadPrefabAsync(string address, string owner = "", CancellationToken ct = default)`；`PrefabAsset.Instantiate(parent, worldPositionStays)`；`PrefabAsset.Dispose()` → 释放母本；句柄释放后 `Asset` 返回 default（不得再用） |
| `IZObjectPoolManager`（Pool） | `CreatePool<T>(ZPoolOptions<T>)`（**参数非法直接拒绝、不入册**）、`Get<T>()`、`Return<T>(T)`、`GetItemsByCount`、`ClearPool<T>()`、`DestroyPool<T>()`；`T : Component, IZObjectPoolItem`；**key = `typeof(T)`**（一类型一池）；`ClearPool/DestroyPool` 幂等静默，`Get*` 池缺失记 Error 返回 default |
| `IZEventBus`（Core） | `Subscribe<T>(object subscriber, Action<T>)` / `UnsubscribeAll(object)` / `Publish<T>(in T)`；**仅主线程**；事件必须是引用类型（`where T : class`） |
| `IZLogger`（Core） | 构造注入；`Error/Warning/Info/Debug/Fatal`（caller 参数自动填充） |

### 2.2 R3 能力（已装齐 1.3.1：core + `R3.Unity` UPM）

| 能力 | 用途（本设计） |
|---|---|
| `ReactiveProperty<T>` / `Subject<T>` | ViewModel 的状态与通知 |
| **`ReactiveCommand<T>`**（非泛型 = `ReactiveCommand<Unit>`） | ViewModel 的命令；可由 `Observable<bool> canExecuteSource` 构造 —— **不需要引入 CommunityToolkit.Mvvm 的 `RelayCommand`** |
| **`DisposableBag`**（struct、add-only、`Add/Clear/Dispose`、低分配、**非线程安全**） | **View 的绑定凭据袋**（§5.3 解绑协议的核心） |
| `AddTo(ref DisposableBag)` / `AddTo(Component\|GameObject)` | 前者入袋；后者挂在 `destroyCancellationToken`（**只在对象销毁时退订**，见附录 B 的坑） |
| `SerializableReactiveProperty<T>` | 需要在 Inspector 里看/调的 VM 属性（可选） |
| Editor 窗口 `ObservableTracker` | 排查**没被释放的订阅**——UI 绑定泄漏的验证工具 |
| Unity PlayerLoop 时间/帧 Provider（`UnityTimeProvider`/`UnityFrameProvider`） | UI 侧 `Debounce`/`DelayFrame` 等算子的语义基础 |

### 2.3 容器事实（VContainer 1.19.0 源码核对）

`ScopedContainer.ResolveCore`（`Runtime/Container.cs:152-184`）：

```
case Lifetime.Singleton → CreateTrackedInstance    // IDisposable 会被加进 Scope 的 disposables
case Lifetime.Scoped    → CreateTrackedInstance    // 同上，Scope 释放时 Dispose
default (Transient)     → registration.SpawnInstance(this)   // **不被跟踪：容器不负责 Dispose**
```

→ **结论（决定 VM 所有权规则）**：VM 若注册为 **Transient，容器不会替你 Dispose**，必须在面板关闭时由 UI 管理器显式释放；若注册为 Singleton/Scoped，则它与 Scope 同生命周期（状态常驻，**不随面板关闭释放**）。

---

## 3. 分层与职责

```
IZPanelManager（对外服务，容器单例）
 ├─ 面板注册表（VM 类型 → View 类型 + address）        ← 显式注册，零反射
 ├─ 栈与层级（Background/Main/Popup/Toast/Loading）
 ├─ 与资源管理器协作（加载面板母本）
 └─ 与对象池协作（一类面板一池）                        ← 见 §7.2：UI 管理器就是"上层组合器"
       │
       ├─ ZPanel（View 基类，MonoBehaviour，无业务逻辑）
       └─ ZPanelViewModel（纯 C#，R3 属性/命令，可单测）
```

| 角色 | 是什么 | 不做什么 |
|---|---|---|
| `IZPanelManager` / `ZPanelManager` | 打开/关闭/返回、栈与层级、加载与池化、生命周期编排 | 不含任何业务规则；不持有"全局 UI 状态" |
| `ZPanel`（View 基类） | 生命周期钩子 + **手写绑定**（`Bind`/`Unbind`） | 不碰容器、不碰资源管理器、不做业务判断 |
| `ZPanelViewModel` | 状态（`ReactiveProperty`）+ 命令（`ReactiveCommand`）+ 请求关闭的信号 | **不引用任何 UnityEngine 类型**（保证可单测） |
| 绑定层 | **只提供协议**（何时 Bind/Unbind、绑定凭据归谁、如何保证不泄漏），**不提供 DSL** | 不做特性/反射式自动绑定 |
| `ZPanelHandle` | 打开凭据（`Close()` / `WaitCloseAsync()` / `IsOpen`），风格同 `AssetHandle` | — |

> **设计取向**：框架提供的是**协议与保证**，不是"绑定框架"。绑定代码在 View 里手写——这正是"最小自研"能既好用又不过度设计的办法（见附录 A）。

---

## 4. 面板栈与生命周期

### 4.1 对外契约（思路级）

```
public interface IZPanelManager : IDisposable
{
    UniTask<ZPanelHandle> OpenAsync<TViewModel>(ZPanelOpenOptions options = default, CancellationToken ct = default)
        where TViewModel : ZPanelViewModel;

    bool TryHandleBack();                  // 返回键（ESC / Android Back）——由输入层调用
    void CloseAll(bool destroy = false);   // destroy=false：全部关闭并归池；true：清池并释放母本
    ZPanelsState GetState();               // 监控用快照（照池/事件总线的 DTO 风格）
}
```

- **面板按 VM 类型寻址**（`OpenAsync<ShopViewModel>()`）：类型安全、零反射、与池的 `typeof(T)` 风格一致；
- `ZPanelOpenOptions`：层级、遮罩行为（点遮罩是否关闭）、`SingleInstance`（重复打开是"置顶复用"还是"再开一个"）、是否缓存 View、VM 生命周期（见 §7.3）。

### 4.2 生命周期状态机

```
不存在 ──OpenAsync──▶ Loading ──加载+实例化+绑定成功──▶ Opened ──Close──▶ Closing ──▶ 归池(缓存)
   ▲                     │                                                              │
   └──── 清池/释放母本 ────┴──(加载失败/取消 → 回滚到"不存在" + 记 Error)                  └──再 Open──▶ Opened(重绑)
```

| 事件 | 钩子顺序（**固定**） |
|---|---|
| 首次创建 | `Instantiate` → `OnCreate()` → `Bind(vm)` → `OnOpen()` |
| 再次打开（复用） | `OnGet`（池）→ `Bind(vm)` → `OnOpen()` |
| 关闭（缓存） | `OnClose()` → `Unbind()` → `OnReturn`（池）→ `SetActive(false)` |
| 真正销毁 | `OnClose()`（若仍打开）→ `Unbind()` → `OnRecycle()` → `OnClear`（池）→ `Destroy` |

- **框架保证**：`Bind`/`Unbind` **成对且由框架调用**，不在使用方手里——这是"复用不残留绑定"的关键（§5.3）；
- View 同时是**池的 `IZObjectPoolItem`**：两套生命周期的映射必须写死（上表就是映射），**不允许使用方自己再调池**。

### 4.3 栈、层级与返回

| 项 | 规则 |
|---|---|
| 层级 | `Background / Main / Popup / Toast / Loading` 五层（roadmap §5.3）；每层一个父 `Transform`，由场景里的 `ZPanelRoot` 预置 |
| 排序 | 同层按打开顺序 `SetAsLastSibling`；跨层由父节点顺序决定（层级固定，不动态穿插） |
| 遮罩 | Popup 层可选遮罩；点遮罩 → `Close()` 还是忽略，由 `ZPanelOpenOptions` 决定 |
| 返回 | `TryHandleBack()` 从**最上层可关闭面板**开始弹一个；**没有可关闭面板时返回 false**，把输入透传给游戏逻辑（框架不吞返回键） |
| 非栈面板 | `Toast / Loading` 不进栈（不受 `Back` 影响），`CloseAll` 仍会处理 |

### 4.4 异步、取消与失败

| 情形 | 语义 |
|---|---|
| `ct` 取消 | **只影响"等待打开"**：取消后回滚（若已加载则释放/归池），**不会**把面板留在半开状态；已经 `Opened` 的面板不受已取消的 ct 影响 |
| 加载失败（address 错/资源缺失） | 记 Error + **抛异常**（与 `LoadAssetAsync` 的"失败就抛"一致），调用方明确知道没打开；栈不受污染 |
| VM 构造失败 | 同上（抛），并把已实例化的 View 归池/销毁，不留孤儿 |
| 重复 `Open` 同一 VM | 按 `SingleInstance` 处理：`true` → 已打开则置顶并返回**同一个 handle**；`false` → 再开一个（各自独立 VM 实例） |
| 重复 `Close` | 幂等（第二次是 no-op） |
| 回调里再 Open/Close | 允许；框架按"先完成当前转换、再处理下一次"的顺序（不允许嵌套破坏栈） |

---

## 5. View 池化与解绑/重绑协议（本设计的核心）

### 5.1 为什么这是最大的坑

View 被池复用后**上一条绑定可能还活着**：旧 VM 的属性变化会打到新面板上（**串台**），并且旧 VM 因为被订阅引用而无法释放（**泄漏**）。手写 UI 时这类 bug 极难查——所以框架必须把它变成"结构性保证"。

### 5.2 池化粒度

| 项 | 结论（**D2 已定**：复用现有池） |
|---|---|
| 一类型一池 | 复用 `IZObjectPoolManager` + `ZObjectPool<T>`（`T = 具体面板 View 类型`），**不自造第二套池** |
| 前置条件 | 面板 View 必须实现 `IZObjectPoolItem`（`OnInitialize/OnGet/OnReturn/OnClear` + 自归还委托） |
| 缓存上限 | 由 `ZPoolOptions.MaxSize` 决定（0 = 不限）；关闭时**归还**、不销毁 |
| 何时真正销毁 | `CloseAll(destroy: true)`、管理器 `Dispose`、场景切换 —— 顺序**先清池 → 再释放母本**（§7.2） |

### 5.3 解绑/重绑协议（写死，使用方不用记）

```
ZPanel（基类，伪代码）
    DisposableBag _bag;                 // R3 的 add-only struct，字段持有、按 ref 传（不复制）

    Bind(vm):
        _bag.Clear();                   // 防御：进 Bind 前必须是干净的
        _vm = vm;
        OnBind(vm);                     // ← 使用方在这里写订阅，一行一个 AddTo(ref _bag)

    Unbind():
        _bag.Dispose();                 // 一次性退掉本次绑定的全部订阅
        OnUnbind();                     // 使用方在这里做"非订阅型"清理（清空文本/图标等）
        _vm = null;
```

三条硬规则：

1. **所有订阅必须进 `_bag`**：View 里的每次 `Subscribe` 都以 `AddTo(ref _bag)` 结尾（写法模板见 §6.2）；框架不扫描、不代管，只保证"袋子的清空时机"；
2. **`Unbind` 由框架在归池/销毁前调用**，使用方无权跳过；`OnUnbind` 用来清理**非订阅状态**（文字、图标、Sprite 引用）——避免"上一位玩家的金币数还印在新面板上"；
3. **不复用 `AddTo(this)` 当解绑**：`AddTo(Component)` 只在**对象销毁**时退订，池化 View 不销毁 → 它挡不住复用串台（详见附录 B）。两条可以并存：`_bag` 管复用、`AddTo(this)` 管真销毁（双保险）。

### 5.4 绑定的边界（1:1）

- 一个 View 同时只绑一个 VM（**1:1**）；共享状态放在 VM 之外的共享服务里（或 `Singleton` VM），不要"多 View 绑同一 VM"（那会让解绑协议失去意义）；
- VM → View 的关闭请求：VM 暴露 `ReactiveCommand`/`Subject<Unit>`（如 `CloseRequested`），View 绑定它并调 `handle.Close()`；**VM 不认识 View、不认识管理器**。

---

## 6. MVVM 最小绑定层

### 6.1 只提供两件事

| 提供 | 说明 |
|---|---|
| 两个基类 | `ZPanel`（生命周期 + `_bag` 协议）、`ZPanelViewModel`（`IDisposable`：释放自己的 R3 资源；提供 `CloseRequested` 这类通用信号） |
| 协议与保证 | `Bind/Unbind` 的调用时机与成对性、袋子清空时机、VM 与 View 1:1、面板关闭时 VM 的释放归属（§7.3） |

**不提供**：属性/集合的通用绑定 DSL、特性标注、自动生成绑定代码、`ICommand` 到 Button 的自动接线（那类用 R3 的 `ReactiveCommand` + 一行手写订阅即可）。

### 6.2 绑定写法模板（使用方视角，思路级）

```
// ShopPanel : ZPanel
protected override void OnBind(ShopViewModel vm)
{
    vm.Title.Subscribe(t => _title.text = t).AddTo(ref _bag);          // 属性 → View
    vm.Gold.Subscribe(g => _gold.text = g.ToString()).AddTo(ref _bag);
    vm.BuyCommand.Subscribe(_ => _buyButton.interactable = true).AddTo(ref _bag);  // 命令态（可选）
    _buyButton.onClick.AddListener(() => vm.Buy.Execute(Unit.Default));            // 输入 → 命令
    vm.CloseRequested.Subscribe(_ => Close()).AddTo(ref _bag);
}

protected override void OnUnbind()
{
    _buyButton.onClick.RemoveAllListeners();   // 非订阅型清理（uGUI 事件归这里）
    _title.text = string.Empty;                // 清显示态
}
```

- **uGUI 的 `onClick` 属于 `OnUnbind`**（它不是 IDisposable）；`RemoveAllListeners` 一行搞定；
- `ReactiveCommand` 的可执行态（`canExecuteSource`）与 Button 的 `interactable` 的同步由使用方决定（框架不代管）。

### 6.3 ViewModel 的可测性（验收要求）

VM 里**不允许出现 `UnityEngine` 类型**（更别说 `GameObject`/`Transform`）→ 于是它能在纯 C# 测试里 `new` 出来（依赖用假实现或简单桩），驱动 `ReactiveProperty` 断言状态变化。这条是"MVVM 有价值"的前提，写进纪律。

---

## 7. 加载、池化与装配

### 7.1 面板注册表（显式，零反射）

```
PublishAsync 前的注册（组装层或 UI Bootstrap）：
    panelRegistry.Register<ShopViewModel, ShopPanel>("UI/ShopPanel");

运行时 OpenAsync<ShopViewModel>()：
    查表 → 得到 View 类型 + address → §7.2 取实例 → 构造 VM → Bind → 入栈
```

- **不做**：目录扫描、特性标注、`Assembly.GetTypes()` —— 与项目"零反射 / IL2CPP 安全"的取向一致；
- 未注册的 VM 类型 → 打开时**抛明确异常** + 记 Error（不静默）。

### 7.2 母本所有权：UI 管理器就是那个"上层组合器"

`pool-manager-design.md` §5.4 定的分工是：**池只收 `GameObject prefab`；"按 address 建池 + 母本托管"由同时依赖 Pool 与 Resources 的上层承担**。对面板而言，**这个上层就是 UI 管理器**：

```
首次打开某面板类型：
    PrefabAsset prefab = await resourceManager.LoadPrefabAsync(address, nameof(ZPanelManager), ct);
    poolManager.CreatePool<ShopPanel>(new ZPoolOptions<ShopPanel> { Prefab = prefab.Prefab, MaxSize = options.CacheSize });
    （母本由 UI 管理器持有引用，登记在"面板类型表"里）

面板类型卸载（CloseAll(destroy:true) / 管理器 Dispose）：
    ① poolManager.ClearPool<T>() / DestroyPool<T>()      // 先清池（池内实例销毁）
    ② prefab.Dispose()                                    // 再释放母本（顺序不能反）
    ③ 从面板类型表移除登记
```

- **顺序纪律由 UI 管理器保证**（正是 `pool-manager-design` 说的"把顺序收敛在一处"）；
- 加载用的 `owner` 传 `nameof(ZPanelManager)`，让资源簿记能看出是谁持有的（资源管理器已支持 caller 参数）。

### 7.3 ViewModel 的所有权（依据 §2.3 的容器事实）

| 情形 | 规则 |
|---|---|
| **默认（Transient VM）** | 容器**不会**替我们 Dispose → **面板关闭时由 UI 管理器显式 `Dispose()` VM**（`ZPanelViewModel : IDisposable`）；VM 内部用 `DisposableBag` 释放自己的 R3 资源 |
| **常驻面板**（设置面板等，需要保留状态） | VM 注册为 `Singleton`/`Scoped`，随 Scope 释放；**明确声明"不随面板关闭释放"**，此时关闭只解绑 View、不 Dispose VM |
| View → VM 的依赖 | VM 由容器构造（构造函数注入）；**View 不碰容器** |

### 7.4 与总线的分工（沿用既有纪律）

| 场景 | 用什么 |
|---|---|
| 跨模块低频通知（资源加载完成、场景切换、音频状态……面板也可监听） | **事件总线**（`Subscribe<T>(subscriber, action)`，注销用 `UnsubscribeAll(this)`） |
| 同一 UI 模块内 / 父子面板之间 | **VM 直接引用或 VM 自己的 `Subject`** —— 不走总线 |
| 高频数据/需要操作符（节流、合并、绑定） | **R3（`ReactiveProperty`/操作符）**，不经总线 |

纪律：**同一件事只发布一次**；总线只做路由、R3 只做加工（`event-bus-design.md` §2.1 与附录 A）。

---

## 8. 与其它模块的边界

| 模块 | 边界 |
|---|---|
| 资源 | 面板预制体**只经** `IZResourceManager`；不在 UI 里直接碰 `Addressables` |
| 池 | View 复用只经 `IZObjectPoolManager`；**使用方不自己调池**（框架代管，避免破坏 §4.2 的生命周期映射） |
| 总线 | 只做跨模块低频通知；UI **不把面板状态发到总线**（状态归 VM） |
| 日志 | 构造注入 `IZLogger`；打开失败/加载失败/绑定异常记 Error |
| 组装层 | 只做：注册 `IZPanelManager` + 调 `panelRegistry.Register<...>()` + 提供场景里的 `ZPanelRoot`（层级父节点） |

---

## 9. 目录与程序集

```
Assets/Zipper/UI/
├── Zipper.UI.asmdef          （引 Core / Pool / Resources / UniTask / VContainer / R3 / R3.Unity）
├── ZPanel.cs / ZPanelViewModel.cs / ZPanelHandle.cs
├── IZPanelManager.cs / ZPanelManager.cs
├── ZPanelOpenOptions.cs / ZPanelRegistry.cs
└── ...
```

- **独立 `Zipper.UI` asmdef**（**D3 已定**：独立程序集，引 Core/Pool/Resources/UniTask/VContainer/R3）：roadmap §4.3 已列为待补；独立程序集才能"可裁剪"（不用 UI 的工程不引它）；
- 命名按 `naming-convention.md`：对外概念 `Z*`/`IZ*`（`IZPanelManager`/`ZPanelManager`/`ZPanel`/`ZPanelViewModel`）；内部机制类型不加前缀（如 `PanelSlot`/`PanelStackEntry`）；
- `Zipper.UI` 依赖 Core/Pool/Resources 是**单向**的，符合 roadmap §4.2 的依赖链 ✓。

---

## 10. 分阶段实施（MVP → 扩展）

| 阶段 | 内容 | 可否独立验收 |
|---|---|---|
| **v1 MVP** | 面板注册表 + 栈与层级 + `OpenAsync`/`Close`/`TryHandleBack` + View 池化 + `Bind/Unbind` 协议 + VM 所有权 + 加载失败与取消语义 | ✅ 是（§11 的 1–7 条） |
| v2 | 遮罩与排序细节、Toast/Loading 层、`GetState()` 监控 DTO、`SingleInstance` 置顶复用 | ✅ 是 |
| v3 | 按需：转场动画钩子、图集/合批、本地化接口（**都不在 MVP 内**） | — |

---

## 11. 验收标准（可判定）

| # | 用例 | 断言 |
|---|---|---|
| 1 | 面板栈顺序 | `Open(A)/Open(B)` → `TryHandleBack()` 关 B、再关 A、再调返回 false 且**不吞输入** |
| 2 | 重复 Open（`SingleInstance`） | 第二次返回**同一 handle**、不新建实例、栈内不重复登记 |
| 3 | 重复 Close / 关闭未打开的面板 | 幂等，不抛、不污染栈 |
| 4 | **复用不留残留绑定** | 同一面板 Open/Close 50 次后：`ObservableTracker` 里该 View 的订阅数不增长；`OnBind`/`OnUnbind` 调用次数相等 |
| 5 | **复用不串台** | A 面板绑定 VM1 → 关闭 → 用 VM2 重开 → 改 VM1 的属性，View **无任何变化** |
| 6 | 异步取消 | `OpenAsync(ct)` 在加载中被取消 → 无孤儿面板（场景对象数/池计数不增长）、已加载资源被释放 |
| 7 | 加载失败 | 错 address → **抛异常** + 记 Error；栈与面板类型表无残留 |
| 8 | 母本生命周期 | `CloseAll(destroy:true)` → 池已清 + `AssetHandle` 已释放（资源簿记归零），且顺序是"先清池再释放母本" |
| 9 | VM 可单测 | 纯 C# 测试里 `new ShopViewModel()` 并驱动属性/命令断言（不引用任何 UnityEngine 类型） |
| 10 | 跨模块通知 | 面板监听总线事件；关闭后 `UnsubscribeAll(this)` 生效（再 Publish 不再触发） |

**验收证据形态**：EditMode 测试（**D6 已定**：落在现有 `Assets/Zipper/Tests/` 的 `Zipper.Tests` 程序集，需给它加 `Zipper.UI` 引用；VM 单测同一处理）+ `ObservableTracker` 计数 + `GetState()` 快照。

---

## 12. 明确不做（MVP 边界外）

| 不做 | 理由 |
|---|---|
| 交互动画 / 转场 / 缓动 | 属表现层，按需在具体面板里用 DOTween/UniTask 自己写；框架只留钩子 |
| UI 性能优化（合批、图集、Canvas 重建） | 与架构无关，属使用方的资源与布局问题 |
| 本地化 / 多语言 | 独立议题；VM 侧留字符串键即可，不塞进框架 |
| **UI Toolkit 路线** | roadmap §5.3 已定 uGUI；两条路线并行会分裂 |
| 子 Scope / 多场景 UI 层级 | roadmap §5.5 明确单场景毕设不需要 |
| 声明式（特性/反射）自动绑定、绑定代码生成 | 与"零反射 / IL2CPP 安全"冲突，且是"过度设计"高风险区（roadmap §9） |
| 通用 MVVM 框架（任意嵌套属性/集合/转换器 DSL） | MVP 边界硬约束；用 R3 操作符在 View 内解决 |
| 一个 VM 驱动多个 View | 破坏 1:1 与解绑协议（共享状态请放服务或 Singleton VM） |
| Toast/Loading 的排队与优先级（v1 只做层） | 需要真实需求再加 |

---

## 13. 已定决策（D1–D7，2026-09-13 使用者拍板）

| # | 决策 | 结论 | 落点 |
|---|---|---|---|
| **D1** | 绑定层：最小自研 vs 引入社区 MVVM | **最小自研**（R3 已提供属性 / 命令 / 凭据袋，见附录 A） | §6；`ZPanel` + `ZPanelViewModel` 两个基类，绑定代码在 View 手写 |
| **D2** | View 池化粒度 | **复用 `IZObjectPoolManager` + `ZObjectPool<T>`**（面板 View 实现 `IZObjectPoolItem`），不自造第二套池 | §5.2、§7.2 |
| **D3** | 是否独立 `Zipper.UI.asmdef` | **是**（引 Core/Pool/Resources/UniTask/VContainer/R3） | §9 |
| **D4** | VM 所有权 | **默认 Transient，面板关闭时由 UI 管理器显式 `Dispose()`**；常驻面板（如设置面板）注册 `Singleton`/`Scoped` 并**显式声明"不随面板关闭释放"**（依据：VContainer 只跟踪 Singleton/Scoped 的 `IDisposable`，见 §2.3） | §7.3 |
| **D5** | 返回键归属 | 由**输入层**调用 `IZPanelManager.TryHandleBack()`；**框架不吞返回键**（没有可关闭面板时返回 `false`，输入透传） | §4.1、§4.3 |
| **D6** | 测试落点 | 落在**现有 `Zipper.Tests`**（`Assets/Zipper/Tests/`），给它加 `Zipper.UI` 引用；VM 单测同程序集 | §11 |
| **D7** | 面板注册表持有者 | **UI 模块内**（`ZPanelRegistry` 由 `ZPanelManager` 持有）；组装层只负责调用 `Register<TViewModel, TPanel>(address)` | §7.1、§8 |

### 13.1 仍待确认（非阻塞）

| # | 项 | 说明 |
|---|---|---|
| C1 | 附录 A 里 Unity 官方 MVVM（App UI）的判据 | 结论（不引入）**不依赖**该项（理由 ② 独立成立）；但"App UI 的绑定层是否只服务 UI Toolkit"这条待使用者一手确认后定稿 |
| C2 | `ZPanelOpenOptions` 的字段集合 | v1 先按 §4.1 列的四项（层级 / 遮罩行为 / `SingleInstance` / 缓存与 VM 生命周期），实现时若有增补再回写 |

---

## 附录 A：绑定层"最小自研 vs 引入社区 MVVM"

| 方案 | 优点 | 代价 | 结论 |
|---|---|---|---|
| **最小自研**（本设计） | 零反射、零依赖新增、与 R3 同一套响应式体系；R3 **自带** `ReactiveCommand`（命令）与 `DisposableBag`（绑定凭据袋）→ 需要的东西已经齐了 | 绑定代码要手写（每个面板十几行） | ✅ **推荐** |
| CommunityToolkit.Mvvm（`ObservableObject`/`RelayCommand`） | 纯 C#、`INotifyPropertyChanged` 生态成熟、命令开箱可用 | ① 与 R3 的 `ReactiveProperty` **两套属性变更机制并存**（和"同一件事只发布一条链路"的纪律冲突）；② 它不含 Unity 侧绑定，View 侧照样手写；③ 多一个依赖 | ❌ 不引入（命令用 R3 `ReactiveCommand`，属性用 `ReactiveProperty`） |
| Unity 官方 MVVM（`com.unity.dt.app-ui`，命名空间 `Unity.AppUI.MVVM`） | 官方维护；`ObservableObject` / `RelayCommand` / `ObservableList` 等**基础类型是纯 C#、与 UI 系统无关** | ① 它的**组件与数据绑定机制建立在 UI Toolkit 之上**（`VisualElement` / `BindableElement` / `UIDocument`）→ **给 uGUI 用不了自动绑定**；② 若只借它的基础类型、再为 uGUI 手写绑定，那与第 1 行"最小自研"没有任何区别，却平白多一个依赖（而这些类型 R3 已经覆盖：`ReactiveProperty` / `ReactiveCommand`） | ❌ 不引入 —— **理由不是"它和 uGUI 无关"，而是"它能帮 uGUI 的那一半我们已经有了"** |

> **一句话**：本项目 UI 的"绑定"其实是"**R3 订阅 + 生命周期协议**"；既然 R3 已经提供了属性、命令与凭据袋，引入第二套 MVVM 只会带来两套并行的属性通知机制。
>
> **本节判据说明（待确认项）**：上表基于"**App UI 的绑定层是 UI Toolkit 的**"这一 API 形态判断（其绑定相关类型均为 `VisualElement` 系）；本节结论只针对"**能不能省掉手写绑定**"——答案是**在 uGUI 下不能**。若将来出现面向 uGUI 的官方绑定层，或 App UI 新增 uGUI 绑定支持，可重新评估这一行（记为待确认）。

---

## 附录 B：池化 View 的 `AddTo(this)` 陷阱（必须知道）

- `subscription.AddTo(this)`（`Component`/`GameObject` 重载）内部是注册到 **`destroyCancellationToken`** ——**只在对象被销毁时退订**（R3 的 `MonoBehaviourExtensions` 就是这么实现的；未激活对象还会额外挂 `ObservableDestroyTrigger`）。
- 池化 View **关闭时不销毁、被复用** → 靠 `AddTo(this)` **挡不住**"上一条绑定还活着" → 换新 VM 打开时出现**串台**（旧 VM 的推送打到新面板）与**泄漏**（旧 VM 被订阅引用住）。
- 所以：**复用这条路径必须用 `_bag` 显式 `Dispose`**（§5.3）；`AddTo(this)` 只作为"真销毁"时的双保险，不能替代它。
- 验证手段：R3 的 Editor 窗口 **`ObservableTracker`** 能列出没释放的订阅——§11 第 4 条验收就用它。

---

## 14. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.2 | 2026-09-13 | **D1–D7 由使用者拍板落定**（§13 由"待决策项"改为"已定决策"）：D1 **最小自研**绑定层；D2 **复用** `IZObjectPoolManager`+`ZObjectPool<T>`；D3 **独立** `Zipper.UI` asmdef；D4 VM **默认 Transient + 面板关闭时由管理器 Dispose**（常驻面板显式声明）；D5 返回键由**输入层**调用、框架不吞；D6 测试落**现有 `Zipper.Tests`**；D7 注册表**归 UI 模块**。同步 §5.2/§9/§11 里指向 D 项的表述；新增 §13.1「仍待确认」（App UI 判据、`ZPanelOpenOptions` 字段集合） |
| v0.1 | 2026-09-13 | 初稿（S1 经使用者批准，方案 A）：目标与范围、前置事实（既有契约 / R3 能力 / VContainer Transient 不跟踪的源码事实）、分层与职责、面板栈与生命周期状态机与钩子顺序表、异步取消与失败语义、**View 池化与解绑/重绑协议**、最小 MVVM 绑定层（含绑定模板与 VM 可测性纪律）、加载与装配（显式注册表、**UI 管理器充当母本托管的上层组合器**）、VM 所有权规则、与总线/资源/池/日志的边界、目录与程序集、分阶段实施、10 条验收、明确不做 9 项、待决 D1–D7、附录 A（最小自研 vs 社区 MVVM）、附录 B（`AddTo(this)` 的池化陷阱） |
