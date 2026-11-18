using System;
using System.IO;
using System.Windows.Forms;
using ScreenCoverControl;

namespace ScreenCoverCasual
{
    sealed partial class CoverForm
    {
        readonly bool _automationEnabled;
        bool _automationPaused, _restoreHotkeyRegistered;
        const int AutomationRestoreHotkeyId = 0x5344;
        UiControlHost _automationHost;

        void InitializeAutomation()
        {
            if (_automationEnabled)
            {
                _restoreHotkeyRegistered = RegisterHotKey(Handle, AutomationRestoreHotkeyId, 0x0002 | 0x0004 | 0x4000, (uint)Keys.L);
                if (!_restoreHotkeyRegistered)
                {
                    MessageBox.Show("Ctrl+Shift+L 恢复键注册失败，新版将退出。", "遮屏控制");
                    _allowClose = true; Close(); return;
                }
            }
            try
            {
                _automationHost = new UiControlHost(this, _automationEnabled, "casual", SuspendForAutomation, RestoreAfterAutomation);
            }
            catch
            {
                MessageBox.Show("本机控制接口未能启动。新版将退出，请检查是否另有实例运行。", "遮屏控制");
                _allowClose = true; Close();
            }
        }

        void SuspendForAutomation()
        {
            if (_automationPaused || _allowClose) throw new InvalidOperationException();
            _automationPaused = true;
            _tick.Stop();
            try
            {
                if (_hook != IntPtr.Zero)
                {
                    if (!UnhookWindowsHookEx(_hook)) throw new IOException("Keyboard hook could not be released.");
                    _hook = IntPtr.Zero;
                }
                CancelAttempt();
                if (_displayCovers != null) _displayCovers.SetSuspended(true);
                TopMost = false;
                Hide();
            }
            catch { RestoreAfterAutomation(); throw; }
        }

        void RestoreAfterAutomation()
        {
            if (_allowClose || IsDisposed) return;
            UpgradeRestored();
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = false;
            Show();
            if (_displayCovers != null) _displayCovers.SetSuspended(false);
            if (_hook == IntPtr.Zero)
            {
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero) throw new IOException("Keyboard hook could not be restored.");
            }
            _automationPaused = false;
            _tick.Start();
            Activate(); _input.Focus(); Invalidate();
        }
    }
}
