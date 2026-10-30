using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuietMonitor {
    public struct ReminderEnvironment {
        public bool Blocked;
        public Rectangle WorkArea;
        public string Reason;
    }
    public static class ReminderEnvironmentProbe {
        [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;public Rectangle Rectangle{get{return Rectangle.FromLTRB(Left,Top,Right,Bottom);}}}
        [StructLayout(LayoutKind.Sequential)]struct MonitorInfo {public int Size;public NativeRect Bounds,Work;public uint Flags;}
        [DllImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern ulong GetTickCount64();
        [DllImport("shell32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern bool IsZoomed(IntPtr window);
        public static long Now {get{return (long)GetTickCount64();}}
        public static ReminderEnvironment Read(bool deferFullscreen){
            var result=new ReminderEnvironment{Blocked=true,Reason="暂缓：当前窗口状态不可用"};
            try{
                int shell;
                // Read-only shell hint plus foreground geometry. No process names, titles or game access.
                if(SHQueryUserNotificationState(out shell)!=0)return result;
                if(ReminderPolicy.ShellBlocks(shell,deferFullscreen)){result.Reason="暂缓：全屏、锁屏或系统免打扰状态";return result;}
                IntPtr foreground=GetForegroundWindow();if(foreground==IntPtr.Zero)return result;
                var monitor=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
                if(!GetMonitorInfo(MonitorFromWindow(foreground,2),ref monitor))return result;
                result.WorkArea=monitor.Work.Rectangle;
                if(deferFullscreen&&foreground!=GetShellWindow()&&foreground!=GetDesktopWindow()){
                    NativeRect rect;if(!GetWindowRect(foreground,out rect))return result;
                    bool caption=(GetWindowLong(foreground,-16)&0x00c00000)!=0;
                    if(ReminderPolicy.CoversMonitor(rect.Rectangle,monitor.Bounds.Rectangle,caption,IsZoomed(foreground))){result.Reason="暂缓：前台窗口全屏";return result;}
                }
                result.Blocked=false;result.Reason="等待点击确认";return result;
            }catch{return result;}
        }
    }
    public sealed class BreakReminderWindow:Form {
        [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;}
        [DllImport("dwmapi.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out NativeRect bounds,int size);
        readonly Action acknowledge;
        readonly Font ownedFont,headingFont;
        readonly Icon ownedIcon;
        public BreakReminderWindow(Action acknowledge){
            this.acknowledge=acknowledge;
            Text="QuietMonitor 久坐提醒";FormBorderStyle=FormBorderStyle.FixedToolWindow;
            ShowInTaskbar=false;TopMost=true;MaximizeBox=false;MinimizeBox=false;
            StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.Dpi;
            BackColor=Color.FromArgb(17,21,28);ForeColor=Color.FromArgb(240,243,246);
            ownedIcon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);Icon=ownedIcon;
            ownedFont=new Font("Microsoft YaHei UI",9.5f);Font=ownedFont;headingFont=new Font(Font.FontFamily,12,FontStyle.Bold);
            ClientSize=new Size(326,154);
            Controls.Add(new Label{Text="该起身活动一下了",AutoSize=true,Location=new Point(15,13),Font=headingFont,ForeColor=Color.FromArgb(88,214,176)});
            Controls.Add(new Label{Text="休息一下、活动身体。\n点击下方按钮后，开始下一轮倒计时。",AutoSize=false,Location=new Point(15,47),Size=new Size(298,49)});
            var button=new Button{Text="知道了，重新计时",Location=new Point(15,105),Size=new Size(296,34),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(88,214,176),ForeColor=Color.FromArgb(17,21,28)};
            button.Click+=delegate{acknowledge();};Controls.Add(button);AcceptButton=button;
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        public void DockTo(Rectangle area,Rectangle panel,bool avoidPanel){
            Rectangle visible=Bounds;
            try{NativeRect frame;if(IsHandleCreated&&DwmGetWindowAttribute(Handle,9,out frame,Marshal.SizeOf(typeof(NativeRect)))==0&&frame.Right>frame.Left&&frame.Bottom>frame.Top)visible=Rectangle.FromLTRB(frame.Left,frame.Top,frame.Right,frame.Bottom);}catch{}
            Point target=ReminderPolicy.Place(area,visible.Size,panel,avoidPanel);
            Point next=new Point(Left+target.X-visible.Left,Top+target.Y-visible.Top);
            if(Location!=next)Location=next;
        }
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
        protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;acknowledge();}base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){ownedFont.Dispose();headingFont.Dispose();ownedIcon.Dispose();}}
    }
    // Uses MonitorContext's existing 1 Hz tick; creates the reminder window only when needed.
    public sealed class BreakReminderRuntime:IDisposable {
        readonly BreakReminderSchedule schedule=new BreakReminderSchedule();
        readonly Func<long> now;
        readonly Func<bool,ReminderEnvironment> environment;
        readonly Func<Rectangle> panelBounds;
        readonly Func<Rectangle> primaryBounds;
        BreakReminderWindow window;bool deferFullscreen,avoidPanel;string pendingReason;
        public BreakReminderRuntime(Func<Rectangle> panel):this(panel,delegate{return ReminderEnvironmentProbe.Now;},ReminderEnvironmentProbe.Read){}
        public BreakReminderRuntime(Func<Rectangle> panel,Func<long> now,Func<bool,ReminderEnvironment> environment):this(panel,now,environment,delegate{return Screen.PrimaryScreen.Bounds;}){}
        public BreakReminderRuntime(Func<Rectangle> panel,Func<long> now,Func<bool,ReminderEnvironment> environment,Func<Rectangle> primaryBounds){panelBounds=panel;this.now=now;this.environment=environment;this.primaryBounds=primaryBounds;}
        public void Configure(AppConfig config){
            schedule.Configure(config.BreakReminderEnabled,config.BreakReminderMinutes,config.BreakReminderEnabled?now():0);deferFullscreen=config.BreakReminderDeferFullscreen;avoidPanel=config.BreakReminderAvoidPanel;
            if(!schedule.Enabled&&window!=null){window.Dispose();window=null;}Tick();
        }
        public void Tick(){
            if(!schedule.Enabled)return;
            if(!schedule.IsDue(now())){if(window!=null)window.Hide();return;}
            ReminderEnvironment current=environment(deferFullscreen);pendingReason=current.Reason;
            if(current.Blocked){if(window!=null)window.Hide();return;}
            if(window==null)window=new BreakReminderWindow(Acknowledge);
            Rectangle area=primaryBounds();
            if(!window.Visible){window.Location=ReminderPolicy.Place(area,window.Size,panelBounds(),avoidPanel);window.Show();}
            window.DockTo(area,panelBounds(),avoidPanel);
        }
        public void Acknowledge(){schedule.Acknowledge(now());pendingReason=null;if(window!=null)window.Hide();}
        public string Status {
            get{
                if(!schedule.Enabled)return "当前已关闭。";
                if(schedule.Pending)return "已到期 · "+(pendingReason??"等待显示");
                long seconds=(schedule.RemainingMilliseconds(now())+999)/1000;
                return String.Format("当前倒计时：{0:00}:{1:00} · 每 {2} 分钟提醒",seconds/60,seconds%60,schedule.Minutes);
            }
        }
        public void Dispose(){if(window!=null){window.Dispose();window=null;}}
    }
}
