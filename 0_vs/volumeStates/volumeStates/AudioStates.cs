using VolumeControl.AudioWrapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace VolumeControl.States
{
    public enum State
    {
        NONE = 0,
        GAME,
        VOICE
    };

    public class AudioState
    {
        public Dictionary<string, float> processPathToVolume = new Dictionary<string, float>(2);
    }

    public class AppReflection
    {
        public Dictionary<AudioSession, BitmapSource> sessionToThumbnail
        {
            get
            {
                return _sessionToThumbnail;
            }
        }
        public Dictionary<AudioSession, BitmapSource> _sessionToThumbnail = new Dictionary<AudioSession, BitmapSource>(2);

        public AudioState ToState()
        {
            Dictionary<string, float> appDefinitions = new Dictionary<string, float>();
            foreach (var session in sessionToThumbnail.Keys)
            {
                appDefinitions[session.ProcessPath] = session.Volume;
            }

            return new AudioState { processPathToVolume = appDefinitions };
        }

        private double Lerp(double a, double b, double t)
        {
            return a * (1 - t) + b * t;
        }

        public void ApplyState(AudioState state, int fadeSpeedInMS)
        {
            foreach (var definition in state.processPathToVolume)
            {
                foreach (var session in sessionToThumbnail.Keys)
                {
                    if (session.ProcessPath == definition.Key)
                    {
                        Task.Run(async () =>
                        {
                            float startValue = session.Volume;
                            float endValue = definition.Value;

                            TimeSpan lerpDuration = new TimeSpan(fadeSpeedInMS * 10000);
                            DateTime startTime = DateTime.Now;
                            DateTime endTime = DateTime.Now + lerpDuration;

                            while (endTime > DateTime.Now)
                            {
                                TimeSpan offset = endTime - DateTime.Now;
                                double value = 1 - (offset.TotalMilliseconds / lerpDuration.TotalMilliseconds);
                                session.Volume = (float)Lerp(startValue, endValue, value);
                                await Task.Delay(((int)((float)1 / 60) * 1000));
                            }
                            session.Volume = endValue;
                        }).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}


