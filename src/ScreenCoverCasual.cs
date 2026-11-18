using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ScreenCoverCasual
{
    static class Program
    {
        public const Keys EMERGENCY_KEY = Keys.Q;

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            ScreenCoverUpgrade.LocalStore.Ensure();
            if (Array.IndexOf(args, "--settings") >= 0) { ScreenCoverUpgrade.Management.Open(); return; }
            if (!File.Exists(ScreenCoverUpgrade.LocalStore.At("legacy-verifier.dat"))) { ScreenCoverUpgrade.Management.Open(); return; }
            bool createdNew;
            using (var mutex = new Mutex(true, "ScreenCover_Casual_v2", out createdNew))
            {
                if (!createdNew) { MessageBox.Show("同一模式的遮屏程序已经运行。请先退出当前实例后再启动。", "ScreenCoverLock"); return; }
                bool automation = Array.IndexOf(args, "--allow-automation") >= 0;
                if (automation && MessageBox.Show("允许当前 Windows 用户的后台程序临时暂停遮屏？\n每次启动重新询问。默认每10分钟租约自动续期，单次总时长不限。\n续期进程失效后，最后租约到期仍会恢复遮屏。\n暂停期间检测到本机鼠标或键盘输入，会立即恢复遮屏。\nCtrl+Shift+L 可立即恢复，Ctrl+Shift+Q 应急退出。\n这不是 Windows 系统锁屏，也不会绕过系统登录。", "启用自动化临时暂停", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) automation = false;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new CoverForm(automation));
            }
        }

        public static string Hash(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    sealed partial class CoverForm : Form
    {
        const int WH_KEYBOARD_LL = 13;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_DISPLAY_REQUIRED = 0x00000002;
        const int MAX_WRONG = 5;

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint SetThreadExecutionState(uint esFlags);

        readonly LowLevelKeyboardProc _proc;
        IntPtr _hook = IntPtr.Zero;
        bool _allowClose;
        System.Windows.Forms.Timer _tick;

        TextBox _input;
        int _wrongCount;
        bool _lockedOut;
        DateTime _retryAt;
        bool _hotkeyRegistered;
        const int EmergencyHotkeyId = 0x5343;
        ScreenCoverSupport.DisplayCovers _displayCovers;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == EmergencyHotkeyId)
            {
                EmergencyExit();
                return;
            }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == AutomationRestoreHotkeyId)
            {
                if (_automationHost != null) { try { _automationHost.RestoreManually(); } catch { } }
                return;
            }
            base.WndProc(ref m);
        }

        void CompleteExit()
        {
            _allowClose = true;
            Unhook();
            Close();
        }

        void RefreshCooldown()
        {
            if (!_lockedOut) return;
            if (DateTime.UtcNow >= _retryAt)
            {
                _lockedOut = false;
                _wrongCount = 0;
                _input.ReadOnly = false;
                _input.Clear();
                if (ContainsFocus) _input.Focus();
            }
            Invalidate();
        }

        public CoverForm(bool automationEnabled)
        {
            _automationEnabled = automationEnabled;
            _proc = HookProc;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = false;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Default;

            _input = new TextBox();
            _input.Location = new Point(-300, -300);
            _input.Size = new Size(1, 1);
            _input.BorderStyle = BorderStyle.None;
            _input.MaxLength = 6;
            _input.ShortcutsEnabled = false;
            _input.UseSystemPasswordChar = true;
            _input.ImeMode = ImeMode.Disable;
            _input.KeyDown += OnKeyDown;
            _input.TextChanged += delegate { UpgradeTextChanged(); };
            Controls.Add(_input);

            Click += OnFormClick;
            MouseDown += delegate { _input.Focus(); };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _hotkeyRegistered = RegisterHotKey(Handle, EmergencyHotkeyId, 0x0002 | 0x0004 | 0x4000, (uint)Keys.Q);
            if (!_hotkeyRegistered)
            {
                MessageBox.Show("Ctrl+Shift+Q 应急退出键注册失败，可能已运行另一个实例。程序将退出。", "ScreenCoverLock");
                _allowClose = true;
                Close();
                return;
            }
            _displayCovers = new ScreenCoverSupport.DisplayCovers(this, delegate { if (!_automationPaused) { Activate(); _input.Focus(); } }, TopMost);
            _displayCovers.Start();
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            Activate();
            _input.Focus();

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 500;
            _tick.Tick += delegate { if (_automationPaused) return; RefreshCooldown(); if (ContainsFocus && !_input.Focused) _input.Focus(); };
            _tick.Start();
            InitializeAutomation();
            InitializeUpgrade();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            ScreenCoverSupport.CoverBackground.Paint(e.Graphics, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_automationEnabled)
                TextRenderer.DrawText(g, "自动化临时暂停已获允许 · Ctrl+Shift+L 立即恢复遮屏", Font,
                    new Rectangle(12, ClientSize.Height - 34, ClientSize.Width - 24, 24), Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (_assets != null)
                _assets.PaintInputAndWarning(g, ClientSize, ClientRectangle, _input.Text.Length, _lockedOut, _state, _settings);
        }

        void OnFormClick(object sender, EventArgs e)
        {
            if (_lockedOut) return;
            _input.Focus();
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Program.EMERGENCY_KEY && e.Control && e.Shift)
            {
                e.SuppressKeyPress = true;
                EmergencyExit();
                return;
            }
            if (_leaving) { e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelAttempt(); return; }
            if (_lockedOut) { e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                TryUnlock();
            }
        }

        void TryUnlock()
        {
            if (_lockedOut || _input.Text.Length == 0) return;
            if (ScreenCoverUpgrade.PasswordVault.VerifyLegacy(_input.Text))
            {
                PasswordSucceeded();
                return;
            }
            UpgradeSubmitFailed();
            _wrongCount++;
            _input.Clear();

            if (_wrongCount >= MAX_WRONG)
            {
                _lockedOut = true;
                _retryAt = DateTime.UtcNow.AddSeconds(30);
                _input.ReadOnly = true;
            }
            Invalidate();
        }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (_automationPaused) return CallNextHookEx(_hook, nCode, wParam, lParam);
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == VK_LWIN || vk == VK_RWIN)
                    return new IntPtr(1);
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                return;
            }
            Unhook();
            base.OnFormClosing(e);
        }

        void Unhook()
        {
            DisposeUpgrade();
            if (_automationHost != null) { _automationHost.Dispose(); _automationHost = null; }
            if (_restoreHotkeyRegistered) { UnregisterHotKey(Handle, AutomationRestoreHotkeyId); _restoreHotkeyRegistered = false; }
            if (_hotkeyRegistered) { UnregisterHotKey(Handle, EmergencyHotkeyId); _hotkeyRegistered = false; }
            if (_displayCovers != null) { _displayCovers.Dispose(); _displayCovers = null; }
            if (_tick != null) { _tick.Stop(); _tick.Dispose(); _tick = null; }
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Unhook();
            base.Dispose(disposing);
        }
    }
}
