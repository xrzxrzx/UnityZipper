namespace Zipper.Audio
{
    public class ZAudioState
    {
        public int PlayingCount { get; private set; }
        public int PoolTotal { get; private set; }
        public int PoolActive { get; private set; }
        public string CurrentBgm { get; private set; }
        public bool IsBgmPlaying { get; private set; }
        public float BgmCrossfade01 { get; private set; }

        internal ZAudioState(int playingCount, int poolTotal, int poolActive, string currentBgm, bool isBgmPlaying, float crossfade01)
        {
            PlayingCount = playingCount;
            PoolTotal = poolTotal;
            PoolActive = poolActive;
            CurrentBgm = currentBgm;
            IsBgmPlaying = isBgmPlaying;
            BgmCrossfade01 = crossfade01;
        }
    }
}