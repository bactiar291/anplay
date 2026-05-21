using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
            Version = 1;
            Name = "AnPlay Macro";
            CreatedUtc = DateTime.UtcNow.ToString("o");
            Events = new List<MacroEvent>();
        }
    }

    public class AppSettings
    {
        public string ApiKeyProtected { get; set; }
        public string Condition { get; set; }
        public string Models { get; set; }
        public string Speed { get; set; }
        public bool LoopPlay { get; set; }
        public int MaxLoops { get; set; }
        public bool SmoothMouse { get; set; }
        public int CheckDelaySeconds { get; set; }
        public bool ConfirmStopTwice { get; set; }
    }

    public class GroqDecision
    {
        public bool Stop;
        public double Confidence;
        public string StopType;
        public string VisibleText;
        public string Evidence;
        public string Reason;
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

        private const string DefaultCondition =
            "berhenti kalau sudah tidak error Unable to send a verification code; " +
            "kalau nomor berhasil masuk dan layar hanya menunggu OTP/kode akses maka berhenti; " +
            "kalau muncul Banned/Blocked/Suspended juga berhenti supaya loop tidak lanjut";

        private const string DefaultVisionModels =
            "meta-llama/llama-4-maverick-17b-128e-instruct, " +
            "meta-llama/llama-4-scout-17b-16e-instruct, " +
            "qwen/qwen3-vl-32b-instruct";

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
        private bool isAiLoop;
        private bool cancelRequested;
        private bool loadingSettings;
        private int lastEventMs;
        private int lastMoveMs;
        private int lastHotkeyMs;
        private Point lastMovePoint = Point.Empty;
        private System.Windows.Forms.Timer settingsTimer;

        private Panel pnlLogo;
        private Panel pnlState;
        private Button btnRecord;
        private Button btnPlay;
        private Button btnStop;
        private Button btnSave;
        private Button btnLoad;
        private Button btnAiLoop;
        private Button btnClearKey;
        private Label lblStatus;
        private Label lblCount;
        private TextBox txtCondition;
        private TextBox txtApiKey;
        private TextBox txtModels;
        private ComboBox cmbSpeed;
        private NumericUpDown numMaxLoops;
        private NumericUpDown numDelay;
        private CheckBox chkLoopPlay;
        private CheckBox chkSmooth;
        private CheckBox chkConfirmStopTwice;
        private TextBox txtLog;

        public MainForm()
        {
            Text = "AnPlay";
            Width = 760;
            Height = 560;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            keyboardProc = KeyboardHookCallback;
            mouseProc = MouseHookCallback;
            BuildUi();
            LoadSettings();
            InstallKeyboardHook();
            RefreshState("Ready. F8 = Record/Stop Record. PrtSc = Play/Stop Playback.");
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
            var header = new Panel { Left = 12, Top = 10, Width = 720, Height = 55 };
            Controls.Add(header);

            pnlLogo = new Panel { Left = 0, Top = 3, Width = 48, Height = 48 };
            pnlLogo.Paint += PaintLogo;
            header.Controls.Add(pnlLogo);

            var title = new Label
            {
                Left = 58,
                Top = 4,
                Width = 230,
                Height = 24,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Text = "AnPlay"
            };
            header.Controls.Add(title);

            var subtitle = new Label
            {
                Left = 60,
                Top = 30,
                Width = 500,
                Height = 20,
                Text = "F8 = Record/Stop Record | PrtSc = Play/Stop Playback"
            };
            header.Controls.Add(subtitle);

            pnlState = new Panel { Left = 640, Top = 11, Width = 58, Height = 28 };
            header.Controls.Add(pnlState);

            var top = new Panel { Left = 12, Top = 70, Width = 720, Height = 44 };
            Controls.Add(top);

            btnRecord = MakeButton("Rec F8", 0, 0, 82, 36, OnRecordClick);
            top.Controls.Add(btnRecord);
            btnPlay = MakeButton("Play PrtSc", 92, 0, 92, 36, OnPlayClick);
            top.Controls.Add(btnPlay);
            btnStop = MakeButton("Stop", 194, 0, 82, 36, OnStopClick);
            top.Controls.Add(btnStop);
            btnSave = MakeButton("Save", 286, 0, 82, 36, OnSaveClick);
            top.Controls.Add(btnSave);
            btnLoad = MakeButton("Load", 378, 0, 82, 36, OnLoadClick);
            top.Controls.Add(btnLoad);

            lblCount = new Label { Left = 475, Top = 8, Width = 235, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            top.Controls.Add(lblCount);

            var playback = new GroupBox { Text = "1. Playback Settings", Left = 12, Top = 120, Width = 720, Height = 72 };
            Controls.Add(playback);

            playback.Controls.Add(new Label { Text = "Speed", Left = 12, Top = 30, Width = 52 });
            cmbSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 66, Top = 26, Width = 76 };
            cmbSpeed.Items.AddRange(new object[] { "0.5", "1", "1.5", "2", "3", "5" });
            cmbSpeed.SelectedItem = "1";
            cmbSpeed.SelectedIndexChanged += OnSettingChanged;
            playback.Controls.Add(cmbSpeed);

            chkLoopPlay = new CheckBox { Text = "Repeat", Left = 160, Top = 28, Width = 78 };
            chkLoopPlay.CheckedChanged += OnSettingChanged;
            playback.Controls.Add(chkLoopPlay);

            playback.Controls.Add(new Label { Text = "Loop limit", Left = 252, Top = 30, Width = 72 });
            numMaxLoops = new NumericUpDown { Left = 326, Top = 26, Width = 68, Minimum = 0, Maximum = 9999, Value = 0 };
            numMaxLoops.ValueChanged += OnSettingChanged;
            playback.Controls.Add(numMaxLoops);

            playback.Controls.Add(new Label { Text = "0 = no limit", Left = 400, Top = 30, Width = 84 });
            chkSmooth = new CheckBox { Text = "Smooth mouse", Left = 510, Top = 28, Width = 145, Checked = true };
            chkSmooth.CheckedChanged += OnSettingChanged;
            playback.Controls.Add(chkSmooth);

            var ai = new GroupBox { Text = "2. AI Auto-Stop (optional)", Left = 12, Top = 200, Width = 720, Height = 230 };
            Controls.Add(ai);

            ai.Controls.Add(new Label { Text = "Stop when", Left = 12, Top = 28, Width = 105 });
            txtCondition = new TextBox { Left = 122, Top = 24, Width = 580, Height = 46, Multiline = true, Text = DefaultCondition };
            txtCondition.TextChanged += OnSettingChanged;
            ai.Controls.Add(txtCondition);

            ai.Controls.Add(new Label { Text = "Groq key", Left = 12, Top = 82, Width = 105 });
            txtApiKey = new TextBox { Left = 122, Top = 78, Width = 380, PasswordChar = '*' };
            txtApiKey.TextChanged += OnSettingChanged;
            ai.Controls.Add(txtApiKey);
            btnClearKey = MakeButton("Clear Key", 514, 76, 86, 28, OnClearKeyClick);
            ai.Controls.Add(btnClearKey);

            ai.Controls.Add(new Label { Text = "Check after", Left = 12, Top = 118, Width = 105 });
            numDelay = new NumericUpDown { Left = 122, Top = 114, Width = 66, Minimum = 1, Maximum = 60, Value = 2 };
            numDelay.ValueChanged += OnSettingChanged;
            ai.Controls.Add(numDelay);
            ai.Controls.Add(new Label { Text = "seconds", Left = 194, Top = 118, Width = 62 });

            chkConfirmStopTwice = new CheckBox { Text = "Confirm twice", Left = 270, Top = 116, Width = 120, Checked = true };
            chkConfirmStopTwice.CheckedChanged += OnSettingChanged;
            ai.Controls.Add(chkConfirmStopTwice);
            btnAiLoop = MakeButton("Start AI", 514, 112, 86, 30, OnAiLoopClick);
            ai.Controls.Add(btnAiLoop);

            ai.Controls.Add(new Label { Text = "Model list", Left = 12, Top = 156, Width = 105 });
            txtModels = new TextBox
            {
                Left = 122,
                Top = 152,
                Width = 580,
                Height = 52,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = DefaultVisionModels
            };
            txtModels.TextChanged += OnSettingChanged;
            ai.Controls.Add(txtModels);

            txtLog = new TextBox { Left = 12, Top = 438, Width = 720, Height = 66, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            Controls.Add(txtLog);
            lblStatus = new Label { Left = 12, Top = 510, Width = 720, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(lblStatus);

            settingsTimer = new System.Windows.Forms.Timer { Interval = 900 };
            settingsTimer.Tick += delegate { settingsTimer.Stop(); SaveSettings(); };
        }

        private Button MakeButton(string text, int left, int top, int width, int height, EventHandler handler)
        {
            var button = new Button { Text = text, Left = left, Top = top, Width = width, Height = height };
            button.Click += handler;
            return button;
        }

        private void PaintLogo(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 48, 48), Color.FromArgb(9, 66, 88), Color.FromArgb(30, 142, 116), 45F))
            using (var white = new SolidBrush(Color.White))
            using (var red = new SolidBrush(Color.FromArgb(231, 59, 70)))
            using (var yellow = new SolidBrush(Color.FromArgb(245, 183, 58)))
            {
                e.Graphics.FillEllipse(bg, 2, 2, 44, 44);
                e.Graphics.FillRectangle(white, 12, 14, 5, 22);
                e.Graphics.FillPolygon(white, new[] { new Point(18, 36), new Point(30, 14), new Point(35, 14), new Point(23, 36) });
                e.Graphics.FillEllipse(red, 30, 8, 10, 10);
                e.Graphics.FillPolygon(yellow, new[] { new Point(29, 28), new Point(39, 34), new Point(29, 40) });
            }
        }

        private void OnRecordClick(object sender, EventArgs e)
        {
            if (isRecording) StopRecording();
            else if (!isPlaying && !isAiLoop) BeginRecordingWithDelay();
        }

        private void BeginRecordingWithDelay()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                SetStatusSafe("Recording starts in 3 seconds. Press F8 again to stop recording.");
                Thread.Sleep(3000);
                BeginInvoke((MethodInvoker)StartRecording);
            });
        }

        private void StartRecording()
        {
            lock (sync)
            {
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
            RefreshState("Recording... press F8 to stop recording.");
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
            if (isRecording) StopRecording();
            StopActiveWork();
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "AnPlay macro (*.json)|*.json|All files (*.*)|*.*";
                dialog.FileName = "macro.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, json.Serialize(macro), Encoding.UTF8);
                RefreshState("Saved macro: " + dialog.FileName);
            }
        }

        private void OnLoadClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "AnPlay macro (*.json)|*.json|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                MacroDocument loaded = json.Deserialize<MacroDocument>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                if (loaded == null || loaded.Events == null) throw new InvalidDataException("Invalid macro file.");
                macro = loaded;
                RefreshState("Loaded macro: " + dialog.FileName);
            }
        }

        private void OnAiLoopClick(object sender, EventArgs e)
        {
            if (isRecording) return;
            if (isAiLoop || isPlaying)
            {
                StopActiveWork();
                return;
            }
            if (macro.Events.Count == 0)
            {
                MessageBox.Show(this, "Record or load a macro first.", "No macro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveSettings();
            cancelRequested = false;
            isAiLoop = true;
            RefreshState("AI Auto-Stop running. Press PrtSc to stop it.");
            ThreadPool.QueueUserWorkItem(delegate { RunAiLoop(); });
        }

        private void OnClearKeyClick(object sender, EventArgs e)
        {
            txtApiKey.Text = "";
            SaveSettings();
            AppendLogSafe("API key cleared from local settings.");
        }

        private void StartPlaybackFromSettings()
        {
            if (macro.Events.Count == 0) return;
            SaveSettings();
            cancelRequested = false;
            int loops = chkLoopPlay.Checked ? (int)numMaxLoops.Value : 1;
            double speed = GetSelectedSpeed();
            bool smooth = chkSmooth.Checked;
            isPlaying = true;
            RefreshState("Playing. Press PrtSc to stop playback.");
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

        private void RunAiLoop()
        {
            int loop = 0;
            int consecutiveStop = 0;
            int maxLoops = (int)GetControlValueSafe(numMaxLoops);
            bool requireTwo = GetCheckedSafe(chkConfirmStopTwice);
            string condition = GetTextSafe(txtCondition);

            try
            {
                while (!cancelRequested)
                {
                    loop++;
                    if (maxLoops > 0 && loop > maxLoops)
                    {
                        RefreshStateSafe("AI stopped: loop limit reached.");
                        return;
                    }

                    SetStatusSafe("AI cycle " + loop + ": playing macro.");
                    PlayMacroLoop(1, GetSelectedSpeedSafe(), GetCheckedSafe(chkSmooth));
                    if (cancelRequested) break;

                    int delaySeconds = (int)GetControlValueSafe(numDelay);
                    SetStatusSafe("Waiting " + delaySeconds + "s before screen check.");
                    SleepCancelable(delaySeconds * 1000);
                    if (cancelRequested) break;

                    GroqDecision decision = CheckStopCondition(condition, loop);
                    if (decision.Stop)
                    {
                        bool terminalError = IsTerminalStopType(decision.StopType);
                        consecutiveStop++;
                        if (terminalError || !requireTwo || consecutiveStop >= 2)
                        {
                            RefreshStateSafe("AI stopped: " + decision.Reason);
                            return;
                        }
                        AppendLogSafe("Stop detected once; confirming once more before stopping.");
                    }
                    else
                    {
                        consecutiveStop = 0;
                    }
                }
                RefreshStateSafe("AI stopped by user.");
            }
            catch (Exception ex)
            {
                RefreshStateSafe("AI error: " + ex.Message);
                AppendLogSafe(ex.ToString());
            }
            finally
            {
                isAiLoop = false;
                isPlaying = false;
                RefreshStateSafe(cancelRequested ? "AI stopped." : "Ready.");
            }
        }

        private GroqDecision CheckStopCondition(string condition, int loop)
        {
            string key = GetTextSafe(txtApiKey).Trim();
            if (key.Length == 0) key = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            if (String.IsNullOrEmpty(key)) throw new InvalidOperationException("Groq API key empty. Paste it once or set GROQ_API_KEY.");

            string[] models = ParseModelList(GetTextSafe(txtModels));
            SetStatusSafe("Capturing screen for AI check...");
            string base64 = CaptureScreenJpegBase64(1280, 72L);

            Exception lastError = null;
            for (int i = 0; i < models.Length; i++)
            {
                string model = models[i];
                try
                {
                    SetStatusSafe("Checking with " + model);
                    GroqDecision decision = AskGroqVision(key, model, condition, base64, loop);
                    ApplyLocalSafetyHeuristics(decision);
                    if (decision.Stop && decision.Confidence < 0.80 && !IsTerminalStopType(decision.StopType))
                    {
                        decision.Stop = false;
                        decision.Reason = "Low confidence stop blocked: " + decision.Reason;
                    }
                    AppendLogSafe("AI loop " + loop + " model=" + model
                        + " stop=" + decision.Stop
                        + " type=" + decision.StopType
                        + " conf=" + decision.Confidence.ToString("0.00")
                        + " evidence=" + Truncate(decision.Evidence, 110)
                        + " reason=" + decision.Reason);
                    return decision;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (i < models.Length - 1 && ShouldRotateModel(ex))
                    {
                        AppendLogSafe("Model failed/limited: " + model + ". Rotating to " + models[i + 1] + ".");
                        continue;
                    }
                    throw;
                }
            }
            throw lastError ?? new InvalidOperationException("AI check failed.");
        }

        private GroqDecision AskGroqVision(string apiKey, string model, string condition, string base64Image, int loop)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string system =
                "You are AnPlay's conservative desktop-state verifier. Return JSON only. "
                + "Your job is to stop an automation loop when the screen has reached a success, OTP-waiting, or terminal-failure state.";

            string prompt =
                "User stop condition: \"" + condition + "\"\n"
                + "Loop number: " + loop + "\n\n"
                + "Reasoning rules:\n"
                + "1. First OCR/read all visible text and status words on the screenshot.\n"
                + "2. Indonesian phrase 'sudah tidak error X' or 'tidak error X' means: continue only while the exact error X is visible. Stop when X is absent AND the screen shows a stable next state such as OTP, kode akses, verification code, enter code, waiting for code, code sent, success, verified, dashboard, logged in, or similar.\n"
                + "3. If the exact error 'Unable to send a verification code' is still visible, return stop=false.\n"
                + "4. If the screen shows OTP/code input, waiting OTP, access code, verification code sent, or the number was accepted, return stop=true with stop_type='success'.\n"
                + "5. If the screen shows Banned, ban, blocked, suspended, disabled, restricted, too many attempts, or abuse protection, return stop=true with stop_type='terminal_error' even if the user only asked for success. This prevents the loop from continuing after damage.\n"
                + "6. If the screen is loading, blank, hidden, or ambiguous with no stable state, return stop=false with stop_type='ambiguous'.\n"
                + "7. Do not guess. Quote visible evidence exactly when possible.\n\n"
                + "Return JSON object only with keys: stop boolean, confidence number 0..1, stop_type string ('success','terminal_error','continue','ambiguous'), visible_text string, evidence string, reason string.";

            var systemMessage = new Dictionary<string, object>();
            systemMessage["role"] = "system";
            systemMessage["content"] = system;

            var textPart = new Dictionary<string, object>();
            textPart["type"] = "text";
            textPart["text"] = prompt;

            var imageUrl = new Dictionary<string, object>();
            imageUrl["url"] = "data:image/jpeg;base64," + base64Image;
            var imagePart = new Dictionary<string, object>();
            imagePart["type"] = "image_url";
            imagePart["image_url"] = imageUrl;

            var userMessage = new Dictionary<string, object>();
            userMessage["role"] = "user";
            userMessage["content"] = new object[] { textPart, imagePart };

            var responseFormat = new Dictionary<string, object>();
            responseFormat["type"] = "json_object";

            var body = new Dictionary<string, object>();
            body["model"] = model;
            body["messages"] = new object[] { systemMessage, userMessage };
            body["temperature"] = 0;
            body["max_completion_tokens"] = 420;
            body["response_format"] = responseFormat;

            string requestJson = json.Serialize(body);
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);

            var request = (HttpWebRequest)WebRequest.Create("https://api.groq.com/openai/v1/chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Authorization"] = "Bearer " + apiKey;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(requestBytes, 0, requestBytes.Length);
            }

            string responseText;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    responseText = reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                string error = ex.Message;
                string status = "";
                var httpResponse = ex.Response as HttpWebResponse;
                if (httpResponse != null) status = ((int)httpResponse.StatusCode).ToString() + " ";
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        error = reader.ReadToEnd();
                    }
                }
                throw new InvalidOperationException("Groq API error " + status + Truncate(error, 600));
            }

            var decisionObject = json.DeserializeObject(ExtractJsonObject(ExtractGroqContent(responseText))) as Dictionary<string, object>;
            if (decisionObject == null) throw new InvalidOperationException("AI decision was not JSON.");

            var decision = new GroqDecision();
            decision.Stop = ToBool(GetDictValue(decisionObject, "stop"));
            decision.Confidence = ToDouble(GetDictValue(decisionObject, "confidence"));
            decision.StopType = Convert.ToString(GetDictValue(decisionObject, "stop_type") ?? "continue");
            decision.VisibleText = Convert.ToString(GetDictValue(decisionObject, "visible_text") ?? "");
            decision.Evidence = Convert.ToString(GetDictValue(decisionObject, "evidence") ?? "");
            decision.Reason = Convert.ToString(GetDictValue(decisionObject, "reason") ?? "");
            return decision;
        }

        private void ApplyLocalSafetyHeuristics(GroqDecision decision)
        {
            string combined = (decision.VisibleText + " " + decision.Evidence + " " + decision.Reason).ToLowerInvariant();
            if (ContainsAny(combined, new[] { "banned", " ban ", "blocked", "suspended", "disabled", "restricted", "too many attempts", "abuse" }))
            {
                decision.Stop = true;
                decision.Confidence = Math.Max(decision.Confidence, 0.95);
                decision.StopType = "terminal_error";
                if (String.IsNullOrWhiteSpace(decision.Reason)) decision.Reason = "Terminal failure state detected locally.";
                return;
            }

            if (ContainsAny(combined, new[] { "otp", "verification code", "access code", "kode akses", "kode otp", "enter code", "waiting for code", "code sent", "sms code" }))
            {
                decision.Stop = true;
                decision.Confidence = Math.Max(decision.Confidence, 0.88);
                decision.StopType = "success";
                if (String.IsNullOrWhiteSpace(decision.Reason)) decision.Reason = "OTP/code waiting state detected locally.";
            }
        }

        private void PlayMacroLoop(int requestedLoops, double speed, bool smooth)
        {
            int count = 0;
            while (!cancelRequested)
            {
                count++;
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
                    SleepCancelable(delay);
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
            int duration = Math.Max(12, Math.Min(durationMs, 500));
            int steps = Math.Max(2, Math.Min(40, duration / 8));
            for (int i = 1; i <= steps && !cancelRequested; i++)
            {
                double t = i / (double)steps;
                double eased = 1 - Math.Pow(1 - t, 3);
                int x = start.x + (int)Math.Round((targetX - start.x) * eased);
                int y = start.y + (int)Math.Round((targetY - start.y) * eased);
                SetCursorPos(x, y);
                Thread.Sleep(Math.Max(1, duration / steps));
            }
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
                    if (lastMove.HasValue && Distance(lastMove.Value, point) < 3 && ev.DelayMs < 80)
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
            RefreshStateSafe("Recording...");
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
                        if (DebounceHotkey())
                        {
                            BeginInvoke((MethodInvoker)delegate { HandleGlobalHotkey(data.vkCode); });
                        }
                        return (IntPtr)1;
                    }
                    if ((data.vkCode == VK_F8 || data.vkCode == VK_SNAPSHOT) && isUp)
                    {
                        return (IntPtr)1;
                    }
                    if (isRecording)
                    {
                        AddEvent(new MacroEvent { Type = isDown ? "KeyDown" : "KeyUp", Vk = data.vkCode });
                    }
                }
            }
            return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        private void HandleGlobalHotkey(int vk)
        {
            if (vk == VK_F8)
            {
                if (isRecording) StopRecording();
                else if (!isPlaying && !isAiLoop) BeginRecordingWithDelay();
                return;
            }
            if (vk == VK_SNAPSHOT)
            {
                if (isRecording) return;
                if (isPlaying || isAiLoop)
                {
                    StopActiveWork();
                    return;
                }
                if (macro.Events.Count > 0) StartPlaybackFromSettings();
            }
        }

        private bool DebounceHotkey()
        {
            int now = Environment.TickCount;
            if (now - lastHotkeyMs < 350) return false;
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
                    if (now - lastMoveMs >= 36 && Distance(lastMovePoint, point) >= 4)
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

        private string CaptureScreenJpegBase64(int maxWidth, long quality)
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            using (var screenshot = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var g = Graphics.FromImage(screenshot))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                }
                Image imageToSave = screenshot;
                Bitmap resized = null;
                if (screenshot.Width > maxWidth)
                {
                    double scale = maxWidth / (double)screenshot.Width;
                    int newWidth = maxWidth;
                    int newHeight = Math.Max(1, (int)Math.Round(screenshot.Height * scale));
                    resized = new Bitmap(newWidth, newHeight);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(screenshot, 0, 0, newWidth, newHeight);
                    }
                    imageToSave = resized;
                }
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        ImageCodecInfo encoder = GetJpegEncoder();
                        if (encoder != null)
                        {
                            var ep = new EncoderParameters(1);
                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                            imageToSave.Save(ms, encoder, ep);
                        }
                        else
                        {
                            imageToSave.Save(ms, ImageFormat.Jpeg);
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
                finally
                {
                    if (resized != null) resized.Dispose();
                }
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

                txtApiKey.Text = UnprotectString(settings.ApiKeyProtected);
                txtCondition.Text = String.IsNullOrWhiteSpace(settings.Condition) ? DefaultCondition : settings.Condition;
                txtModels.Text = String.IsNullOrWhiteSpace(settings.Models) ? DefaultVisionModels : settings.Models;
                SetSpeedSelection(String.IsNullOrWhiteSpace(settings.Speed) ? "1" : settings.Speed);
                chkLoopPlay.Checked = settings.LoopPlay;
                numMaxLoops.Value = Clamp(settings.MaxLoops, (int)numMaxLoops.Minimum, (int)numMaxLoops.Maximum);
                chkSmooth.Checked = hasSettings ? settings.SmoothMouse : true;
                numDelay.Value = Clamp(settings.CheckDelaySeconds <= 0 ? 2 : settings.CheckDelaySeconds, (int)numDelay.Minimum, (int)numDelay.Maximum);
                chkConfirmStopTwice.Checked = hasSettings ? settings.ConfirmStopTwice : true;
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
                settings.ApiKeyProtected = ProtectString(txtApiKey.Text.Trim());
                settings.Condition = txtCondition.Text;
                settings.Models = txtModels.Text;
                settings.Speed = Convert.ToString(cmbSpeed.SelectedItem ?? "1");
                settings.LoopPlay = chkLoopPlay.Checked;
                settings.MaxLoops = (int)numMaxLoops.Value;
                settings.SmoothMouse = chkSmooth.Checked;
                settings.CheckDelaySeconds = (int)numDelay.Value;
                settings.ConfirmStopTwice = chkConfirmStopTwice.Checked;

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
        }

        private static string SettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnPlay", "settings.json");
        }

        private static string ProtectString(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string UnprotectString(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                byte[] plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        private string[] ParseModelList(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) value = DefaultVisionModels;
            string[] raw = value.Split(new char[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var models = new List<string>();
            foreach (string item in raw)
            {
                string model = item.Trim();
                if (model.Length > 0 && !models.Contains(model)) models.Add(model);
            }
            if (models.Count == 0) models.Add("meta-llama/llama-4-maverick-17b-128e-instruct");
            return models.ToArray();
        }

        private bool ShouldRotateModel(Exception ex)
        {
            string msg = ex == null ? "" : ex.Message.ToLowerInvariant();
            if (msg.Contains("401") || msg.Contains("invalid api key") || msg.Contains("unauthorized")) return false;
            return ContainsAny(msg, new[] { "429", "rate limit", "rate_limit", "too many requests", "quota", "limit exceeded", "model", "does not support", "403", "404", "500", "503" });
        }

        private string ExtractGroqContent(string responseText)
        {
            var root = json.DeserializeObject(responseText) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("choices")) throw new InvalidOperationException("Groq response missing choices.");
            var choices = root["choices"] as object[];
            if (choices == null || choices.Length == 0) throw new InvalidOperationException("Groq choices empty.");
            var choice = choices[0] as Dictionary<string, object>;
            var message = choice != null && choice.ContainsKey("message") ? choice["message"] as Dictionary<string, object> : null;
            if (message == null || !message.ContainsKey("content")) throw new InvalidOperationException("Groq message content missing.");
            return Convert.ToString(message["content"]);
        }

        private static object GetDictValue(Dictionary<string, object> dict, string key)
        {
            return dict.ContainsKey(key) ? dict[key] : null;
        }

        private static string ExtractJsonObject(string text)
        {
            if (text == null) return "{}";
            string trimmed = text.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstLine = trimmed.IndexOf('\n');
                if (firstLine >= 0) trimmed = trimmed.Substring(firstLine + 1);
                int fence = trimmed.LastIndexOf("```");
                if (fence >= 0) trimmed = trimmed.Substring(0, fence);
                trimmed = trimmed.Trim();
            }
            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start) return trimmed.Substring(start, end - start + 1);
            return trimmed;
        }

        private static bool ToBool(object value)
        {
            if (value is bool) return (bool)value;
            string s = Convert.ToString(value).Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }

        private static double ToDouble(object value)
        {
            try { return Convert.ToDouble(value); }
            catch { return 0; }
        }

        private static bool ContainsAny(string haystack, string[] needles)
        {
            foreach (string needle in needles)
            {
                if (haystack.Contains(needle)) return true;
            }
            return false;
        }

        private static bool IsTerminalStopType(string stopType)
        {
            return Convert.ToString(stopType).ToLowerInvariant().Contains("terminal");
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

        private static ImageCodecInfo GetJpegEncoder()
        {
            ImageCodecInfo[] encoders = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo encoder in encoders)
            {
                if (encoder.FormatID == ImageFormat.Jpeg.Guid) return encoder;
            }
            return null;
        }

        private void SleepCancelable(int ms)
        {
            int remaining = ms;
            while (remaining > 0 && !cancelRequested)
            {
                int chunk = Math.Min(remaining, 40);
                Thread.Sleep(chunk);
                remaining -= chunk;
            }
        }

        private void RefreshState(string status)
        {
            lblCount.Text = macro.Events.Count + " events | F8 Rec | PrtSc Play";
            lblStatus.Text = status;

            if (isRecording)
            {
                pnlState.BackColor = Color.FromArgb(36, 170, 82);
                btnRecord.BackColor = Color.FromArgb(36, 170, 82);
                btnRecord.ForeColor = Color.White;
                btnRecord.Text = "Recording";
            }
            else if (isPlaying || isAiLoop)
            {
                pnlState.BackColor = Color.FromArgb(36, 104, 220);
                btnRecord.BackColor = SystemColors.Control;
                btnRecord.ForeColor = SystemColors.ControlText;
                btnRecord.Text = "Rec F8";
            }
            else
            {
                pnlState.BackColor = Color.FromArgb(210, 48, 58);
                btnRecord.BackColor = Color.FromArgb(210, 48, 58);
                btnRecord.ForeColor = Color.White;
                btnRecord.Text = "Rec F8";
            }

            btnPlay.Text = isPlaying ? "Stop Play" : "Play PrtSc";
            btnAiLoop.Text = isAiLoop ? "Stop AI" : "Start AI";
            btnRecord.Enabled = !isPlaying && !isAiLoop;
            btnPlay.Enabled = !isRecording && !isAiLoop && macro.Events.Count > 0;
            btnAiLoop.Enabled = !isRecording && !isPlaying && macro.Events.Count > 0;
            btnSave.Enabled = !isRecording && macro.Events.Count > 0;
            btnLoad.Enabled = !isRecording && !isPlaying && !isAiLoop;
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

        private string GetTextSafe(TextBox box)
        {
            if (box.InvokeRequired) return (string)box.Invoke(new Func<string>(delegate { return box.Text; }));
            return box.Text;
        }

        private decimal GetControlValueSafe(NumericUpDown box)
        {
            if (box.InvokeRequired) return (decimal)box.Invoke(new Func<decimal>(delegate { return box.Value; }));
            return box.Value;
        }

        private bool GetCheckedSafe(CheckBox box)
        {
            if (box.InvokeRequired) return (bool)box.Invoke(new Func<bool>(delegate { return box.Checked; }));
            return box.Checked;
        }

        private double GetSelectedSpeedSafe()
        {
            if (cmbSpeed.InvokeRequired) return (double)cmbSpeed.Invoke(new Func<double>(GetSelectedSpeed));
            return GetSelectedSpeed();
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

        private static string Truncate(string value, int limit)
        {
            if (String.IsNullOrEmpty(value) || value.Length <= limit) return value;
            return value.Substring(0, limit);
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
