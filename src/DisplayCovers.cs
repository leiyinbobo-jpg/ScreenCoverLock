using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenCoverSupport
{
    // One opaque window per monitor; the primary window owns password input.
    sealed class DisplayCovers : IDisposable
    {
        readonly Form owner;
        readonly Action focusInput;
        readonly bool topMost;
        readonly List<Form> covers = new List<Form>();
        bool disposed;
        bool suspended;

        public DisplayCovers(Form owner, Action focusInput, bool topMost)
        {
            this.owner = owner;
            this.focusInput = focusInput;
            this.topMost = topMost;
        }

        public void Start()
        {
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Rebuild();
        }

        void DisplayChanged(object sender, EventArgs e)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke((Action)delegate { if (!disposed) Rebuild(); }); }
            catch (InvalidOperationException) { }
        }

        void Rebuild()
        {
            var oldCovers = covers.ToArray();
            covers.Clear();
            owner.Bounds = Screen.PrimaryScreen.Bounds;
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Primary) continue;
                var cover = new BackgroundCoverForm();
                cover.FormBorderStyle = FormBorderStyle.None;
                cover.StartPosition = FormStartPosition.Manual;
                cover.Bounds = screen.Bounds;
                cover.BackColor = Color.Black;
                cover.ShowInTaskbar = false;
                cover.TopMost = topMost;
                cover.Activated += delegate { if (!disposed) focusInput(); };
                cover.MouseDown += delegate { if (!disposed) focusInput(); };
                cover.FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (!disposed && covers.Contains((Form)sender) && e.CloseReason == CloseReason.UserClosing)
                        e.Cancel = true;
                };
                covers.Add(cover);
                if (!suspended) cover.Show(owner);
            }
            foreach (Form cover in oldCovers) cover.Dispose();
            owner.Invalidate();
        }

        public void RefreshBackground() { foreach (Form cover in covers) cover.Invalidate(); }

        public void SetSuspended(bool value)
        {
            if (disposed) return;
            suspended = value;
            if (value) { foreach (Form cover in covers) cover.Hide(); }
            else Rebuild();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            foreach (Form cover in covers) cover.Dispose();
            covers.Clear();
        }
    }
}
