using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace ScreenCoverControl
{
    public sealed class UiControlHost : IDisposable
    {
        readonly Control owner;
        readonly SessionLease lease;
        readonly InputGuard inputGuard;
        readonly ControlPipe pipe;
        readonly System.Windows.Forms.Timer timer;
        volatile bool disposed;

        sealed class UiWork
        {
            public readonly object Sync = new object();
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public readonly Stopwatch Watch = Stopwatch.StartNew();
            public bool Cancelled;
            public Dictionary<string, object> Reply;
        }
        public UiControlHost(Control owner, bool enabled, string mode, Action suspend, Action restore)
        {
            this.owner = owner;
                        inputGuard = new InputGuard(delegate {
                owner.BeginInvoke((Action)delegate {
                    if (!disposed) { try { lease.RestoreAfterPhysicalInput(); } catch { } }
                });
            });
            lease = new SessionLease(enabled, mode,
                delegate { inputGuard.Arm(); suspend(); },
                delegate { restore(); inputGuard.Dispose(); }, null);
            pipe = new ControlPipe(ControlPipe.NameFor(mode), Dispatch);
            timer = new System.Windows.Forms.Timer(); timer.Interval = 250;
            timer.Tick += delegate { if (!disposed) { try { lease.Poll(); } catch { /* retry restoration next tick */ } } };
            try { pipe.Start(); timer.Start(); }
            catch { pipe.Dispose(); timer.Dispose(); inputGuard.Dispose(); throw; }
        }
        Dictionary<string, object> Dispatch(Dictionary<string, object> request)
        {
            var work = new UiWork();
            try
            {
                if (disposed || owner.IsDisposed) return SessionLease.Error("OFFLINE", "遮屏程序正在退出。");
                owner.BeginInvoke((Action)delegate
                {
                    lock (work.Sync)
                    {
                        // A request which timed out while queued must never
                        // uncover the screen later when the UI becomes free.
                        if (work.Cancelled || disposed || work.Watch.ElapsedMilliseconds >= 2500) return;
                        work.Reply = lease.Handle(request);
                        work.Done.Set();
                    }
                });
                if (!work.Done.WaitOne(2500))
                {
                    lock (work.Sync)
                    {
                        if (work.Reply != null) return work.Reply;
                        work.Cancelled = true;
                    }
                    return SessionLease.Error("UI_TIMEOUT", "界面忙碌，未开始新的暂停操作。");
                }
                return work.Reply;
            }
            catch { return SessionLease.Error("OFFLINE", "遮屏控制暂时不可用。"); }
            finally
            {
                // Do not dispose Done on a timeout: a queued delegate may still
                // reference it. Its SafeWaitHandle will be reclaimed by the GC.
            }
        }
        public void RestoreManually() { lease.RestoreManually(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Stop(); timer.Dispose(); pipe.Dispose(); inputGuard.Dispose();
        }
    }
}
