using UnityEngine.Audio;

namespace Zipper.Audio
{
    public class ZAudioOptions
    {
        public AudioMixer Mixer;
        public int InitialPoolSize = 4;
        public int MaxPoolSize = 32;

        public const string MixerAddress = "Zipper/Audio/Mixer/ZipperAudioMixer";

        public const string MasterParam = "MasterVolume";
        public const string BgmParam = "BgmVolume";
        public const string SfxParam = "SfxVolume";
        public const string VoiceParam = "VoiceVolume";
        public const string UiParam = "UiVolume";

        public const string MasterGroupName = "Master";
        public const string BgmGroupName = "BGM";
        public const string SfxGroupName = "SFX";
        public const string VoiceGroupName = "Voice";
        public const string UiGroupName = "UI";

        public string GetParam(ZAudioBus bus) => bus switch
        {
            ZAudioBus.Master => MasterParam,
            ZAudioBus.Bgm => BgmParam,
            ZAudioBus.Sfx => SfxParam,
            ZAudioBus.Voice => VoiceParam,
            ZAudioBus.Ui => UiParam,
            _ => throw new System.ArgumentOutOfRangeException(nameof(bus), bus, null)
        };

        public string GetGroupName(ZAudioBus bus) => bus switch
        {
            ZAudioBus.Master => MasterGroupName,
            ZAudioBus.Bgm => BgmGroupName,
            ZAudioBus.Sfx => SfxGroupName,
            ZAudioBus.Voice => VoiceGroupName,
            ZAudioBus.Ui => UiGroupName,
            _ => throw new System.ArgumentOutOfRangeException(nameof(bus), bus, null)
        };
    }
}