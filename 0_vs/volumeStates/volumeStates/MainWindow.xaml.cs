using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace volumeStates
{
    public class AudioState
    {
        public Dictionary<string, float> appToVolume = new Dictionary<string, float>(2);
    }

    public partial class MainWindow : Window
    {
        Dictionary<State, AudioState> states = new Dictionary<State, AudioState>(2);
        Dictionary<State, Tuple<ModifierKeys, Key>> keys = new Dictionary<State, Tuple<ModifierKeys, Key>>(2);
        Dictionary<State, Hotkey> hotkeys = new Dictionary<State, Hotkey>(2);

        AudioDevice currentAudioDevice = AudioUtilities.GetDefaultSpeaker();
        AudioState currentAudioState = new AudioState();

        void AddDefinition(string appName, float volume)
        {
            currentAudioState.appToVolume[appName] = volume;
        }

        void RemoveDefinition(string appName)
        {
            currentAudioState.appToVolume.Remove(appName);
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

            AudioDeviceDropdown.SelectedValue = currentAudioDevice.Id;
        }

        public MainWindow()
        {
            InitializeComponent();

            RefreshAudioDevices();

            foreach (AudioSession session in AudioUtilities.GetAllSessions())
            {
                if (session.Process != null)
                {
                    // only the one associated with a defined process
                    Console.WriteLine(session.Process.ProcessName + ": " + session.Volume);
                }
            }
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
            states[state] = currentAudioState;
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

        private void SetButton(object sender, RoutedEventArgs e)
        {
            State state = SenderToState(sender);

            ButtonListenModal buttonListenModal = new ButtonListenModal();
            if (buttonListenModal.ShowDialog() == true)
            {
                keys[state] = new Tuple<ModifierKeys, Key>(buttonListenModal.Modifiers, buttonListenModal.PressedKey);
                hotkeys[state] = new Hotkey(this, (uint)KeyInterop.VirtualKeyFromKey(keys[state].Item2), keys[state].Item1);
            }
        }
    }
}
