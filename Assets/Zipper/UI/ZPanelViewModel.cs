using R3;
using System;

namespace Zipper.UI
{
    public abstract class ZPanelViewModel : IDisposable
    {
        public ReactiveCommand<Unit> CloseRequested { get; } = new ReactiveCommand<Unit>();

        /// <summary>
        /// 子类的 R3 订阅统一放这里：`SomeProp.Subscribe(...).AddTo(ref _bag);`
        /// ★ 子类【不要再自己声明一个 _bag】✗ —— 那会遮蔽这个（CS0108），
        ///   导致基类的袋子永远空着、而子类那份又依赖各自覆写 Dispose 才能释放 ✓
        /// </summary>
        protected DisposableBag _bag;

        protected ZPanelViewModel()
        {
            CloseRequested.AddTo(ref _bag);     // 命令本身也是 IDisposable → 一并进袋子
        }

        public virtual void Dispose()
        {
            _bag.Dispose();                     // ★ 统一释放（含 CloseRequested）
        }
    }
}