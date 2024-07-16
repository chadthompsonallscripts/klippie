using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using klippie.Properties;
using WK.Libraries.SharpClipboardNS;

namespace klippie
{
    public partial class KlippieForm : Form
    {
        public SharpClipboard Klipboard { get; set; }
        public KlippieForm()
        {
            InitializeComponent();
            Klipboard = new SharpClipboard();
            Klipboard.ClipboardChanged += ClipboardChanged;
        }

        private void ClipboardChanged(object sender, SharpClipboard.ClipboardChangedEventArgs e)
        {
            var clipboard = ClipboardAwsValueFromClipboardDataObject();

            if (string.IsNullOrEmpty(clipboard))
                return;

            (Dictionary<string, string> args, string label) = GetAwsParameters(clipboard);

            if (args == null || !args.Any())
                return;

            WriteFile(args, label);

            if (Settings.Default["PostProcessCommand"] != null)
            {
                var postProcessCommands = Settings.Default["PostProcessCommand"].ToString();
                string extension = postProcessCommands.Split('.')[1].Split(' ')[0];
                string command = $"{postProcessCommands.Split('.')[0]}.{extension}";
                string arguments = postProcessCommands.Split('.')[1].Replace($"{extension} ", string.Empty);
                Process process = Process.Start(command, arguments);
                process?.WaitForExit();
            }
        }

        private void KlippieForm_Load(object sender, EventArgs e)
        {
            this.Closed += OnClosed;
            Hide();
            notifyIcon1.Visible = true;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            notifyIcon1.Dispose();
            this.Dispose();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
            this.Dispose();
        }

        private (Dictionary<string, string>, string) GetAwsParameters(string clipboard)
        {
            var clipboardLines = clipboard.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            if (clipboardLines.Length != 4)
                return (null, null);

            var label = clipboardLines[0].StartsWith("[") ? clipboardLines[0] : string.Empty;
            var args = clipboardLines
                .Where(x => x.StartsWith("aws"))
                .ToDictionary(
                    key => key.Split(new[] { "=" }, 2, StringSplitOptions.None)[0],
                    value => value.Split(new[] { "=" }, 2, StringSplitOptions.None)[1]);

            return (args, label);
        }

        private void WriteFile(Dictionary<string, string> args, string label)
        {
            //get file
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(userDirectory, ".aws", "credentials");

            TextReader textReader = File.OpenText(path);

            string line;
            var newFile = new List<string>();
            var labels = new List<string>();
            bool overwrite = false;
            while ((line = textReader.ReadLine()) != null)
            {
                if (line.StartsWith("["))
                {
                    overwrite = (line.Contains(label) || line.Contains("[default]"));
                    if(overwrite) 
                        labels.Add(label);
                }

                var added = false;
                foreach (KeyValuePair<string, string> arg in args
                             .Where(arg => line.StartsWith(arg.Key) && overwrite))
                {
                    newFile.Add($"{arg.Key}={arg.Value}");
                    added = true;
                }

                if (!added)
                {
                    newFile.Add(line);
                }
            }
            textReader.Close();
            textReader.Dispose();

            File.WriteAllLines(path, newFile);

            SendToast(labels);
        }

        private void SendToast(List<string> labels)
        {
            new ToastContentBuilder()
                .AddText("Credentials updated.")
                .AddText("default & " + string.Join(" & ", labels.ToArray()))
                .Show(); 
        }

        private string ClipboardAwsValueFromClipboardDataObject()
        {
            var clipboardObject = Clipboard.GetDataObject();
            var clipboardFormats = clipboardObject?.GetFormats(false);
            var clipboardBackup = clipboardFormats?
                .ToDictionary(key => key, key => clipboardObject?.GetData(key, false));

            return clipboardBackup?
                .FirstOrDefault(
                    c => c.Key.ToLower() == "text" &&
                        c.Value.ToString().Contains("aws")
                    ).Value?.ToString();
        }
    }
}
