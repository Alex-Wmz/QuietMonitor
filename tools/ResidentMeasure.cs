// One-shot, read-only process measurement. No game memory reads, hooks or input.
// Keep aggregates in memory; write exactly one local JSON after the interval.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class ResidentMeasure {
    [StructLayout(LayoutKind.Sequential)]struct Io {public ulong Reads,Writes,Others,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct Mem {public uint Size,PageFaults;public UIntPtr PeakWorkingSet,WorkingSet,QuotaPeakPaged,QuotaPaged,QuotaPeakNonPaged,QuotaNonPaged,Pagefile,PeakPagefile,Private;}
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    delegate bool EnumProc(IntPtr hwnd,IntPtr parameter);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll")]static extern bool GetProcessIoCounters(IntPtr handle,out Io counters);
    [DllImport("psapi.dll")]static extern bool GetProcessMemoryInfo(IntPtr handle,out Mem memory,uint size);
    [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int maximum);
    sealed class ProcessSample:IDisposable {
        readonly int pid;readonly IntPtr handle;long cpuStart;Io ioStart;bool cpuValid,ioValid;
        public int MemorySamples,MemoryFailures;public double WorkingSum,WorkingPeak,PrivateSum,PrivatePeak;
        public ProcessSample(int id){pid=id;if(id==0)return;handle=OpenProcess(0x1000,false,id);long a,b,k,u;cpuValid=handle!=IntPtr.Zero&&GetProcessTimes(handle,out a,out b,out k,out u);if(cpuValid){GetProcessTimes(handle,out a,out b,out k,out u);cpuStart=k+u;}ioValid=handle!=IntPtr.Zero&&GetProcessIoCounters(handle,out ioStart);}
        public void Sample(){if(pid==0)return;Mem m;if(handle==IntPtr.Zero||!GetProcessMemoryInfo(handle,out m,(uint)Marshal.SizeOf(typeof(Mem)))){MemoryFailures++;return;}double w=m.WorkingSet.ToUInt64()/1048576.0,p=m.Private.ToUInt64()/1048576.0;MemorySamples++;WorkingSum+=w;PrivateSum+=p;WorkingPeak=Math.Max(w,WorkingPeak);PrivatePeak=Math.Max(p,PrivatePeak);}
        public object Result(double seconds){long a,b,k,u;Io io;bool cpu=cpuValid&&GetProcessTimes(handle,out a,out b,out k,out u),writes=ioValid&&GetProcessIoCounters(handle,out io);double? cpuPercent=null;ulong? bytes=null,operations=null;if(pid==0){cpuPercent=0;bytes=0;operations=0;}else{if(cpu){GetProcessTimes(handle,out a,out b,out k,out u);cpuPercent=(k+u-cpuStart)/10000000.0/seconds/Environment.ProcessorCount*100;}if(writes){GetProcessIoCounters(handle,out io);bytes=io.WriteBytes-ioStart.WriteBytes;operations=io.Writes-ioStart.Writes;}}
            return new{Present=pid!=0,QueryHandleAvailable=handle!=IntPtr.Zero,CpuMachinePercent=cpuPercent,WorkingSetMeanMiB=MemorySamples>0?(double?)(WorkingSum/MemorySamples):pid==0?0:(double?)null,WorkingSetPeakMiB=MemorySamples>0?(double?)WorkingPeak:pid==0?0:(double?)null,PrivateMeanMiB=MemorySamples>0?(double?)(PrivateSum/MemorySamples):pid==0?0:(double?)null,PrivatePeakMiB=MemorySamples>0?(double?)PrivatePeak:pid==0?0:(double?)null,ProcessIoWriteBytes=bytes,ProcessIoWriteOperations=operations,MemorySamples,MemoryFailures};}
        public void Dispose(){if(handle!=IntPtr.Zero)CloseHandle(handle);}
    }
    static IntPtr VisibleWindow(int pid,bool layered){IntPtr result=IntPtr.Zero;if(pid==0)return result;EnumWindows(delegate(IntPtr w,IntPtr p){uint owner;GetWindowThreadProcessId(w,out owner);if(owner==pid&&IsWindowVisible(w)&&((GetWindowLong(w,-20)&0x80000)!=0)==layered){result=w;return false;}return true;},IntPtr.Zero);return result;}
    static IntPtr VisibleNamedWindow(int pid,string title){IntPtr result=IntPtr.Zero;if(pid==0)return result;EnumWindows(delegate(IntPtr w,IntPtr p){uint owner;GetWindowThreadProcessId(w,out owner);if(owner==pid&&IsWindowVisible(w)){var text=new StringBuilder(128);GetWindowText(w,text,text.Capacity);if(text.ToString()==title){result=w;return false;}}return true;},IntPtr.Zero);return result;}
    static object WindowInfo(IntPtr w){if(w==IntPtr.Zero)return null;Rect r;GetWindowRect(w,out r);int style=GetWindowLong(w,-16),extended=GetWindowLong(w,-20);return new{X=r.Left,Y=r.Top,Width=r.Right-r.Left,Height=r.Bottom-r.Top,Dpi=GetDpiForWindow(w),Style=style,ExtendedStyle=extended,TopMost=(extended&8)!=0,NoActivate=(extended&0x08000000)!=0,Transparent=(extended&0x20)!=0,Layered=(extended&0x80000)!=0,HasCaption=(style&0x00c00000)!=0};}
    static int Main(string[] args){try{
        if(args.Length<5||args.Length>6)throw new ArgumentException("seconds output.json scene monitorPid gamePid [warmupSeconds]");
        int seconds=Math.Max(5,Math.Min(1800,Int32.Parse(args[0]))),monitorPid=Int32.Parse(args[3]),gamePid=Int32.Parse(args[4]);
        string output=Path.GetFullPath(args[1]);if(File.Exists(output))throw new IOException("Output exists; choose a new filename.");
        int warmup=args.Length==6?Math.Max(0,Math.Min(60,Int32.Parse(args[5]))):15;
        Console.WriteLine("Warmup "+warmup+" seconds; measurement has not started.");Thread.Sleep(warmup*1000);
        SetProcessDPIAware();var started=DateTime.UtcNow;var self=Process.GetCurrentProcess();TimeSpan ownCpu=self.TotalProcessorTime;
        IntPtr overlay=VisibleWindow(monitorPid,true),game=VisibleWindow(gamePid,false),reminder=VisibleNamedWindow(monitorPid,"QuietMonitor 久坐提醒");object overlayBefore=WindowInfo(overlay),gameBefore=WindowInfo(game),reminderBefore=WindowInfo(reminder);
        int samples=0,foregroundGame=0,foregroundMonitor=0,overlayVisible=0,settingsVisible=0,reminderVisible=0;long idle0,kernel0,user0;bool systemValid=GetSystemTimes(out idle0,out kernel0,out user0);
        using(var monitor=new ProcessSample(monitorPid))using(var gameProcess=new ProcessSample(gamePid)){
            var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){
                monitor.Sample();gameProcess.Sample();if(overlay==IntPtr.Zero){overlay=VisibleWindow(monitorPid,true);if(overlayBefore==null)overlayBefore=WindowInfo(overlay);}if(reminder==IntPtr.Zero){reminder=VisibleNamedWindow(monitorPid,"QuietMonitor 久坐提醒");if(reminderBefore==null)reminderBefore=WindowInfo(reminder);}uint foreground;GetWindowThreadProcessId(GetForegroundWindow(),out foreground);if(gamePid!=0&&foreground==gamePid)foregroundGame++;if(monitorPid!=0&&foreground==monitorPid)foregroundMonitor++;if(overlay!=IntPtr.Zero&&IsWindowVisible(overlay))overlayVisible++;if(VisibleNamedWindow(monitorPid,"QuietMonitor 设置")!=IntPtr.Zero)settingsVisible++;if(reminder!=IntPtr.Zero&&IsWindowVisible(reminder))reminderVisible++;samples++;
                if(samples%30==0)Console.WriteLine("Measured "+samples+" samples; summary remains in memory.");Thread.Sleep(Math.Max(1,(int)Math.Min(1000,seconds*1000-clock.ElapsedMilliseconds)));
            }
            double elapsed=clock.Elapsed.TotalSeconds;long idle1,kernel1,user1;double? systemCpu=null;if(systemValid&&GetSystemTimes(out idle1,out kernel1,out user1)){long total=kernel1-kernel0+user1-user0;if(total>0)systemCpu=100.0*(total-(idle1-idle0))/total;}
            var result=new{SchemaVersion=2,Scene=args[2],StartedUtc=started,WarmupSeconds=warmup,MeasuredSeconds=elapsed,LogicalProcessors=Environment.ProcessorCount,Monitor=monitor.Result(elapsed),Game=gameProcess.Result(elapsed),SystemCpuMachinePercent=systemCpu,CollectorCpuMachinePercent=(self.TotalProcessorTime-ownCpu).TotalSeconds/elapsed/Environment.ProcessorCount*100,Samples=samples,ForegroundGameSamples=foregroundGame,ForegroundMonitorSamples=foregroundMonitor,OverlayVisibleSamples=overlayVisible,SettingsVisibleSamples=settingsVisible,ReminderVisibleSamples=reminderVisible,OverlayBefore=overlayBefore,OverlayAfter=WindowInfo(overlay),ReminderBefore=reminderBefore,ReminderAfter=WindowInfo(reminder),GameBefore=gameBefore,GameAfter=WindowInfo(game),ScreenWidth=Screen.PrimaryScreen.Bounds.Width,ScreenHeight=Screen.PrimaryScreen.Bounds.Height,GpuProcessOverhead="Not collected: no validated per-process GPU counter",WriteScope="Process I/O deltas after explicit warmup; excludes this final output write. Not whole-system disk or NAND writes."};
            File.WriteAllText(output,new JavaScriptSerializer().Serialize(result),new UTF8Encoding(false));Console.WriteLine("Complete; one summary written.");
        }return 0;
    }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}}
}
