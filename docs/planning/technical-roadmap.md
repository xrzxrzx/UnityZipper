# Zipper Unity 框架 — 技术路线规划（v0.8 待审批）

> 状态：**v0.8 草稿，待审批**
> 定位：本文件是框架开发的总体技术路线，回答「做什么、怎么做、按什么顺序做」；**不含代码实现**。
> 依据：协调者规范（先文档、后编码；无批准、不实施）；`docs/standards/agent-role.md`（AI 只做设计，代码由使用者实现）。
> 变更记录：v0.2 融入调研结论；v0.3 定为 **.unitypackage 右键导出、拆箱即用**（放弃 UPM 包形态）；**v0.4（2026-09-06）** 同步近期现实与决策——现状盘点更新、资源管理器键体系改为 address/label、Core 定为"零框架依赖"例外、新增事件总线决策、新增 Core 基础设施一节、里程碑状态刷新；**v0.5** 事件总线改**双实现**（自研 + R3 封装，R3 只负责 UI 通知）；**v0.6** 事件总线命名统一框架约定（`IZEventBus` / `ZEventBus` / `ZR3EventBus`，见命名规范）；**v0.7（2026-09-06）** 事件总线由双实现**收敛为单实现 + R3 桥**（删除 `ZR3EventBus`，R3 只做响应式流与订阅侧操作符加工）；**分发相关（UPM 依赖与"随包自包含"的冲突）按使用者决定暂缓，待其另行决策**。

---

## 1. 背景与目标

- 在 Unity 2022.3 LTS 工程中自研一套自用游戏框架插件，覆盖：**基础设施（Core）、资源管理器、音频管理器、UI 管理器、对象池（普通 + ECS）**。
- 定位：**自用为主**，服务于 2027 年 4-6 月毕业设计答辩演示，因此**开发速度优先于完备性**，但需保证架构可讲、可演示、可扩展。
- **分发形态（v0.3 已定，v0.4 维持）**：**右键导出 .unitypackage、导入拆箱即用**——目标工程拿到包双击导入即可跑。
  > ⚠️ **待决（暂缓）**：该目标要求"依赖随包自包含"，但当前 UniTask / VContainer / Addressables 均为 UPM（其中 UniTask 为 git 源），**UPM/git 依赖无法进 .unitypackage**。使用者已决定：**现阶段设计不考虑分发**，分发方案（回 vendor 或列前置依赖）留待后续单独决策（见 §6、§9）。

## 2. 现状盘点（2026-09-06）

| 项 | 现状 | 影响 |
|---|---|---|
| Unity 版本 | 2022.3.62f2c1（LTS） | 支持 Entities 1.0.x、Addressables 1.x |
| 渲染管线 | URP 14.0.12 | 无特殊限制 |
| UI | uGUI 1.0.0 + TextMeshPro 3.0.7 | UI 方案定为 uGUI |
| DI | VContainer 1.19.0（UPM 包，位于 Packages/） | 容器装配可用；是否 vendor 随分发决策（暂缓） |
| 异步 | **UniTask 2.5.11（UPM git 源）** | 已由 vendor 源码版切换；UPM 包内自带 Addressables 扩展程序集 |
| 绑定层 | **R3 1.3.1 已引入**：核心库 `R3.dll`（NuGet 包，置于 `Assets/Packages/`）+ Unity 集成 UPM git 包 `com.cysharp.r3` | 只做**响应式流**，不承担事件路由（v0.7）；上手笔记见本地 `LocalNotes/r3-tutorial.md`（不入库） |
| 资源层 | **Addressables 1.22.3（已安装）** | 资源管理器底层就绪 |
| DOTS | 未安装 | ECS 对象池的前置依赖，M4 再引 |
| 已有代码 | `Assets/Zipper/{Core,DI,Pool,Resources}`（asmdef 已建；Resources 含 Asset 句柄三件套） | 骨架已落地，M0 迁移已完成 |
| 工程治理 | git 已 init 并推送远端仓库 `xrzxrzx/UnityZipper`；`docs/` 已建（planning/research/architecture/standards/memory） | M0 治理部分完成；AGENTS.d 与 C# 编码规范仍缺 |
| 设计文档 | 资源管理器设计稿 v0.3、Core 设计稿 v0.7 | 供实现参照（只含思路级伪代码） |

## 3. 技术选型总览

| 决策点 | 结论 | 备注 |
|---|---|---|
| 分发形态 | .unitypackage 右键导出、拆箱即用 | v0.3 定；依赖形态待决（暂缓） |
| 依赖策略 | 目标：第三方依赖随包自包含 | **当前现实为 UPM 依赖，冲突待决（暂缓）** |
| 资源加载 | **Addressables 1.22.3**（+ UniTask.Addressables） | 已装 |
| **资源键体系** | **框架公共 API 只收 address / label（Addressables 原生 key）** | v0.4 修订：原「枚举键约定」方案**已废弃**；业务键清单归使用工程 |
| ECS 深度 | 完整 DOTS（Entities + Burst + Jobs + Collections） | M4 |
| UI 体系 / 架构 | uGUI + TMP；**MVVM**，绑定层用 R3 1.3.1（**已引入**） | R3 = 流加工，不是第二条总线 |
| 依赖注入 | VContainer | 模块经容器装配 |
| 异步 | UniTask（对外接口统一） | 已切 UPM |
| 对象池（普通） | 自研 `ZObjectPool<T>` 演进 | 已有骨架 |
| **事件总线** | **单实现**：`IZEventBus` 接口 + `ZEventBus`（均在 Core，零依赖，容器注册为 Scope 单例）；**R3 桥**（`AsObservable` 扩展，依赖 R3 的程序集，不进 Core）只做订阅侧操作符加工 | v0.7 定案（v0.5 双实现已废弃）：**事件路由统一走总线**，不再按 UI/非 UI 分两条总线；**同一件事只发布一次**，需要节流/合并的一方在订阅侧（R3 桥）加工 |
| 命名空间 | `Zipper.*` | 与现状一致 |

**版本基线**：UniTask 2.5.11（UPM git）、Addressables 1.22.3、VContainer 1.19.0；DOTS 相关（Entities 1.0.16 等）待 M4 以 Package Manager 实际解析为准。

## 4. 架构与程序集

### 4.1 设计原则

1. **程序集级解耦**：每模块独立 asmdef、依赖单向、可裁剪。
2. **容器装配**：模块实例由 VContainer 组装注入。
3. **异步统一**：对外异步接口一律 UniTask，支持 CancellationToken。
4. **依赖倒置**：模块面向接口编程（UI/音频只依赖资源管理器接口）。
5. **地基零框架依赖（v0.4 新增）**：`Zipper.Core` 只依赖 Unity 引擎，不引 VContainer / UniTask / Addressables / R3——保证任何层都能引用它而不被迫传递依赖。
6. **先核心后外围**：Core、对象池、资源为地基，UI/音频为上层，ECS 池独立。

### 4.2 程序集依赖

```
Zipper.Core          — 基础设施：日志系统、事件总线、断言、扩展、工具   （零框架依赖）
Zipper.Pool          — 普通对象池（ZObjectPool<T> 演进）                依赖 Core
Zipper.Resources     — 资源管理器（Addressables 封装）                  依赖 Core
Zipper.Audio         — 音频管理器                                      依赖 Core/Pool/Resources
Zipper.UI            — UI 管理器（uGUI + MVVM/R3）                     依赖 Core/Pool/Resources
Zipper.ECS.Pool      — ECS 对象池（完整 DOTS）                         依赖 Core（独立）
Zipper.Runtime       — 组装层：VContainer Scope、引导                   依赖以上全部
Zipper.Editor        — 编辑器扩展、校验                                 仅 Editor 平台
```

- 单向依赖：`Core → Pool → Resources → {Audio, UI}`；`ECS.Pool` 自成一体。
- **asmdef 引用要求（v0.4 修订）**：
  - `Zipper.Core`：**references 为空**（只依赖引擎）
  - 其余 Zipper.*：按各自需要显式引用（UniTask / VContainer / 官方包程序集）；`Zipper.Resources` 需引 **UniTask、Unity.Addressables、Unity.ResourceManager、UniTask.Addressables**（**asmdef 引用不传递**，缺一即编译期报类型不可见）
  - Post-v0.4 修订：原「所有 Zipper.* 都引 UniTask/VContainer」不再成立（Core 例外）

### 4.3 目录布局（现状）

```
Assets/Zipper/
├── Core/          Zipper.Core.asmdef（空，待实现日志系统与事件总线）
├── DI/            GameLifetimeScope + ResourcesBootstrapper（Assembly-CSharp，暂未程序集化）
├── Pool/          Zipper.Pool.asmdef（ZObjectPool<T> + IZObjectPoolItem）
└── Resources/     Zipper.Resources.asmdef
     └── Asset/    句柄基类 / 泛型句柄 / 预制体母本持有者
```

- 待补：`Audio/`、`UI/`、`ECS.Pool/`、`Runtime/`（组装层程序集）、`Editor/`、`Samples/`；`DI/` 内容未来并入 `Runtime/`。
- 第三方依赖当前位于 UPM（不在 Assets/），与"随包导出"目标的冲突见 §6。

### 4.4 程序集创建与配置指引（操作规范）

- **何时需要 asmdef**：需要按编译边界引用、控制依赖、区分 Editor/运行时时。
- **创建步骤**：目录右键 → Create → Assembly Definition；Inspector 中确认程序集名与文件名一致、按 §4.2 添加 References（禁止循环引用）、运行时程序集保持 Any Platform、Editor 专用程序集只勾 Editor、Auto Referenced 默认开。
- **常见坑**：引用官方包程序集前需确认包已安装；asmdef 目录不可嵌套；改 asmdef 名会连带改所有引用它的 GUID。

## 5. 各模块技术路线

### 5.1 资源管理器（Zipper.Resources）

- **底层**：Addressables 1.22.3；**接入**：UniTask.Addressables 的 `ToUniTask()`。
- **键体系（v0.4 修订）**：公共 API **只收 address / label**；框架不内置业务资源清单（枚举键方案废弃）；业务键常量归使用工程；"业务键 → address 解析器"列为后续演进。
- **对外能力**：异步加载/释放（返回**句柄凭证**）、预制体母本加载（供对象池保活）、一次性实例化、预加载/预热、加载失败事件、兜底清理。
- **簿记纪律**：引用计数依托 Addressables 原生，封装层**只记账不计数**；句柄释放语义唯一（`Dispose`：销号 + 释放内层，幂等）；不提供"按地址一键释放"。
- 生命周期：随 VContainer Scope 创建/释放；支持全局取消。
- 验收标准：加载/释放正确性、并发重复加载去重、取消不泄漏、池化路径顺序正确（详见设计稿 §9）。
- 设计稿：`docs/architecture/resource-manager-design.md`（v0.3）。

### 5.2 音频管理器（Zipper.Audio）

- **底层**：原生 AudioSource + AudioMixer；AudioSource 走普通对象池。
- 对外能力：2D/3D 音效与 BGM 三类播放接口；播放句柄（停止/暂停/续播）；淡入淡出（UniTask 驱动）；AudioMixer 分组与音量持久化；BGM 独占通道。
- 验收标准：播放/停止/淡入淡出正确性、AudioSource 池复用无泄漏、音量持久化生效。

### 5.3 UI 管理器（Zipper.UI）— 重点模块，uGUI + MVVM

- **面板管理**：面板栈（Open/Close/Back）；层级（背景/主界面/弹窗/提示/加载）；遮罩与排序；生命周期异步化；View 关闭不销毁、归还对象池、再开重绑。
- **MVVM**：View（无业务逻辑）/ ViewModel（纯 C#，R3 属性与命令，可单测）/ 绑定层（R3 驱动，含池化复用的解绑重绑协议）。
- **加载**：面板预制体经资源管理器异步加载（按 address）；ViewModel 由容器构造。
- **MVP 边界**：先「面板栈 + 生命周期 + 基础绑定」。
- 验收标准：面板栈正确、View 复用无绑定泄漏、ViewModel 单测通过、设置面板 Demo 走通全链路。

### 5.4 对象池

**5.4.1 普通对象池（Zipper.Pool）**
- 现有 `ZObjectPool<T>`（要求 `T : Component, IZObjectPoolItem`，自归还委托 + OnInitialize/OnGet/OnReturn/OnClear）演进：预热、容量上限与超限策略、统计、显隐策略、与资源管理器母本对接。
- 已知改进点：归还流程的"先回调后标记"存在自归还重入风险（建议先标记后回调）；显隐不代管需实现者自觉。
- 与内置池关系：临时集合/轻量复用用内置 `CollectionPool`/`GenericPool`；框架级 GameObject 池自研。

**5.4.2 ECS 对象池（Zipper.ECS.Pool）— 完整 DOTS**
- 硬约束：结构变更走 ECB 且主线程播放；实体不挂 MonoBehaviour；Baking 走 Baker。
- 设计要点：按 Archetype 批量预分配；空闲实体入 NativeQueue（防重复入池）；池系统与使用侧系统分离；热路径 Burst+Job；池统计。
- 验收标准：万级实体稳定；与普通池性能对比数据；Burst 编译通过。

### 5.5 组装层（Zipper.Runtime）

- VContainer Scope 注册全部 Manager / ViewModel / 池；async 生命周期（`IAsyncStartable` 等）与 CancellationToken 注入；包内"一键引导"组件；场景切换策略由 Scope 层级控制。
- **现状**：`GameLifetimeScope` 已注册 `IZResourceManager→ZResourceManager`（单例）与 `ResourcesBootstrapper`（入口点）；日志系统初始化将来在此接入。

### 5.6 基础设施（Zipper.Core）— v0.4 新增

- **日志系统**：分级（Trace…Fatal）、模块 tag、编译期剥离（Release 剥离 Info 及以下）、运行时级别控制、Console + 文件双输出、线程安全。
- **事件总线（单实现 + R3 桥，v0.7）**：`IZEventBus` 接口 + `ZEventBus`（唯一实现，均在 Core）；**R3 在本框架只负责 UI 响应式流**（绑定、面板与 ViewModel 通知），事件路由统一走总线；需要操作符的场景在**订阅侧**经 R3 桥（`AsObservable` 扩展，不进 Core）加工——**不新建第二条总线实现**。边界纪律：只用于"跨模块低频通知"，不用于请求-响应、状态查询、高频数据流（后者走 R3 流或直接引用）；同一件事只发布一次。
- 其它：`ZAssert` 断言、按需扩展方法、版本常量。
- 设计稿：`docs/architecture/core-design.md`（v0.7）。

## 6. 打包与分发（目标不变，依赖形态待决）

- **目标（v0.3 已定）**：右键 **Export Package** 导出 `Assets/` 下的框架内容（含 asmdef 与 LICENSE），导入目标工程即用；Sample 场景做分发验证。
- **前置依赖**：官方 UPM 包（Addressables；DOTS 于 M4）无法随包，需目标工程自行安装——此点与"拆箱即用"的张力已知。
- **待决（暂缓）**：UniTask / VContainer 当前为 UPM（UniTask 为 git 源），同样无法随 .unitypackage。**使用者决定现阶段设计不考虑分发**，方案（回 vendor 源码 / 列为前置依赖）留待后续。
- 分发验证流程（将来执行）：干净测试工程 → 装前置依赖 → 导入包 → 跑 Sample。

## 7. 工程治理

| 项 | 状态 |
|---|---|
| git 仓库 | ✅ 已 init 并推送 `xrzxrzx/UnityZipper`（白名单只跟踪 `Assets/Zipper` + `docs`） |
| 提交规范 | ✅ `docs/standards/git-workflow.md`（Conventional Commits；禁 rebase/force-push） |
| AI 协作分工 | ✅ `docs/standards/agent-role.md`（AI 只做设计与思路级伪代码，代码由使用者实现） |
| 文档体系 | ✅ `docs/{planning,research,architecture,standards,memory}` 已建 |
| AGENTS.d/ | ⬜ 未落地（协调者规范体系） |
| C# 编码规范 | ⬜ 未编写（无规范不编码） |
| 测试 | ⬜ 未开始（计划 EditMode 单测为主） |

## 8. 开发里程碑（状态刷新）

| 阶段 | 时间 | 内容 | 状态 |
|---|---|---|---|
| M0 工程治理 | 2026-09 上旬 | git/仓库、docs 体系、目录重组与 asmdef、Addressables 引入 | **基本完成**（AGENTS.d、C# 规范待补） |
| M1 地基 | 2026-09~10 | Core 基础设施（日志/事件总线）、普通对象池完善、资源管理器实现 | **进行中**：Core 设计稿就绪、资源管理器骨架与设计稿就绪；实现待做 |
| M2 UI 核心 | 2026-10~11 | UI 管理器 MVP（面板栈 + MVVM 绑定 + View 池化） | 未开始（R3 待引入） |
| M3 音频+整合 | 2026-11~12 | 音频管理器；三模块整合；Samples | 未开始 |
| M4 ECS 池 | 2027-01~02 | DOTS 引入；ECS 对象池 + 性能验证 | 未开始 |
| M5 毕设收尾 | 2027-03 | 演示工程集成；文档/架构图；答辩材料 | 未开始 |

- 缓冲：M1-M4 后各留 1-2 周；答辩前 1 个月冻结新特性。裁剪顺序：ECS 池高级特性 → 音频高级特性 → UI 高级绑定。

## 9. 风险与待决项

| 风险/待决项 | 说明 | 应对 |
|---|---|---|
| **依赖形态与分发冲突** | UniTask（UPM git）/ VContainer（UPM）无法随 .unitypackage | **暂缓**（使用者决定）；后续在"回 vendor"与"列前置依赖"间决策 |
| .unitypackage 与官方包边界 | Addressables/DOTS 只能作前置依赖 | 导入指引文档 + 干净工程验证（将来执行） |
| 事件总线滥用 | 易演变为全局状态与隐式控制流 | 白名单/黑名单纪律（只做跨模块低频通知）+ 订阅凭据强制可释放 + 严格遵守"同一件事只发布一次"，见 Core 设计稿 §4.3 |
| 总线与 R3 职责漂移 | 单实现后仍可能有人把节流/状态塞进总线，或为用操作符另起一条流 | 纪律：总线只做**路由**、R3 只做**加工**；需要操作符时在订阅侧用 R3 桥，不新增总线实现 |
| Core 依赖偏差 | Core 不引 VContainer/UniTask，与旧版 §4.2 不同 | 已在本文件 v0.4 修订记录中说明 |
| DOTS 版本兼容 | 版本细节未获权威背书 | M4 前以本机 Package Manager 解析为准 |
| MVVM 复杂度失控 | 绑定层自研易过度设计 | MVP 边界硬约束（§5.3） |
| 毕设时间假设 | 答辩是否 2027 年 4-6 月 | **待使用者确认** |

## 附录

- 调研报告：`docs/research/unity-framework-tech-facts.md`（其中的 UPM 包形态方案**已被 v0.3 的右键导出取代**）
- 模块设计稿：`docs/architecture/resource-manager-design.md`、`docs/architecture/core-design.md`
- 规范：`docs/standards/git-workflow.md`、`docs/standards/agent-role.md`、`docs/standards/naming-convention.md`（Z/IZ 前缀约定）
- 进度记忆：`docs/memory/progress-2026-09-05-06.md`

## 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.8 | 2026-09-10 | **更正对 R3 异常模型的描述**：R3 用 `OnErrorResume`（异常**不会**自动退订），故"用 R3 做总线必然因异常语义复制自研内核"的说法作废；«单实现 + R3 桥»结论不变，依据改为**路由**（R3 无"按类型全局登记订阅者"机制）。详见 `docs/architecture/core-design.md` v0.8 §4.3 |
| v0.7 | 2026-09-10 | **事件总线由「双实现」收敛为「单实现 + R3 桥」（使用者决策）**：删除 `ZR3EventBus`；§2 现状盘点绑定层、§3 选型表事件总线行、§4.1（原则未变）、§5.6 基础设施、§9 风险表同步改写；新增风险行「总线与 R3 职责漂移」。决策依据见 `docs/architecture/core-design.md` §4.3（R3 无类型路由的全局注册表、用 R3 当总线仍要自建路由、桥比第二实现更小更强） |
| v0.6 | 2026-09-06 | 事件总线命名统一框架约定（`IZEventBus` / `ZEventBus` / `ZR3EventBus`） |
| v0.5 | 2026-09-06 | 事件总线改双实现（自研 + R3 封装，R3 只负责 UI 通知）——**已由 v0.7 取代** |
| v0.4 | 2026-09-06 | 同步现状盘点、键体系改 address/label、Core 定为零框架依赖例外、新增事件总线决策与 Core 基础设施一节、里程碑状态刷新 |
| v0.3 | 2026-09 上旬 | 定为 .unitypackage 右键导出、拆箱即用（放弃 UPM 包形态） |
| v0.2 | 2026-09 上旬 | 融入调研结论 |

## 审批记录

- [ ] 使用者批准 v0.8（日期：____，意见：____）
