using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace GameJam.ItchPublisher
{
    public readonly struct ButlerResult
    {
        public readonly int ExitCode;
        public readonly string Output;
        public readonly bool TimedOut;
        public readonly bool Cancelled;
        public readonly string StartError;

        public ButlerResult(int exitCode, string output, bool timedOut = false, bool cancelled = false, string startError = null)
        {
            ExitCode = exitCode;
            Output = output ?? "";
            TimedOut = timedOut;
            Cancelled = cancelled;
            StartError = startError;
        }

        public bool Ok => ExitCode == 0 && !TimedOut && !Cancelled && StartError == null;

        public string Message
        {
            get
            {
                if (StartError != null) return StartError;
                if (Cancelled) return "cancelled";
                if (TimedOut) return "timed out";
                var text = Output.Trim();
                return text.Length > 500 ? "…" + text.Substring(text.Length - 500) : text;
            }
        }
    }

    /// <summary>Remote channel state, as reported by "butler status".</summary>
    [Serializable]
    public class ChannelStatus
    {
        public string channel;
        public string upload;
        public string build;
        public string version;
    }

    /// <summary>
    /// Finds, installs and runs butler, itch.io's upload tool (https://itch.io/docs/butler/).
    /// Run() is safe to call from a background thread.
    /// </summary>
    public static class Butler
    {
        public static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string ExeName => IsWindows ? "butler.exe" : "butler";

        /// <summary>Where "Install butler" puts it (shared by all projects).</summary>
        public static string ManagedDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ItchPublisher", "butler");

        static string DownloadUrl
        {
            get
            {
                var os = IsWindows ? "windows" : IsMac ? "darwin" : "linux";
                return $"https://broth.itch.zone/butler/{os}-amd64/LATEST/archive/default";
            }
        }

        // ---------------------------------------------------------------- locate

        /// <summary>Path to butler or null. Order: preferences → installed by this tool → itch app → PATH.</summary>
        public static string Locate(out string source)
        {
            var custom = ItchUserPrefs.ButlerPathOverride;
            if (!string.IsNullOrEmpty(custom))
            {
                var path = Directory.Exists(custom) ? Path.Combine(custom, ExeName) : custom;
                source = "custom path";
                return File.Exists(path) ? path : null;
            }

            var managed = Path.Combine(ManagedDir, ExeName);
            if (File.Exists(managed)) { source = "installed by Itch Publisher"; return managed; }

            var fromApp = FindInItchApp();
            if (fromApp != null) { source = "itch app"; return fromApp; }

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), ExeName);
                    if (File.Exists(candidate)) { source = "PATH"; return candidate; }
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }

            source = null;
            return null;
        }

        /// <summary>The itch desktop app keeps its own copy of butler.</summary>
        static string FindInItchApp()
        {
            var appData = IsMac
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "itch")
                : IsWindows
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "itch");
            var root = Path.Combine(appData, "broth", "butler");
            try
            {
                var chosen = Path.Combine(root, ".chosen-version");
                if (File.Exists(chosen))
                {
                    var exe = Path.Combine(root, "versions", File.ReadAllText(chosen).Trim(), ExeName);
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { /* ignore a broken itch install */ }
            return null;
        }

        /// <summary>The file "butler login" saves the API key to, or null if not logged in.</summary>
        public static string CredentialsPath
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var candidates = new List<string>
                {
                    Path.Combine(home, ".config", "itch", "butler_creds"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch", "butler_creds"),
                };
                if (IsMac)
                    candidates.Insert(0, Path.Combine(home, "Library", "Application Support", "itch", "butler_creds"));
                return candidates.FirstOrDefault(File.Exists);
            }
        }

        // ---------------------------------------------------------------- run

        static Process current;
        static readonly object CurrentLock = new();

        /// <summary>
        /// Runs butler and waits for it to exit. Each output line goes to <paramref name="onLine"/>
        /// (on the calling thread's background reader, not the main thread).
        /// The API key is passed through the environment, never on the command line.
        /// </summary>
        public static ButlerResult Run(string exe, string args, string apiKey, Action<string> onLine = null,
            CancellationToken token = default, int timeoutMs = 0)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return new ButlerResult(-1, null, startError: "butler was not found");

            var output = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    WorkingDirectory = ItchProjectSettings.ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                if (!string.IsNullOrEmpty(apiKey)) psi.EnvironmentVariables[ItchUserPrefs.EnvVar] = apiKey;

                using var process = new Process { StartInfo = psi };
                DataReceivedEventHandler handler = (_, e) =>
                {
                    if (e.Data == null) return;
                    lock (output) output.AppendLine(e.Data);
                    try { onLine?.Invoke(e.Data); }
                    catch (Exception ex) { Debug.LogException(ex); }
                };
                process.OutputDataReceived += handler;
                process.ErrorDataReceived += handler;

                process.Start();
                lock (CurrentLock) current = process;
                // Never wait for console input (e.g. a y/N question).
                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var watch = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        if (token.IsCancellationRequested)
                        {
                            Kill(process);
                            return new ButlerResult(-1, Snapshot(output), cancelled: true);
                        }
                        if (timeoutMs > 0 && watch.ElapsedMilliseconds > timeoutMs)
                        {
                            Kill(process);
                            return new ButlerResult(-1, Snapshot(output), timedOut: true);
                        }
                    }
                    // The parameterless overload also waits for the async output readers to drain.
                    process.WaitForExit();
                    return new ButlerResult(process.ExitCode, Snapshot(output));
                }
                finally
                {
                    lock (CurrentLock) current = null;
                }
            }
            catch (Win32Exception e)
            {
                return new ButlerResult(-1, null, startError: "couldn't start butler: " + e.Message);
            }
            catch (Exception e)
            {
                return new ButlerResult(-1, Snapshot(output), startError: e.Message);
            }
        }

        static string Snapshot(StringBuilder sb)
        {
            lock (sb) return sb.ToString();
        }

        static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch { /* already gone */ }
        }

        /// <summary>Kills the butler process currently being run, if any.</summary>
        public static void KillCurrent()
        {
            lock (CurrentLock)
                if (current != null) Kill(current);
        }

        public static string Quote(string arg) => "\"" + (arg ?? "").Replace("\"", "\\\"") + "\"";

        public static string ParseVersion(string output)
        {
            var m = Regex.Match(output ?? "", @"v?(\d+\.\d+\.\d+[\w.\-]*)");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Parses the table printed by "butler status user/game".</summary>
        public static List<ChannelStatus> ParseStatus(string output)
        {
            var result = new List<ChannelStatus>();
            int iChannel = -1, iUpload = -1, iBuild = -1, iVersion = -1;

            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("|")) continue;
                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();

                if (iChannel < 0)
                {
                    iChannel = Array.FindIndex(cells, c => c.Equals("CHANNEL", StringComparison.OrdinalIgnoreCase));
                    iUpload = Array.FindIndex(cells, c => c.Equals("UPLOAD", StringComparison.OrdinalIgnoreCase));
                    iBuild = Array.FindIndex(cells, c => c.Equals("BUILD", StringComparison.OrdinalIgnoreCase));
                    iVersion = Array.FindIndex(cells, c => c.Equals("VERSION", StringComparison.OrdinalIgnoreCase));
                    continue;
                }

                string Cell(int i) => i >= 0 && i < cells.Length ? cells[i] : "";
                if (string.IsNullOrEmpty(Cell(iChannel))) continue;
                result.Add(new ChannelStatus
                {
                    channel = Cell(iChannel),
                    upload = Cell(iUpload),
                    build = Cell(iBuild),
                    version = Cell(iVersion),
                });
            }
            return result;
        }

        // ---------------------------------------------------------------- install / login

        public static bool IsInstalling { get; private set; }

        /// <summary>Downloads the latest butler from itch.io's official server (broth.itch.zone).</summary>
        public static void Install(Action<bool, string> done)
        {
            if (IsInstalling) return;
            IsInstalling = true;

            var zip = Path.Combine(Path.GetTempPath(), "butler-" + Guid.NewGuid().ToString("N") + ".zip");
            var request = new UnityWebRequest(DownloadUrl, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true },
                timeout = 300,
            };
            var progressId = Progress.Start("Downloading butler", "itch.io upload tool");
            EditorApplication.CallbackFunction tick = () => Progress.Report(progressId, request.downloadProgress);
            EditorApplication.update += tick;

            request.SendWebRequest().completed += _ =>
            {
                EditorApplication.update -= tick;
                bool ok = false;
                string message;
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        message = "Download failed: " + request.error;
                    }
                    else
                    {
                        if (Directory.Exists(ManagedDir)) Directory.Delete(ManagedDir, true);
                        Directory.CreateDirectory(ManagedDir);
                        ZipFile.ExtractToDirectory(zip, ManagedDir);

                        var exe = Path.Combine(ManagedDir, ExeName);
                        if (!IsWindows && File.Exists(exe))
                            Process.Start("chmod", "+x \"" + exe + "\"")?.WaitForExit(5000);

                        ok = File.Exists(exe);
                        message = ok ? exe : "The archive doesn't contain " + ExeName;
                    }
                }
                catch (Exception e)
                {
                    message = "Install failed: " + e.Message + (e is IOException or UnauthorizedAccessException
                        ? " (is butler running right now?)"
                        : "");
                }
                finally
                {
                    request.Dispose();
                    try { File.Delete(zip); } catch { /* temp file */ }
                    Progress.Finish(progressId, Progress.Status.Succeeded);
                    IsInstalling = false;
                }
                done?.Invoke(ok, message);
            };
        }

        /// <summary>
        /// Starts "butler login" in its own console window: it opens the browser,
        /// and the key is saved to <see cref="CredentialsPath"/>.
        /// </summary>
        public static bool StartLogin(string exe, out string error)
        {
            error = null;
            try
            {
                if (IsWindows)
                {
                    Process.Start(new ProcessStartInfo(exe, "login") { UseShellExecute = true });
                }
                else
                {
                    // No reliable way to pop up a terminal: run hidden, butler opens the browser itself.
                    var psi = new ProcessStartInfo(exe, "login")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        p.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log("[butler] " + e.Data); };
                        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.Log("[butler] " + e.Data); };
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
