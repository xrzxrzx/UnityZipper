using System.Collections.Generic;
using R3;
using Zipper.UI;

namespace Zipper.Tests.Fakes
{
    /// <summary>
    /// 测试用 ViewModel：纯 C#（无 UnityEngine 类型 ✓ 对应设计稿 §11-9），
    /// 并记录所有实例，供"复用不串台 / 订阅不叠加"的断言使用。
    ///
    /// <para><b>为什么它在 Zipper.TestSupport（非 Editor 程序集）</b>：
    /// 它被 <see cref="TestPanel"/> 用作泛型参数，而 TestPanel 是 MonoBehaviour、
    /// 必须能被 <c>AddComponent</c> —— Editor-only 程序集里的脚本无法挂到场景对象上 ✗</para>
    /// </summary>
    public sealed class TestPanelViewModel : ZPanelViewModel
    {
        /// <summary>本次测试期间创建过的所有实例（每个测试的 SetUp 里清空）。</summary>
        public static readonly List<TestPanelViewModel> All = new List<TestPanelViewModel>();

        public ReactiveProperty<int> Value { get; } = new ReactiveProperty<int>(0);

        public int DisposeCount;

        DisposableBag _bag;

        public TestPanelViewModel()
        {
            All.Add(this);
            Value.AddTo(ref _bag);        // 自己的订阅进自己的袋子
        }

        public override void Dispose()
        {
            DisposeCount++;
            _bag.Dispose();
            base.Dispose();
        }

        /// <summary>测试辅助：推一个新值（用于验证"旧 VM 推值到不了新 View"）。</summary>
        public void Push(int v) => Value.Value = v;
    }
}
