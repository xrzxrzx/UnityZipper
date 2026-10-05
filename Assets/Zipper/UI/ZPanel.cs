using R3;
using UnityEngine;
using Zipper.Pool;

namespace Zipper.UI
{
    public abstract class ZPanel : MonoBehaviour, IZObjectPoolItem
    {
        internal System.Action CloseRequester { get; set; }

        protected DisposableBag _bag;
        protected IZUISfx _sfx;

        public IZObjectPoolItem.ReturnToPoolDelegate ReturnToPool { get; set; }

        protected ZPanelViewModel ViewModel { get; private set; }

        internal void SetSfx(IZUISfx sfx) => _sfx = sfx;

        public void OnInitialize()
        {
            OnCreate();
        }

        public void OnGet()
        {
            gameObject.SetActive(true);
        }

        public void OnReturn()
        {
            gameObject.SetActive(false);
        }

        public void OnClear()
        {
            OnRecycle();
        }

        internal void Bind(ZPanelViewModel vm)
        {
            _bag.Clear();
            ViewModel = vm;
            OnBind(vm);
        }

        internal void Unbind()
        {
            _bag.Dispose();
            OnUnbind();
            ViewModel = null;
        }

        internal void Open()
        {
            OnOpen();
        }

        internal void Close()
        {
            OnClose();
        }

        internal void ReturnSelf()
        {
            ReturnToPool?.Invoke(this);
        }

        protected void RequestClose()
        {
            CloseRequester?.Invoke();
        }

        protected virtual void OnCreate() { }
        protected virtual void OnBind(ZPanelViewModel vm) { }
        protected virtual void OnOpen() { }
        protected virtual void OnClose() { }
        protected virtual void OnUnbind() { }
        protected virtual void OnRecycle() { }
    }

    public abstract class ZPanel<TViewModel> : ZPanel where TViewModel : ZPanelViewModel
    {
        protected sealed override void OnBind(ZPanelViewModel vm)
        {
            OnBind((TViewModel)vm);
        }

        protected virtual void OnBind(TViewModel vm) { }
    }
}