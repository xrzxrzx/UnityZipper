using System;
using Zipper.UI.Contract;

namespace Zipper.UI
{
    public class ZPanelState
    {
        public Type ViewModelType { get; private set; }
        public ZPanelLayer Layer { get; private set; }
        public bool IsOpen { get; private set; }

        internal ZPanelState(Type viewModelType, ZPanelLayer layer, bool isOpen)
        {
            ViewModelType = viewModelType;
            Layer = layer;
            IsOpen = isOpen;
        }
    }
}