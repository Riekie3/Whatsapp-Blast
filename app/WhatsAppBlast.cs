// WhatsApp Blast app - a window with buttons for the steps in README.txt.
// It runs the same .bat files (with FROM_APP set, so they close by themselves)
// and shows their output under "Details". Build with build-app.bat.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WhatsAppBlast
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            if (args.Length == 2 && args[0] == "--screenshot")
            {
                // For testing the layout: save a picture of the window and close.
                string outPath = args[1];
                form.Shown += delegate
                {
                    Application.DoEvents();
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                };
            }
            Application.Run(form);
        }
    }

    class Badge : Control
    {
        public Badge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(MainForm.Green))
                e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class CheckResult
    {
        public bool NoList;
        public string ListName = "";
        public int Rows = -1, Ready = -1, Problems, Minutes = -1;
    }

    class MainForm : Form
    {
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);
        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

        public static readonly Color Green = Color.FromArgb(30, 142, 62);
        static readonly Color PaleGreen = Color.FromArgb(160, 196, 170);
        static readonly Color Red = Color.FromArgb(197, 34, 31);
        static readonly Color Orange = Color.FromArgb(184, 98, 0);
        static readonly Color Grey = Color.FromArgb(90, 95, 100);
        static readonly Color CardBack = Color.FromArgb(243, 246, 244);
        const string ChromeSettingUrl = "chrome://inspect/#remote-debugging";

        readonly string dir;
        Button installBtn, nodeBtn, chromeBtn, listBtn, importBtn, picsBtn, checkBtn, sendBtn, stopBtn, reportBtn, gapSaveBtn;
        Label installStatus, chromeStatus, listStatus, checkStatus, sendStatus;
        NumericUpDown gapBox;
        ProgressBar progress;
        TextBox logBox;
        Timer timer;

        Process running;
        string runningTask; // install, check, send, tool
        readonly StringBuilder output = new StringBuilder();
        bool nodeFound, stopping;
        string nodeProblem = "";
        int savedGap = -1, sendTotal, sent, failed, notOnWa, alreadySent;

        public MainForm()
        {
            dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            SuspendLayout();
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "WhatsApp Blast";
            BackColor = Color.White;
            ClientSize = new Size(980, 624);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label { Text = "WhatsApp Blast", Location = new Point(14, 10), AutoSize = true, Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Green };
            var sub = new Label { Text = "Send each person their own picture through WhatsApp Web, using your own Chrome. Go through the steps in order.", Location = new Point(17, 44), AutoSize = true, ForeColor = Grey };
            var guide = new LinkLabel { Text = "Open the guide", AutoSize = true, Location = new Point(870, 18), Anchor = AnchorStyles.Top | AnchorStyles.Right, LinkColor = Green };
            guide.LinkClicked += delegate { OpenWith("notepad.exe", P("README.txt")); };
            Controls.AddRange(new Control[] { title, sub, guide });

            // 1. Install
            var c1 = Card(68, 86, 1, "Install (first time only)", "Downloads what the tool needs. Needs internet and takes about a minute.");
            installBtn = Btn(c1, "Install", 50, 50, 100, delegate { InstallClicked(); }, true);
            nodeBtn = Btn(c1, "Get Node.js", 156, 50, 110, delegate { OpenWith("https://nodejs.org", null); }, false);
            installStatus = Status(c1, 160, 55, 390);

            // 2. Chrome
            var c2 = Card(160, 126, 2, "Connect your Chrome",
                "Lets the tool use the Chrome that is logged in to WhatsApp Web. Click the button and follow the 3 short steps. " +
                "Untick the setting again when you have finished sending.");
            chromeBtn = Btn(c2, "Open Chrome setting", 50, 88, 160, delegate { ChromeClicked(); }, false);
            chromeStatus = Status(c2, 220, 93, 330);

            // 3. Contact list
            var c3 = Card(292, 86, 3, "Contact list and pictures", "");
            listStatus = (Label)c3.Tag;
            listBtn = Btn(c3, "Open contact list", 50, 50, 140, delegate { ListClicked(); }, false);
            importBtn = Btn(c3, "Use another Excel file…", 196, 50, 175, delegate { ImportClicked(); }, false);
            picsBtn = Btn(c3, "Pictures folder", 377, 50, 120, delegate { OpenWith("explorer.exe", P("pics")); }, false);

            // 4. Check
            var c4 = Card(384, 86, 4, "Check the list", "Sends nothing. Finds bad numbers and missing pictures, and shows how long sending takes.");
            checkBtn = Btn(c4, "Check list", 50, 50, 110, delegate { CheckClicked(); }, true);
            checkStatus = Status(c4, 170, 55, 380);

            // 5. Send
            var c5 = Card(476, 136, 5, "Send", null);
            var gapLabel = new Label { Text = "Wait between chats:", Location = new Point(50, 36), AutoSize = true, BackColor = CardBack };
            gapBox = new NumericUpDown { Location = new Point(178, 33), Size = new Size(60, 24), Minimum = 5, Maximum = 600, TextAlign = HorizontalAlignment.Center };
            var secLabel = new Label { Text = "seconds", Location = new Point(243, 36), AutoSize = true, BackColor = CardBack };
            gapSaveBtn = Btn(c5, "Save", 310, 31, 70, delegate { SaveGap(); }, false);
            gapSaveBtn.Height = 26;
            c5.Controls.AddRange(new Control[] { gapLabel, gapBox, secLabel });
            sendBtn = Btn(c5, "Send now", 50, 66, 110, delegate { SendClicked(); }, true);
            stopBtn = Btn(c5, "Stop", 166, 66, 80, delegate { StopClicked(); }, false);
            reportBtn = Btn(c5, "Open send report", 252, 66, 140, delegate { ReportClicked(); }, false);
            progress = new ProgressBar { Location = new Point(50, 102), Size = new Size(496, 10) };
            sendStatus = Status(c5, 50, 114, 500);
            c5.Controls.Add(progress);

            // Details
            var detailsLabel = new Label { Text = "Details", Location = new Point(592, 68), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            logBox = new TextBox
            {
                Location = new Point(592, 92), Size = new Size(374, 520), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(250, 250, 250), Font = new Font("Consolas", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.AddRange(new Control[] { detailsLabel, logBox });
            ResumeLayout(false);
            PerformLayout();

            timer = new Timer { Interval = 2000 };
            timer.Tick += delegate { UpdateChromeStatus(); UpdateListStatus(); };

            if (!File.Exists(P("send.js")))
            {
                MessageBox.Show("This app must stay in the WhatsApp Blast folder, next to send.js.\n\nMove it back there and open it again.",
                    "WhatsApp Blast", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Load += delegate { Close(); };
                return;
            }

            CheckNode();
            UpdateInstallStatus();
            UpdateChromeStatus();
            UpdateListStatus();
            ReadGap();
            SetBusy(false);
            stopBtn.Enabled = false;
            timer.Start();
            Log("Ready. Go through the steps on the left in order.");
        }

        // ---------- building the window ----------

        Panel Card(int y, int height, int number, string title, string desc)
        {
            var card = new Panel { Location = new Point(16, y), Size = new Size(560, height), BackColor = CardBack };
            var badge = new Badge { Text = number.ToString(), Location = new Point(12, 10), Size = new Size(28, 28), BackColor = CardBack, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            var t = new Label { Text = title, Location = new Point(50, 12), AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), BackColor = CardBack };
            card.Controls.Add(badge);
            card.Controls.Add(t);
            if (desc != null)
            {
                var d = new Label { Text = desc, Location = new Point(50, 33), Size = new Size(500, height > 100 ? 52 : 17), ForeColor = Grey, BackColor = CardBack };
                card.Controls.Add(d);
                card.Tag = d;
                fitToCard.Add(d);
            }
            Controls.Add(card);
            return card;
        }

        Button Btn(Control parent, string text, int x, int y, int w, EventHandler click, bool primary)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30), UseVisualStyleBackColor = !primary };
            if (primary)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Green;
                b.ForeColor = Color.White;
                b.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                b.EnabledChanged += delegate { b.BackColor = b.Enabled ? Green : PaleGreen; };
            }
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        Label Status(Control parent, int x, int y, int w)
        {
            var l = new Label { Location = new Point(x, y), Size = new Size(w, 19), AutoEllipsis = true, BackColor = CardBack, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            parent.Controls.Add(l);
            fitToCard.Add(l);
            return l;
        }

        // Labels end 10 px before the card's right edge, whatever the screen scaling.
        readonly System.Collections.Generic.List<Label> fitToCard = new System.Collections.Generic.List<Label>();

        int S(int v) { return (int)Math.Round(v * AutoScaleFactor.Width); }

        void FitLabels()
        {
            foreach (var l in fitToCard)
                l.Width = Math.Max(S(40), l.Parent.ClientSize.Width - l.Left - S(10));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateInstallStatus();
        }

        static void SetStatus(Label l, string text, Color color)
        {
            l.Text = text;
            l.ForeColor = color;
        }

        string P(string name) { return Path.Combine(dir, name); }

        void Log(string line)
        {
            logBox.AppendText(line + "\r\n");
        }

        void Msg(string text, MessageBoxIcon icon)
        {
            MessageBox.Show(this, text, "WhatsApp Blast", MessageBoxButtons.OK, icon);
        }

        bool Ask(string text, MessageBoxIcon icon)
        {
            return MessageBox.Show(this, text, "WhatsApp Blast", MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void OpenWith(string program, string file)
        {
            try
            {
                if (file == null) Process.Start(new ProcessStartInfo(program) { UseShellExecute = true });
                else Process.Start(program, "\"" + file + "\"");
            }
            catch (Exception ex) { Msg("Could not open it: " + ex.Message, MessageBoxIcon.Warning); }
        }

        void OpenFile(string file)
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex) { Msg("Could not open " + Path.GetFileName(file) + ": " + ex.Message, MessageBoxIcon.Warning); }
        }

        void SetBusy(bool busy)
        {
            foreach (var b in new Control[] { installBtn, listBtn, importBtn, checkBtn, sendBtn, gapBox })
                b.Enabled = !busy;
            gapSaveBtn.Enabled = !busy && savedGap >= 0;
            stopBtn.Enabled = busy && runningTask == "send";
            if (savedGap < 0) gapBox.Enabled = false;
        }

        // ---------- running the .bat files ----------

        void Run(string task, string arguments, Action<int> done)
        {
            var psi = new ProcessStartInfo("cmd.exe", arguments)
            {
                WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["FROM_APP"] = "1";
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            output.Clear();
            DataReceivedEventHandler onLine = (s, e) =>
            {
                if (e.Data == null) return;
                try { BeginInvoke(new Action<string>(OnLine), e.Data); } catch (InvalidOperationException) { }
            };
            p.OutputDataReceived += onLine;
            p.ErrorDataReceived += onLine;
            p.Exited += delegate
            {
                p.WaitForExit(); // wait for the last lines of output
                int code = p.ExitCode;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        running = null;
                        runningTask = null;
                        p.Dispose();
                        SetBusy(false);
                        done(code);
                    }));
                }
                catch (InvalidOperationException) { }
            };
            Log("");
            Log("=== " + DateTime.Now.ToString("HH:mm:ss") + " " + task + " ===");
            running = p;
            runningTask = task;
            SetBusy(true);
            try { p.Start(); }
            catch (Exception ex)
            {
                running = null; runningTask = null; SetBusy(false);
                Log("Could not start: " + ex.Message);
                done(-1);
                return;
            }
            p.StandardInput.Close(); // so a "pause" can never wait for a key
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }

        void RunBat(string bat, string task, Action<int> done)
        {
            Run(task, "/d /s /c \"\"" + P(bat) + "\"\"", done);
        }

        void RunTool(string args, Action<int> done)
        {
            Run("tool", "/d /c node send.js " + args, done);
        }

        void OnLine(string line)
        {
            Log(line);
            output.AppendLine(line);
            if (runningTask == "send") SendLine(line);
        }

        static void KillTree(int pid)
        {
            try
            {
                var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
                k.WaitForExit(5000);
            }
            catch { }
        }

        // ---------- 1. install ----------

        void CheckNode()
        {
            nodeFound = false;
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/d /c node -v") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi))
                {
                    string v = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(5000);
                    var m = Regex.Match(v, @"^v(\d+)\.");
                    if (!m.Success) nodeProblem = "Node.js is not installed. Click Get Node.js, install it, then reopen this app.";
                    else if (int.Parse(m.Groups[1].Value) < 18) nodeProblem = "Node.js " + v + " is too old. Install a newer one, then reopen this app.";
                    else nodeFound = true;
                }
            }
            catch { nodeProblem = "Node.js is not installed. Click Get Node.js, install it, then reopen this app."; }
        }

        bool Installed()
        {
            return Directory.Exists(P(@"node_modules\whatsapp-web.js")) && Directory.Exists(P(@"node_modules\xlsx"));
        }

        void UpdateInstallStatus()
        {
            nodeBtn.Visible = !nodeFound;
            installStatus.Left = (nodeFound ? installBtn.Right : nodeBtn.Right) + S(10);
            FitLabels();
            if (!nodeFound) SetStatus(installStatus, nodeProblem, Red);
            else if (Installed()) SetStatus(installStatus, "✔ Installed", Green);
            else SetStatus(installStatus, "Not installed yet: click Install.", Orange);
        }

        bool ReadyToRun()
        {
            if (!nodeFound) { Msg(nodeProblem, MessageBoxIcon.Warning); return false; }
            if (!Installed()) { Msg("Click Install in step 1 first.", MessageBoxIcon.Information); return false; }
            return true;
        }

        void InstallClicked()
        {
            if (!nodeFound) { Msg(nodeProblem, MessageBoxIcon.Warning); return; }
            SetStatus(installStatus, "Installing… (about a minute)", Grey);
            RunBat("1-INSTALL.bat", "install", code =>
            {
                UpdateInstallStatus();
                if (code != 0 || !Installed())
                    SetStatus(installStatus, "Install failed. Check the internet and see Details.", Red);
            });
        }

        // ---------- 2. Chrome ----------

        static bool ChromeConnected()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            if (!File.Exists(Path.Combine(local, @"Google\Chrome\User Data\DevToolsActivePort"))) return false;
            var list = Process.GetProcessesByName("chrome");
            foreach (var p in list) p.Dispose();
            return list.Length > 0;
        }

        void UpdateChromeStatus()
        {
            if (ChromeConnected()) SetStatus(chromeStatus, "● Connected: the tool can use your Chrome", Green);
            else SetStatus(chromeStatus, "● Not connected yet", Red);
        }

        static string FindChrome()
        {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"))
                    {
                        var v = k == null ? null : k.GetValue("") as string;
                        if (!string.IsNullOrEmpty(v) && File.Exists(v.Trim('"'))) return v.Trim('"');
                    }
                }
                catch { }
            }
            foreach (var env in new[] { "ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA" })
            {
                string f = Path.Combine(Environment.GetEnvironmentVariable(env) ?? "", @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(f)) return f;
            }
            return null;
        }

        void ChromeClicked()
        {
            string chrome = FindChrome();
            if (chrome == null)
            {
                Msg("Google Chrome was not found. Install Chrome and log in to WhatsApp Web first.", MessageBoxIcon.Warning);
                return;
            }
            // Chrome does not let other programs open its chrome:// pages, so the address is pasted by hand.
            try { Clipboard.SetText(ChromeSettingUrl); } catch { }
            string text = (ChromeConnected() ? "Chrome is already connected. You can use this page to untick the setting after sending.\n\n" : "") +
                "Chrome opens a new tab when you click OK. The address of the setting page is already copied.\n\n" +
                "1. In the new tab, press Ctrl+V, then Enter.\n" +
                "2. Tick “Allow remote debugging for this browser instance”.\n" +
                "3. Come back here. Step 2 turns green.\n\n" +
                "Use the Chrome window that is logged in to WhatsApp Web.";
            if (MessageBox.Show(this, text, "Connect your Chrome", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            try { Clipboard.SetText(ChromeSettingUrl); } catch { }
            try { Process.Start(chrome, ChromeSettingUrl); }
            catch (Exception ex) { Msg("Could not open Chrome: " + ex.Message, MessageBoxIcon.Warning); return; }
            Log("Opened a Chrome tab. Paste " + ChromeSettingUrl + " (Ctrl+V) and press Enter.");
        }

        // ---------- 3. contact list ----------

        void UpdateListStatus()
        {
            const string cols = "Columns: phone, image (a picture in the pics folder), caption.";
            if (File.Exists(P("contacts.xlsx"))) SetStatus(listStatus, "Using contacts.xlsx. " + cols, Grey);
            else if (File.Exists(P("contacts.csv"))) SetStatus(listStatus, "Using contacts.csv. " + cols, Grey);
            else SetStatus(listStatus, "No list yet: click Open contact list to make one.", Orange);
        }

        void ListClicked()
        {
            string xlsx = P("contacts.xlsx"), csv = P("contacts.csv");
            if (File.Exists(xlsx)) { OpenFile(xlsx); return; }
            if (!ReadyToRun()) return;
            if (File.Exists(csv))
            {
                var a = MessageBox.Show(this,
                    "Your list is contacts.csv. When Excel saves a CSV file it can damage long phone numbers (for example 6.01E+10).\n\n" +
                    "Make an Excel copy, contacts.xlsx, and use it from now on? Your CSV is kept as contacts-before-excel.csv.\n\n" +
                    "Yes: make the Excel copy and open it\nNo: open the CSV in Notepad instead",
                    "Contact list", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (a == DialogResult.No) { OpenWith("notepad.exe", csv); return; }
                if (a != DialogResult.Yes) return;
                RunTool("--to-excel", code =>
                {
                    UpdateListStatus();
                    if (File.Exists(xlsx)) OpenFile(xlsx);
                    else Msg("Could not make the Excel copy. Your CSV was not changed. See Details.", MessageBoxIcon.Warning);
                });
                return;
            }
            RunTool("--new-excel", code =>
            {
                UpdateListStatus();
                if (File.Exists(xlsx))
                {
                    OpenFile(xlsx);
                    Msg("A new contact list is open in Excel with 2 example rows.\n\nReplace them with your own, put the pictures in the pics folder, then save (Ctrl+S).",
                        MessageBoxIcon.Information);
                }
                else Msg("Could not make the list. See Details.", MessageBoxIcon.Warning);
            });
        }

        void ImportClicked()
        {
            string xlsx = P("contacts.xlsx");
            string chosen;
            using (var dlg = new OpenFileDialog { Title = "Choose your contact list", Filter = "Excel files (*.xlsx)|*.xlsx" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                chosen = dlg.FileName;
            }
            if (string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(xlsx), StringComparison.OrdinalIgnoreCase))
            {
                Msg("That is already the list in use.", MessageBoxIcon.Information);
                return;
            }
            string backup = null;
            try
            {
                if (File.Exists(xlsx))
                {
                    backup = P("contacts-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xlsx");
                    File.Move(xlsx, backup);
                }
                File.Copy(chosen, xlsx);
            }
            catch (IOException ex)
            {
                Msg("Could not copy the file. If contacts.xlsx is open in Excel, close it and try again.\n\n" + ex.Message, MessageBoxIcon.Warning);
                return;
            }
            UpdateListStatus();
            Log("Copied " + chosen + " to contacts.xlsx" + (backup == null ? "." : " (old list kept as " + Path.GetFileName(backup) + ")."));
            string note = backup == null ? "" : "\n\nYour old list is kept as " + Path.GetFileName(backup) + ".";
            if (File.Exists(P("contacts.csv"))) note += "\n\ncontacts.xlsx is now used instead of contacts.csv.";
            Msg("Your Excel file is now the contact list (contacts.xlsx)." + note + "\n\nThe list will be checked now.", MessageBoxIcon.Information);
            if (Installed() && nodeFound) CheckClicked();
        }

        bool ListSaved()
        {
            if (!File.Exists(P("~$contacts.xlsx"))) return true;
            return Ask("contacts.xlsx is open in Excel.\n\nSave it (Ctrl+S) first, or your latest changes will not be used.\n\nContinue anyway?", MessageBoxIcon.Question);
        }

        // ---------- 4. check ----------

        static CheckResult ParseCheck(string text)
        {
            var r = new CheckResult();
            r.NoList = text.Contains("No contacts.xlsx or contacts.csv found");
            var m = Regex.Match(text, @"List: (.+?) - (\d+) rows");
            if (m.Success) { r.ListName = m.Groups[1].Value; r.Rows = int.Parse(m.Groups[2].Value); }
            m = Regex.Match(text, @"(\d+) row\(s\) ready to send");
            if (m.Success) r.Ready = int.Parse(m.Groups[1].Value);
            m = Regex.Match(text, @"(\d+) row\(s\) have problems");
            if (m.Success) r.Problems = int.Parse(m.Groups[1].Value);
            m = Regex.Match(text, @"about (\d+) minute");
            if (m.Success) r.Minutes = int.Parse(m.Groups[1].Value);
            return r;
        }

        void ShowCheck(CheckResult r)
        {
            if (r.NoList) SetStatus(checkStatus, "No contact list yet. Use step 3.", Red);
            else if (r.Ready < 0) SetStatus(checkStatus, "The check did not finish. See Details.", Red);
            else
            {
                string s = r.Ready + " ready";
                if (r.Problems > 0) s += " · " + r.Problems + " with problems (see Details)";
                s += " · about " + r.Minutes + " min";
                SetStatus(checkStatus, (r.Problems > 0 ? "" : "✔ ") + s, r.Problems > 0 || r.Ready == 0 ? Orange : Green);
            }
        }

        void CheckClicked()
        {
            if (!ReadyToRun() || !ListSaved()) return;
            SetStatus(checkStatus, "Checking…", Grey);
            RunBat("2-CHECK-LIST.bat", "check", code => ShowCheck(ParseCheck(output.ToString())));
        }

        // ---------- 5. send ----------

        void ReadGap()
        {
            try
            {
                var m = Regex.Match(File.ReadAllText(P("send.js"), Encoding.UTF8), @"const GAP_SECONDS = (\d+);");
                if (m.Success)
                {
                    savedGap = int.Parse(m.Groups[1].Value);
                    gapBox.Value = Math.Max(gapBox.Minimum, Math.Min(gapBox.Maximum, savedGap));
                }
            }
            catch { savedGap = -1; }
        }

        bool SaveGap()
        {
            int v = (int)gapBox.Value;
            if (v < 20 && !Ask("A wait shorter than 20 seconds raises the risk of WhatsApp banning the number.\n\nSave " + v + " seconds anyway?", MessageBoxIcon.Warning))
                return false;
            try
            {
                string file = P("send.js");
                string text = File.ReadAllText(file, Encoding.UTF8);
                string updated = Regex.Replace(text, @"const GAP_SECONDS = \d+;", "const GAP_SECONDS = " + v + ";");
                if (updated == text && !text.Contains("const GAP_SECONDS = " + v + ";")) throw new Exception("setting not found in send.js");
                File.WriteAllText(file, updated, new UTF8Encoding(false));
                savedGap = v;
                SetStatus(sendStatus, "Saved: each chat now waits " + v + " seconds.", Green);
                Log("Wait between chats set to " + v + " seconds.");
                return true;
            }
            catch (Exception ex)
            {
                Msg("Could not save the wait time: " + ex.Message, MessageBoxIcon.Warning);
                return false;
            }
        }

        int CountSentBefore()
        {
            try
            {
                int n = 0;
                foreach (var line in File.ReadAllLines(P("sent-log.csv")))
                    if (line.Contains(",SENT,")) n++;
                return n;
            }
            catch { return 0; }
        }

        void SendClicked()
        {
            if (!ReadyToRun()) return;
            if (savedGap >= 0 && (int)gapBox.Value != savedGap && !SaveGap()) return;
            if (!ChromeConnected())
            {
                Msg("Chrome is not connected yet.\n\nDo step 2 first: in your logged-in Chrome, tick “Allow remote debugging for this browser instance”.", MessageBoxIcon.Warning);
                return;
            }
            if (!ListSaved()) return;
            SetStatus(sendStatus, "Checking the list first…", Grey);
            RunTool("--check", code =>
            {
                var r = ParseCheck(output.ToString());
                ShowCheck(r);
                if (r.NoList || r.Ready <= 0)
                {
                    SetStatus(sendStatus, "Nothing to send.", Orange);
                    Msg("There is nothing ready to send. Fix the list (see Details) and try again.", MessageBoxIcon.Information);
                    return;
                }
                int before = CountSentBefore();
                string text = "Send " + r.Ready + " picture(s) now to the people in " + r.ListName + "?\n\n" +
                    "This messages real people and cannot be undone. It takes about " + r.Minutes + " minute(s).";
                if (r.Problems > 0) text += "\n\n" + r.Problems + " row(s) with problems will be skipped.";
                if (before > 0) text += "\n\nThe send report already lists " + before + " sent picture(s). Those are not sent again.";
                text += "\n\nBefore you click Yes:\n• Close any WhatsApp Web tab you have open\n• Click your logged-in Chrome window once\n\n" +
                    "Keep this window open and the PC on until it says Finished.";
                if (!Ask(text, MessageBoxIcon.Warning))
                {
                    SetStatus(sendStatus, "Not sent.", Grey);
                    return;
                }
                StartSend(r.Ready);
            });
        }

        void StartSend(int total)
        {
            sendTotal = total;
            sent = failed = notOnWa = alreadySent = 0;
            stopping = false;
            progress.Maximum = Math.Max(total, 1);
            progress.Value = 0;
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED); // keep the PC awake while sending
            SetStatus(sendStatus, "Opening WhatsApp in your Chrome… click Allow if Chrome asks.", Grey);
            RunBat("3-SEND.bat", "send", SendDone);
        }

        void SendLine(string line)
        {
            if (line.Contains("): SENT ")) sent++;
            else if (line.Contains("): FAILED")) failed++;
            else if (line.Contains("): not on WhatsApp")) notOnWa++;
            else if (line.Contains("already sent before - skipped")) alreadySent++;
            else if (line.Contains("Still waiting for WhatsApp"))
            {
                SetStatus(sendStatus, "Waiting for WhatsApp: look at the new tab in Chrome.", Orange);
                return;
            }
            else if (!line.Contains("Logged in.") && !line.Contains("waiting ")) return;

            int done = sent + failed + notOnWa + alreadySent;
            progress.Value = Math.Min(done, progress.Maximum);
            string s = "Sending… " + done + " of " + sendTotal + " · sent " + sent;
            if (failed + notOnWa > 0) s += " · failed " + (failed + notOnWa);
            if (alreadySent > 0) s += " · already sent " + alreadySent;
            SetStatus(sendStatus, s, Green);
        }

        void SendDone(int code)
        {
            SetThreadExecutionState(ES_CONTINUOUS);
            var m = Regex.Match(output.ToString(), @"Finished\. Sent (\d+), skipped (\d+), failed (\d+)");
            if (m.Success)
            {
                progress.Value = progress.Maximum;
                string s = "Finished: sent " + m.Groups[1].Value + ", failed " + m.Groups[3].Value + ", skipped " + m.Groups[2].Value + ".";
                SetStatus(sendStatus, s, m.Groups[3].Value == "0" ? Green : Orange);
                Msg(s + "\n\nEach person's result is in the send report.\n\nRemember to untick “Allow remote debugging” in Chrome.", MessageBoxIcon.Information);
            }
            else if (stopping)
            {
                SetStatus(sendStatus, "Stopped. Click Send now later to continue; sent pictures are skipped.", Orange);
            }
            else
            {
                SetStatus(sendStatus, "Sending did not finish. See Details.", Red);
                Msg("Sending did not finish. See Details on the right.\n\nYou can click Send now again: pictures already sent are skipped.", MessageBoxIcon.Warning);
            }
            stopping = false;
        }

        void StopClicked()
        {
            if (running == null || runningTask != "send") return;
            if (!Ask("Stop sending?\n\nPictures already sent stay sent. Click Send now later to continue with the rest.", MessageBoxIcon.Question)) return;
            stopping = true;
            if (running != null) KillTree(running.Id);
            Log("Stopped by you. You can close the WhatsApp tab the tool opened.");
        }

        void ReportClicked()
        {
            if (File.Exists(P("sent-log.csv"))) OpenFile(P("sent-log.csv"));
            else Msg("There is no send report yet. It is made when you send.", MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running != null)
            {
                string q = runningTask == "send"
                    ? "Sending is still running. Stop it and close?\n\nPictures already sent stay sent."
                    : "The tool is still working. Stop it and close?";
                if (!Ask(q, MessageBoxIcon.Warning)) { e.Cancel = true; return; }
                try { KillTree(running.Id); } catch { }
                SetThreadExecutionState(ES_CONTINUOUS);
            }
            if (timer != null) timer.Stop();
            base.OnFormClosing(e);
        }
    }
}
