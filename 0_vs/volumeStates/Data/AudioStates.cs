using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VolumeStates.AudioWrapper;

namespace VolumeStates.Data
{
    public class AppStatus
    {
        private Dictionary<string, float> processPathToVolume = new Dictionary<string, float>(2);
        public Dictionary<string, float> ProcessPathToVolume
        {
            get { return processPathToVolume; }
        }

        public AppStatus(Dictionary<string, float> pathToVolume)
        {
            processPathToVolume = pathToVolume;
        }
    }

    public class AppReflection
    {
        private Dictionary<AudioSession, BitmapSource> sessionToThumbnail = new Dictionary<AudioSession, BitmapSource>(2);
        public Dictionary<AudioSession, BitmapSource> SessionToThumbnail
        {
            get
            {
                return sessionToThumbnail;
            }
        }

        private int fadeInMS = 250;
        public int FadeInMS { get => fadeInMS; set => fadeInMS = value; }

        public AppStatus ToStatus()
        {
            Dictionary<string, float> appDefinitions = new Dictionary<string, float>();
            foreach (var session in SessionToThumbnail.Keys)
            {
                appDefinitions[session.ProcessPath] = session.Volume;
            }

            return new AppStatus(appDefinitions);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a * (1 - t) + b * t;
        }

        public void ApplyState(AppStatus state)
        {
            if (state == null || state.ProcessPathToVolume == null)
            {
                throw new InvalidCastException("state cannot be applied - invariant supplied");
            }

            foreach (var definition in state.ProcessPathToVolume)
            {
                foreach (var session in SessionToThumbnail.Keys)
                {
                    if (session.ProcessPath == definition.Key)
                    {
                        Task.Run(async () =>
                        {
                            float startValue = session.Volume;
                            float endValue = definition.Value;

                            TimeSpan lerpDuration = new TimeSpan(FadeInMS * 10000);
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


