using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AnPlayApp
{
    public class MacroEvent
    {
        public string Type { get; set; }
        public int DelayMs { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Button { get; set; }
        public int Delta { get; set; }
        public int Vk { get; set; }
    }

    public class MacroDocument
    {
        public int Version { get; set; }
        public string Name { get; set; }
        public string CreatedUtc { get; set; }
        public List<MacroEvent> Events { get; set; }

        public MacroDocument()
        {
            Version = 2;
            Name = "AnPlay Macro";
            CreatedUtc = DateTime.UtcNow.ToString("o");
            Events = new List<MacroEvent>();
        }
    }

    public class AppSettings
    {
        public string Speed { get; set; }
        public bool LoopPlay { get; set; }
        public int MaxLoops { get; set; }
        public bool SmoothMouse { get; set; }
    }

    public class MainForm : Form
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int VK_F8 = 0x77;
        private const int VK_SNAPSHOT = 0x2C;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x01000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private readonly object sync = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Stopwatch stopwatch = new Stopwatch();
        private MacroDocument macro = new MacroDocument();
        private IntPtr keyboardHook = IntPtr.Zero;
        private IntPtr mouseHook = IntPtr.Zero;
        private LowLevelKeyboardProc keyboardProc;
        private LowLevelMouseProc mouseProc;
        private bool isRecording;
        private bool isPlaying;
        private bool cancelRequested;
        private bool loadingSettings;
        private int lastEventMs;
        private int lastMoveMs;
        private int lastHotkeyMs;
        private Point lastMovePoint = Point.Empty;
        private System.Windows.Forms.Timer settingsTimer;

        private GradientPanel hero;
        private Panel cardControls;
        private Panel cardSettings;
        private Panel cardLog;
        private Panel statusPill;
        private Button btnRecord;
        private Button btnPlay;
        private Button btnStop;
        private Button btnSave;
        private Button btnLoad;
        private Label lblStatus;
        private Label lblCount;
        private Label lblMode;
        private ComboBox cmbSpeed;
        private NumericUpDown numMaxLoops;
        private CheckBox chkLoopPlay;
        private CheckBox chkSmooth;
        private TextBox txtLog;

        private Theme theme;

        public MainForm()
        {
            Text = "AnPlay";
            ClientSize = new Size(820, 570);
            MinimumSize = new Size(836, 609);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            theme = Theme.Dark();
            keyboardProc = KeyboardHookCallback;
            mouseProc = MouseHookCallback;
            BuildUi();
            LoadSettings();
            ApplyTheme();
            InstallKeyboardHook();
            RefreshState("Ready. F8 langsung rekam/stop. PrtSc play/stop.");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            cancelRequested = true;
            StopMouseHook();
            StopKeyboardHook();
            base.OnFormClosing(e);
        }

        private void BuildUi()
        {
            BackColor = theme.Background;
            Controls.Clear();

            hero = new GradientPanel
            {
                Left = 16,
                Top = 14,
                Width = 788,
                Height = 104,
                Radius = 14,
                ColorA = theme.HeroA,
                ColorB = theme.HeroB
            };
            Controls.Add(hero);

            var badge = new LogoPanel { Left = 18, Top = 20, Width = 54, Height = 54 };
            hero.Controls.Add(badge);

            var title = new Label
            {
                Left = 86,
                Top = 18,
                Width = 360,
                Height = 30,
                Text = "AnPlay",
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
                ForeColor = Color.White
            };
            hero.Controls.Add(title);

            var subtitle = new Label
            {
                Left = 90,
                Top = 55,
                Width = 500,
                Height = 22,
                Text = "Rekam aksi, replay halus, tetap ringan dan 100% offline.",
                ForeColor = Color.FromArgb(214, 226, 238),
                Font = new Font("Segoe UI", 9.5F)
            };
            hero.Controls.Add(subtitle);

            statusPill = new Panel { Left = 638, Top = 31, Width = 126, Height = 42 };
            statusPill.Paint += PaintStatusPill;
            hero.Controls.Add(statusPill);

            lblMode = new Label
            {
                Left = 0,
                Top = 10,
                Width = 126,
                Height = 22,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "READY",
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.White
            };
            statusPill.Controls.Add(lblMode);

            cardControls = MakeCard(16, 134, 788, 126);
            Controls.Add(cardControls);

            btnRecord = MakeButton("Rekam F8", 18, 18, 150, 46, theme.Good, OnRecordClick);
            cardControls.Controls.Add(btnRecord);
            btnPlay = MakeButton("Play PrtSc", 180, 18, 150, 46, theme.Primary, OnPlayClick);
            cardControls.Controls.Add(btnPlay);
            btnStop = MakeButton("Stop", 342, 18, 110, 46, theme.Danger, OnStopClick);
            cardControls.Controls.Add(btnStop);
            btnSave = MakeButton("Simpan", 464, 18, 106, 46, theme.Secondary, OnSaveClick);
            cardControls.Controls.Add(btnSave);
            btnLoad = MakeButton("Load", 582, 18, 106, 46, theme.Secondary, OnLoadClick);
            cardControls.Controls.Add(btnLoad);

            lblCount = new Label
            {
                Left = 20,
                Top = 76,
                Width = 360,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
            };
            cardControls.Controls.Add(lblCount);

            var hint = new Label
            {
                Left = 394,
                Top = 76,
                Width = 362,
                Height = 28,
                Text = "F8 rekam/stop. PrtSc play/stop.",
                TextAlign = ContentAlignment.MiddleRight
            };
            cardControls.Controls.Add(hint);

            cardSettings = MakeCard(16, 276, 788, 152);
            Controls.Add(cardSettings);

            AddLabel(cardSettings, "Kecepatan", 22, 22, 82);
            cmbSpeed = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = 112,
                Top = 18,
                Width = 98,
                Height = 28
            };
            cmbSpeed.Items.AddRange(new object[] { "0.5", "1", "1.5", "2", "3", "5" });
            cmbSpeed.SelectedItem = "1";
            cmbSpeed.SelectedIndexChanged += OnSettingChanged;
            cardSettings.Controls.Add(cmbSpeed);

            chkLoopPlay = MakeCheckBox("Loop replay", 242, 18, 130);
            chkLoopPlay.CheckedChanged += OnSettingChanged;
            cardSettings.Controls.Add(chkLoopPlay);

            AddLabel(cardSettings, "Batas loop", 408, 22, 82);
            numMaxLoops = new NumericUpDown
            {
                Left = 500,
                Top = 18,
                Width = 82,
                Minimum = 0,
                Maximum = 9999,
                Value = 0
            };
            numMaxLoops.ValueChanged += OnSettingChanged;
            cardSettings.Controls.Add(numMaxLoops);

            AddLabel(cardSettings, "0 = tanpa batas", 594, 22, 128);

            chkSmooth = MakeCheckBox("Gerak cursor halus", 22, 70, 180);
            chkSmooth.Checked = true;
            chkSmooth.CheckedChanged += OnSettingChanged;
            cardSettings.Controls.Add(chkSmooth);

            var themeBadge = new Label
            {
                Left = 242,
                Top = 70,
                Width = 154,
                Height = 26,
                Text = "Tema Dark Focus",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
            themeBadge.Paint += PaintMiniPill;
            cardSettings.Controls.Add(themeBadge);

            var staticNote = new Label
            {
                Left = 430,
                Top = 70,
                Width = 312,
                Height = 30,
                Text = "Mode offline: tidak pakai AI, API key, atau internet.",
                TextAlign = ContentAlignment.MiddleRight
            };
            cardSettings.Controls.Add(staticNote);

            cardLog = MakeCard(16, 444, 788, 86);
            Controls.Add(cardLog);

            txtLog = new TextBox
            {
                Left = 14,
                Top = 14,
                Width = 760,
                Height = 58,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical
            };
            cardLog.Controls.Add(txtLog);

            lblStatus = new Label
            {
                Left = 18,
                Top = 538,
                Width = 768,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblStatus);

            settingsTimer = new System.Windows.Forms.Timer { Interval = 500 };
            settingsTimer.Tick += delegate { settingsTimer.Stop(); SaveSettings(); };
        }

        private Panel MakeCard(int left, int top, int width, int height)
        {
            var panel = new Panel { Left = left, Top = top, Width = width, Height = height };
            panel.Paint += PaintCard;
            return panel;
        }

        private Button MakeButton(string text, int left, int top, int width, int height, Color color, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color);
            button.Click += handler;
            return button;
        }

        private CheckBox MakeCheckBox(string text, int left, int top, int width)
        {
            return new CheckBox
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 26,
                FlatStyle = FlatStyle.Flat
            };
        }

        private void AddLabel(Control parent, string text, int left, int top, int width)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }

        private void PaintCard(object sender, PaintEventArgs e)
        {
            var panel = (Panel)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using (var path = RoundedRect(rect, 10))
            using (var brush = new SolidBrush(theme.Card))
            using (var pen = new Pen(theme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void PaintStatusPill(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, statusPill.Width - 1, statusPill.Height - 1);
            Color fill = isRecording ? theme.Good : isPlaying ? theme.Primary : theme.Danger;
            using (var path = RoundedRect(rect, 21))
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        private void PaintMiniPill(object sender, PaintEventArgs e)
        {
            var label = (Label)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, label.Width - 1, label.Height - 1);
            using (var path = RoundedRect(rect, 13))
            using (var brush = new LinearGradientBrush(rect, theme.Primary, theme.Good, 0F))
            {
                e.Graphics.FillPath(brush, path);
            }
            TextRenderer.DrawText(e.Graphics, label.Text, label.Font, rect, label.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void ApplyTheme()
        {
            theme = Theme.Dark();
            BackColor = theme.Background;
            if (hero != null)
            {
                hero.ColorA = theme.HeroA;
                hero.ColorB = theme.HeroB;
                hero.Invalidate();
            }
            ApplyControlTheme(this);
            Refresh();
        }

        private void ApplyControlTheme(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (control is Label && control != lblMode)
                {
                    control.ForeColor = control.Parent == hero && control.Text == "AnPlay" ? Color.White : control.Parent == hero ? Color.FromArgb(214, 226, 238) : theme.Text;
                    control.BackColor = Color.Transparent;
                }
                if (control is Label && control.Text == "Tema Dark Focus")
                {
                    control.ForeColor = Color.White;
                }
                else if (control is CheckBox)
                {
                    control.ForeColor = theme.Text;
                    control.BackColor = Color.Transparent;
                }
                else if (control is ComboBox || control is NumericUpDown)
                {
                    control.ForeColor = theme.Text;
                    control.BackColor = theme.Input;
                }
                else if (control is TextBox)
                {
                    control.ForeColor = theme.Text;
                    control.BackColor = theme.Input;
                }
                ApplyControlTheme(control);
            }
            if (lblStatus != null) lblStatus.ForeColor = theme.Muted;
            if (lblCount != null) lblCount.ForeColor = theme.Text;
            if (txtLog != null)
            {
                txtLog.BackColor = theme.Input;
                txtLog.ForeColor = theme.Muted;
            }
            if (cardControls != null) cardControls.Invalidate();
            if (cardSettings != null) cardSettings.Invalidate();
            if (cardLog != null) cardLog.Invalidate();
        }

        private void OnRecordClick(object sender, EventArgs e)
        {
            if (isRecording) StopRecording();
            else if (!isPlaying) StartRecording();
        }

        private void StartRecording()
        {
            lock (sync)
            {
                if (isRecording) return;
                macro = new MacroDocument();
                lastEventMs = 0;
                lastMoveMs = 0;
                lastMovePoint = Point.Empty;
                stopwatch.Reset();
                stopwatch.Start();
                cancelRequested = false;
                isRecording = true;
                InstallMouseHook();
            }
            AppendLogSafe("Recording started instantly.");
            RefreshState("Recording. Press F8 again to stop.");
        }

        private void StopRecording()
        {
            lock (sync)
            {
                if (!isRecording) return;
                isRecording = false;
                stopwatch.Stop();
                StopMouseHook();
                macro.Events = OptimizeRecordedEvents(macro.Events);
            }
            AppendLogSafe("Recorded " + macro.Events.Count + " events.");
            RefreshState("Recording stopped. Press PrtSc to replay.");
        }

        private void OnPlayClick(object sender, EventArgs e)
        {
            if (isRecording) return;
            if (isPlaying) StopActiveWork();
            else StartPlaybackFromSettings();
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (isRecording)
            {
                StopRecording();
                return;
            }
            if (isPlaying) StopActiveWork();
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "AnPlay macro (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = "macro.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, json.Serialize(macro), Encoding.UTF8);
                    RefreshState("Macro tersimpan: " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    RefreshState("Save gagal: " + ex.Message);
                    AppendLogSafe(ex.ToString());
                }
            }
        }

        private void OnLoadClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "AnPlay macro (*.json)|*.json|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    MacroDocument loaded = json.Deserialize<MacroDocument>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                    if (loaded == null || loaded.Events == null) throw new InvalidDataException("Invalid macro file.");
                    macro = loaded;
                    RefreshState("Macro dimuat: " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    RefreshState("Load gagal: " + ex.Message);
                    AppendLogSafe(ex.ToString());
                }
            }
        }

        private void StartPlaybackFromSettings()
        {
            if (macro.Events.Count == 0)
            {
                RefreshState("No macro. Record with F8 or load a JSON first.");
                return;
            }
            SaveSettings();
            cancelRequested = false;
            int loops = chkLoopPlay.Checked ? (int)numMaxLoops.Value : 1;
            double speed = GetSelectedSpeed();
            bool smooth = chkSmooth.Checked;
            isPlaying = true;
            RefreshState(chkLoopPlay.Checked ? "Loop playback running. PrtSc stops." : "Playback running. PrtSc stops.");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    PlayMacroLoop(loops, speed, smooth);
                    RefreshStateSafe(cancelRequested ? "Playback stopped." : "Playback finished.");
                }
                catch (Exception ex)
                {
                    RefreshStateSafe("Playback error: " + ex.Message);
                    AppendLogSafe(ex.ToString());
                }
                finally
                {
                    isPlaying = false;
                    RefreshStateSafe(cancelRequested ? "Playback stopped." : "Ready.");
                }
            });
        }

        private void StopActiveWork()
        {
            cancelRequested = true;
            RefreshState("Stop requested.");
        }

        private void PlayMacroLoop(int requestedLoops, double speed, bool smooth)
        {
            int count = 0;
            while (!cancelRequested)
            {
                count++;
                SetStatusSafe(requestedLoops == 0 ? "Loop " + count + " running." : "Loop " + count + "/" + requestedLoops + " running.");
                PlayMacroOnce(speed, smooth);
                if (requestedLoops > 0 && count >= requestedLoops) break;
            }
        }

        private void PlayMacroOnce(double speed, bool smooth)
        {
            List<MacroEvent> events;
            lock (sync) events = new List<MacroEvent>(macro.Events);

            foreach (var ev in events)
            {
                if (cancelRequested) break;
                int delay = AdjustDelay(ev.DelayMs, speed);
                if (ev.Type == "Move")
                {
                    if (smooth) SmoothMoveTo(ev.X, ev.Y, delay);
                    else
                    {
                        SleepCancelable(delay);
                        SetCursorPos(ev.X, ev.Y);
                    }
                }
                else
                {
                    if (smooth && IsPointerEvent(ev)) SmoothMoveTo(ev.X, ev.Y, delay);
                    else SleepCancelable(delay);
                    if (cancelRequested) break;
                    ExecuteEvent(ev);
                }
            }
        }

        private void ExecuteEvent(MacroEvent ev)
        {
            if (ev.Type == "MouseDown")
            {
                SetCursorPos(ev.X, ev.Y);
                mouse_event(MouseFlag(ev.Button, true), 0, 0, 0, UIntPtr.Zero);
            }
            else if (ev.Type == "MouseUp")
            {
                SetCursorPos(ev.X, ev.Y);
                mouse_event(MouseFlag(ev.Button, false), 0, 0, 0, UIntPtr.Zero);
            }
            else if (ev.Type == "Wheel")
            {
                SetCursorPos(ev.X, ev.Y);
                mouse_event(ev.Button == "Horizontal" ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, 0, 0, ev.Delta, UIntPtr.Zero);
            }
            else if (ev.Type == "KeyDown")
            {
                keybd_event((byte)ev.Vk, 0, 0, UIntPtr.Zero);
            }
            else if (ev.Type == "KeyUp")
            {
                keybd_event((byte)ev.Vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void SmoothMoveTo(int targetX, int targetY, int durationMs)
        {
            POINT start;
            if (!GetCursorPos(out start))
            {
                SetCursorPos(targetX, targetY);
                return;
            }
            int duration = Math.Max(0, durationMs);
            int moveDuration = Math.Max(8, Math.Min(duration, 900));
            if (duration > moveDuration) SleepCancelable(duration - moveDuration);
            if (cancelRequested) return;

            int distance = Math.Max(Math.Abs(targetX - start.x), Math.Abs(targetY - start.y));
            if (distance <= 1 || moveDuration <= 8)
            {
                SetCursorPos(targetX, targetY);
                return;
            }

            int steps = Math.Max(2, Math.Min(120, Math.Max(moveDuration / 8, distance / 4)));
            var timer = Stopwatch.StartNew();
            for (int i = 1; i <= steps && !cancelRequested; i++)
            {
                int due = (int)Math.Round(i * (moveDuration / (double)steps));
                int wait = due - (int)timer.ElapsedMilliseconds;
                if (wait > 0) SleepCancelable(wait);
                if (cancelRequested) break;

                double t = moveDuration <= 0 ? 1 : Math.Min(1, timer.ElapsedMilliseconds / (double)moveDuration);
                double eased = t * t * (3 - 2 * t);
                int x = start.x + (int)Math.Round((targetX - start.x) * eased);
                int y = start.y + (int)Math.Round((targetY - start.y) * eased);
                SetCursorPos(x, y);
            }
            if (!cancelRequested) SetCursorPos(targetX, targetY);
        }

        private List<MacroEvent> OptimizeRecordedEvents(List<MacroEvent> input)
        {
            var output = new List<MacroEvent>();
            int carryDelay = 0;
            Point? lastMove = null;
            foreach (var original in input)
            {
                var ev = CloneEvent(original);
                ev.DelayMs += carryDelay;
                carryDelay = 0;
                if (ev.Type == "Move")
                {
                    Point point = new Point(ev.X, ev.Y);
                    if (lastMove.HasValue && Distance(lastMove.Value, point) < 2 && ev.DelayMs < 45)
                    {
                        carryDelay += ev.DelayMs;
                        continue;
                    }
                    lastMove = point;
                }
                else
                {
                    lastMove = null;
                }
                output.Add(ev);
            }
            if (carryDelay > 0 && output.Count > 0) output[output.Count - 1].DelayMs += carryDelay;
            return output;
        }

        private MacroEvent CloneEvent(MacroEvent ev)
        {
            return new MacroEvent
            {
                Type = ev.Type,
                DelayMs = ev.DelayMs,
                X = ev.X,
                Y = ev.Y,
                Button = ev.Button,
                Delta = ev.Delta,
                Vk = ev.Vk
            };
        }

        private void AddEvent(MacroEvent ev)
        {
            lock (sync)
            {
                int now = (int)stopwatch.ElapsedMilliseconds;
                ev.DelayMs = Math.Max(0, now - lastEventMs);
                lastEventMs = now;
                macro.Events.Add(ev);
            }
            RefreshStateSafe("Recording. " + macro.Events.Count + " events captured.");
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    if ((data.vkCode == VK_F8 || data.vkCode == VK_SNAPSHOT) && isDown)
                    {
                        if (DebounceHotkey()) BeginInvoke((MethodInvoker)delegate { HandleGlobalHotkey(data.vkCode); });
                        return (IntPtr)1;
                    }
                    if ((data.vkCode == VK_F8 || data.vkCode == VK_SNAPSHOT) && isUp) return (IntPtr)1;
                    if (isRecording) AddEvent(new MacroEvent { Type = isDown ? "KeyDown" : "KeyUp", Vk = data.vkCode });
                }
            }
            return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        private void HandleGlobalHotkey(int vk)
        {
            if (vk == VK_F8)
            {
                if (isRecording) StopRecording();
                else if (!isPlaying) StartRecording();
                return;
            }
            if (vk == VK_SNAPSHOT)
            {
                if (isRecording) return;
                if (isPlaying)
                {
                    StopActiveWork();
                    return;
                }
                StartPlaybackFromSettings();
            }
        }

        private bool DebounceHotkey()
        {
            int now = Environment.TickCount;
            if (now - lastHotkeyMs < 160) return false;
            lastHotkeyMs = now;
            return true;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && isRecording)
            {
                int msg = wParam.ToInt32();
                var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int x = data.pt.x;
                int y = data.pt.y;

                if (msg == WM_MOUSEMOVE)
                {
                    int now = (int)stopwatch.ElapsedMilliseconds;
                    Point point = new Point(x, y);
                    if (now - lastMoveMs >= 18 && Distance(lastMovePoint, point) >= 2)
                    {
                        lastMoveMs = now;
                        lastMovePoint = point;
                        AddEvent(new MacroEvent { Type = "Move", X = x, Y = y });
                    }
                }
                else if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    AddEvent(new MacroEvent { Type = "MouseDown", X = x, Y = y, Button = MouseButtonName(msg) });
                }
                else if (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP)
                {
                    AddEvent(new MacroEvent { Type = "MouseUp", X = x, Y = y, Button = MouseButtonName(msg) });
                }
                else if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL)
                {
                    short delta = (short)((data.mouseData >> 16) & 0xffff);
                    AddEvent(new MacroEvent { Type = "Wheel", X = x, Y = y, Button = msg == WM_MOUSEHWHEEL ? "Horizontal" : "Vertical", Delta = delta });
                }
            }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private void InstallKeyboardHook()
        {
            if (keyboardHook != IntPtr.Zero) return;
            IntPtr module = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
            if (keyboardHook == IntPtr.Zero) throw new InvalidOperationException("Could not install keyboard hook.");
        }

        private void StopKeyboardHook()
        {
            if (keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
        }

        private void InstallMouseHook()
        {
            StopMouseHook();
            IntPtr module = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            if (mouseHook == IntPtr.Zero) throw new InvalidOperationException("Could not install mouse hook.");
        }

        private void StopMouseHook()
        {
            if (mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
        }

        private void LoadSettings()
        {
            loadingSettings = true;
            try
            {
                AppSettings settings = null;
                string path = SettingsPath();
                bool hasSettings = File.Exists(path);
                if (hasSettings) settings = json.Deserialize<AppSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (settings == null) settings = new AppSettings();

                SetSpeedSelection(String.IsNullOrWhiteSpace(settings.Speed) ? "1" : settings.Speed);
                chkLoopPlay.Checked = settings.LoopPlay;
                numMaxLoops.Value = Clamp(settings.MaxLoops, (int)numMaxLoops.Minimum, (int)numMaxLoops.Maximum);
                chkSmooth.Checked = hasSettings ? settings.SmoothMouse : true;
            }
            catch (Exception ex)
            {
                AppendLogSafe("Settings load skipped: " + ex.Message);
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private void SaveSettings()
        {
            if (loadingSettings) return;
            try
            {
                var settings = new AppSettings();
                settings.Speed = Convert.ToString(cmbSpeed.SelectedItem ?? "1");
                settings.LoopPlay = chkLoopPlay.Checked;
                settings.MaxLoops = (int)numMaxLoops.Value;
                settings.SmoothMouse = chkSmooth.Checked;

                string path = SettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json.Serialize(settings), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppendLogSafe("Settings save failed: " + ex.Message);
            }
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (loadingSettings || settingsTimer == null) return;
            settingsTimer.Stop();
            settingsTimer.Start();
            RefreshState(lblStatus.Text);
        }

        private static string SettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnPlay", "settings.json");
        }

        private static int AdjustDelay(int delayMs, double speed)
        {
            if (speed <= 0) speed = 1;
            return Math.Max(0, (int)Math.Round(delayMs / speed));
        }

        private static int Distance(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        private static bool IsPointerEvent(MacroEvent ev)
        {
            return ev.Type == "MouseDown" || ev.Type == "MouseUp" || ev.Type == "Wheel";
        }

        private static uint MouseFlag(string button, bool down)
        {
            if (button == "Right") return down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
            if (button == "Middle") return down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
            return down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
        }

        private static string MouseButtonName(int msg)
        {
            if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP) return "Right";
            if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP) return "Middle";
            return "Left";
        }

        private void SleepCancelable(int ms)
        {
            int remaining = ms;
            while (remaining > 0 && !cancelRequested)
            {
                int chunk = Math.Min(remaining, 30);
                Thread.Sleep(chunk);
                remaining -= chunk;
            }
        }

        private void RefreshState(string status)
        {
            int eventCount = macro.Events == null ? 0 : macro.Events.Count;
            string loop = chkLoopPlay != null && chkLoopPlay.Checked
                ? ((int)numMaxLoops.Value == 0 ? "loop unlimited" : "loop " + (int)numMaxLoops.Value)
                : "single play";
            lblCount.Text = eventCount + " events | " + loop + " | F8 Rec | PrtSc Play";
            lblStatus.Text = status;

            if (isRecording) lblMode.Text = "REC";
            else if (isPlaying) lblMode.Text = chkLoopPlay.Checked ? "LOOP" : "PLAY";
            else lblMode.Text = "READY";

            btnRecord.Text = isRecording ? "Stop F8" : "Rekam F8";
            btnPlay.Text = isPlaying ? "Stop PrtSc" : "Play PrtSc";
            btnRecord.Enabled = !isPlaying || isRecording;
            btnPlay.Enabled = !isRecording && eventCount > 0;
            btnSave.Enabled = !isRecording && eventCount > 0;
            btnLoad.Enabled = !isRecording && !isPlaying;
            btnStop.Enabled = isRecording || isPlaying;
            if (statusPill != null) statusPill.Invalidate();
        }

        private void RefreshStateSafe(string status)
        {
            if (IsDisposed) return;
            try { BeginInvoke((MethodInvoker)delegate { RefreshState(status); }); } catch { }
        }

        private void SetStatusSafe(string status)
        {
            if (IsDisposed) return;
            try { BeginInvoke((MethodInvoker)delegate { lblStatus.Text = status; }); } catch { }
        }

        private void AppendLogSafe(string line)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine);
                });
            }
            catch { }
        }

        private double GetSelectedSpeed()
        {
            double value;
            string text = Convert.ToString(cmbSpeed.SelectedItem ?? "1").Replace(',', '.');
            if (!Double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)) value = 1;
            if (value <= 0) value = 1;
            return value;
        }

        private void SetSpeedSelection(string value)
        {
            string normalized = value.Replace(',', '.');
            for (int i = 0; i < cmbSpeed.Items.Count; i++)
            {
                if (Convert.ToString(cmbSpeed.Items[i]) == normalized)
                {
                    cmbSpeed.SelectedIndex = i;
                    return;
                }
            }
            cmbSpeed.SelectedItem = "1";
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (theme == null)
            {
                base.OnPaintBackground(e);
                return;
            }
            Rectangle rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (var brush = new LinearGradientBrush(rect, theme.Background, Color.FromArgb(13, 26, 32), 90F))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
            using (var pen = new Pen(Color.FromArgb(22, 34, 211, 238), 1F))
            {
                for (int y = 96; y < rect.Height; y += 112)
                {
                    e.Graphics.DrawLine(pen, 0, y, rect.Width, y + 28);
                }
            }
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        private sealed class GradientPanel : Panel
        {
            public Color ColorA { get; set; }
            public Color ColorB { get; set; }
            public int Radius { get; set; }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = RoundedRect(rect, Radius))
                using (var brush = new LinearGradientBrush(rect, ColorA, ColorB, 0F))
                using (var glow = new Pen(Color.FromArgb(90, 34, 211, 238)))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(glow, path);
                }
            }
        }

        private sealed class LogoPanel : Panel
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var outer = new SolidBrush(Color.FromArgb(16, 185, 129)))
                using (var inner = new SolidBrush(Color.FromArgb(6, 13, 24)))
                using (var cyan = new SolidBrush(Color.FromArgb(34, 211, 238)))
                using (var white = new SolidBrush(Color.White))
                using (var amber = new SolidBrush(Color.FromArgb(250, 204, 21)))
                {
                    e.Graphics.FillEllipse(outer, 0, 0, 54, 54);
                    e.Graphics.FillEllipse(inner, 5, 5, 44, 44);
                    e.Graphics.FillRectangle(cyan, 15, 15, 5, 24);
                    e.Graphics.FillPolygon(white, new[] { new Point(23, 39), new Point(36, 15), new Point(42, 15), new Point(29, 39) });
                    e.Graphics.FillPolygon(amber, new[] { new Point(34, 27), new Point(45, 34), new Point(34, 41) });
                }
            }
        }

        private sealed class Theme
        {
            public Color Background;
            public Color Card;
            public Color Input;
            public Color Text;
            public Color Muted;
            public Color Border;
            public Color Primary;
            public Color Secondary;
            public Color Good;
            public Color Danger;
            public Color HeroA;
            public Color HeroB;

            public static Theme Dark()
            {
                return new Theme
                {
                    Background = Color.FromArgb(8, 13, 24),
                    Card = Color.FromArgb(14, 22, 35),
                    Input = Color.FromArgb(7, 13, 24),
                    Text = Color.FromArgb(241, 245, 249),
                    Muted = Color.FromArgb(163, 178, 197),
                    Border = Color.FromArgb(40, 58, 79),
                    Primary = Color.FromArgb(14, 165, 233),
                    Secondary = Color.FromArgb(99, 102, 241),
                    Good = Color.FromArgb(16, 185, 129),
                    Danger = Color.FromArgb(239, 68, 68),
                    HeroA = Color.FromArgb(7, 13, 24),
                    HeroB = Color.FromArgb(12, 96, 105)
                };
            }

        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
