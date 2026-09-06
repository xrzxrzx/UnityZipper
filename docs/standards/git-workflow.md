# Zipper 仓库 Git 工作流

> 状态：v1.0 生效
> 定位：本仓库（`UnityZipper`）的日常提交操作规范，回答「提交什么、怎么提交、什么不能做」。
> 依据：`docs/planning/technical-roadmap.md` §7「工程治理」；协调者 Git 规范（Conventional Commits；禁 rebase/force-push；合并用 Squash）。
> 关联：仓库远端 `https://github.com/xrzxrzx/UnityZipper.git`（分支 `main`）。

---

## 1. 仓库边界：只跟踪什么

本仓库**只跟踪两类内容**，靠仓库根 `.gitignore`（白名单模式）强制保证：

| 跟踪 | 内容 |
|---|---|
| `Assets/Zipper/` | 框架全部代码：`.cs` + `.asmdef` + **`.meta`**（GUID 必须随包，asmdef 引用与导入别处不脱钩） |
| `docs/` | 全部文档（planning / research / architecture / standards / memory） |
| `.gitignore` | 白名单本身 |

**一律不入库**（被 `.gitignore` 排除，勿用 `git add -f` 强加）：Unity 工程其它目录（`Assets/` 下 Zipper 之外、`Library/`、`Temp/`、`obj/`、`Logs/`、`UserSettings/`、`Packages/`、`ProjectSettings/`）、`.csproj/.sln` 等由 Unity 生成的工程文件、场景/预制体等美术资源。

> 放文件须知：想让某个文件被跟踪，必须放在 `Assets/Zipper/` 或 `docs/` 内；放外面 commit 不会包含它（也别用 `-f` 绕过）。

## 2. 日常提交三步

```bash
git add -A            # 暂存全部变更（白名单内）
git status            # 先看：确认只有预期文件
git commit -m "type(scope): 摘要"
git push              # 推送到 origin/main
```

`git status` 养成习惯：白名单模式下，若出现 Zipper/docs 之外的文件路径，说明有文件放错了位置或有人用了 `-f`。

## 3. 提交信息：Conventional Commits

格式：`<type>(<scope>): <subject>`，subject 用祈使句、中文或英文皆可（与正文注释语言保持一致）。

| type | 何时用 | 示例 |
|---|---|---|
| `feat` | 新功能/新模块 | `feat(pool): 对象池支持预热与容量上限` |
| `fix` | 修 bug | `fix(resources): Dispose 遍历中修改集合崩溃` |
| `docs` | 只改文档 | `docs: 固化 Zipper 仓库 Git 工作流` |
| `refactor` | 重构不改变行为 | `refactor(resources): 簿记改单一 HashSet` |
| `chore` | 工程杂项（.gitignore/依赖） | `chore: 清理 UniTask 包缓存` |

scope 用模块名：`core` / `pool` / `resources` / `di` / `docs`……多个模块改动或纯文档可不写 scope。

## 4. 红线（不可违反）

- ❌ **禁止 `git rebase` / `git push -f`**：改写已推送历史。多人协作后合并一律 **Squash Merge**。
- ❌ 不提交"编译不过"的代码（提交前自查：Unity 编译 0 error）。
- ❌ 不把 Unity 生成物/大文件/本地配置（见 §1 排除表）入库。
- ❌ 小改动/临时改动/不能确保满意的内容**不主动提交**，先问使用者。
- ✅ 代码与文档同步：改了行为，对应 `docs/` 同步更新后一起提交。

## 5. 首次克隆/换机拉取后的动作

```bash
git clone https://github.com/xrzxrzx/UnityZipper.git
# 把 Assets/Zipper 与 docs 拷回 Unity 工程对应位置（或直接在工程里操作）
```

- 因 `.meta` 已入库，asmdef 间 GUID 引用不丢；但 Unity 若整体移动目录需在编辑器里重新导入一次。
- 本仓库**不含**第三方依赖源码（UniTask 等）与官方包——它们是工程级前置依赖，见 `docs/planning/technical-roadmap.md` §6。

## 6. 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v1.0 | 2026-09 | 初版：仓库边界 / 三步提交 / Conventional Commits / 红线 |
