# Zipper 命名约定

> 状态：v0.3 草案（2026-09-06，使用者给出核心规则：品牌名 Zipper 取首字母 **Z**；并明确"内部机制类型不加前缀"；v0.3 随事件总线单实现化删除 `ZR3EventBus` 示例）
> 定位：框架自有类型/成员的命名规则，避免同一套代码里出现多种风格。**待使用者确认。**
> 关联：`docs/standards/agent-role.md`、`docs/architecture/core-design.md`（§4.3 事件总线）

---

## 1. 核心规则：Z 前缀

| 类别 | 规则 | 正例 |
|---|---|---|
| 框架自有类型（类/结构/枚举） | **`Z*`** | `ZLog`、`ZEventBus`、`ZResourceManager`、`ZObjectPool`、`ZPrefabAsset` |
| 框架自有接口 | **`IZ*`** | `IZLogger`、`IZEventBus`、`IZResourceManager`、`IZObjectPoolItem` |
| 事件类型 | `Z*` + **过去式**（表达"已发生"） | `ZResourceLoaded`、`ZSceneChanged`、`ZUIPanelOpened` |
| 命名空间 | `Zipper.<模块>` | `Zipper.Core.Logging`、`Zipper.Resources` |

之所以不用 `ZipperXxx` 全名：类型名太长会拖累可读性（`ZipperResourceManager.LoadAssetAsync` 啰嗦），首字母 Z 已能一眼区分"框架自有"与"业务/第三方"。

## 2. 例外（技术栈标识优先）

**体现第三方技术栈的实现类，技术栈名放前缀位，不再叠 Z**：即 `Z` + `<技术栈名>` + `<用途>` 的形态（Z = 框架自有，技术栈名 = 明确其实现栈）。

> **v0.3 说明**：本节原示例 **`ZR3EventBus` 已废弃**——事件总线在 Core 设计稿 v0.7 收敛为**单实现**（`IZEventBus` / `ZEventBus`），不再有"封装 R3 的总线实现"；R3 只以**订阅侧桥**的形态出现（见 `docs/architecture/core-design.md` §4.3）。
> **规则本身仍然有效**，但目前框架内暂无符合本节形态的类型；**等实际出现（如未来某个明确绑定第三方栈的实现类）再补正例**，不预先虚构类型名。

## 3. 成员与文件命名

| 项 | 规则 |
|---|---|
| 公共成员 / 属性 | PascalCase |
| 私有字段 | `_camelCase` |
| 局部变量 / 参数 | camelCase |
| 常量 | PascalCase（不用全大写） |
| 异步方法 | 后缀 `Async` |
| 文件名 | PascalCase，与主类型同名；一个文件一个主类型 |
| asmdef | 与程序集名一致（`Zipper.Core.asmdef`） |
| 目录 | PascalCase；命名空间与目录保持一致 |
| 文档文件 | kebab-case 英文（`core-design.md`、`naming-convention.md`） |

## 4. 前缀的适用范围与例外（v0.2 明确）

**加 Z / IZ 前缀的**：框架的**对外概念与入口**——模块级管理器、公开服务、公共设施及其接口。
例：`ZLog` / `IZLogger`、`ZEventBus` / `IZEventBus`、`ZResourceManager` / `IZResourceManager`、`ZObjectPool` / `IZObjectPoolItem`。

**不加前缀的**：**内部机制类型**——服务于某个模块的内部实现；虽然类型是 `public`（外部可访问、可出现在签名里），但**不属于框架对外的品牌 API**，因此保持朴素命名。
例（使用者 2026-09-06 明确）：`AssetHandle<T>`、`AssetHandleBase`、`PrefabAsset` —— 资源管理器签发/持有的句柄与母本载体，属内部机制。

**判定口径**：问一句"这是框架给使用者看的**概念/入口**，还是某个模块**内部流转用的机制/数据载体**？"—— 前者加前缀，后者不加。**`public` ≠ 品牌 API。**

## 5. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.3 | 2026-09-06 | **删除 `ZR3EventBus` 示例**（事件总线由双实现收敛为单实现，见 `core-design.md` §4.3 / v0.7）：§2 保留"技术栈标识优先"规则但不再举该类型为正例、不预先虚构替代名；§4 前缀正例表移除 `ZR3EventBus` |
| v0.2 | 2026-09-06 | §4 由"存量不一致项（待决定）"改为**明确规则**：前缀只用于"对外概念与入口"，**内部机制类型不加前缀**（`AssetHandle<T>`/`AssetHandleBase`/`PrefabAsset` 为例）；补判定口径"`public` ≠ 品牌 API" |
| v0.1 | 2026-09-06 | 初版：Z/IZ 前缀规则、事件过去式、成员与文件命名 |
