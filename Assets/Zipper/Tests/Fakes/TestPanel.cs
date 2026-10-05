using R3;
using UnityEngine;
using Zipper.UI;

namespace Zipper.Tests.Fakes
{
    /// <summary>
    /// 测试用面板 View：把生命周期钩子的调用次数、以及"订阅回调被命中几次"记录在公开字段上，
    /// 让测试能直接断言框架的钩子顺序与解绑协议（而不是靠观察 UI）。
    /// </summary>
    public sealed class TestPanel : ZPanel<TestPanelViewModel>
    {
        // ── 生命周期钩子的调用计数（框架调、测试读）──
        public int CreateCount;
        public int BindCount;
        public int OpenCount;
        public int CloseCount;
        public int UnbindCount;
        public int RecycleCount;

        // ── 订阅探头 ──
        /// <summary>★ 关键判据：每次 Value 回调 +1。若 50 次复用后解绑失效，这个数会 &gt; 1。</summary>
        public int ApplyCallCount;
        public int LastAppliedValue = -1;
        public bool SawCloseRequested;

        protected override void OnCreate() => CreateCount++;

        protected override void OnBind(TestPanelViewModel vm)
        {
            BindCount++;
            vm.Value.Subscribe(v =>
            {
                LastAppliedValue = v;
                ApplyCallCount++;                 // ★ 计数：重复订阅会在这里暴露
            }).AddTo(ref _bag);                    // ★ 用基类的袋子（框架在 Unbind 时统一退订）

            vm.CloseRequested.Subscribe(_ => SawCloseRequested = true).AddTo(ref _bag);
        }

        protected override void OnOpen() => OpenCount++;
        protected override void OnClose() => CloseCount++;
        protected override void OnUnbind() => UnbindCount++;
        protected override void OnRecycle() => RecycleCount++;

        /// <summary>测试辅助：模拟"面板上的按钮点了"（走框架的唯一关闭出口）。</summary>
        public void SimulateRequestClose() => RequestClose();

        /// <summary>探针：框架注入的音效接口是否还在（归池前应被清空）。</summary>
        public bool HasSfx => _sfx != null;

        /// <summary>探针：框架注入的关闭出口是否还在（归池前应被清空）。</summary>
        public bool HasCloseRequester => CloseRequester != null;
    }
}
