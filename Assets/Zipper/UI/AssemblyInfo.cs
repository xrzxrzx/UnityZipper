using System.Runtime.CompilerServices;

// 让测试程序集能看到内部机制类型与成员（照 Audio 模块 AssemblyInfo 的做法）：
//   internal 类型：PanelRegistry / Registration / PanelStackEntry
//   internal 成员：ZPanelManager.Close / ZPanel.Bind / Unbind / Open / Close / SetSfx / CloseRequester
// 说明：不为了「可测性」把这些改成 public —— 内部机制不该出现在对外契约面上。
[assembly: InternalsVisibleTo("Zipper.Tests")]
