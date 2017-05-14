﻿using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using NAudio.Wave.SampleProviders;

namespace SpotifyRecorder.Forms.UI
{
    public partial class MainForm : Form
    {
        private enum ApplicationState
        {
            NotRecording = 1,
            WaitingForRecording = 2,
            Recording = 3,
            Closing = 4
        }

        private SoundCardRecorder SoundCardRecorder { get; set; }

        private Timer songTicker;
        private FolderBrowserDialog folderDialog;
        private ApplicationState _currentApplicationState = ApplicationState.NotRecording;

        private static Object locker = new Object();

        public MainForm()
        {
            InitializeComponent();

            //check if it is windows 7
            if (Environment.OSVersion.Version.Major < 6)
            {
                MessageBox.Show("This application is optimized for windows 7");
                Close();
            }

            Load += OnLoad;
            Closing += OnClosing;
        }

        private void ChangeApplicationState(ApplicationState newState)
        {
            ChangeGui(newState);

            switch (_currentApplicationState)
            {
                case ApplicationState.NotRecording:
                    switch (newState)
                    {
                        case ApplicationState.NotRecording:
                            break;
                        case ApplicationState.WaitingForRecording:
                            break;
                        case ApplicationState.Recording:
                            StartRecording(songLabel.Text);
                            break;
                        case ApplicationState.Closing:
                            if(SoundCardRecorder != null)
                            {
                                SoundCardRecorder.Dispose();
                            }
                            songTicker.Stop();
                            break;
                    }

                    break;
                case ApplicationState.WaitingForRecording:
                    switch (newState)
                    {
                        case ApplicationState.NotRecording:
                            break;
                        case ApplicationState.WaitingForRecording:
                            throw new Exception(string.Format("NY {0} - {1}",_currentApplicationState,newState));
                        case ApplicationState.Recording:
                            StartRecording(songLabel.Text);
                            break;
                        case ApplicationState.Closing:
                            if (SoundCardRecorder != null)
                            {
                                SoundCardRecorder.Dispose();
                            }
                            songTicker.Stop();
                            break;
                    }
                    break;
                case ApplicationState.Recording:
                    switch (newState)
                    {
                        case ApplicationState.NotRecording:
                            StopRecording();
                            break;
                        case ApplicationState.Recording: //file changed
                            Thread.Sleep(750);
                            StopRecording();
                            StartRecording(songLabel.Text);
                            break;
                        case ApplicationState.WaitingForRecording: //file changed
                            StopRecording();
                            break;
                        case ApplicationState.Closing:
                            if (SoundCardRecorder != null)
                            {
                                SoundCardRecorder.Dispose();
                            }
                            songTicker.Stop();
                            break;
                    }
                    break;

            }
            _currentApplicationState = newState;
        }

        private void ChangeGui(ApplicationState state)
        {
            switch (state)
            {
                case ApplicationState.NotRecording:
                    browseButton.Enabled = true;
                    buttonStartRecording.Enabled = true;
                    buttonStopRecording.Enabled = false;
                    bitrateComboBox.Enabled = true;
                    thresholdCheckBox.Enabled = true;
                    thresholdTextBox.Enabled = true;
                    break;
                case ApplicationState.WaitingForRecording:
                    browseButton.Enabled = false;
                    buttonStartRecording.Enabled = false;
                    buttonStopRecording.Enabled = true;
                    bitrateComboBox.Enabled = false;
                    thresholdCheckBox.Enabled = false;
                    thresholdTextBox.Enabled = false;
                    break;
                case ApplicationState.Recording:
                    browseButton.Enabled = false;
                    buttonStartRecording.Enabled = false;
                    buttonStopRecording.Enabled = true;
                    bitrateComboBox.Enabled = false;
                    thresholdCheckBox.Enabled = false;
                    thresholdTextBox.Enabled = false;
                    break;

            }

        }
        /// <summary>
        /// After the form is created
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void OnLoad(object sender, EventArgs eventArgs)
        {
       
            //load the different bitrates
            LoadBitrateCombo();

            //Load user settings
            LoadUserSettings();

            //check if spotify title is changing
            songTicker = new Timer { Interval = Settings.Default.SongscanInterval };
            songTicker.Tick += SongTickerTick;
            songTicker.Start();

            //set the change event if filePath is 
            songLabel.Text = string.Empty;

            folderDialog = new FolderBrowserDialog { SelectedPath = outputFolderTextBox.Text };

            versionLabel.Text = string.Format("Version {0}", Application.ProductVersion);

            ChangeApplicationState(_currentApplicationState);
        }

        /// <summary>
        /// When the application is closing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="cancelEventArgs"></param>
        private void OnClosing(object sender, CancelEventArgs cancelEventArgs)
        {
            ChangeApplicationState(ApplicationState.Closing);
            Util.SetDefaultBitrate((int)bitrateComboBox.SelectedItem);
            Util.SetDefaultOutputPath(outputFolderTextBox.Text);
            Util.SetDefaultThreshold((int)thresholdTextBox.Value);
            Util.SetDefaultThreshold((int)thresholdTextBox.Value);
            Util.SetDefaultThresholdEnabled(thresholdCheckBox.Checked);
        }

        void SongTickerTick(object sender, EventArgs e)
        {
            //get the current title from spotify
            string song = GetSpotifySong();

            if (!songLabel.Text.Equals(song))
            {
                songLabel.Text = song;
                if (songLabel.Text.Trim().Length > 0)
                {
                    if (_currentApplicationState != ApplicationState.NotRecording)
                        ChangeApplicationState(ApplicationState.Recording);
                    else if (_currentApplicationState == ApplicationState.Recording)
                        ChangeApplicationState(ApplicationState.WaitingForRecording);
                }
                else
                {
                    if(_currentApplicationState==ApplicationState.Recording)
                        ChangeApplicationState(ApplicationState.WaitingForRecording);

                }
            }
        }


        private void ButtonPlayClick(object sender, EventArgs e)
        {
            if (listBoxRecordings.SelectedItem != null)
            {
                Process.Start(CreateOutputFile((string)listBoxRecordings.SelectedItem, "mp3"));
            }
        }

        private void ButtonDeleteClick(object sender, EventArgs e)
        {
            if (listBoxRecordings.SelectedItem != null)
            {
                try
                {
                    File.Delete(CreateOutputFile((string)listBoxRecordings.SelectedItem, "mp3"));
                    listBoxRecordings.Items.Remove(listBoxRecordings.SelectedItem);
                    if (listBoxRecordings.Items.Count > 0)
                    {
                        listBoxRecordings.SelectedIndex = 0;
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show("Could not delete recording");
                }
            }
        }

        private void ButtonOpenFolderClick(object sender, EventArgs e)
        {
            Process.Start(outputFolderTextBox.Text);
        }

        private void ButtonStartRecordingClick(object sender, EventArgs e)
        {
            ChangeApplicationState(songLabel.Text.Trim().Length > 0
                                       ? ApplicationState.Recording
                                       : ApplicationState.WaitingForRecording);
        }

        private void ButtonStopRecordingClick(object sender, EventArgs e)
        {
            ChangeApplicationState(ApplicationState.NotRecording);
        }

        private void ClearButtonClick(object sender, EventArgs e)
        {
            listBoxRecordings.Items.Clear();
        }

        private string CreateOutputFile(string song, string extension)
        {
            song = RemoveInvalidFilePathCharacters(song, string.Empty);
            return Path.Combine(outputFolderTextBox.Text, string.Format("{0}.{1}", song, extension));
        }
        private void StartRecording(string song)
        {
            if (!string.IsNullOrEmpty(song))
            {
                lock (locker)
                {
                    if (SoundCardRecorder!=null)
                        StopRecording();

                    var file = CreateOutputFile(songLabel.Text, "wav");

                    SoundCardRecorder = new SoundCardRecorder(
                                    file,
                                    songLabel.Text);
                    SoundCardRecorder.Start();

                    SoundCardRecorder.PostVolumeMeter.StreamVolume += OnPostVolumeMeter;

                }
            }

        }

        private void StopRecording()
        {
            lock (locker)
            {
                if (SoundCardRecorder != null)
                {
                    SoundCardRecorder.Stop();
                    var filePath = SoundCardRecorder.FilePath;
                    var song = SoundCardRecorder.Song;
                    var duration = SoundCardRecorder.Duration;
                    if (thresholdCheckBox.Checked && duration.Seconds < (int) thresholdTextBox.Value)
                    {
                        File.Delete(filePath);
                        SoundCardRecorder.Dispose();
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            int newItemIndex = listBoxRecordings.Items.Add(song);
                            listBoxRecordings.SelectedIndex = newItemIndex;
                            int bitrate = (int)bitrateComboBox.SelectedValue;
                            var copy = SoundCardRecorder;
                            Task t = new Task(() => ConvertToMp3(copy, bitrate));
                            t.Start();
                        }
                        else
                        {
                            SoundCardRecorder.Dispose();
                        }

                    }
                    volumeMeter1.Amplitude = 0;
                    volumeMeter2.Amplitude = 0;
                    SoundCardRecorder = null;
                }
            }

        }


        private void ConvertToMp3(SoundCardRecorder scr, int bitrate)
        {
            if (!File.Exists(CreateOutputFile(scr.Song, "wav")))
                return;

            Process process = new Process();
            process.StartInfo.UseShellExecute = false;
            //process.StartInfo.RedirectStandardOutput = true;
            //process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = false;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;

            Mp3Tag tag = Util.ExtractMp3Tag(scr.Song);

            process.StartInfo.FileName = "lame.exe";
            process.StartInfo.Arguments = string.Format("-b {2} --tt \"{3}\" --ta \"{4}\"  \"{0}\" \"{1}\"",
                CreateOutputFile(scr.Song, "wav"),
                CreateOutputFile(scr.Song, "mp3"),
                bitrate,
                tag.Title,
                tag.Artist);

            process.StartInfo.WorkingDirectory = new FileInfo(Application.ExecutablePath).DirectoryName;
            process.Start();
            process.WaitForExit(20000);
            if (!process.HasExited)
                process.Kill();
            File.Delete(CreateOutputFile(scr.Song, "wav"));
        }

        private void LoadBitrateCombo()
        {
            List<int> bitrate = new List<int> { 96, 128, 160, 192, 320 };

            bitrateComboBox.DataSource = bitrate;
        }

        /// <summary>
        /// load the setting from a previous session
        /// </summary>
        private void LoadUserSettings()
        {
            //get/set the device
            string defaultDevice = Util.GetDefaultDevice();

            //set the default output to the music directory
            outputFolderTextBox.Text = Util.GetDefaultOutputPath();

            //set the default bitrate
            bitrateComboBox.SelectedItem = Util.GetDefaultBitrate();

            thresholdTextBox.Value = Util.GetDefaultThreshold();
            thresholdCheckBox.Checked = Util.GetDefaultThresholdEnabled();

        }

        private string GetSpotifySong()
        {
            Process[] process = Process.GetProcessesByName("spotify");
            if (process != null && process.Length > 0)
            {
                var song = Process.GetProcessesByName("spotify")[0].MainWindowTitle;
                if (song == "Spotify")
                {
                    return string.Empty;
                }
                else
                {
                    return song;
                }

            }
            return string.Empty;

        }

        public static string RemoveInvalidFilePathCharacters(string filename, string replaceChar)
        {
            string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
            return r.Replace(filename, replaceChar);
        }

        private void HelpLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://spotifyrecorder.codeplex.com");

        }

        private void BrowseButtonClick(object sender, EventArgs e)
        {
            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                outputFolderTextBox.Text = folderDialog.SelectedPath;
                Util.SetDefaultOutputPath(folderDialog.SelectedPath);
            }
        }

        private void OpenMixerButtonClick(object sender, EventArgs e)
        {
            Process.Start("sndvol");
        }

        void OnPostVolumeMeter(object sender, StreamVolumeEventArgs e)
        {
            // we know it is stereo
            volumeMeter1.Amplitude = e.MaxSampleValues[0];
            volumeMeter2.Amplitude = e.MaxSampleValues[1];
        }
    }
}
