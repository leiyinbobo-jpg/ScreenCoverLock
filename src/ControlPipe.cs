using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ScreenCoverControl
{
    public sealed class ControlPipe : IDisposable
    {
        readonly string name;
        readonly Func<Dictionary<string, object>, Dictionary<string, object>> handler;
        readonly int windowsSession = Process.GetCurrentProcess().SessionId;
        readonly object sync = new object();
        volatile bool stopping;
        NamedPipeServerStream active;
        Thread worker;
        public readonly ManualResetEvent Ready = new ManualResetEvent(false);
        public Exception StartError { get; private set; }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);

        public ControlPipe(string name, Func<Dictionary<string, object>, Dictionary<string, object>> handler)
        { this.name = name; this.handler = handler; }

        public static string NameFor(string mode)
        {
            return "ScreenCover.Control.v1." + WindowsIdentity.GetCurrent().User.Value + "." +
                Process.GetCurrentProcess().SessionId + "." + mode.ToLowerInvariant();
        }
        public static PipeSecurity OwnerSecurity()
        {
            var owner = WindowsIdentity.GetCurrent().User;
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(owner);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }
        public void Start()
        {
            worker = new Thread(Loop); worker.IsBackground = true; worker.Name = "ScreenCover control"; worker.Start();
            if (!Ready.WaitOne(3000)) throw new IOException("控制接口启动超时。");
            if (StartError != null) throw new IOException("控制接口启动失败。", StartError);
        }
        void Loop()
        {
            bool first = true;
            while (!stopping)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, OwnerSecurity()))
                    {
                        lock (sync) { if (stopping) return; active = pipe; }
                        first = false; Ready.Set();
                        pipe.WaitForConnection();
                        uint client;
                        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out client) ||
                            Process.GetProcessById((int)client).SessionId != windowsSession) continue;
                        var serializer = new JavaScriptSerializer(); serializer.MaxJsonLength = 4096; serializer.RecursionLimit = 8;
                        Dictionary<string, object> reply;
                        try
                        {
                            var request = serializer.Deserialize<Dictionary<string, object>>(ReadLine(pipe));
                            if (request == null) throw new IOException();
                            reply = handler(request);
                        }
                        catch { reply = SessionLease.Error("BAD_REQUEST", "请求无效或超时。"); }
                        byte[] bytes = Encoding.UTF8.GetBytes(serializer.Serialize(reply) + "\n");
                        IAsyncResult write = pipe.BeginWrite(bytes, 0, bytes.Length, null, null);
                        using (write.AsyncWaitHandle)
                        {
                            if (!write.AsyncWaitHandle.WaitOne(3000)) continue;
                            pipe.EndWrite(write);
                        }
                    }
                }
                catch (Exception error)
                {
                    if (first) { StartError = error; Ready.Set(); return; }
                    if (!stopping) Thread.Sleep(50);
                }
                finally { lock (sync) { active = null; } }
            }
        }
        static string ReadLine(Stream stream)
        {
            var result = new MemoryStream();
            byte[] one = new byte[1];
            var watch = Stopwatch.StartNew();
            while (result.Length < 4096)
            {
                int remaining = 3000 - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0) throw new IOException("Request timeout");
                IAsyncResult read = stream.BeginRead(one, 0, 1, null, null);
                using (read.AsyncWaitHandle)
                {
                    if (!read.AsyncWaitHandle.WaitOne(remaining)) throw new IOException("Request timeout");
                    if (stream.EndRead(read) == 0) throw new EndOfStreamException();
                }
                if (one[0] == 10) return new UTF8Encoding(false, true).GetString(result.ToArray());
                result.WriteByte(one[0]);
            }
            throw new IOException("Request too long");
        }
        public void Dispose()
        {
            stopping = true;
            lock (sync) { if (active != null) { try { active.Dispose(); } catch { } } }
            if (worker != null && worker != Thread.CurrentThread) worker.Join(1000);
            // Ready is retained until the worker has stopped; no wait handle is
            // disposed while an in-flight startup might still signal it.
            if (worker == null || !worker.IsAlive) Ready.Dispose();
        }
    }
}
