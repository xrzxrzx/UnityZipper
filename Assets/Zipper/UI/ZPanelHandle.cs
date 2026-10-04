using Cysharp.Threading.Tasks;

namespace Zipper.UI
{
    public class ZPanelHandle
    {
        ZPanelManager _panelManager;
        UniTaskCompletionSource _tcs;

        public bool IsOpen { get; private set; }

        internal ZPanelHandle(ZPanelManager panelManager)
        {
            _panelManager = panelManager;
            _tcs = new UniTaskCompletionSource();
            IsOpen = true;
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            _panelManager.Close(this);
        }

        public UniTask WaitCloseAsync()
        {
            return _tcs.Task;
        }

        internal void MarkClosed()
        {
            IsOpen = false; 
            _tcs.TrySetResult();
        }
    }
}