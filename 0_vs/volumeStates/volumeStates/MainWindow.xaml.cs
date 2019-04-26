using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace volumeStates
{
    internal static class IconUtilities
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        public static ImageSource ToImageSource(this Icon icon)
        {
            Bitmap bitmap = icon.ToBitmap();
            IntPtr hBitmap = bitmap.GetHbitmap();

            ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            if (!DeleteObject(hBitmap))
            {
                throw new Win32Exception();
            }

            return wpfBitmap;
        }
    }

    public class DLLIconConverter : IValueConverter
    {
        public string FileName { get; set; }
        public int Number { get; set; }

        [DllImport("Shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int ExtractIconEx(string sFile, int iIndex, out IntPtr piLargeVersion, out IntPtr piSmallVersion, int amountIcons);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            IntPtr large;
            IntPtr small;
            ExtractIconEx(FileName, Number, out large, out small, 1);
            try
            {
                return Icon.FromHandle(large).ToImageSource();
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
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
                                await Task.Delay(((int)((float)1/60) * 1000));
                            }
                            session.Volume = endValue;
                        }).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    public enum State
    {
        NONE = 0,
        GAME,
        VOICE
    };

    public class HotkeyCollection
    {
        private Dictionary<State, Hotkey> hotkeysByState = new Dictionary<State, Hotkey>
        {
            { State.GAME, null },
            { State.VOICE, null }
        };
        public void SetKeyPerState(State state, Window parent, uint key, ModifierKeys modifier, Hotkey.OnHotKeyPressed action)
        {
            hotkeysByState[state]?.Unmap();
            hotkeysByState[state] = new Hotkey(parent, key, modifier)
            {
                onHotKeyPressed = action
            };
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        Dictionary<State, AudioState> states = new Dictionary<State, AudioState>
        {
            { State.GAME, new AudioState() },
            { State.VOICE, new AudioState() }
        };
        Dictionary<State, Tuple<ModifierKeys, Key>> keys = new Dictionary<State, Tuple<ModifierKeys, Key>>(2);
        HotkeyCollection hotkeys = new HotkeyCollection();

        AudioDevice currentAudioDevice = AudioUtilities.GetDefaultDevice();

        AppReflection _currentAudioReflection = new AppReflection();
        public AppReflection currentAudioReflection
        {
            get
            {
                return _currentAudioReflection;
            }
            set
            {
                _currentAudioReflection = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("currentAudioReflection"));
            }
        }

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

            currentAudioDevice = AudioUtilities.GetDefaultDevice();

            AudioDeviceDropdown.SelectedValue = currentAudioDevice.Id;
        }

        public void RefreshAppList(AudioDevice device)
        {
            AppReflection newReflection = new AppReflection();
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

                    newReflection.sessionToThumbnail.Add(session, source);
                }
            }
            currentAudioReflection = newReflection;
        }

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            RefreshAudioDevices();

            RefreshAppList(currentAudioDevice);
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
                hotkeys.SetKeyPerState(
                    state,
                    this,
                    (uint)KeyInterop.VirtualKeyFromKey(keys[state].Item2),
                    keys[state].Item1,
                    () =>
                    {
                        currentAudioReflection.ApplyState(states[state], int.Parse(FadeSpeedInMS.Text));
                    }
                );
            }
        }

        private void AudioDeviceDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                currentAudioDevice = (AudioDevice)e.AddedItems[0];
                RefreshAppList(currentAudioDevice);
            }
        }

        private void RefreshList(object sender, RoutedEventArgs e)
        {
            RefreshAudioDevices();
            RefreshAppList(currentAudioDevice);
        }
    }
}
