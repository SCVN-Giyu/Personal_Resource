using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Supercent.LunaTextureOptimizer.Editor
{
    public static class ProcessRunner
    {
        const int TIMEOUT_MS = 120000;

        public static bool Run(string executable, IEnumerable<string> arguments, out string error)
        {
            string output;
            return Run(executable, arguments, out output, out error);
        }

        public static bool Run(string executable, IEnumerable<string> arguments, out string output, out string error)
        {
            var info = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(info))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(TIMEOUT_MS))
                {
                    try { process.Kill(); }
                    catch { /* already exited */ }
                    output = null;
                    error = "timed out after " + TIMEOUT_MS / 1000 + " s";
                    return false;
                }
                process.WaitForExit();
                output = stdout.Result;
                error = process.ExitCode == 0 ? null : "exit code " + process.ExitCode + ": " + (stderr.Result + stdout.Result).Trim();
                return process.ExitCode == 0;
            }
        }

        public static string Quote(string argument)
        {
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }
    }
}
