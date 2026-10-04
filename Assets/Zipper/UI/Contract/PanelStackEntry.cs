namespace Zipper.UI.Contract
{
    internal sealed class PanelStackEntry
    {
        public ZPanel View { get; }
        public ZPanelViewModel ViewModel { get; }
        public ZPanelHandle Handle { get; }
        public ZPanelLayer Layer { get; }
        public ZPanelOpenOptions Options { get; }
        public long Sequence { get; }//打开顺序（同层排序用）
        public bool OwnsViewModel { get; }

        internal PanelStackEntry(ZPanel view, ZPanelViewModel viewModel, ZPanelHandle handle, ZPanelLayer layer, ZPanelOpenOptions options, long sequence, bool ownsViewModel)
        {
            View = view;
            ViewModel = viewModel;
            Handle = handle;
            Layer = layer;
            Options = options;
            Sequence = sequence;
            OwnsViewModel = ownsViewModel;
        }
    }
}