using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace ScreenCoverControl
{
    public sealed class ControlError : Exception
    {
        public readonly string Code;
        public ControlError(string code, string message) : base(message) { Code = code; }
    }

    // Independent from WinForms so lease/failure logic can be tested without
    // creating any window or touching the currently running cover program.
    public sealed class SessionLease
    {
        public const int MaximumLeaseSeconds = 600;
        public const int MaximumSessionSeconds = 1800;
        readonly Func<double> now;
        readonly Action suspend, restore;
        readonly object sync = new object();
        readonly bool enabled;
        readonly string mode;
        readonly string instance = Guid.NewGuid().ToString("N");
        string token;
        bool recoveryPending;
        string lastRestoreReason = "none";
        double leaseEnd, hardEnd;

        public SessionLease(bool enabled, string mode, Action suspend, Action restore,
                            Func<double> clock)
        {
            this.enabled = enabled; this.mode = mode;
            this.suspend = suspend; this.restore = restore;
            now = clock ?? delegate { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; };
        }

        public Dictionary<string, object> Handle(Dictionary<string, object> request)
        {
            lock (sync)
            {
                try
                {
                    Poll();
                    string action = Text(request, "action");
                    if (action == "status") return Snapshot();
                    if (action == "pause")
                    {
                        if (!enabled) throw new ControlError("NOT_ENABLED", "本次启动未授权临时暂停。");
                        if (token != null) throw new ControlError("BUSY", "已有操作会话，请先恢复遮屏。");
                        int seconds = Number(request, "leaseSeconds", 60, MaximumLeaseSeconds);
                        int max = Number(request, "maxSeconds", 0, MaximumSessionSeconds);
                        if (max != 0 && max < 60) throw new ControlError("BAD_REQUEST", "总时长应为0（不限）或至少60秒。");
                        string candidate = NewToken();
                        try { suspend(); }
                        catch
                        {
                            recoveryPending = true;
                            try { restore(); recoveryPending = false; } catch { }
                            throw new ControlError("SUSPEND_FAILED", "未能暂停，已尝试恢复遮屏。");
                        }
                        lastRestoreReason = "none";
                        token = candidate;
                        hardEnd = max == 0 ? double.PositiveInfinity : now() + max;
                        leaseEnd = Math.Min(now() + seconds, hardEnd);
                        var reply = Snapshot();
                        reply["session"] = token; // returned only to the acquiring client
                        return reply;
                    }
                    if (action == "renew" || action == "resume")
                    {
                        if (token == null) throw new ControlError("NO_SESSION", "会话已结束，遮屏已恢复或正在恢复。");
                        if (!SameToken(token, Text(request, "session")))
                            throw new ControlError("INVALID_SESSION", "会话凭据不匹配。");
                        if (action == "resume") End();
                        else leaseEnd = Math.Min(now() + Number(request, "leaseSeconds", 60, MaximumLeaseSeconds), hardEnd);
                        return Snapshot();
                    }
                    throw new ControlError("UNKNOWN_ACTION", "不支持的控制命令。");
                }
                catch (ControlError error) { return Error(error.Code, error.Message); }
                catch { return Error("CONTROL_FAILED", "控制操作未完成，请使用手动恢复或应急退出。"); }
            }
        }

        public void Poll()
        {
            lock (sync)
            {
                if (recoveryPending || (token != null && (now() >= leaseEnd || now() >= hardEnd))) End();
            }
        }
        public void RestoreManually()
        {
            lock (sync) { if (token != null || recoveryPending) End(); }
        }
        public void RestoreAfterPhysicalInput()
        {
            lock (sync)
            {
                if (token == null && !recoveryPending) return;
                lastRestoreReason = "physical_input";
                recoveryPending = true;
                End();
            }
        }
        void End()
        {
            // Clear the lease only after restoration succeeds. A failed restore
            // is retried by the host's next timer tick; it cannot be renewed.
            recoveryPending = true;
            restore(); token = null; leaseEnd = hardEnd = 0; recoveryPending = false;
        }
        Dictionary<string, object> Snapshot()
        {
            return new Dictionary<string, object> {
                {"ok", true}, {"mode", mode}, {"instance", instance},
                {"inputGuard", true}, {"lastRestoreReason", lastRestoreReason}, {"automationAllowed", enabled}, {"state", token == null ? "covered" : "paused"},
                {"leaseSecondsRemaining", token == null ? 0 : Math.Max(0, (int)Math.Ceiling(leaseEnd - now()))},
                {"unlimitedSession", token != null && double.IsPositiveInfinity(hardEnd)},
                {"supportsUnlimitedSession", true},
                {"sessionSecondsRemaining", token != null && double.IsPositiveInfinity(hardEnd)
                    ? (object)null : token == null ? 0 : Math.Max(0, (int)Math.Ceiling(hardEnd - now()))}
            };
        }
        public static Dictionary<string, object> Error(string code, string message)
        {
            return new Dictionary<string, object> { {"ok", false}, {"error", code}, {"message", message} };
        }
        static string Text(Dictionary<string, object> request, string name)
        {
            object value;
            if (!request.TryGetValue(name, out value) || !(value is string) || ((string)value).Length > 256)
                throw new ControlError("BAD_REQUEST", "缺少或无效的请求字段。");
            return (string)value;
        }
        static int Number(Dictionary<string, object> request, string name, int min, int max)
        {
            object value;
            int number;
            if (!request.TryGetValue(name, out value) || !(value is int) ||
                !int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out number) || number < min || number > max)
                throw new ControlError("BAD_REQUEST", "时长超出允许范围。");
            return number;
        }
        static string NewToken()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        static bool SameToken(string expected, string supplied)
        {
            if (expected.Length != supplied.Length) return false;
            int diff = 0;
            for (int i = 0; i < expected.Length; i++) diff |= expected[i] ^ supplied[i];
            return diff == 0;
        }
    }
}
