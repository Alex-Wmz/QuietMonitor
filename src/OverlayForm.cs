using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuietMonitor {
    public sealed class OverlayForm:Form {
        const int Layered=0x80000,Transparent=0x20,ToolWindow=0x80,NoActivate=0x08000000;
        [StructLayout(LayoutKind.Sequential)]struct PointNative{public int X,Y;public PointNative(int x,int y){X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential)]struct SizeNative{public int X,Y;public SizeNative(int x,int y){X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Operation,Flags,Alpha,Format;}
        [DllImport("user32.dll")]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref PointNative position,ref SizeNative size,IntPtr source,ref PointNative origin,int color,ref Blend blend,int flags);
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr item);
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr window,int index,int value);
        [DllImport("user32.dll")]static extern bool ReleaseCapture();
        [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        AppConfig config;
        string signature;
        bool placementNeeded=true;
        Rectangle lastWorkingArea;
        public OverlayForm(AppConfig initial,ContextMenuStrip menu){
            config=initial;Text="QuietMonitor 悬浮监控";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual;ContextMenuStrip=menu;TopMost=initial.TopMost;
            Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);FormClosed+=delegate{Icon.Dispose();};
            MouseWheel+=delegate(object sender,MouseEventArgs e){if(!config.ClickThrough){extraOffset+=e.Delta<0?1:-1;signature=null;}};
            MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&!config.ClickThrough){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);ActiveLayout.Monitor=Screen.FromPoint(Cursor.Position).DeviceName;Place(Width,Height);CapturePosition();signature=null;}};
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{CreateParams result=base.CreateParams;result.ExStyle|=Layered|ToolWindow|NoActivate;if(config!=null&&config.ClickThrough)result.ExStyle|=Transparent;return result;}}
        public void Configure(AppConfig updated){
            bool modeChanged=config.Mode!=updated.Mode;CapturePosition();config=updated;signature=null;placementNeeded|=modeChanged;
            TopMost=config.TopMost;if(IsHandleCreated){int style=GetWindowLong(Handle,-20);SetWindowLong(Handle,-20,config.ClickThrough?style|Transparent:style&~Transparent);}
        }
        LayoutConfig ActiveLayout{get{return config.Mode=="GameBar"?config.GameBar:config.Desktop;}}
        Screen TargetScreen(){
            Screen remembered=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==ActiveLayout.Monitor);if(remembered!=null)return remembered;
            return ActiveLayout.X==int.MinValue||ActiveLayout.Y==int.MinValue?Screen.PrimaryScreen:Screen.FromRectangle(new Rectangle(ActiveLayout.X,ActiveLayout.Y,Math.Max(1,Width),Math.Max(1,Height)));
        }
        public void CapturePosition(){if(!IsHandleCreated||placementNeeded)return;ActiveLayout.X=Left;ActiveLayout.Y=Top;ActiveLayout.Monitor=Screen.FromRectangle(Bounds).DeviceName;}
        public void ResetPosition(){ActiveLayout.X=int.MinValue;ActiveLayout.Y=int.MinValue;ActiveLayout.Monitor="";placementNeeded=true;signature=null;}
        int extraOffset;
        public void Render(Snapshot snapshot){
            if(!Visible)return;
            OverlayContent content=OverlayPainter.Build(snapshot,config);string next=content.Signature+"/"+extraOffset;
            Rectangle area=TargetScreen().WorkingArea;if(area!=lastWorkingArea){placementNeeded=true;lastWorkingArea=area;}
            if(next==signature&&!placementNeeded)return;signature=next;
            float scale=DeviceDpiScale();
            using(Bitmap bitmap=OverlayPainter.Paint(content,config,scale,Math.Max(1,area.Width),Math.Max(1,area.Height),extraOffset)){
                // The initial warmup bar is narrower. Keep its center when live readings resize it.
                if(config.Mode=="GameBar"&&!config.AutoDock&&!placementNeeded)Left=OverlayPainter.Recenter(Left,Width,bitmap.Width);
                Size=bitmap.Size;Place(bitmap.Width,bitmap.Height);Present(bitmap);
            }
        }
        float DeviceDpiScale(){using(Graphics g=CreateGraphics())return g.DpiX/96f;}
        void Place(int width,int height){
            Screen target=TargetScreen();bool defaultPosition=ActiveLayout.X==int.MinValue||ActiveLayout.Y==int.MinValue;
            Point requested=placementNeeded&&!defaultPosition?new Point(ActiveLayout.X,ActiveLayout.Y):Location;
            Location=OverlayPainter.PlaceWithin(target.WorkingArea,new Size(width,height),requested,config.Mode,config.AutoDock||placementNeeded&&defaultPosition,0);
            ActiveLayout.Monitor=target.DeviceName;placementNeeded=false;
        }
        void Present(Bitmap bitmap){
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),handle=IntPtr.Zero,old=IntPtr.Zero;
            try{handle=bitmap.GetHbitmap(Color.FromArgb(0));old=SelectObject(memory,handle);PointNative position=new PointNative(Left,Top),origin=new PointNative(0,0);SizeNative size=new SizeNative(bitmap.Width,bitmap.Height);Blend blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,screen,ref position,ref size,memory,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception();}
            finally{if(old!=IntPtr.Zero)SelectObject(memory,old);if(handle!=IntPtr.Zero)DeleteObject(handle);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
        }
    }
}
