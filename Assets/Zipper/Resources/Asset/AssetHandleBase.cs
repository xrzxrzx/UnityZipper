namespace Zipper.Resources.Asset
{
    public abstract class AssetHandleBase : System.IDisposable
    {
        public string Address { get; protected set; } = string.Empty;
        public string Owner { get; protected set; } = string.Empty;
        public bool IsReleased { get; protected set; } = false;

        public abstract void Release();

        public void Dispose() => Release();
    }
}