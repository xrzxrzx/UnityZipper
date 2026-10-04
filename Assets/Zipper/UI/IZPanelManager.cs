using Cysharp.Threading.Tasks;
using System;

namespace Zipper.UI
{
    public interface IZPanelManager : IDisposable
    {
        UniTask<ZPanelHandle> OpenAsync<T>(ZPanelOpenOptions options = default) where T : ZPanelViewModel;
        void Register<TViewModel, TView>(string address) where TViewModel : ZPanelViewModel where TView : ZPanel;

        bool TryHandleBack();//返回键
        void CloseAll(bool destroy = false);//关闭所有面板（destroy为true时销毁面板）
        ZPanelsState GetState();
    }
}