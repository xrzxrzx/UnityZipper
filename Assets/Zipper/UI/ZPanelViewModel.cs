using R3;
using System;

namespace Zipper.UI
{
    public abstract class ZPanelViewModel : IDisposable
    {
        public ReactiveCommand<Unit> CloseRequested { get; } = new ReactiveCommand<Unit>();

        protected DisposableBag _bag;

        public virtual void Dispose()
        {
            CloseRequested.Dispose();
        }
    }
}