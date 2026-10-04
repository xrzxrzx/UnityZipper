namespace Zipper.UI
{
    public interface IZUISfx
    {
        void Play(string address);

        void Preload(string address);
    }

    public sealed class ZUiSfxNoop : IZUISfx
    {
        public void Play(string address) { }
        public void Preload(string address) { }
    }
}