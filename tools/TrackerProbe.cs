using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.ViewModels;

namespace GBCWorkHub.Tools
{
    public static class TrackerProbe
    {
        [STAThread]
        public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += (s, e) =>
            {
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var vm = new MainViewModel();
                        var pc = new RemotePcDto { PcName = "KEB-7VY98V3", IpAddress = "172.16.49.61", RdpPort = 3389 };
                        string msg;
                        Console.WriteLine("START=" + vm.StartRemoteSession(pc, out msg) + " STATUS=" + vm.CurrentStatus);
                        string payload = "GBCWORKHUB::{\"type\":\"GBC_RDP_STATUS\",\"schemaVersion\":1,\"computerName\":\"KEB-7VY98V3\",\"windowsUser\":\"T\\\\u\",\"clientName\":null,\"collectedAt\":\"2026-07-27 14:00:00\",\"latestEventId\":25,\"latestRecordId\":99001,\"sessionRaw\":\"u rdp-tcp#0 3 Active\",\"events\":[{\"recordId\":99001,\"eventId\":25,\"eventTime\":\"2026-07-27 14:00:00\",\"eventMessage\":\"ok\"}]}";
                        DoEvents(3500); // pass grace
                        vm.HandleClipboardText(payload, true);
                        DoEvents(500);
                        Console.WriteLine("CONFIRMED STATUS=" + vm.CurrentStatus + " SRC=" + vm.StatusSource + " CONF=" + vm.IsRdpSessionConfirmed);
                        KillAllMstsc();
                        for (int i = 0; i < 30; i++)
                        {
                            DoEvents(500);
                            if (vm.StatusSource == "LOCAL_MSTSC_EXIT" && vm.CurrentStatus == "사용 가능")
                                break;
                        }
                        Console.WriteLine("END STATUS=" + vm.CurrentStatus + " SRC=" + vm.StatusSource + " MSG=" + vm.LastReceiveMessage + " EVENT=" + vm.LatestEventId);
                        vm.HandleClipboardText(payload, true);
                        Console.WriteLine("REPROCESS STATUS=" + vm.CurrentStatus + " (expect 사용 가능)");
                        // cancel path
                        vm.StartRemoteSession(pc, out msg);
                        DoEvents(3500);
                        KillAllMstsc();
                        for (int i = 0; i < 30; i++)
                        {
                            DoEvents(500);
                            if (vm.StatusSource == "LOCAL_MSTSC_EXIT") break;
                        }
                        Console.WriteLine("CANCEL STATUS=" + vm.CurrentStatus + " MSG=" + vm.LastReceiveMessage);
                        vm.Dispose();
                    }
                    catch (Exception ex) { Console.WriteLine("ERROR=" + ex); }
                    finally { app.Shutdown(); }
                }));
            };
            app.Run();
            return 0;
        }
        static void KillAllMstsc()
        {
            foreach (var p in Process.GetProcessesByName("mstsc"))
            {
                try { p.Kill(); } catch { }
                try { p.WaitForExit(5000); } catch { }
                try { p.Dispose(); } catch { }
            }
        }
        static void DoEvents(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
