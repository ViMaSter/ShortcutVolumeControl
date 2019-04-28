using VolumeControl.AudioWrapper;
using VolumeControl.States;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace volumeStates
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region members
        public event PropertyChangedEventHandler PropertyChanged;

        AudioDevice CurrentAudioDevice = AudioUtilities.GetDefaultDevice();
        AppReflection _currentAudioReflection = new AppReflection();
        public AppReflection CurrentAudioReflection
        {
            get
            {
                return _currentAudioReflection;
            }
            set
            {
                _currentAudioReflection = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentAudioReflection"));
            }
        }
        HotkeyCollection hotkeys = null;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            hotkeys = new HotkeyCollection(() => CurrentAudioReflection);

            RefreshAudioDevices();
        }

        #region audio data
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

            CurrentAudioDevice = AudioUtilities.GetDefaultDevice();

            AudioDeviceDropdown.SelectedValue = CurrentAudioDevice.Id;
        }

        public void RefreshAppList(AudioDevice device)
        {
            AppReflection newReflection = new AppReflection();
            newReflection.FadeInMS = CurrentAudioReflection.FadeInMS;
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

                    newReflection.SessionToThumbnail.Add(session, source);
                }
            }
            CurrentAudioReflection = newReflection;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentAudioReflection"));
        }

        private void RefreshList(object sender, RoutedEventArgs e)
        {
            RefreshAudioDevices();
        }
        #endregion

        #region UI callbacks
        private void OnAudioDeviceDropdownChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                CurrentAudioDevice = (AudioDevice)e.AddedItems[0];
                RefreshAppList(CurrentAudioDevice);
            }
        }

        public void OnPreviewFadeSpeedInput(object sender, RoutedEventArgs e)
        {
            TextBox senderBox = (TextBox)sender;
            
            int newValue;
            if (!int.TryParse(senderBox.Text, out newValue))
            {
                if (e != null)
                {
                    e.Handled = true;
                }
                newValue = 0;
            }

            CurrentAudioReflection.FadeInMS = newValue;
        }

        private void OnSetStateClick(object sender, RoutedEventArgs e)
        {
            hotkeys.DisableAllHotkeys();
            ButtonListenModal buttonListenModal = new ButtonListenModal();
            if (buttonListenModal.ShowDialog() == true)
            {
                hotkeys.SetKeyPerState(
                    buttonListenModal.Modifiers,
                    buttonListenModal.PressedKey,
                    CurrentAudioReflection.ToState()
                );
            }
            hotkeys.EnableAllHotkeys();
        }
        #endregion

    }
}
