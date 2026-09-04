using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GBCWorkHub.Updater
{
    /// <summary>
    /// Runs from %TEMP% only. Never replace the Updater that is currently executing from the install folder.
    /// Flow: wait main → hash check → staging extract → backup → replace → launch (rollback on failure).
    /// </summary>
    internal static class Program
    {
        private static string _logPath;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var opt = Args.Parse(args);
                if (opt == null || string.IsNullOrWhiteSpace(opt.PackagePath) || string.IsNullOrWhiteSpace(opt.InstallDir))
                {
                    WriteUsage();
                    return 2;
                }

                Directory.CreateDirectory(opt.WorkDir);
                _logPath = Path.Combine(opt.WorkDir, "updater.log");
                Log("Updater start workDir=" + opt.WorkDir);

                if (opt.WaitPid > 0)
                    WaitForProcessExit(opt.WaitPid, opt.WaitTimeoutMs);

                if (!File.Exists(opt.PackagePath))
                    throw new FileNotFoundException("Package not found", opt.PackagePath);

                if (!string.IsNullOrWhiteSpace(opt.Sha256))
                {
                    string actual = ComputeSha256Hex(opt.PackagePath);
                    if (!string.Equals(actual, opt.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Package SHA256 mismatch. expected=" + opt.Sha256 + " actual=" + actual);
                    Log("SHA256 OK");
                }

                string staging = Path.Combine(opt.WorkDir, "staging");
                string backup = Path.Combine(opt.WorkDir, "backup");
                PrepareEmptyDir(staging);
                PrepareEmptyDir(backup);

                ZipFile.ExtractToDirectory(opt.PackagePath, staging);
                Log("Extracted to staging");

                var stagedFiles = ListFilesRelative(staging);
                if (stagedFiles.Count == 0)
                    throw new InvalidOperationException("Package is empty");

                Directory.CreateDirectory(opt.InstallDir);
                BackupExisting(opt.InstallDir, backup, stagedFiles);
                Log("Backup done count=" + stagedFiles.Count);

                try
                {
                    ApplyStaging(staging, opt.InstallDir, stagedFiles);
                    Log("Apply done");
                }
                catch (Exception applyEx)
                {
                    Log("Apply failed: " + applyEx);
                    try
                    {
                        RestoreBackup(backup, opt.InstallDir, stagedFiles);
                        Log("Rollback OK");
                    }
                    catch (Exception rbEx)
                    {
                        Log("Rollback failed: " + rbEx);
                    }
                    throw;
                }

                if (!string.IsNullOrWhiteSpace(opt.LaunchPath) && File.Exists(opt.LaunchPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = opt.LaunchPath,
                        WorkingDirectory = opt.InstallDir,
                        UseShellExecute = true
                    });
                    Log("Launched " + opt.LaunchPath);
                }

                return 0;
            }
            catch (Exception ex)
            {
                try { Log("FATAL: " + ex); } catch { }
                try
                {
                    Console.Error.WriteLine(ex.ToString());
                }
                catch { }
                return 1;
            }
        }

        private static void WriteUsage()
        {
            Console.WriteLine("GBCWorkHubUpdater --package <zip> --install-dir <dir> [--sha256 <hex>] [--wait-pid <pid>] [--launch <exe>] [--work-dir <dir>]");
        }

        private static void WaitForProcessExit(int pid, int timeoutMs)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    Log("Waiting for PID " + pid);
                    if (!p.WaitForExit(timeoutMs))
                    {
                        Log("Timeout waiting for PID; attempting Kill");
                        try { p.Kill(); } catch { }
                        p.WaitForExit(10000);
                    }
                }
            }
            catch (ArgumentException)
            {
                Log("PID already exited: " + pid);
            }

            // Brief settle so file locks release.
            Thread.Sleep(800);
        }

        private static void BackupExisting(string installDir, string backupDir, List<string> relativeFiles)
        {
            foreach (string rel in relativeFiles)
            {
                string src = Path.Combine(installDir, rel);
                if (!File.Exists(src))
                    continue;
                string dest = Path.Combine(backupDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(src, dest, true);
            }
        }

        private static void ApplyStaging(string staging, string installDir, List<string> relativeFiles)
        {
            foreach (string rel in relativeFiles)
            {
                string src = Path.Combine(staging, rel);
                string dest = Path.Combine(installDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? installDir);
                File.Copy(src, dest, true);
            }
        }

        private static void RestoreBackup(string backupDir, string installDir, List<string> relativeFiles)
        {
            foreach (string rel in relativeFiles)
            {
                string bak = Path.Combine(backupDir, rel);
                string dest = Path.Combine(installDir, rel);
                if (File.Exists(bak))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? installDir);
                    File.Copy(bak, dest, true);
                }
            }
        }

        private static List<string> ListFilesRelative(string root)
        {
            var list = new List<string>();
            foreach (string full in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrEmpty(rel))
                    list.Add(rel);
            }
            return list;
        }

        private static void PrepareEmptyDir(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using (var fs = File.OpenRead(filePath))
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void Log(string message)
        {
            string line = DateTime.Now.ToString("o") + " " + message + Environment.NewLine;
            if (!string.IsNullOrEmpty(_logPath))
                File.AppendAllText(_logPath, line, Encoding.UTF8);
        }

        private sealed class Args
        {
            public string PackagePath;
            public string InstallDir;
            public string Sha256;
            public string LaunchPath;
            public string WorkDir;
            public int WaitPid;
            public int WaitTimeoutMs = 120000;

            public static Args Parse(string[] args)
            {
                if (args == null || args.Length == 0)
                    return null;

                var o = new Args();
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    string v = (i + 1 < args.Length) ? args[i + 1] : null;
                    if (EqualsArg(a, "--package") && v != null) { o.PackagePath = v; i++; }
                    else if (EqualsArg(a, "--install-dir") && v != null) { o.InstallDir = v; i++; }
                    else if (EqualsArg(a, "--sha256") && v != null) { o.Sha256 = v; i++; }
                    else if (EqualsArg(a, "--launch") && v != null) { o.LaunchPath = v; i++; }
                    else if (EqualsArg(a, "--work-dir") && v != null) { o.WorkDir = v; i++; }
                    else if (EqualsArg(a, "--wait-pid") && v != null) { int.TryParse(v, out o.WaitPid); i++; }
                    else if (EqualsArg(a, "--wait-timeout-ms") && v != null) { int.TryParse(v, out o.WaitTimeoutMs); i++; }
                }

                if (string.IsNullOrWhiteSpace(o.WorkDir))
                    o.WorkDir = Path.Combine(Path.GetTempPath(), "GBCWorkHubUpdater", Guid.NewGuid().ToString("N"));

                return o;
            }

            private static bool EqualsArg(string a, string name)
            {
                return string.Equals(a, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
