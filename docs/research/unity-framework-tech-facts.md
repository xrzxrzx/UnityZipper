# Unity 框架插件技术事实调研报告

> 调研日期：基于 2024–2025 年公开资料
> 目标工程：Unity 2022.3.62f2c1 LTS + URP 14.0.12，VContainer 1.19.0、UniTask（Assets/Plugins 源码版，含 UniTask.Addressables / UniTask.DOTween / UniTask.TextMeshPro）、uGUI 1.0.0 + TextMeshPro 3.0.7
> 调研范围：为「Zipper.*」框架插件（资源/音频/UI 管理器 + 对象池）提供技术事实依据
> 说明：凡未能在权威来源直接核实的内容，一律标注 **「待核实」**。

---

## 1. DOTS / ECS 版本兼容性（Unity 2022.3 LTS）

### 结论

Unity 2022.3 LTS 上 DOTS 的正式（非 preview）版本线是 **Entities 1.0.x**，其中 **Entities 1.0.16** 是 1.0 线的最后一个稳定版本，也是 2022.3 手册 API 文档收录的版本。配套依赖（UPM 包名与版本）为：

| 包名（UPM） | 推荐版本 | 说明 |
|---|---|---|
| `com.unity.entities` | **1.0.16** | 核心 ECS；2022.3 手册文档化版本 |
| `com.unity.burst` | **1.8.x**（Entities 1.0.16 的 package.json 依赖为 1.8.9，**待核实**精确 patch） | 编译优化 |
| `com.unity.collections` | **2.1.4** | Native 容器；Entities 1.0.16 依赖 |
| `com.unity.mathematics` | **1.2.6** | 数学库 |
| `com.unity.jobs` | **1.1.1** | Job System 扩展 |

- `com.unity.entities.graphics`（Entities Graphics，与 Entities 同版本线 1.0.16）用于 Hybrid 渲染（URP 下用 ECS 渲染时可选）。
- 安装方式：Package Manager 的 **Unity Registry** 中搜索 `Entities` 及其依赖包即可；也可以直接编辑 `manifest.json` 指定版本。
- Entities 1.1.x 及以上主要面向 Unity 2023.1+；**「Entities 1.1.x 是否官方支持 2022.3」待核实**，稳妥做法是 2022.3 一律用 1.0.x 线。

### 关键事实

- Entities 1.0 是 2022.3 上的正式版本，Unity 官方 2022.3 手册包含 `com.unity.entities` 章节（docs.unity3d.com/2022.3/Documentation/Manual/com.unity.entities.html）。
- 1.0 线从 2022.2/2021.3.16f1 起可用，2022.3 LTS 全程兼容（官方手册覆盖到 2022.3）。
- API 文档站点 `com.unity.entities@1.0` 的最新版本即 1.0.16（API 页均标注 1.0.16）。
- **Baking（1.0 的转换系统）**：用 `Baker<TSource>` 取代旧版 `IConvertGameObjectToEntity`；Baking 仅在编辑器导入期执行（SubScene 烘焙），运行时不再转换。注意 Baking 依赖编辑器流程，纯运行时动态创建实体用 `EntityManager.Instantiate` / `CreateEntity`。
- **EntityCommandBuffer（ECB）**：结构性变更（创建/销毁实体、加删组件）必须经 ECB 在主线程系统播放（如 `BeginSimulationEntityCommandBufferSystem` / `EndSimulationEntityCommandBufferSystem`）；job 内用 `EntityCommandBuffer.ParallelWriter`；ECB 播放有延迟（按系统组顺序），注意时序；ECB 播放后自动回收，勿重复使用。
- **MonoBehaviour 与 ECS 混用**：实体不能挂 MonoBehaviour；Hybrid 场景经 `Entities Graphics` 渲染；托管组件（`IManagedComponentData`）1.0 支持但受限（不能进 Burst job、不保证确定性）；GameObject 预制体烘焙后由 `EntityManager.Instantiate` 实例化，多组件 GameObject 需要 `LinkedEntityGroup` 支持整棵层级。
- 已知注意点（社区反馈）：2022.3 上导入 Entities 可能遇到 **API Compatibility Level / .NET 4.x 报错**（CSDN 有专门解决方案，社区源，具体编辑器差异**待核实**）；Baking 缓存、SubScene 边界、IL2CPP 裁剪等为社区常报问题（具体 issue 清单**待核实**）。

### 来源 URL

- https://docs.unity3d.com/2022.3/Documentation/Manual/com.unity.entities.html （Unity 2022.3 手册：Entities）
- https://docs.unity3d.com/Packages/com.unity.entities@1.0/changelog/CHANGELOG.html （Entities 1.0.16 变更日志）
- https://docs.unity3d.com/Packages/com.unity.entities@1.0/api/Unity.Entities.EntityCommandBuffer.html （ECB API）
- https://docs.unity3d.com/Packages/com.unity.collections@2.1/changelog/CHANGELOG.html （Collections 2.1.4）
- https://github.com/needle-mirror/com.unity.entities/releases （版本线镜像，确认 1.0.16 / 1.3.x 存在）
- https://github.com/needle-mirror/com.unity.collections/releases （Collections 版本线）
- https://versionalert.com/unity/package-releases/com.unity.entities/1.0.16 （版本登记页）
- https://raw.githubusercontent.com/Unity-Technologies/EntityComponentSystemSamples/master/EntitiesSamples/Docs/entity-command-buffers.md （官方示例仓库 ECB 说明）
- https://blog.csdn.net/dengdun6257/article/details/102283413 （2022.3 导入 Entities 1.0 的 .NET 4.x 报错方案，社区源）

---

## 2. ECS 对象池（实体复用）实现模式

### 结论

ECS 世界没有「GameObject 对象池」的直接对应物，主流做法分四类，按「性能/复杂度」阶梯排列：

1. **预分配 Archetype + NativeQueue<Entity> 空闲复用（推荐，高频场景）**：`EntityManager.Instantiate(prefab)` 批量预创建一批实体（或 `CreateEntity`），把 Entity 存入 `NativeQueue<Entity>`；获取时 Dequeue 并置为活跃，归还时 Enqueue 并禁用。零结构性变更（仅改组件数据/Enableable 标记），性能最好。
   - 适用：子弹、粒子、单位等高频生成/销毁、数量有上限的场景。
   - 坑：必须防「重复入池」（同一 Entity 被归还两次会污染队列，用 `IEnableableComponent` 的 Enabled 状态判断）；队列与 World/System 生命周期绑定（World 销毁时 NativeQueue 要 Dispose）；预分配量即上限，超出需扩容策略。
2. **ECB 延迟生成/销毁（简单安全）**：System 内用 `EntityCommandBuffer.Instantiate` / `DestroyEntity` 批量排队，由 ECBSystem 播放。
   - 适用：频率不高、结构简单、不需要极致性能的场景；也用于「按帧批量」的自然合批。
   - 坑：播放延迟一帧/按系统顺序，取到实体不是即时的；频繁结构性变更会造成 archetype 碎片与重排成本；ParallelWriter 的写入顺序不保证。
3. **ComponentData 标记复用（固定上限）**：实体常驻，用「alive/in-use」标记位区分空闲/活跃，System 每帧按标记过滤处理。
   - 适用：弹幕、网格实例等**固定上限**、需要「全部常驻」语义的场景。
   - 坑：内存不释放、每帧仍要遍历过滤；但完全避免分配与结构性变更，Chunk 数据连续性好。
4. **官方示例模式**：Unity 官方 EntitiesSamples 主要演示「prefab 烘焙 + EntityManager.Instantiate + ECB 销毁」（如 SpawnAndRemove），社区在此基础上叠加 NativeQueue 复用；官方暂无「正式对象池」模块，需自建。

**框架建议**：普通 ECS 池用「模式 1 + 模式 2 组合」——预分配 + NativeQueue 复用，销毁时走 ECB；上限可控时用模式 3。

### 关键事实

- ECS 官方社区对「对象池是否必要」的讨论结论：ECS 下「创建实体」本身就是轻量操作（结构变更才是重操作），**若创建/销毁频率极高或要避免结构性变更，才需要池化**（Unity Discussions #754627）。
- `EntityManager.Instantiate` 是 1.0 的正式实例化 API；官方文档明确结构性变更必须走 ECB 或在主线程安全点执行。

### 来源 URL

- https://discussions.unity.com/t/is-object-pool-a-valid-optimization-pattern-in-ecs/754627 （ECS 对象池是否有效的官方论坛讨论）
- https://github.com/Unity-Technologies/EntityComponentSystemSamples （官方示例仓库，含 SpawnAndRemove 等）
- https://raw.githubusercontent.com/Unity-Technologies/EntityComponentSystemSamples/master/EntitiesSamples/Docs/entity-command-buffers.md （官方 ECB 指南）
- https://docs.unity3d.com/Packages/com.unity.entities@1.0/api/Unity.Entities.EntityCommandBuffer.html （ECB API）

---

## 3. Addressables

### 结论

- **2022.3 上的推荐版本线是 1.x**：`com.unity.addressables` 的 1.21.x（已确认存在 1.21.21 变更日志）与 1.22.x（已确认存在 1.22.3 变更日志）。**「Unity 官方对 2022.3 给出的确切推荐 patch 号」待核实**；实践中 2022.3 + Addressables 1.21/1.22 均被广泛使用。
- **Addressables 2.0/2.1** 是面向 Unity 6（6000.x）的新一代（2024 年社区讨论热度高）；**「2.x 是否支持 2022.3」待核实**，2022.3 工程不建议冒险上 2.x。
- **UniTask.Addressables**：用户工程 Assets/Plugins 源码版 UniTask 自带该扩展程序集，提供 `AsyncOperationHandle` / `AssetReference` 的 **`ToUniTask()`** 扩展（含 CancellationToken 重载），可 `await` 加载并直接拿资产。若未来升级 Addressables 2.x，需确认 UniTask.Addressables 是否跟进其句柄 API（**待核实**）。
- **Addressables vs Resources 取舍**（框架插件内资源管理器选型）：
  - 热更：Addressables 支持远程内容、Catalog、内容构建管线（Content Build）；Resources 完全不可热更。
  - 内存：Addressables 引用计数管理 + 释放未用资产（`Addressables.Release` / 自动卸载）；Resources 常驻内存。
  - 初始化成本：Addressables 首次初始化需加载 catalog（有一次性成本，可预初始化）；Resources 零成本但代价是内存常驻。
  - 构建集成：Addressables 需 Addressables Settings、Group、构建脚本与 CI 配合；Resources 零配置。
  - 结论：框架资源管理器以 **Addressables 为核心**（热更、内存友好），Resources 仅作内置兜底/首屏资源。

### 关键事实

- Addressables 1.22.3 变更日志页与 1.21.21 变更日志页均存在，证明 1.x 线仍在维护。
- UniTask GitHub issue #111 讨论了「Addressables 同步加载」（`WaitForCompletion`），属于边界用法；主流用法仍是异步 `ToUniTask()`。

### 来源 URL

- https://docs.unity3d.com/Packages/com.unity.addressables@1.22/changelog/CHANGELOG.html （1.22.3）
- https://docs.unity3d.com/Packages/com.unity.addressables@1.21/changelog/CHANGELOG.html （1.21.21）
- https://docs.unity3d.com/Manual/com.unity.addressables.html （Addressables 官方手册）
- https://github.com/Cysharp/UniTask/issues/111 （同步加载讨论）
- https://discussions.unity.com/t/addressables-2-0-2-1-rundown-of-main-benefits/947850 （2.x 讨论）

---

## 4. UI 方案取舍：uGUI vs UI Toolkit（2022.3 时代）

### 结论

- **官方立场（2022.3 手册《Comparison of UI systems in Unity》）**：uGUI 是久经考验、生态成熟的运行时 UI 方案，适合复杂游戏 UI；UI Toolkit 是官方力推的下一代 UI 系统（编辑器 UI 已默认使用），运行时 UI 自 2021.2 引入 Runtime Panel，**2022.3 已可用但成熟度仍不及 uGUI**。
- 对「游戏运行时 UI 管理器（面板栈、生命周期、层级管理）」：**uGUI 是 2022.3 时代的稳妥主流选择**。uGUI 的 Canvas + sortingOrder/overrideSorting 层级体系成熟，面板栈/生命周期框架有大量社区参考（也便于自研）。
- UI Toolkit Runtime UI 在 2022.3 的成熟度：支持 UIDocument + PanelSettings（屏幕空间/世界空间）、UXML/USS、UI Builder、运行时事件、基础数据绑定；**但**游戏运行时场景下仍有关键短板（社区 2024 年讨论：部分 uGUI 特性缺失、性能与调试体验、生态与第三方组件少、异形屏/遮罩等适配，**具体缺失功能清单待核实**）。
- 结论：框架插件的 UI 管理器（Zipper.UI）**首选 uGUI**；UI Toolkit 可平行保留为「HUD/工具型 UI」的备选方案，不建议在 2022.3 上把运行时 UI 管理器的地基建在 UI Toolkit 上。

### 关键事实

- 2022.3 官方手册有专门的 UI 系统对比页（UI-system-compare.html），明确给出各系统定位。
- 官方提供「Migrate from uGUI to UI Toolkit」迁移指南，说明两套系统并存且迁移路径存在。

### 来源 URL

- https://docs.unity3d.com/2022.3/Documentation/Manual/UI-system-compare.html （2022.3 UI 系统对比）
- https://docs.unity3d.com/Manual/UIE-Transitioning-From-UGUI.html （uGUI→UI Toolkit 迁移指南）
- https://discussions.unity.com/t/state-of-ui-toolkit-runtime/943269 （社区对 UI Toolkit Runtime 状态的讨论）

---

## 5. 对象池对比：UnityEngine.Pool 内置池 vs 手写池

### 结论

- **内置池**（`UnityEngine.Pool`，2021+ 正式引入，官方手册有专门章节）：
  - `ObjectPool<T>`：核心实现，支持 `createFunc`、`actionOnGet`、`actionOnRelease`、`actionOnDestroy`、`collectionCheck`（断言防重复归还）、`maxSize`（**超出时释放的对象直接销毁**）。
  - `GenericPool<T>`：ObjectPool 的静态默认实例封装（随手可用）。
  - `CollectionPool<TCollection,TItem>`：供 `ListPool<T>`、`HashSetPool<T>`、`DictionaryPool<TKey,TValue>` 等集合池使用（临时列表/字典缓存首选）。
  - 另有 `LinkedPool<T>`、`UnsafeGenericPool<T>` 等变体。
  - 优点：官方维护、零依赖、struct 实现轻量、GC 友好；缺点：**无预热（warmup）API、无统计、不感知 GameObject/prefab、超容量即销毁而非可配置策略**。
- **手写池的常见增强需求**（框架场景）：预热（initialSize 预创建）、最大容量（严格模式可抛异常）、统计（CountAll/CountActive/CountInactive、创建次数）、自动回收（超时/场景切换自动归还）、按 prefab 绑定实例化、批量获取/归还、统一清理（Clear 销毁全部）。
- **结论**：
  - 临时小对象/集合缓存 → 直接用内置 `CollectionPool`/`ObjectPool`，不要重复造轮子。
  - GameObject 池（含 prefab、生命周期回调、预热、统计）→ 手写池更有价值（内置池不感知 prefab、无预热、超容量销毁语义与游戏需求不匹配）；也可以在保留现有手写池外部 API 的前提下，内部改用对内置池的包装以降低维护成本（可选）。
  - 两者可共存：内置池管「瞬时对象」，手写池管「GameObject 生命周期对象」。

### 关键事实

- 官方手册「Pooling and reusing objects」明确把池化列为性能优化手段，内置 `UnityEngine.Pool` 是官方推荐实现。
- `ObjectPool<T>` 的 `maxSize` 语义是「超出时销毁」，与游戏框架常见「严格上限 + 报错/扩容」语义不同——这是选型时最容易踩的点。

### 来源 URL

- https://docs.unity3d.com/Manual/performance-reusable-code.html （官方手册：池化与复用）
- https://docs.unity3d.com/ScriptReference/Pool.ObjectPool_1.html （ObjectPool<T> API）
- https://docs.unity3d.com/ScriptReference/Pool.GenericPool_1.html （GenericPool<T> API）
- https://docs.unity3d.com/ScriptReference/Pool.CollectionPool_2.html （CollectionPool API）

---

## 6. VContainer + UniTask 集成

### 结论

- **async 生命周期**：VContainer 官方支持 UniTask 异步生命周期。`RegisterEntryPoint<T>()` 后可用 `InitializeAsync()` / `PostInitializeAsync()` / `StartAsync()` / `PostStartAsync()` 等注册异步入口；入口方法可注入 **`CancellationToken`**（LifetimeScope 销毁时自动取消）。
- **异步工厂**：`RegisterAsync` / `AsyncLazy<T>` 支持延迟异步创建依赖（注入 `ILazy<T>` 首次访问才执行）。
- **PlayerLoop 集成**：VContainer 的 EntryPoint 由它安装进 Unity PlayerLoop 的 PlayerLoopSystem 驱动（Initialization/EarlyUpdate/Update/LateUpdate 等阶段，**精确阶段名与可配置性待核实**）；UniTask 依赖自身的 PlayerLoopHelper（自动初始化）。两者叠加时注意：阶段顺序（VContainer 的 loop 与 UniTask 的 loop 相互独立）、避免在 async 生命周期中阻塞主线程（死锁风险）、CancellationToken 全程透传。
- **版本**：VContainer 1.19.0（用户已装）支持 UniTask 异步集成；**「支持 async 生命周期所需的最低 VContainer 版本」待核实**，1.10+ 一般即可，1.19.0 无此顾虑。
- **注意事项**：
  - async 生命周期方法内不要做阻塞等待（`WaitForCompletion` 等），统一用 UniTask await；
  - 注入的 CancellationToken 是「Scope 级」取消信号，可用于资源加载、协程替代等；
  - 若工程自定义 PlayerLoop（如重排 PlayerLoopSystem），需确认 VContainer 的 loop 与 UniTask 的 loop 都仍在执行。

### 关键事实

- VContainer 官方文档有专门的「VContainer + UniTask」集成页，涵盖 CancellationToken 注入与异步入口点。
- VContainer 与 UniTask 的集成是官方一等公民（非第三方 hack），DeepWiki 等镜像亦有记录。

### 来源 URL

- https://vcontainer.hadashikick.jp/integrations/unitask （VContainer 官方：UniTask 集成）
- https://vcontainer.hadashikick.jp/integrations/entrypoint （VContainer 官方：入口点）
- https://github.com/hadashiA/VContainer （官方仓库，README 与文档源）
- https://deepwiki.com/hadashiA/VContainer/6.2-unitask-integration （文档镜像）

---

## 7. UPM 包结构与内嵌开发做法

> ⚠️ **本节已被取代（标注于 2026-09-06）**：这里调研的是 **UPM 包形态**方案。`docs/planning/technical-roadmap.md` v0.3 已决定改用 **.unitypackage 右键导出、拆箱即用**、放弃 UPM 包形态；v0.4 进一步标注了"UPM 依赖与随包自包含的冲突待决（暂缓）"。本节内容仅作技术背景保留，**不作为实施依据**。

### 结论

- **标准 UPM 包目录规范**（官方手册「Package layout」，2022.3 同样适用）：

```
com.zipper.core/
├── package.json          # 必需：name/version/displayName/description/unity/dependencies/author
├── README.md             # 惯例
├── CHANGELOG.md          # 惯例
├── LICENSE.md            # 惯例
├── Runtime/              # 运行时代码
│   ├── Zipper.Core.asmdef
│   └── ...
├── Editor/               # 编辑器代码（引用 Runtime 程序集）
│   ├── Zipper.Core.Editor.asmdef
│   └── ...
├── Documentation~        # ~ 后缀目录：不进入 Asset Database
├── Samples~              # 示例，~ 后缀同上
├── Tests~                # 测试
│   └── Zipper.Core.Tests.asmdef（+ .asmref 注册测试程序集）
└── .gitignore / .npmignore
```

  - 要点：`package.json` 的 `name` 必须是 `com.zipper.*` 形式；`Runtime/` 与 `Editor/` 各自 asmdef 并隔离；`Documentation~`/`Samples~`/`Tests~` 用 `~` 后缀避免被当作资源导入；测试用 `.asmref` 把测试脚本关联到测试 asmdef。
- **工程内嵌入式开发推荐做法**（com.zipper.* 插件开发）：
  - **方式 A：`Packages/` 内嵌（embedded package，推荐单工程）**——把 `com.zipper.core` 直接放进工程 `Packages/` 目录，Unity 自动识别为 embedded package，`manifest.json` 无需显式声明（Unity 自动加 "embedded" 记录）。随仓库版本控制最直接，无需处理路径。
  - **方式 B：`file:` 本地引用**——`manifest.json` 写 `"com.zipper.core": "file:../Packages/com.zipper.core"`（相对工程根）或 `file:xxx.tgz`。包可放仓库外，便于多工程复用；改代码后 Unity 自动重导入。
  - **方式 C：Git URL + tag**——包推到远端仓库后以 `https://...git#tag` 引用，适合发布版本。
  - 结论：框架插件开发期用 **A（内嵌）** 最快；需要多工程共享时切 **B**；对外发布走 **C + UPM 规范**。2022.3 上 A/B 均无已知坑；2023.2 起 Package Manager 对本地包解析行为有变化（社区讨论），与本工程无关。

### 关键事实

- 官方手册「Package layout」明确 asmdef 分离、`~` 后缀目录、package.json 字段要求。
- 官方手册「Local folder or tarball paths」（upm-localpath）明确 `file:` 引用语法与相对路径规则。
- 社区讨论确认 2023.2 后 Package Manager 行为变化（与本工程 2022.3 无关）。

### 来源 URL

- https://docs.unity3d.com/Manual/cus-layout.html （官方：UPM 包布局）
- https://docs.unity3d.com/Manual/upm-localpath.html （官方：本地路径/tarball 引用）
- https://docs.unity3d.com/Manual/upm-embed.html （官方：embedded package，**待核实**精确 URL，若失效以 cus-layout 为准）
- https://discussions.unity.com/t/package-manager-behavior-change-in-unity-2023-2-0/933917 （2023.2 PM 行为变化）

---

## 与现有代码的关系（Assets\Script\Zipper\Pool\ObjectPool.cs）

> 本小节基于对现有 `Assets\Script\Zipper\Pool\ObjectPool.cs`（212 行）的实际阅读评估。

### 现有实现画像

- 泛型 `ObjectPool<T> where T : Component, IObjectPoolItem`，**prefab 绑定 + Component 实例化**（`Object.Instantiate(prefab)`）；
- 数据结构：`Queue<T>` 空闲队列 + 三个 `HashSet<T>`（inactiveItems 快速判重 / allItems 归属校验 / clearPendingItems 清理中语义）；
- 生命周期：`IObjectPoolItem` 接口（OnInitialize/OnGet/OnReturn/OnClear）+ 四个委托回调（InitializeAction/GetAction/ReturnAction/ClearAction），触发顺序「先接口方法、后委托」；
- 增强能力：预热 `initialSize`、严格 `maxSize`（超限抛异常）、批量获取 `GetItemsByCount`、`Clear()`（活跃对象标记 pending，归还即销毁）、统计（CountAll/CountActive/CountInactive + totalCount）。

### 与 UnityEngine.Pool 的关系

| 能力 | 现有手写池 | UnityEngine.Pool.ObjectPool<T> |
|---|---|---|
| prefab 绑定实例化 | ✅ 内置 | ❌ 需自己写 createFunc |
| 预热（initialSize） | ✅ | ❌ 无 |
| 严格最大容量（抛异常） | ✅ | ⚠️ 超限销毁（语义不同） |
| 生命周期接口 + 委托回调 | ✅ 双通道 | ⚠️ 仅委托（actionOnGet 等） |
| 批量获取 | ✅ | ❌ |
| 统计 | ✅ | ⚠️ 仅 CountAll/Active/Inactive |
| 快速判重（防重复归还） | ✅ HashSet | ⚠️ collectionCheck 断言（仅调试） |
| 轻量/官方维护 | ❌ 自维护 | ✅ |

**结论**：现有池已经实现了框架级 GameObject 池所需的绝大部分增强（预热/严格容量/统计/批量/清理语义），这些正是内置池的短板；内置池的价值在于轻量与官方维护。因此：
1. **保留现有手写池作为 GameObject 池是正确的**（它是「按 prefab 的 Component 池」，与框架的资源/音频/UI 管理器场景匹配）；
2. 临时对象与集合缓存（临时 List、字典、可回收小对象）直接用内置 `CollectionPool`/`ObjectPool`，不必扩展现有池去覆盖；
3. 可选优化：现有池内部实现（Queue+HashSet+委托链）可以重构为「包装 UnityEngine.Pool.ObjectPool<T> + 外层预热/统计/生命周期」，减少自维护面——但这是纯内部重构，收益有限，非必须。

### 与 ECS 池的关系

- 两者**不在同一维度**：现有池解决 MonoBehaviour/GameObject 世界（uGUI 面板、特效、音频对象、URP 下的行为对象）的复用；ECS 池解决实体世界高频生成/销毁（大量单位、弹幕），且复用手段完全不同（NativeQueue<Entity> + Enableable 标记 vs 实例化/销毁 GameObject）。
- 框架中应**两者并存**：`Zipper.Pool` 提供普通对象池（现有实现），另设 ECS 池模块（预分配 + NativeQueue + ECB 组合，见第 2 节）；对外可抽象统一接口（如 `IPool<T>` 与 `IEntityPool`），内部实现各自独立，避免把 ECS 池硬塞进 GameObject 池的委托回调模型。

---

## 待核实项汇总

1. Entities 1.0.16 的 package.json 中 Burst 依赖的精确 patch（1.8.9 为记忆值，可能为 1.8.8/1.8.9）。
2. Entities 1.1.x 在 Unity 2022.3 上的官方支持性（最低编辑器版本要求）。
3. Entities 1.0 对工程 API Compatibility Level（.NET Standard 2.1 vs .NET Framework 4.x）的精确要求。
4. Addressables 2.0/2.1 的最低编辑器版本（是否支持 2022.3）。
5. Unity 官方对 2022.3 上 Addressables 的确切「推荐」patch 号（1.21.x 与 1.22.x 的官方口径）。
6. UI Toolkit Runtime UI 在 2022.3 缺失的具体功能清单（社区列举项，如遮罩/异形屏/性能细节）。
7. VContainer 支持 UniTask async 生命周期所需的最低版本号。
8. 2022.3 + Entities 1.0.16 的已知问题清单（Baking 缓存、SubScene 边界、IL2CPP 裁剪等具体 issue）。
9. 官方 embedded package 手册页（upm-embed）的精确 URL。
