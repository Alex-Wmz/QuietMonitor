using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuietMonitor {
    sealed class Hotkeys:NativeWindow,IDisposable {
        [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
        readonly Action<int> action;public int Failed;
        public Hotkeys(Action<int> action){this.action=action;CreateHandle(new CreateParams{Caption="QuietMonitor hotkeys",Parent=new IntPtr(-3)});int[] keys={(int)Keys.M,(int)Keys.B,(int)Keys.L,(int)Keys.S};for(int i=0;i<keys.Length;i++)if(!RegisterHotKey(Handle,i+1,0x4003,(uint)keys[i]))Failed++;}
        protected override void WndProc(ref Message message){if(message.Msg==0x0312)action(message.WParam.ToInt32());base.WndProc(ref message);}
        public void Dispose(){for(int i=1;i<=4;i++)UnregisterHotKey(Handle,i);DestroyHandle();}
    }
    sealed class MonitorContext:ApplicationContext {
        readonly ConfigStore store;readonly Sampler sampler;readonly OverlayForm overlay;readonly NotifyIcon tray;readonly Hotkeys hotkeys;readonly EventWaitHandle settingsRequest;
        readonly System.Windows.Forms.Timer timer;readonly System.Windows.Forms.Timer exitTimer;
        readonly BreakReminderRuntime reminder;
        AppConfig config;SettingsForm settings;bool overlayVisible=true;
        public MonitorContext(ConfigStore store,AppConfig config,bool openSettings,int exitAfter){
            this.store=store;this.config=config;sampler=new Sampler(config);settingsRequest=new EventWaitHandle(false,EventResetMode.AutoReset,Program.SettingsEventName);
            var menu=new ContextMenuStrip();menu.Items.Add("设置…",null,delegate{OpenSettings();});menu.Items.Add("显示 / 隐藏悬浮窗",null,delegate{ToggleVisibility();});menu.Items.Add("桌面面板 / 顶部横条",null,delegate{SwitchMode();});menu.Items.Add("鼠标穿透 / 解除锁定",null,delegate{ToggleLock();});menu.Items.Add("重置当前位置",null,delegate{overlay.ResetPosition();overlay.Render(sampler.Latest);});
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add("保存当前设置与位置",null,delegate{Save();});menu.Items.Add("以管理员身份重启",null,delegate{Elevate();});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出",null,delegate{ExitThread();});
            overlay=new OverlayForm(config,menu);overlay.Show();overlay.Render(sampler.Latest);
            reminder=new BreakReminderRuntime(delegate{return overlayVisible?overlay.Bounds:Rectangle.Empty;});reminder.Configure(config);
            tray=new NotifyIcon{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath),Text="QuietMonitor · 传感器内存采样",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=delegate{OpenSettings();};
            hotkeys=new Hotkeys(delegate(int id){if(id==1)ToggleVisibility();else if(id==2)SwitchMode();else if(id==3)ToggleLock();else OpenSettings();});
            timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=delegate{if(settingsRequest.WaitOne(0))OpenSettings();overlay.Render(sampler.Latest);reminder.Tick();};timer.Start();
            if(exitAfter>0){exitTimer=new System.Windows.Forms.Timer{Interval=Math.Min(exitAfter,3600)*1000};exitTimer.Tick+=delegate{ExitThread();};exitTimer.Start();}
            if(openSettings)OpenSettings();
            if(hotkeys.Failed>0)tray.ShowBalloonTip(5000,"QuietMonitor","部分快捷键已被其他程序占用，请使用托盘菜单。",ToolTipIcon.Info);
        }
        void OpenSettings(){if(settings==null||settings.IsDisposed){settings=new SettingsForm(sampler,config,store,Apply,Elevate,delegate{return reminder.Status;});settings.FormClosed+=delegate{settings=null;};settings.Show();}else{settings.Show();settings.Activate();}}
        void Apply(AppConfig updated,bool save){
            overlay.CapturePosition();updated.Desktop=config.Desktop;updated.GameBar=config.GameBar;
            if(save)store.Save(updated);overlay.Configure(updated);config=updated;sampler.Configure(updated);overlay.Render(sampler.Latest);reminder.Configure(updated);
        }
        void Save(){try{overlay.CapturePosition();store.Save(config);tray.ShowBalloonTip(2500,"QuietMonitor","设置与位置已保存。",ToolTipIcon.Info);}catch(Exception error){MessageBox.Show("保存失败："+error.GetType().Name,"QuietMonitor");}}
        void ToggleVisibility(){overlayVisible=!overlayVisible;if(overlayVisible){overlay.Show();overlay.Render(sampler.Latest);}else overlay.Hide();}
        void SwitchMode(){AppConfig updated=config.Clone();updated.Mode=config.Mode=="Desktop"?"GameBar":"Desktop";Apply(updated,false);}
        void ToggleLock(){AppConfig updated=config.Clone();updated.ClickThrough=!updated.ClickThrough;Apply(updated,false);}
        void Elevate(){try{var info=new ProcessStartInfo(Application.ExecutablePath,"--handoff --config-dir "+Program.Quote(store.DirectoryPath)){UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory};Process.Start(info);ExitThread();}catch(Win32Exception error){if(error.NativeErrorCode!=1223)MessageBox.Show("管理员重启失败："+error.GetType().Name,"QuietMonitor");}}
        protected override void ExitThreadCore(){timer.Stop();timer.Dispose();if(exitTimer!=null){exitTimer.Stop();exitTimer.Dispose();}hotkeys.Dispose();settingsRequest.Dispose();if(settings!=null)settings.Close();reminder.Dispose();overlay.Close();overlay.Dispose();tray.Visible=false;tray.Icon.Dispose();tray.Dispose();sampler.Dispose();base.ExitThreadCore();}
    }
    static class Program {
        public static string MutexName="Local\\QuietMonitor.Desktop.v1",SettingsEventName="Local\\QuietMonitor.Settings.v1";
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        [StructLayout(LayoutKind.Sequential)]struct IoCounters{public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
        [DllImport("kernel32.dll")]static extern bool GetProcessIoCounters(IntPtr process,out IoCounters counters);
        public static string Quote(string value){return "\""+value.Replace("\"","")+"\"";}
        [STAThread]static int Main(string[] args){
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuietMonitor");bool showSettings=false,handoff=false;int exitAfter=0,diagnose=0;string diagnosticPath=null;
            for(int i=0;i<args.Length;i++){
                if(args[i]=="--config-dir"&&i+1<args.Length)directory=Path.GetFullPath(args[++i]);
                else if(args[i]=="--settings")showSettings=true;else if(args[i]=="--handoff")handoff=true;
                else if(args[i]=="--exit-after"&&i+1<args.Length)Int32.TryParse(args[++i],out exitAfter);
                else if(args[i]=="--diagnose"&&i+2<args.Length){Int32.TryParse(args[++i],out diagnose);diagnosticPath=Path.GetFullPath(args[++i]);}
            }
            var store=new ConfigStore(directory);AppConfig config=store.Load();
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuietMonitor");
            if(!String.Equals(Path.GetFullPath(directory).TrimEnd('\\'),Path.GetFullPath(standard).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)){
                using(var hash=System.Security.Cryptography.SHA256.Create()){string suffix=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\').ToUpperInvariant()))).Replace("-","").Substring(0,16);MutexName+="."+suffix;SettingsEventName+="."+suffix;}
            }
            if(diagnose>0&&diagnosticPath!=null)return Diagnose(config,Math.Max(5,Math.Min(600,diagnose)),diagnosticPath);
            using(var mutex=new Mutex(false,MutexName)){
                bool owned=false;try{try{owned=mutex.WaitOne(handoff?10000:0);}catch(AbandonedMutexException){owned=true;}
                    if(!owned){try{using(var request=EventWaitHandle.OpenExisting(SettingsEventName))request.Set();}catch{MessageBox.Show("QuietMonitor 已在运行，请使用系统托盘菜单或 Ctrl+Alt+S 打开设置。","QuietMonitor");}return 0;}
                    SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var context=new MonitorContext(store,config,showSettings||!File.Exists(store.FilePath),exitAfter))Application.Run(context);return 0;
                }catch(Exception error){MessageBox.Show("QuietMonitor 无法启动："+error.GetType().Name+"。请确认完整解压软件和依赖文件。","QuietMonitor",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
                finally{if(owned)mutex.ReleaseMutex();}
            }
        }
        static int Diagnose(AppConfig config,int seconds,string output){
            try{using(var sampler=new Sampler(config)){
                Thread.Sleep(5000);Process process=Process.GetCurrentProcess();IoCounters before,after;GetProcessIoCounters(process.Handle,out before);TimeSpan cpu=process.TotalProcessorTime;Stopwatch clock=Stopwatch.StartNew();
                Thread.Sleep(seconds*1000);process.Refresh();GetProcessIoCounters(process.Handle,out after);Snapshot snapshot=sampler.Latest;
                var result=new {Application="QuietMonitor "+Application.ProductVersion,MeasuredSeconds=clock.Elapsed.TotalSeconds,CpuMachinePercent=(process.TotalProcessorTime-cpu).TotalSeconds/clock.Elapsed.TotalSeconds/Environment.ProcessorCount*100,WorkingSetMiB=process.WorkingSet64/1048576.0,PrivateMiB=process.PrivateMemorySize64/1048576.0,ProcessIoWriteBytes=after.WriteBytes-before.WriteBytes,ProcessIoWriteOperations=after.WriteOperations-before.WriteOperations,Snapshot=snapshot};
                File.WriteAllText(output,new JavaScriptSerializer().Serialize(result),new UTF8Encoding(false));return 0;
            }}catch{return 2;}
        }
    }
}
