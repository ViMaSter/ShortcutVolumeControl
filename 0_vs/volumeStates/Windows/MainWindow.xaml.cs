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
using System.Globalization;
using VolumeStates.AudioWrapper;
using VolumeStates.Data;
namespace VolumeStates
{
    public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
    {
        #region members
        public event PropertyChangedEventHandler PropertyChanged;

        AudioDevice CurrentAudioDevice = AudioSession.RequestDefaultSpeakers();
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAudioReflection)));
            }
        }
        HotkeyMappings hotkeys = null;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            hotkeys = new HotkeyMappings(() => CurrentAudioReflection);

            RefreshAudioDevices();
        }
        
        #region audio data
        public void RefreshAudioDevices()
        {
            AudioDeviceDropdown.Items.Clear();

            foreach (var device in AudioSession.RequestAllDevices())
            {
                if (device.State.HasFlag(AudioDeviceStates.Active))
                {
                    AudioDeviceDropdown.Items.Add(device);
                }
            }

            CurrentAudioDevice = AudioSession.RequestDefaultSpeakers();

            AudioDeviceDropdown.SelectedValue = CurrentAudioDevice.Id;
        }

        public void RefreshAppList(AudioDevice device)
        {
            AppReflection newReflection = new AppReflection();
            newReflection.FadeInMS = CurrentAudioReflection.FadeInMS;
            foreach (AudioSession session in AudioSession.RequestAllSessions(device))
            {
                if (session.Process != null)
                {
                    BitmapSource source = null;
                    if (!string.IsNullOrEmpty(session.ProcessPath))
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAudioReflection)));
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
            if (sender == null)
            {
                throw new ArgumentNullException(nameof(sender), Properties.Resources.OnPreviewFadeSpeedInput_OnPreviewFadeSpeedInput_shall_only_be_called_from_the_UI_and_therefore__sender__mustn_t_be_null);
            }
            TextBox senderBox = (TextBox)sender;

            if (!int.TryParse(senderBox.Text, out var newValue))
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
                    CurrentAudioReflection.ToStatus()
                );
            }
            hotkeys.EnableAllHotkeys();

            UpdateUserSettings();
        }

        private void OnClearStateClick(object sender, RoutedEventArgs e)
        {
            hotkeys.DisableAllHotkeys();
            ButtonListenModal buttonListenModal = new ButtonListenModal();
            buttonListenModal.Owner = this;
            if (buttonListenModal.ShowDialog() == true)
            {
                hotkeys.RemoveMapping(
                    buttonListenModal.Modifiers,
                    buttonListenModal.PressedKey
                );
            }
            hotkeys.EnableAllHotkeys();

            UpdateUserSettings();
        }
        #endregion

        #region FFXIV connection
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "FFXIVIs")]
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

        private bool isInCutscene = false;
        public bool IsInCutscene { get => isInCutscene;
            set
            {
                isInCutscene = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInCutscene)));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnectedToGame)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FFXIVConnectionLabel)));
            }
        }

        private string statusBarText = "Ready";
        public string StatusBarText
        {
            get => statusBarText;
            set
            {
                statusBarText = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] ", CultureInfo.InvariantCulture) + value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBarText)));
            }
        }

        FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType currentProcessType;
        Progress<FFXIVCutsceneFlagWatcher.StatusUpdate> statusUpdate;
        FFXIVCutsceneFlagWatcher cutsceneWatcher;

        private void OnFFXIVConnectionUpdate(object sender, FFXIVCutsceneFlagWatcher.StatusUpdate e)
        {
            currentProcessType = e.CurrentProcess;

            StatusBarText = string.Format(CultureInfo.CurrentCulture, "Status: {0} / {1} | Progress: {2}%", ((int)e.CurrentProcess) + 1, (int)FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType.Done,  (int)(e.ProcessPercentage * 100));

            if (e.CurrentProcess == FFXIVCutsceneFlagWatcher.StatusUpdate.ProcessType.Done)
            {
                IsConnectedToGame = e.ReadyToWatch;
                StatusBarText = e.ReadyToWatch ? "Successfully connected to game client" : "Unable to connect to game client";
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FFXIVIsConnecting)));
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
                                StatusBarText = "Cutscene started, triggered associated state";
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
                                StatusBarText = "Cutscene ended, triggered associated state";
                            }
                        }
                    }

                    IsInCutscene = isWatchingCutscene;
                });
            }).ConfigureAwait(true);
        }

        private void DisconnectFromGame()
        {
            cutsceneWatcher.StopWatcher();
            cutsceneWatcher = null;
            IsConnectedToGame = false;
            IsInCutscene = false;

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
                await ConnectToGame().ConfigureAwait(true);
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

            if (CutsceneState != null)
            {
                JObject cutsceneHotkey = new JObject();
                cutsceneHotkey.Add("modifier", (int)CutsceneState.Item1);
                cutsceneHotkey.Add("key", (int)CutsceneState.Item2);
                FFXIVSettings.Add("cutsceneHotkey", cutsceneHotkey);
            }

            if (GameplayState != null)
            {
                JObject gameplayHotkey = new JObject();
                gameplayHotkey.Add("modifier", (int)GameplayState.Item1);
                gameplayHotkey.Add("key", (int)GameplayState.Item2);
                FFXIVSettings.Add("gameplayHotkey", gameplayHotkey);
            }

            return FFXIVSettings;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "FFXIV")]
        public bool DeserializeFFXIVSettings(dynamic FFXIVSettingsBlob)
        {
            if (FFXIVSettingsBlob == null)
            {
                return false;
            }

            if (FFXIVSettingsBlob.gameplayHotkey != null)
            {
                GameplayState = new Tuple<ModifierKeys, Key>((ModifierKeys)FFXIVSettingsBlob.gameplayHotkey.modifier, (Key)FFXIVSettingsBlob.gameplayHotkey.key);
            }

            if (FFXIVSettingsBlob.cutsceneHotkey != null)
            {
                CutsceneState = new Tuple<ModifierKeys, Key>((ModifierKeys)FFXIVSettingsBlob.cutsceneHotkey.modifier, (Key)FFXIVSettingsBlob.cutsceneHotkey.key);
            }

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

        public void DeserializePerAppVolumeStates(dynamic states)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states), Properties.Resources.DeserializePerAppVolumeStates__Cannot_deserialize_app_settings__provided_JSON_blob_is_null);
            }

            hotkeys.ClearMappings();
            foreach (var entry in states)
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
        }

        public bool Deserialize(string jsonBlob)
        {
            dynamic deserializedJson = JsonConvert.DeserializeObject(jsonBlob);

            if (deserializedJson.version != JSONVersion)
            {
                return false;
            }

            CurrentAudioReflection.FadeInMS = deserializedJson.fadeInMS;
            FadeSpeedInMS.Text = CurrentAudioReflection.FadeInMS.ToString(CultureInfo.InvariantCulture);

            DeserializeFFXIVSettings(deserializedJson.FFXIV);

            DeserializePerAppVolumeStates(deserializedJson.states);

            return true;
        }
        #endregion

        public void RestoreFromUserSettings()
        {
            if (Properties.Settings.Default["stateJSON"].ToString().Length > 0)
            {
                StatusBarText = "Restoring user settings...";
                Deserialize(Properties.Settings.Default["stateJSON"].ToString());
                StatusBarText = "Successfully restored user settings";
            }
        }

        public void UpdateUserSettings()
        {
            Properties.Settings.Default["stateJSON"] = SerializeGeneralSettings().ToString(Formatting.None);
            Properties.Settings.Default.Save();
            Debug.WriteLine("New config: " + SerializeGeneralSettings().ToString(Formatting.None));
            StatusBarText = "Updated user settings";
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            RestoreFromUserSettings();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
   		}

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // free managed resources
                if (cutsceneWatcher != null)
                {
                    cutsceneWatcher.Dispose();
                    cutsceneWatcher = null;
                }
            }
        }

        Windows.PositionTracking pos;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            pos = new Windows.PositionTracking();
            pos.Show();
        }
    }
}
