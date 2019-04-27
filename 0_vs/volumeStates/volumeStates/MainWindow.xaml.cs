using VolumeControl.AudioWrapper;
using VolumeControl.States;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace volumeStates
{
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
