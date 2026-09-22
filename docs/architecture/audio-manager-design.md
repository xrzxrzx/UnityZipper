# Zipper 音频管理器设计（Zipper.Audio）

> 状态：**v0.1 草稿，待审阅**
> 定位：音频管理器 MVP 的**契约、机制与边界**——三类播放（2D 音效 / 3D 音效 / BGM）、AudioSource 池化、播放句柄、淡入淡出、AudioMixer 分组与音量持久化、与 UI 的对接（UI 音效）。**只给设计思路与思路级伪代码，不含实现代码**。
> 实施归属：AI 只负责架构设计与思路级伪代码；**代码实现由使用者完成**（`docs/standards/agent-role.md`）。
> 关联：`docs/planning/technical-roadmap.md` §5.2（音频路线）/§5.4.1（对象池）/§4.2（依赖链）、`docs/architecture/pool-manager-design.md`（§5.4 组合器）、`docs/architecture/resource-manager-design.md`、`docs/architecture/event-bus-design.md`、`docs/architecture/ui-manager-design.md`（UI 音效对接）、`docs/standards/naming-convention.md`
> 变更记录：v0.1 初稿（使用者要求：音频先行，因 UI 管理器要绑定 UI 音效）

---

## 0. 这份文档怎么读

| 读者 | 建议路径 |
|---|---|
| 要立刻实现 MVP 的人 | §1 范围 → §3 分层 → §4 播放模型（含池化约束）→ §5 句柄 → §7 音量 |
| 要把关的人 | §2 前置事实 → §4.2「AudioSource 不能直接池化」→ §9 UI 音效对接 → §13 验收 → §15 待决 |
| 做 UI 的人 | §9（音频侧提供什么 / 依赖方向怎么走） |

---

## 1. 目标与范围

### 1.1 要解决什么

1. **三类播放**：2D 音效（UI/环境提示）、3D 音效（世界内、带位置衰减）、BGM（独占、循环、可交叉淡入）；
2. **AudioSource 复用**：不每次 `Instantiate`——按池取用、用完归还；并发播放数由池容量控制；
3. **播放控制**：播放句柄（停止 / 暂停 / 续播 / 淡出）、播放结束通知；
4. **音量体系**：AudioMixer 分组（Master / BGM / SFX / Voice / UI）+ 线性音量 ↔ dB 换算 + **持久化**；
5. **与 UI 对接**：UI 能"播一个 UI 音效"而不必知道 AudioSource / 池 / Mixer（§9）。

### 1.2 管什么 / 不管什么

**管**：播放 API 与句柄契约、AudioSource 池化机制、淡入淡出的时序与取消、分组音量与持久化、BGM 通道、clip 的加载与缓存归属、主线程约定、验收标准。

**不管**（见 §14）：音频 DSP 效果链（混响/滤波等由 AudioMixer 资产配置）、空间音频高级特性（HRTF/Ambisonic）、语音聊天、节奏游戏级精确同步（`dspTime` 级）、音频资源导入设置规范、同时播放数限流与优先级抢占（v1 不做）。

---

## 2. 前置事实（已核对，2026-09-13）

### 2.1 依赖模块的契约

| 来源 | 要点 |
|---|---|
| `IZObjectPoolManager`（Pool） | `CreatePool<T>(ZPoolOptions<T>)`，**`T : Component, IZObjectPoolItem`**；`Get<T>()` / `Return<T>(T)` / `ClearPool<T>()` / `DestroyPool<T>()`；**key = `typeof(T)`（一类型一池）**；`CreatePool` 会校验 `options.Prefab.GetComponent<T>() != null`，不通过则**拒绝且不入册** |
| `IZResourceManager`（Resources） | `LoadAssetAsync<T>(address, owner, ct)` → `AssetHandle<T>`（`handle.Asset` / `Dispose()` 释放、幂等、释放后 `Asset` 返回 default） |
| `IZLogger`（Core） | 构造注入 |
| `IZEventBus`（Core） | 仅主线程；事件必须引用类型；`Subscribe<T>(subscriber, action)` + `UnsubscribeAll(subscriber)` |

### 2.2 Unity 音频事实（本设计据此定型）

| 事实 | 影响 |
|---|---|
| `AudioSource` 是 **Unity 内置组件**，**无法实现自定义接口** | **不能直接 `ZObjectPool<AudioSource>`**（约束 `T : IZObjectPoolItem` 不满足）→ 需要包装组件（§4.2） |
| Unity 音频 API（`Play`/`Stop`/`volume`/`AudioMixer.SetFloat`…）**必须在主线程** | 本模块所有公开 API **仅主线程**（非主线程记 Error 并忽略，与总线/池同一取向） |
| `AudioSource` **没有"播放结束"事件**，只能轮询 `isPlaying` | 模块需要一个 **tick 驱动**（§3.3） |
| `AudioSource.PlayOneShot` 不改变 `clip`/`isPlaying` 语义，无法查询"这一发是否播完" | 需要句柄/结束通知的场景用 `Play()`（独占一个 source）而不是 `PlayOneShot` |
| `AudioMixer` 暴露参数用 **dB**，线性音量换算 `dB = 20 * log10(v)`；`v = 0` → 取 `-80dB`（Mixer 默认最小） | 音量 API 对外用**线性 0..1**，内部换算（§7.2） |
| 暂停/`timeScale = 0` 会影响 `Time.deltaTime` | 淡入淡出用 **`Time.unscaledDeltaTime`**（§6.1） |

### 2.3 容器事实（沿用 UI 设计稿 §2.3 的核对结论）

VContainer **只跟踪 Singleton / Scoped 的 `IDisposable`**（`Container.cs:152-184`），Transient 不跟踪 → 音频管理器注册为 **Singleton**（随 Scope 释放，容器会替我们 `Dispose`）✓。

---

## 3. 分层与职责

### 3.1 结构

```
IZAudioManager（对外服务，容器 Singleton）
 ├─ AudioSource 池（复用 IZObjectPoolManager，池元素 = 包装组件）
 ├─ BGM 通道（两个常驻 AudioSource，A/B 轮转做交叉淡入）
 ├─ clip 缓存（address → AssetHandle<AudioClip>）
 ├─ 分组音量（AudioMixer exposed parameter + PlayerPrefs 持久化）
 └─ 播放登记表（谁在播、归谁、何时归池）
       ▲
       └─ ZAudioHandle（对外凭据：Stop/Pause/Resume/IsPlaying/WaitFinishedAsync）
```

| 角色 | 是什么 | 不做什么 |
|---|---|---|
| `IZAudioManager` / `ZAudioManager` | 播放/停止/音量/BGM/clip 缓存/池与通道的编排 | 不含业务规则（"什么时候该响"由使用方决定） |
| `PooledAudioSource`（内部包装组件，**不加前缀**） | 一个 `MonoBehaviour`，内含 `AudioSource`；实现 `IZObjectPoolItem`；持有本实例的播放状态与 fade 取消令牌 | 不做资源加载、不认识总线/UI |
| `ZAudioHandle` | 播放凭据（风格同 `AssetHandle` / `ZPanelHandle`） | 不暴露 `AudioSource`（防使用方绕过池） |
| `ZAudioBus`（枚举） | `Master / Bgm / Sfx / Voice / Ui`（分组；名字 → Mixer 组名的映射放在选项里） | — |

### 3.2 名字与命名

- 对外概念：`IZAudioManager` / `ZAudioManager` / `ZAudioHandle` / `ZAudioBus` / `ZAudioPlayOptions`；
- 内部机制类型**不加前缀**（`PooledAudioSource`），与 `AssetHandle` / `Subscription` 一致；
- 目录 `Assets/Zipper/Audio/`，程序集 `Zipper.Audio`（引 Core / Pool / Resources / UniTask / VContainer）。

### 3.3 tick 驱动（因为 AudioSource 没有完成事件）

两个选项（**待决 D2**）：

| 方案 | 做法 | 评价 |
|---|---|---|
| **A. 复用容器的 `ITickable`** | `ZAudioManager` 实现 `VContainer.Unity.ITickable`，注册时 `.As<ITickable>()`；VContainer 的 EntryPointDispatcher 会把它收进 `IReadOnlyList<ITickable>` 并逐帧调用 | **推荐**：不新增常驻 GameObject；代价是本模块引 VContainer（roadmap §4.2 允许模块按需引用） |
| B. 自带驱动组件 | 音频模块自己造一个 `DontDestroyOnLoad` 的 driver GameObject（同日志模块 `ZMainThreadDispatcherDriver` 的做法） | 不引 VContainer，但多一个常驻对象 |

tick 里只做**低频**工作：检查已播完的 source → 归还池 + 完成 `WaitFinishedAsync`；以及推进未完成的 fade（或 fade 由各自的 UniTask 循环推进，tick 只做结束检测）——**二选一，别重复驱动**（待决 D3）。

---

## 4. 播放模型

### 4.1 三类播放

| 类型 | 用什么 source | 关键参数 |
|---|---|---|
| **2D 音效** | 池化 `PooledAudioSource` | `spatialBlend = 0`；一次性；用完归池 |
| **3D 音效** | 池化 `PooledAudioSource` | `spatialBlend = 1`；播放前把 source 的 `transform.position` 设到目标点；衰减由 `AudioSource` 的 rolloff 曲线（配置在原型上）控制 |
| **BGM** | **两个常驻 source 轮转**（不池化） | `loop = true`；交叉淡入淡出；同一时刻只有一个"当前" |

### 4.2 ⚠️ 关键约束：`AudioSource` 不能直接池化

`ZObjectPool<T>` 要求 `T : Component, IZObjectPoolItem`，而 Unity 的 `AudioSource` **无法实现我们的接口**。三条出路：

| 方案 | 说明 | 结论 |
|---|---|---|
| **(a) 包装组件**（推荐） | `PooledAudioSource : MonoBehaviour, IZObjectPoolItem`（内含 `AudioSource`），池化对象是它 | ✅ 不动池模块、还能挂"本实例的 fade/状态"逻辑 |
| (b) 放宽池的约束 | 把 `ZObjectPool<T>` 从"要求接口"改成"用委托注入回调" | ❌ 池模块已 v1 定稿、且会让池契约变复杂（回调注入面变大） |
| (c) 音频模块自建轻量池 | `Queue<AudioSource>` 自管 | ❌ 与"复用现有池"的既定取向冲突，且丢掉池的统计/上限/Clear 语义 |

**运行时原型母本（不需要任何资源文件）**：

```
初始化（首次需要池时）：
    prototype = new GameObject("[Zipper] AudioSourcePrototype");
    prototype.SetActive(false);                                  // 原型不发声、不可见
    var pooled = prototype.AddComponent<PooledAudioSource>();    // 内部会确保有 AudioSource
    Object.DontDestroyOnLoad(prototype);

    poolManager.CreatePool<PooledAudioSource>(new ZPoolOptions<PooledAudioSource>
    {
        Prefab = prototype,          // ← CreatePool 会校验 prototype.GetComponent<PooledAudioSource>() != null ✓
        InitialSize = options.InitialSources,
        MaxSize = options.MaxSources,
        OnGet = ..., OnReturn = ..., OnClear = ...
    });

销毁（模块 Dispose）：
    ① poolManager.ClearPool<PooledAudioSource>() / DestroyPool<PooledAudioSource>()   // 先清池
    ② Object.Destroy(prototype)                                                       // 再销毁原型（顺序不能反）
```

> `ZPoolOptions<T>.Prefab` 收的是 **GameObject**（不需要是资产 prefab）✓ 所以"代码里 new 一个原型"完全成立，音频模块因此**不依赖任何预制体资源**。
> 注意：原型 inactive → 池克隆出来的实例也是 inactive ✓ 归还时 `SetActive(false)`、取出播放前 `SetActive(true)`（在包装组件的 `OnGet/OnReturn` 里做，使用方不操心）。

### 4.3 池容量与"取不到"的语义

- `MaxSize = 0` 表示不限 → 但音频上**建议设上限**（例如 32），否则"一次爆炸 200 个音效"会把对象数拉爆；
- 池满时 `Get<T>()` 返回 `default`（null，池的既有语义）→ 音频侧处理策略（**待决 D4**）：
  - **丢弃并记 Debug**（推荐 v1：不打断游戏，日志能查）；
  - 或"抢占最老的一个"（需要模块自己维护播放顺序，复杂度高，v2 再说）。

### 4.4 公开 API（思路级）

```
public interface IZAudioManager : IDisposable
{
    // ① 便捷：一次性音效（UI 常用；不需要句柄）
    void PlaySfx(string address, in ZAudioPlayOptions options = default);                  // 2D
    void PlaySfx(string address, Vector3 position, in ZAudioPlayOptions options = default); // 3D

    // ② 完整：需要控制（停止/暂停/淡出/等播完）
    UniTask<ZAudioHandle> PlaySfxAsync(string address, in ZAudioPlayOptions options = default, CancellationToken ct = default);
    UniTask<ZAudioHandle> PlayBgmAsync(string address, float crossfadeSeconds = 0f, CancellationToken ct = default);

    // ③ 音量
    float GetVolume(ZAudioBus bus);
    void SetVolume(ZAudioBus bus, float volume01);        // 立即生效 + 持久化

    // ④ 预热 / 释放
    UniTask PreloadAsync(string address, CancellationToken ct = default);
    void ReleaseAllClips();

    ZAudioState GetState();                               // 监控快照（照池/总线的 DTO 风格）
}
```

- `ZAudioPlayOptions`（v1 字段集，**待决 C2**）：`Bus`（默认 `Sfx`）、`Volume01`、`Pitch`、`Loop`、`FadeInSeconds`、`SpatialBlend`（3D 用）；
- **两套入口的分工**：UI 点击音这类 fire-and-forget 用 ①（内部拿句柄、播完自动归还）；需要"停止/淡出/等播完"的用 ②。

---

## 5. 播放句柄与生命周期

### 5.1 句柄契约

```
ZAudioHandle（凭据式）
    bool IsPlaying { get; }
    void Stop(float fadeOutSeconds = 0f);      // 幂等；淡出结束后归还池
    void Pause(); void Resume();
    UniTask WaitFinishedAsync(CancellationToken ct = default);   // 播完 / 被停止 都会完成
```

- `Stop()` / 播完 / 模块 Dispose → **同一套归还路径**（归还 = `SetActive(false)` + `ResetState()`（清 clip/位置/音量）+ `poolManager.Return(item)`）；
- 句柄不暴露 `AudioSource`——使用方拿不到就绕不过池；
- **重复 Stop 幂等**；`WaitFinishedAsync` 在已结束时立即完成。

### 5.2 生命周期表

| 事件 | 顺序 |
|---|---|
| 播放（2D/3D） | `Get<PooledAudioSource>` → `SetActive(true)` → 设置 clip/音量/位置 → `Play()` → 登记到播放表 |
| 播完 | tick 检测 `!isPlaying` → `ResetState()` → `Return` → 完成 `WaitFinishedAsync` |
| `Stop(fadeOut)` | 取消该实例的 fade 令牌 → 淡出（若 >0）→ `ResetState()` → `Return` |
| 池满 | 按 §4.3 的策略处理（默认丢弃 + Debug 日志） |
| 模块 `Dispose` | 停 BGM → 取消所有 fade → 归还全部在播实例 → **先 `ClearPool` 再销毁原型** → 释放全部 clip 句柄 |

---

## 6. 淡入淡出

### 6.1 驱动方式

- 用 **UniTask 循环**插值 `AudioSource.volume`，步长用 **`Time.unscaledDeltaTime`**（暂停/`timeScale` 不影响；音频淡入淡出不应被游戏暂停冻结）；
- 时长 ≤ 0 → 直接设目标音量（不做循环）；
- **每个 `PooledAudioSource` 实例持一个 `CancellationTokenSource`**：新的 fade 或 `Stop` 会先取消上一个 → 保证"最后一次意图获胜"，不会出现两个循环抢同一个 `volume`。

### 6.2 BGM 交叉淡入

```
PlayBgmAsync(address, crossfade):
    取非当前的那个常驻 source（A/B 轮转）
    → 新 source: clip = 新曲、volume = 0、Play()
    → 并行：新 source 淡入到目标音量、旧 source 淡出到 0
    → 交叉完成后：旧 source.Stop()、清 clip
    → 记录"当前 source = 新"
```

- **必须两个 source**：同一个 source 换 clip 会先"咔嚓"截断旧曲，做不到真正的交叉；
- 交叉期间的 `SetVolume(Bgm, ...)` 要作用到"两个 source 的目标音量"（实现细节：把目标音量记在模块级 `_bgmVolume01`，fade 读它）；
- 快速连续切歌：新请求取消上一次未完成的交叉（同样"最后一次意图获胜"）。

### 6.3 clip 加载与播放的时序

- `PlaySfxAsync(address, ...)`：先**加载**（可能耗时）→ 再取池实例 → 播放。
  - 加载期间 `ct` 取消 → 直接放弃（不取池、不留痕迹）✓；
  - 加载完成但**池已满** → 按 §4.3 处理；
- 避免"声音比画面晚半秒"的常见坑：需要即时响的场景用 **`PreloadAsync` 预热**（UI 点击音就该在面板打开时预热）。

---

## 7. 分组与音量

### 7.1 AudioMixer 分组

- Mixer 资产里有 5 个组：`Master / BGM / SFX / Voice / UI`（`UI` 组挂在 `SFX` 下或与 `SFX` 平级，由使用方的 Mixer 资产决定）；
- `ZAudioBus` 枚举 → Mixer 组名的映射写在 `ZAudioOptions` 里（**不在代码里硬编码组名字符串**，便于业务改名）；
- 播放时把 `AudioSource.outputAudioMixerGroup` 设为对应的组（`PooledAudioSource` 在播放前设置）。

### 7.2 线性音量 ↔ dB

```
ToDb(v01)  : v01 <= 0.0001f ? -80f : Mathf.Log10(v01) * 20f
SetVolume  : mixer.SetFloat(paramName, ToDb(volume01))
```

- **对外只用线性 0..1**（使用方不该关心 dB）；内部换算；
- Mute 用 `v01 = 0` 表达（→ `-80dB`）。

### 7.3 持久化

| 项 | 结论 |
|---|---|
| 存储 | `PlayerPrefs`（v1 够用；框架暂无设置模块） |
| key 约定 | `Zipper.Audio.Volume.<Bus>`（如 `Zipper.Audio.Volume.Master`）——**带框架前缀**，避免与业务 key 撞车 |
| 写入时机 | `SetVolume` 立即写（低频操作）；`PlayerPrefs.Save()` 由使用方在合适时机调（或模块在 `OnApplicationPause(true)`/`Dispose` 时补一次，与日志模块的 flush 时机同理） |
| 启动读取 | 模块初始化时读回并 `SetFloat`（**先于**任何播放） |
| 默认值 | 全 1.0（满音量） |

---

## 8. BGM 通道（独占）

| 项 | 规则 |
|---|---|
| 通道数 | 2 个常驻 `AudioSource`（A/B 轮转），**不进池**（池是给一次性音效的） |
| 独占语义 | 同一时刻只有一个"当前 BGM"；新 `PlayBgmAsync` ⇒ 交叉淡入替换 |
| 循环 | `loop = true`（BGM 默认循环；要单次播放用 `PlaySfx` 走 SFX 组） |
| 暂停 | `Pause()`/`Resume()` 作用在当前 source；模块 `OnApplicationPause` 可选自动暂停（**待决 C3**） |
| 不跨场景销毁 | 两个 source 挂在模块自己的常驻对象上（`DontDestroyOnLoad`） |
| 状态可查 | `GetState()` 里带"当前 BGM address / 是否在播 / 交叉进度" |

---

## 9. 与 UI 的对接（UI 音效）★ 本次新增的重点

### 9.1 音频侧提供什么（UI 只需要这些）

| UI 的需求 | 音频侧 API |
|---|---|
| 点击/悬停/返回等一次性音效 | `PlaySfx(address)`（2D，fire-and-forget，播完自动归还） |
| 面板打开时的音效要在**点击瞬间**响 | `PreloadAsync(address)` 预热（面板打开/首次交互前调） |
| 需要"切面板时淡出上一个音效" | `PlaySfxAsync(...)` 拿句柄 → `Stop(fadeOut)` |
| UI 音量单独可调 | `ZAudioBus.Ui` 分组 + `SetVolume(ZAudioBus.Ui, v)` |
| UI 不关心地址细节 | **地址由使用工程决定**（框架不内置业务清单，与 `resource-manager-design` 的键体系纪律一致）：UI 侧持"音效地址常量"或一个薄的 `UISfx` 配置表 |

**音频侧必须保证的两条**（UI 依赖它们）：
1. `PlaySfx` **不抛异常**：地址无效/加载失败 → 记 Error 后静默返回（UI 的点击不该因为少一个音效而崩）；
2. `PlaySfx` **不产生句柄泄漏**：走 ① 号便捷入口的调用方不需要管理任何凭据。

### 9.2 依赖方向（**待决 D8**，需使用者拍板）

> 编号说明：这是与 `ui-manager-design.md` §13.2 **同一个决策**，编号沿用 UI 稿的 **D8**（音频稿此处不再另起 D1，避免两处编号打架）。音频稿自身的待决项为 D2–D4 与 C1–C3。

`roadmap §4.2` 现在的依赖链是 `Core → {Pool, Resources} → {Audio, UI}`——**UI 与音频是并列的，UI 不依赖 Audio**。而"UI 要播音效"必须有某种连接。三条路：

| 方案 | 做法 | 优点 | 代价 |
|---|---|---|---|
| **① UI → Audio 直接依赖** | `Zipper.UI` 的 asmdef 引 `Zipper.Audio`；`ZPanel` 基类可注入 `IZAudioManager`（或 View 自行注入） | 最直白、零额外抽象 | 横向依赖；roadmap §4.2 依赖链要改成 `… → Audio → UI`；**UI 模块失去"可裁剪"**（没音频的工程也得带 Audio） |
| **② UI 定义抽象 + 组装层注入**（**推荐**） | `Zipper.UI` 内部定义 `IZUISfx`（只有 `Play(string address)` / `Preload(...)` 几个成员）+ 一个"什么都不做"的默认实现；组装层（Assembly-CSharp 的 `GameLifetimeScope`，它同时可见 UI 与 Audio）注册一个薄适配：`builder.Register<IZUISfx, ZAudioUISfx>(Lifetime.Singleton)` | **零横向依赖**、UI 可独立裁剪、可单测（塞假实现）、符合 roadmap §4.1 原则 1/4（依赖单向 + 依赖倒置） | 多一个接口 + 一小段组装代码（约 10 行） |
| ③ 走事件总线 | UI `Publish<ZUISfxRequested>(...)`，音频模块订阅 | 零依赖 | **与既有纪律冲突**：`event-bus-design` §2.1 明确「要对方做事 → 接口方法 + UniTask；总线只做"某件事发生了"的通知」——"请播放音效"是**请求**不是**事实**；且事件类型放哪都会造成一方依赖另一方（放 `Core/Events` 才能两边都不欠，但那是把业务语义塞进 Core） |

> **我的建议：方案 ②**（UI 侧一个小接口 + 组装层 10 行适配）。它保住"UI 可裁剪"与依赖单向，代价很小。
> 若你更看重简单直接、且接受"UI 必带音频"，方案 ① 也可以——那就把 `roadmap §4.2` 的依赖链改成 `Core → {Pool, Resources} → Audio → UI`（**这一行改不改取决于你这个决定**）。

### 9.3 无论选哪个方案，UI 侧的使用形态都一样

```
// View 里（OnBind 阶段）
_sfx.Play(UISfx.Click);                    // 点击音
_sfx.Play(UISfx.PanelOpen);

// 面板首次打开时预热，避免第一声延迟
_sfx.Preload(UISfx.Click);                 // 内部转成 asset address → 音频模块加载并缓存
```

- `_sfx` 是注入进来的（方案 ② 是 `IZUISfx`，方案 ① 是 `IZAudioManager`）；
- **不要在每次点击时 `await` 加载**：音频模块内部缓存 + `Preload` 才是"点了就响"的保证；
- UI 侧不做"音效地址 → 播放"的映射逻辑之外的事（不选 AudioSource、不管音量组）。

---

## 10. 与其它模块的边界

| 模块 | 边界 |
|---|---|
| 资源 | clip **只经** `IZResourceManager.LoadAssetAsync<AudioClip>`；模块内部按 address 缓存 `AssetHandle<AudioClip>`，`ReleaseAllClips()`/`Dispose` 统一释放；**不做引用计数**（与资源模块"只记账不计数"一致） |
| 池 | AudioSource 复用只经 `IZObjectPoolManager`；**使用方永远拿不到 `AudioSource`**（句柄里不暴露） |
| 总线 | **音频模块不订阅任何事件**（UI 音效走 §9.2 的接口方案，不走总线）；可选：音频**发布**事实事件（如 `ZBgmChanged`）供 UI 显示——**v1 不做** |
| 日志 | 构造注入 `IZLogger`；加载失败/池满/参数非法记日志（`PlaySfx` 的失败路径**不抛**，见 §9.1） |
| 组装层 | 注册 `IZAudioManager`（Singleton）；若选方案 ② 再注册 `IZUISfx` 适配；提供场景中的 AudioMixer 资产引用（放 `ZAudioOptions`） |
| UI | 见 §9（音频侧提供能力 + 依赖方向三方案） |

---

## 11. 目录与程序集

```
Assets/Zipper/Audio/
├── Zipper.Audio.asmdef        （引 Core / Pool / Resources / UniTask / VContainer）
├── IZAudioManager.cs / ZAudioManager.cs
├── ZAudioHandle.cs / ZAudioBus.cs / ZAudioPlayOptions.cs / ZAudioOptions.cs
├── ZAudioState.cs             （监控 DTO，照池/总线的形状）
└── Internal/
    ├── PooledAudioSource.cs   （包装组件，实现 IZObjectPoolItem）
    ├── AudioBusChannel.cs     （BGM 双 source 轮转 + 交叉淡入）
    └── ClipCache.cs           （address → AssetHandle<AudioClip>）
```

- 依赖方向：`Audio → Core / Pool / Resources`（**不依赖 UI**）✓ 与 roadmap §4.2 一致（除非选方案 ① 且改成 `Audio → UI` 的反向——**那条不要做**，UI 依赖 Audio 才是自然方向）；
- 命名按 `naming-convention.md`：对外 `Z*`/`IZ*`，内部机制（`PooledAudioSource` / `ClipCache`）不加前缀。

---

## 12. 分阶段实施

| 阶段 | 内容 | 可否独立验收 |
|---|---|---|
| **v1 MVP** | 包装组件 + 运行时原型母体 + 池化 2D/3D 播放 + `PlaySfxAsync`/句柄 + 池化归还 + 失败不抛 + UI 音效最小对接（§9.1 的两条保证） | ✅ 是（§13-1..6） |
| v2 | BGM 双通道交叉淡入 + `AudioMixer` 分组与音量持久化 + `Preload`/`ReleaseAllClips` + `GetState()` | ✅ 是（§13-7..10） |
| v3（按需） | 池满抢占策略、同时播放上限/优先级、`Pause/Resume` 与 `OnApplicationPause` 策略、按"音效 id 表"的批量预热 | — |

---

## 13. 验收标准（可判定）

| # | 用例 | 断言 |
|---|---|---|
| 1 | 2D 音效播放 | `PlaySfx(addr)` 后确实出声（或 `GetState()` 显示在播）；播完自动归还池（池 `CountActive` 归零） |
| 2 | **池复用无泄漏** | 连续播 100 次同一音效 → 在播实例数不超过 `MaxSize`、池 `CountAll` 稳定不增长、无 `MissingReference` 报错 |
| 3 | 3D 音效位置 | 传入位置后声音的衰减与方向正确（`transform.position` 已设置、`spatialBlend = 1`） |
| 4 | 失败不抛 | 错 address / 未预热 → **不抛异常**、记 Error、返回空句柄（UI 点击不崩） |
| 5 | 池满策略 | 超过 `MaxSize` 的并发播放 → 按约定处理（v1 丢弃 + Debug 日志），不抛、不无限增长 |
| 6 | 句柄控制 | `Stop(0.3f)` → 0.3s 内淡出并归还；`Stop` 重复调用幂等；`WaitFinishedAsync` 在播完/被停止后都会完成 |
| 7 | BGM 独占 | `PlayBgm(A)` → `PlayBgm(B, crossfade: 1f)` → A 淡出、B 淡入、**同一时刻只有一个在播**；快速连续切歌不残留旧曲 |
| 8 | 音量分组 | `SetVolume(Sfx, 0)` 后 SFX 组静音、BGM 不受影响；`SetVolume(Master, v)` 影响全部 |
| 9 | 音量持久化 | 设置后重启（模拟）→ 读回相同值；非法值（<0 / >1 / NaN）被夹紧到 [0,1] |
| 10 | 关闭期 | 模块 `Dispose` 后：所有音效停止、池已清、原型已销毁、clip 句柄全部释放（资源簿记归零）、再调 API 不抛（`_disposed` 宽容） |
| 11 | 主线程约定 | 非主线程调播放/音量 → 记 Error 并忽略，不抛、不破坏状态 |

**验收证据形态**：EditMode 测试（`Zipper.Tests`，需加 `Zipper.Audio` 引用；纯逻辑部分如"音量夹紧/dB 换算/选项校验"可完全脱离 Unity 音频硬件测）+ Editor 手工试听清单（带位置的 3D 音效、交叉淡入听感）。

---

## 14. 明确不做（v1）

| 不做 | 理由 |
|---|---|
| DSP 效果链的代码化管理（混响/滤波/快照过渡） | 由 AudioMixer 资产配置，代码只切组与音量 |
| 空间音频高级特性（HRTF / Ambisonic / 遮挡遮蔽） | 毕设 Demo 不需要；将来按需 |
| 节奏/精确同步（`dspTime` 级调度） | 与游戏类型无关，属独立需求 |
| 同时播放数限流、优先级抢占、按距离剔除 | v3 按需（v1 只靠池 `MaxSize` 兜底） |
| 音频资源的导入设置规范（压缩格式/预加载） | 属资源规范，写在使用工程侧 |
| 语音/麦克风 | 不在框架范围 |
| UI 音效的"业务地址表" | 框架不内置业务清单（与资源模块同一纪律）★ |
| 音频模块发布事件（`ZBgmChanged` 等） | v1 不做；UI 要显示 BGM 名时可经总线补（届时按 event-bus-design §8 的两级规则放类型） |

---

## 15. 待决策项（需使用者拍板）

> **D8 见 §9.2**（与 `ui-manager-design.md` §13.2 同一决策，编号统一）；下表为音频稿自身的待决项。

| # | 决策 | 我的建议 | 影响 |
|---|---|---|---|
| **D8** | UI 音效的依赖方向（§9.2 三方案；**与 `ui-manager-design.md` §13.2 同一决策**，编号统一用 D8） | **方案 ②：UI 定义 `IZUISfx` 抽象 + 组装层注入适配** | 决定 roadmap §4.2 依赖链是否要改成 `Audio → UI`；决定 UI 模块可裁剪性 |
| **D2** | tick 驱动：VContainer `ITickable` vs 自带 driver 组件 | **`ITickable`**（不新增常驻 GameObject） | 决定 `Zipper.Audio` 是否引 VContainer |
| **D3** | fade 由谁推进：各自 UniTask 循环 vs tick 统一推进 | **各自 UniTask 循环**（tick 只做"播完检测 + 归还"） | 避免两处驱动同一状态 |
| **D4** | 池满时的策略 | **丢弃 + Debug 日志**（v1） | 影响"音效密集场景"的表现 |
| **C1** | `ZAudioOptions` 字段集（Mixer 资产引用、组名映射、初始/上限 source 数、rolloff 配置） | 先按 §4.2/§7.1 列的最小集 | 影响初始化签名 |
| **C2** | `ZAudioPlayOptions` 字段集 | §4.4 列的五项（Bus/Volume/Pitch/Loop/FadeIn/SpatialBlend） | 影响 API 面 |
| **C3** | `OnApplicationPause` 时是否自动暂停音频 | v1 不自动（由使用方决定）；若做，写进文档 | 影响移动端体验 |

---

## 16. 附录：与其它模块的既有约定对照（防漂移）

| 约定 | 出处 | 本设计如何遵守 |
|---|---|---|
| 凭据式资源（谁签发谁释放） | `resource-manager-design` | `ZAudioHandle` 同风格；**但便捷入口 `PlaySfx` 不暴露凭据**（内部自动归还） |
| 池只管"取/还"，不管资源 | `pool-manager-design` §5.4 | 音频管理器作为"上层"持有 clip 句柄；池只收原型 GameObject |
| "按 address 建池 + 母本托管"由上层组合器承担 | 同上 | **音频管理器对 AudioSource 池就是这个组合器**（先清池 → 再销毁原型） |
| 框架不内置业务键清单 | `resource-manager-design` 键体系 | 音频只收 address；音效清单属使用工程 |
| "要对方做事"用接口方法，不用总线 | `event-bus-design` §2.1 | §9.2 因此**不选**总线方案（方案 ③） |
| 主线程约定 + 关闭期宽容 | `event-bus-design` §4.2/§5.3、`pool-manager-design` | §2.2、§13-10/11 |
| 命名：对外 `Z*`/`IZ*`，内部机制不加前缀 | `naming-convention.md` §4 | §3.2（`PooledAudioSource` / `ClipCache` 不加前缀） |

---

## 17. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.1 | 2026-09-13 | 初稿（使用者要求"音频先行"，因 UI 管理器要绑定 UI 音效）：目标与范围、前置事实（既有契约 / **Unity 音频事实** / 容器事实）、分层与职责（含 tick 驱动的两方案）、播放模型（三类播放 + **`AudioSource` 不能直接池化的关键约束与包装组件方案** + **运行时原型母本、零资源依赖** + 池满语义 + 公开 API）、播放句柄与生命周期表、淡入淡出（unscaled 时间、per-instance 取消令牌、BGM 双 source 交叉淡入）、分组与音量（dB 换算、PlayerPrefs key 约定、启动读回）、BGM 独占通道、**§9 与 UI 的对接（UI 音效：音频侧提供的 API 与两条保证 + 依赖方向三方案对照，推荐方案 ②）**、与资源/池/总线/日志/组装层的边界、目录与程序集、分阶段实施、11 条验收、明确不做 8 项、待决 D8（与 UI 稿同一编号）与 D2–D4、C1–C3、附录（与既有模块约定的防漂移对照） |
