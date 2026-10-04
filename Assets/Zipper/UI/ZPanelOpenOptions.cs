using Zipper.UI.Contract;

namespace Zipper.UI
{
    public class ZPanelOpenOptions
    {
        public ZPanelLayer Layer { get; set; } = ZPanelLayer.Main;
        public bool CloseOnMaskClick { get; set; } = false;
        public bool SingleInstance { get; set; } = false;
        public bool CacheView { get; set; } = true;
    }
}