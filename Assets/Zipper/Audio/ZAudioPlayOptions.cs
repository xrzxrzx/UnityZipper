namespace Zipper.Audio
{
    public class ZAudioPlayOptions
    {
        public ZAudioBus Bus { get; set; } = ZAudioBus.Sfx;
        public float Volume { get; set; } = 1f;
        public float Pitch { get; set; } = 1f;
        public bool Loop { get; set; } = false;
        public float FadeInSeconds { get; set; } = 0f;
        public float SpatialBlend { get; set; } = 0f;
    }
}