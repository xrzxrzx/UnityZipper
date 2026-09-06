namespace Zipper.Pool
{
    public interface IZObjectPoolItem
    {
        delegate void ReturnToPoolDelegate(IZObjectPoolItem item);
        ReturnToPoolDelegate ReturnToPool { get; set; }

        void OnInitialize();
        void OnGet();
        void OnReturn();
        void OnClear();
    }
}