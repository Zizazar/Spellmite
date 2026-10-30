using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GameJam.TeamTools
{
    public readonly struct GitResult
    {
        public readonly int ExitCode;
        public readonly string Output;
        public readonly string Error;
        public readonly bool TimedOut;
        public readonly bool GitMissing;

        public GitResult(int exitCode, string output, string error, bool timedOut = false, bool gitMissing = false)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
            TimedOut = timedOut;
            GitMissing = gitMissing;
        }

        public bool Ok => ExitCode == 0 && !TimedOut && !GitMissing;

        /// <summary>Short human-readable error description.</summary>
        public string Message
        {
            get
            {
                if (GitMissing) return "git was not found in PATH";
                if (TimedOut) return "timed out waiting for git / the server";
                var text = string.IsNullOrWhiteSpace(Error) ? Output : Error;
                text = text.Trim();
                return text.Length > 400 ? text.Substring(0, 400) + "…" : text;
            }
        }
    }

    /// <summary>
    /// Runs git from the Unity project root. Safe to call from a background thread.
    /// </summary>
    [InitializeOnLoad]
    public static class Git
    {
        public static readonly string ProjectRoot;

        static Git()
        {
            // Application.dataPath is only available on the main thread, so cache it up front.
            ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
        }

        public static string Quote(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";

        public static GitResult Run(string args, int timeoutMs = 30000)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // No interactive console prompts: a background process must never hang.
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

                using var process = Process.Start(psi);
                process.StandardInput.Close();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { /* already exited */ }
                    return new GitResult(-1, null, null, timedOut: true);
                }

                process.WaitForExit();
                return new GitResult(process.ExitCode, stdout.Result, stderr.Result);
            }
            catch (Win32Exception)
            {
                return new GitResult(-1, null, null, gitMissing: true);
            }
            catch (Exception e)
            {
                return new GitResult(-1, null, e.Message);
            }
        }

        /// <summary>Runs git in the background; the callback is invoked on the main thread.</summary>
        public static void RunAsync(string args, Action<GitResult> onDone, int timeoutMs = 30000)
        {
            Task.Run(() => Run(args, timeoutMs))
                .ContinueWith(t => MainThread.Post(() => onDone?.Invoke(t.Result)));
        }
    }
}
