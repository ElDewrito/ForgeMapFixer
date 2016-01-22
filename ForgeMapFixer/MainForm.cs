using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ForgeMapFixer
{
    public partial class MainForm : Form
    {
        private const string MapDir = @"mods\maps";

        private readonly List<string> _fixedMaps = new List<string>(); 

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (!VerifyInstall())
            {
                MessageBox.Show("Please place ForgeMapFixer.exe in the same folder as your ElDewrito 0.5.0.2 install.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            worker.RunWorkerAsync();
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            worker.ReportProgress(0, "Initializing...");
            var tagMap = LoadTagMap();

            worker.ReportProgress(0, "Searching for map files...");
            var files = FindMaps();

            _fixedMaps.Clear();
            for (var i = 0; i < files.Length; i++)
            {
                try
                {
                    var file = files[i];
                    worker.ReportProgress(i * 100 / files.Length, "Fixing " + file + "...");
                    if (FixMap(file, tagMap))
                        _fixedMaps.Add(file);
                }
                catch
                {
                    // TODO: Error handling...
                }
            }

            worker.ReportProgress(100, "Invalidating cached maps...");
            InvalidatePreferences();

            worker.ReportProgress(100, "Done!");
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;
            statusLabel.Text = e.UserState.ToString();
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (_fixedMaps.Count > 0)
            {
                var message = $"Successfully fixed {_fixedMaps.Count} map(s):\n";
                message = _fixedMaps.Aggregate(message, (current, map) => current + $"\n{map}");
                MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("No Forge maps seem to be installed.", Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            Close();
        }

        private static bool VerifyInstall()
        {
            if (!File.Exists("eldorado.exe") || !File.Exists("mtndew.dll"))
                return false;

            // Check that version >= 0.5.0.1
            var version = FileVersionInfo.GetVersionInfo("mtndew.dll");
            return version.FileMajorPart > 0 || version.FileMinorPart > 5 ||
                   (version.FileMinorPart == 5 && (version.FileBuildPart > 0 || version.FilePrivatePart >= 1));
        }

        private static string[] FindMaps()
        {
            return Directory.Exists(MapDir)
                ? Directory.GetFiles(MapDir, "*.map", SearchOption.AllDirectories)
                : new string[0];
        }

        private static Dictionary<int, int> LoadTagMap()
        {
            var tagMap = Properties.Resources.ForgeTagMap;
            var result = new Dictionary<int, int>();
            using (var reader = new StringReader(tagMap))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        break;
                    line = line.Trim();
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                        continue;
                    int oldIndex, newIndex;
                    if (!int.TryParse(parts[0].Trim(), NumberStyles.HexNumber, null, out oldIndex))
                        continue;
                    if (!int.TryParse(parts[1].Trim(), NumberStyles.HexNumber, null, out newIndex))
                        continue;
                    result[oldIndex] = newIndex;
                }
            }
            return result;
        } 

        private static bool FixMap(string path, IReadOnlyDictionary<int, int> tagMap)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
            {
                // Check that the file is valid
                // I'm just using hardcoded offsets here; this should work for most stuff...
                if (stream.Length < 0xE1E0)
                    return false;
                var reader = new BinaryReader(stream);
                var writer = new BinaryWriter(stream);
                if (new string(reader.ReadChars(4)) != "_blf")
                    return false;
                stream.Position = 0x138;
                if (new string(reader.ReadChars(4)) != "mapv")
                    return false;

                // Go to the tag table and replace all of the indices
                for (var i = 0; i < 256; i++)
                {
                    stream.Position = 0xD498 + i * 0xC;
                    var tagIndex = reader.ReadInt32();
                    if (tagIndex < 0)
                        continue;
                    int newIndex;
                    if (!tagMap.TryGetValue(tagIndex, out newIndex))
                        continue;
                    stream.Position -= 4;
                    writer.Write(newIndex);
                }
            }
            return true;
        }

        private static void InvalidatePreferences()
        {
            if (!File.Exists("preferences.dat"))
                return;
            using (var writer = new BinaryWriter(File.Open("preferences.dat", FileMode.Open, FileAccess.Write)))
            {
                writer.BaseStream.Position = 0x378; // "multiplayer valid" flag
                writer.Write(0);
                writer.BaseStream.Position = 0xEA90; // "forge valid" flag
                writer.Write(0);
            }
        }
    }
}
