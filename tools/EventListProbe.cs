using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using GBCWorkHub.DTO;
using GBCWorkHub.UI.ViewModels;
public class P {
 [STAThread] static int Main() {
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.Startup+=(s,e)=>app.Dispatcher.BeginInvoke(new Action(()=>{
   try{
    var vm=new MainViewModel();
    var pc=new RemotePcDto{PcName="KEB-7VY98V3",IpAddress="172.16.49.61"};
    string m; vm.StartRemoteSession(pc,out m);
    Do(3500);
    vm.HandleClipboardText("GBCWORKHUB::{\"type\":\"GBC_RDP_STATUS\",\"schemaVersion\":1,\"computerName\":\"KEB-7VY98V3\",\"windowsUser\":\"A\\\\u\",\"clientName\":null,\"collectedAt\":\"2026-07-27 15:00:00\",\"latestEventId\":25,\"latestRecordId\":88001,\"sessionRaw\":\"u rdp-tcp#0 3 Active\",\"events\":[{\"recordId\":88001,\"eventId\":25,\"eventTime\":\"2026-07-27 15:00:00\",\"eventMessage\":\"User: A\\\\u\\r\\nSession ID: 3\\r\\nSource Network Address: 1.1.1.1\"}]}", true);
    Do(300);
    Console.WriteLine("AFTER_CONNECT count="+vm.RecentEventCount+" top="+vm.RecentEvents[0].EventDescription);
    foreach(var p in Process.GetProcessesByName("mstsc")){try{p.Kill();}catch{} try{p.WaitForExit(3000);}catch{}}
    for(int i=0;i<30;i++){ Do(500); if(vm.StatusSource=="LOCAL_MSTSC_EXIT") break; }
    Console.WriteLine("AFTER_END status="+vm.CurrentStatus+" count="+vm.RecentEventCount+" top="+vm.RecentEvents[0].EventDescription+" id="+vm.RecentEvents[0].EventId);
    Console.WriteLine("LATEST_EVENT_ID_UNCHANGED="+vm.LatestEventId);
    vm.Dispose();
   } catch(Exception ex){ Console.WriteLine(ex);} finally{ app.Shutdown();}
  }));
  app.Run(); return 0;
 }
 static void Do(int ms){ var f=new DispatcherFrame(); var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)}; t.Tick+=(s,e)=>{t.Stop(); f.Continue=false;}; t.Start(); Dispatcher.PushFrame(f);} 
}
