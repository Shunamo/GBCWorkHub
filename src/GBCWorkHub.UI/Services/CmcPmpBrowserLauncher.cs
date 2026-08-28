using System;
using System.Configuration;
using System.Diagnostics;

namespace GBCWorkHub.UI.Services
{
    /// <summary>
    /// CMC ManageEngine PMP Auto Logon 페이지를 기본 브라우저로 연다.
    /// </summary>
    public static class CmcPmpBrowserLauncher
    {
        public const string DefaultAutoLogonUrl =
            "https://pmp.cmcdubai.ae/PassTrixMain.cc#/AutoLogonFullView";

        public static string GetAutoLogonUrl()
        {
            string configured = (ConfigurationManager.AppSettings["CMC.Host"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(configured))
                return DefaultAutoLogonUrl;

            // Host에 도메인만 넣은 경우 Auto Logon 경로 보강
            if (configured.IndexOf("://", StringComparison.Ordinal) < 0
                && configured.IndexOf('/') < 0)
            {
                return "https://" + configured.TrimEnd('/') + "/PassTrixMain.cc#/AutoLogonFullView";
            }

            return configured;
        }

        public static LaunchResult TryOpenAutoLogon()
        {
            string url = GetAutoLogonUrl();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                DiagnosticLogger.Info("CMC_PMP", "Opened browser url=" + url);
                return LaunchResult.Ok(url);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Error("CMC_PMP", "Open failed: " + ex.Message);
                return LaunchResult.Fail("브라우저를 열 수 없습니다.\n" + ex.Message + "\nURL: " + url);
            }
        }

        public struct LaunchResult
        {
            public bool Succeeded;
            public string Message;

            public static LaunchResult Ok(string message)
            {
                return new LaunchResult { Succeeded = true, Message = message ?? string.Empty };
            }

            public static LaunchResult Fail(string message)
            {
                return new LaunchResult { Succeeded = false, Message = message ?? "실패" };
            }
        }
    }
}
