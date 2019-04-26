using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace volumeStates
{
    public class VolumePercentageConverter : IValueConverter
    {
        private double GetDoubleValue(object parameter, double defaultValue)
        {
            double a;
            if (parameter != null)
            {
                try
                {
                    a = System.Convert.ToDouble(parameter);
                }
                catch
                {
                    a = defaultValue;
                }
            }
            else
            {
                a = defaultValue;
            }
            return a;
        }

        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return ((int)Math.Round((GetDoubleValue(value, 0.0) * 100))) + "%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return double.Parse(((string)value).TrimEnd('%')) / 100;
        }

        #endregion
    }

    public class AudioState
    {
        public Dictionary<string, float> processPathToVolume = new Dictionary<string, float>(2);
    }

    public class AppReflection
    {
        public Dictionary<AudioSession, BitmapSource> sessionToThumbnail = new Dictionary<AudioSession, BitmapSource>(2);
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
                                await Task.Delay(((int)((float)1/60) * 1000));
                            }
                            session.Volume = endValue;
                        }).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    public partial class MainWindow : Window
    {
        Dictionary<State, AudioState> states = new Dictionary<State, AudioState>(2);
        Dictionary<State, Tuple<ModifierKeys, Key>> keys = new Dictionary<State, Tuple<ModifierKeys, Key>>(2);
        Dictionary<State, Hotkey> hotkeys = new Dictionary<State, Hotkey>(2);

        AudioDevice currentAudioDevice = AudioUtilities.GetDefaultDevice();
        AppReflection currentAudioReflection = new AppReflection();

        public void RefreshAudioDevices()
        {
            AudioDeviceDropdown.Items.Clear();

            foreach(var device in AudioUtilities.GetAllDevices())
            {
                if (device.State == AudioDeviceState.Active)
                {
                    AudioDeviceDropdown.Items.Add(device);
                }
            }

            AudioDeviceDropdown.SelectedValue = currentAudioDevice.Id;
        }

        public void RefreshAppList(AudioDevice device)
        {
            currentAudioReflection = new AppReflection();
            foreach (AudioSession session in AudioUtilities.GetAllSessions(device))
            {
                if (session.Process != null)
                {
                    BitmapSource source = null;
                    if (session.Process.GetMainModuleFileName() != null)
                    {
                        System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(session.ProcessPath);
                        source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                    icon.Handle,
                                    new Int32Rect(0, 0, icon.Width, icon.Height),
                                    BitmapSizeOptions.FromEmptyOptions());
                    }
                    
                    currentAudioReflection.sessionToThumbnail.Add(session, source);
                }
            }

            AppList.ItemsSource = currentAudioReflection.sessionToThumbnail;
        }

        public MainWindow()
        {
            InitializeComponent();

            RefreshAudioDevices();

            RefreshAppList(currentAudioDevice);
        }

        public enum State
        {
            NONE = 0,
            GAME,
            VOICE
        }

        State SenderToState(object sender)
        {
            return (State)Enum.Parse(typeof(State), ((Button)sender).Tag.ToString());
        }

        private void SetState(object sender, RoutedEventArgs e)
        {
            State state = SenderToState(sender);
            states[state] = currentAudioReflection.ToState();
        }

        private State currentButtonSetState = State.NONE;
        public State CurrentButtonSetState
        {
            get
            {
                return currentButtonSetState;
            }
            set
            {
                currentButtonSetState = value;
            }
        }

        public void PreviewFadeSpeedInput(object sender, KeyEventArgs e)
        {
            if (((TextBox)sender).Text.Length == 0)
            {
                ((TextBox)sender).Text = "0";
            }

            int a;
            if (!int.TryParse(((TextBox)sender).Text, out a))
            {
                e.Handled = true;
            }
        }

        private void SetButton(object sender, RoutedEventArgs e)
        {
            State state = SenderToState(sender);

            ButtonListenModal buttonListenModal = new ButtonListenModal();
            if (buttonListenModal.ShowDialog() == true)
            {
                keys[state] = new Tuple<ModifierKeys, Key>(buttonListenModal.Modifiers, buttonListenModal.PressedKey);
                hotkeys[state] = new Hotkey(this, (uint)KeyInterop.VirtualKeyFromKey(keys[state].Item2), keys[state].Item1);
                hotkeys[state].onHotKeyPressed = () => {
                    currentAudioReflection.ApplyState(states[state], int.Parse(FadeSpeedInMS.Text));
                };
            }
        }

        private void AudioDeviceDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            currentAudioDevice = (AudioDevice)e.AddedItems[0];
            RefreshAppList(currentAudioDevice);
        }
    }
}
