using Cysharp.Threading.Tasks;

namespace Zipper.UI
{
    public class ZPanelHandle
    {
        public bool IsOpen { get; private set; }

        internal ZPanelHandle()
        {
            IsOpen = true;
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
        }

        public UniTask WaitCloseAsync()
        {
            //TODO 之后改
            return UniTask.WaitUntil(() => !IsOpen);
        }
    }
}