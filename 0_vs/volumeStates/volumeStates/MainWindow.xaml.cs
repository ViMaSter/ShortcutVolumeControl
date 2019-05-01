using VolumeControl.AudioWrapper;
using VolumeControl.States;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Configuration;

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

            foreach (var device in AudioUtilities.GetAllDevices())
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

            UpdateUserSettings();
        }

        private void OnSetStateClick(object sender, RoutedEventArgs e)
        {
            hotkeys.DisableAllHotkeys();
            ButtonListenModal buttonListenModal = new ButtonListenModal();
            buttonListenModal.Owner = this;
            if (buttonListenModal.ShowDialog() == true)
            {
                hotkeys.SetKeyPerState(
                    buttonListenModal.Modifiers,
                    buttonListenModal.PressedKey,
                    CurrentAudioReflection.ToState()
                );
            }
            hotkeys.EnableAllHotkeys();

            UpdateUserSettings();
        }
        #endregion

        #region FFXIV connection
        public bool FFXIVIsConnecting {
            get
            {
                if (statusUpdate == null)
                {
                    return true;
                }
                if (currentProcessType == FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType.Done)
                {
                    return true;
                }
                return false;
            }
        }
        public string FFXIVConnectionLabel
        {
            get
            {
                return IsConnectedToGame ? "Disconnect from FFXIV" : "Connect to FFXIV...";
            }
        }

        private bool isInCutsceneFlag = false;
        public bool IsInCutsceneFlag { get => isInCutsceneFlag;
            set
            {
                isInCutsceneFlag = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsInCutsceneFlag"));
            }
        }

        private bool isConnectedToGame = false;
        public bool IsConnectedToGame
        {
            get
            {
                return isConnectedToGame;
            }
            set
            {
                isConnectedToGame = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsConnectedToGame"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FFXIVConnectionLabel"));
            }
        }

        private string statusBarText = "Ready";
        public string StatusBarText
        {
            get => statusBarText;
            set
            {
                statusBarText = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] ") + value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("StatusBarText"));
            }
        }

        FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType currentProcessType;
        Progress<FFXIVCutsceneFlagWatcher.StatusUpdate> statusUpdate;
        FFXIVCutsceneFlagWatcher cutsceneWatcher;

        private void OnFFXIVConnectionUpdate(object sender, FFXIVCutsceneFlagWatcher.StatusUpdate e)
        {
            currentProcessType = e.CurrentProcess;

            StatusBarText = string.Format("Status: {0} ({1} / {2}) | Progress: {3}%", e.CurrentProcess, ((int)e.CurrentProcess) + 1, (int)FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType.COUNT,  (int)(e.ProcessPercentage * 100));

            if (e.CurrentProcess == FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType.Done)
            {
                IsConnectedToGame = e.ReadyToWatch;
                StatusBarText = e.ReadyToWatch ? "Successfully connected to game client" : "Unable to connect to game client";
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FFXIVIsConnecting"));
        }

        private async Task ConnectToGame()
        {
            ConnectToFFXIVWindow connectConfirmation = new ConnectToFFXIVWindow();
            connectConfirmation.Owner = this;
            if (connectConfirmation.ShowDialog() == false)
            {
                return;
            }

            await Task.Run(() =>
            {
                statusUpdate = new Progress<FFXIVCutsceneFlagWatcher.StatusUpdate>();
                statusUpdate.ProgressChanged += OnFFXIVConnectionUpdate;
                cutsceneWatcher = new FFXIVCutsceneFlagWatcher(statusUpdate);
                Debug.Assert(cutsceneWatcher.CanWatch(), "Cutscene watcher couldn't establish connection to FFXIV");
                cutsceneWatcher.StartWatcher((bool isWatchingCutscene) =>
                {
                    if (isWatchingCutscene)
                    {
                        if (CutsceneState != null)
                        {
                            if (!hotkeys.AttemptPress(CutsceneState))
                            {
                                StatusBarText = "Cutscene started, but the cutscene hotkey has no associated state";
                            }
                            else
                            {
                                StatusBarText = "Cutscene started, triggering associated state...";
                            }
                        }
                    }
                    else
                    {
                        if (GameplayState != null)
                        {
                            if (!hotkeys.AttemptPress(GameplayState))
                            {
                                StatusBarText = "Cutscene ended, but the cutscene hotkey has no associated state";
                            }
                            else
                            {
                                StatusBarText = "Cutscene ended, triggering associated state...";
                            }
                        }
                    }

                    IsInCutsceneFlag = isWatchingCutscene;
                });
            });
        }

        private void DisconnectFromGame()
        {
            cutsceneWatcher.StopWatcher();
            cutsceneWatcher = null;
            IsConnectedToGame = false;
            IsInCutsceneFlag = false;

            StatusBarText = "Successfully disconnected from game client";
        }

        private async void ToggleFFXIVConnection(object sender, RoutedEventArgs e)
        {
            if (IsConnectedToGame)
            {
                DisconnectFromGame();
            }
            else
            {
                await ConnectToGame();
            }
        }

        Tuple<ModifierKeys, Key> CutsceneState = null;
        Tuple<ModifierKeys, Key> GameplayState = null;
        void SetHotkey(ref Tuple<ModifierKeys, Key> state)
        {
            hotkeys.DisableAllHotkeys();
            ButtonListenModal buttonListenModal = new ButtonListenModal();
            buttonListenModal.Owner = this;
            if (buttonListenModal.ShowDialog() == true)
            {
                state = new Tuple<ModifierKeys, Key>(
                    buttonListenModal.Modifiers,
                    buttonListenModal.PressedKey
                );
            }
            else
            {
                state = null;
            }
            UpdateUserSettings();
            hotkeys.EnableAllHotkeys();
        }

        private void SetCutsceneHotkey(object sender, RoutedEventArgs e)
        {
            SetHotkey(ref CutsceneState);
        }
        private void SetGameplayHotkey(object sender, RoutedEventArgs e)
        {
            SetHotkey(ref GameplayState);
        }

        #region Serialization
        public JObject SerializeFFXIVSettings()
        {
            JObject FFXIVSettings = new JObject();

            JObject cutsceneHotkey = new JObject();
            cutsceneHotkey.Add("modifier", (int)CutsceneState.Item1);
            cutsceneHotkey.Add("key", (int)CutsceneState.Item2);

            JObject gameplayHotkey = new JObject();
            gameplayHotkey.Add("modifier", (int)GameplayState.Item1);
            gameplayHotkey.Add("key", (int)GameplayState.Item2);

            FFXIVSettings.Add("cutsceneHotkey", cutsceneHotkey);
            FFXIVSettings.Add("gameplayHotkey", gameplayHotkey);

            return FFXIVSettings;
        }

        public bool DeserializeFFXIVSettings(dynamic FFXIVSettingsBlob)
        {
            if (FFXIVSettingsBlob == null)
            {
                return false;
            }
            GameplayState = new Tuple<ModifierKeys, Key>((ModifierKeys)FFXIVSettingsBlob.gameplayHotkey.modifier, (Key)FFXIVSettingsBlob.gameplayHotkey.key);
            CutsceneState = new Tuple<ModifierKeys, Key>((ModifierKeys)FFXIVSettingsBlob.cutsceneHotkey.modifier, (Key)FFXIVSettingsBlob.cutsceneHotkey.key);

            return true;
        }
        #endregion
        #endregion

        #region Serialization
        private const uint JSONVersion = 0;
        public JObject SerializeGeneralSettings()
        {
            JObject root = new JObject();
            root.Add("version", JSONVersion);
            root.Add("fadeInMS", CurrentAudioReflection.FadeInMS);
            root.Add("states", hotkeys.Serialize());
            root.Add("FFXIV", SerializeFFXIVSettings());
            return root;
        }
        public bool Deserialize(string jsonBlob)
        {
            dynamic deserializedJson = JsonConvert.DeserializeObject(jsonBlob);

            if (deserializedJson.version != JSONVersion)
            {
                return false;
            }

            DeserializeFFXIVSettings(deserializedJson.FFXIV);

            CurrentAudioReflection.FadeInMS = deserializedJson.fadeInMS;
            FadeSpeedInMS.Text = CurrentAudioReflection.FadeInMS.ToString();

            hotkeys.ClearMappings();
            foreach (var entry in deserializedJson.states)
            {
                Dictionary<string, float> appDefinitions = new Dictionary<string, float>();
                foreach (var app in entry.apps)
                {
                    appDefinitions.Add((string)app.process, (float)app.volume);
                }

                hotkeys.SetKeyPerState(
                    (ModifierKeys)entry.hotkey.modifier,
                    (Key)entry.hotkey.key,
                    new AppStatus(appDefinitions)
                );
            }
            hotkeys.EnableAllHotkeys();

            return true;
        }
        #endregion

        public void RestoreFromUserSettings()
        {
            if (Properties.Settings.Default["stateJSON"].ToString().Length > 0)
            {
                StatusBarText = "Restoring user settings...";
                Deserialize(Properties.Settings.Default["stateJSON"].ToString());
                StatusBarText = "Successfully restored user settings...";
            }
        }

        public void UpdateUserSettings()
        {
            Properties.Settings.Default["stateJSON"] = SerializeGeneralSettings().ToString(Formatting.None);
            Properties.Settings.Default.Save();
            StatusBarText = "Updated user settings...";
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            RestoreFromUserSettings();
        }
    }
}
