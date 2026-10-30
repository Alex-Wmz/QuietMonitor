using System;
using System.Drawing;

namespace QuietMonitor {
    // The caller supplies monotonic milliseconds. No settings writes or reminder history.
    public sealed class BreakReminderSchedule {
        public bool Enabled { get; private set; }
        public bool Pending { get; private set; }
        public int Minutes { get; private set; }
        long deadline;
        public void Configure(bool enabled,int minutes,long now){
            minutes=Math.Max(1,Math.Min(480,minutes));
            bool restart=enabled&&(!Enabled||minutes!=Minutes);
            Enabled=enabled;Minutes=minutes;
            if(!enabled){Pending=false;deadline=0;}
            else if(restart)Acknowledge(now);
        }
        public bool IsDue(long now){if(Enabled&&now>=deadline)Pending=true;return Enabled&&Pending;}
        public void Acknowledge(long now){Pending=false;deadline=Enabled?now+(long)Minutes*60000:0;}
        public long RemainingMilliseconds(long now){return !Enabled||Pending?0:Math.Max(0,deadline-now);}
    }
    public static class ReminderPolicy {
        // A generic "busy" hint can persist for a windowed foreground app; validate geometry.
        // Defer explicit absence, exclusive D3D, presentation and first-login quiet states.
        public static bool ShellBlocks(int state,bool deferFullscreen){
            return state<1||state>7||state==1||deferFullscreen&&(state==3||state==4||state==6);
        }
        public static bool CoversMonitor(Rectangle window,Rectangle monitor,bool caption,bool maximized){
            if(window.Width<=0||window.Height<=0||monitor.Width<=0||monitor.Height<=0||caption&&maximized)return false;
            return window.Left<=monitor.Left+2&&window.Top<=monitor.Top+2&&window.Right>=monitor.Right-2&&window.Bottom>=monitor.Bottom-2;
        }
        public static Point Place(Rectangle area,Size size,Rectangle monitorPanel,bool avoidPanel){
            const int gap=12;
            int right=Math.Max(area.Left,area.Right-size.Width),top=area.Top;
            var target=new Rectangle(right,top,size.Width,size.Height);
            if(avoidPanel&&!monitorPanel.IsEmpty&&target.IntersectsWith(monitorPanel)){
                int beside=monitorPanel.Left-size.Width-gap;
                if(beside>=area.Left)target.X=beside;else target.Y=monitorPanel.Bottom+gap;
            }
            return new Point(Math.Max(area.Left,Math.Min(target.X,area.Right-size.Width)),Math.Max(area.Top,Math.Min(target.Y,area.Bottom-size.Height)));
        }
    }
}
