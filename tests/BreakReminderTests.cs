using System;
using System.Drawing;
using System.IO;
using System.Text;
using QuietMonitor;

static class BreakReminderTests {
    public static void Run(Action<bool,string> check){
        var s=new BreakReminderSchedule();s.Configure(false,30,0);
        check(!s.IsDue(9999999999),"disabled reminder never becomes due");
        s.Configure(true,30,1000);
        check(!s.IsDue(1800999)&&s.IsDue(1801000),"reminder starts a complete interval when enabled");
        check(s.IsDue(9000000)&&s.Pending,"unacknowledged reminder stays pending without accumulating a queue");
        s.Configure(true,30,9000000);
        check(s.Pending,"unrelated settings apply preserves a pending reminder");
        s.Acknowledge(9000000);
        check(!s.IsDue(10799999)&&s.IsDue(10800000),"acknowledgement restarts from click time rather than original deadline");
        s.Configure(true,10,10800000);
        check(!s.Pending&&!s.IsDue(11399999)&&s.IsDue(11400000),"changing the interval starts a new interval");
        s.Configure(false,10,11400000);
        check(!s.Pending&&!s.IsDue(9999999999),"disabling clears both pending and countdown state");
        s.Configure(true,1,5000000000);
        check(!s.IsDue(5000059999)&&s.IsDue(5000060000),"monotonic timing works beyond the 32-bit uptime boundary");
        s.Configure(true,1,5000060100);s.Acknowledge(5000060100);s.Configure(true,1,5000080000);
        check(s.RemainingMilliseconds(5000080000)==40100,"saving the same interval never postpones a countdown");
        check(s.IsDue(6000000000),"large elapsed or resume gap produces one overdue reminder");
        s.Configure(false,1,6000000000);s.Configure(true,1,6000000100);
        check(!s.IsDue(6000060099),"re-enabling starts fresh instead of replaying old pending state");
        check(ReminderPolicy.ShellBlocks(1,false)&&ReminderPolicy.ShellBlocks(1,true),"locked or absent session defers reminders even without fullscreen suppression");
        check(ReminderPolicy.ShellBlocks(3,true)&&ReminderPolicy.ShellBlocks(4,true)&&ReminderPolicy.ShellBlocks(6,true),"shell exclusive, presentation and quiet states defer");
        check(!ReminderPolicy.ShellBlocks(2,true),"generic shell busy hint requires foreground geometry instead of indefinite deferral");
        check(!ReminderPolicy.ShellBlocks(5,true)&&!ReminderPolicy.ShellBlocks(7,true),"ordinary and Store-app shell states still allow geometry checks");
        check(!ReminderPolicy.ShellBlocks(2,false)&&!ReminderPolicy.ShellBlocks(3,false),"optional fullscreen suppression can be disabled");
        check(ReminderPolicy.ShellBlocks(0,false)&&ReminderPolicy.ShellBlocks(99,true),"unknown shell states defer conservatively");
        var screen=new Rectangle(0,0,1920,1080);
        check(ReminderPolicy.CoversMonitor(screen,screen,false,false),"borderless fullscreen geometry defers");
        check(!ReminderPolicy.CoversMonitor(new Rectangle(0,0,1920,1040),screen,false,false),"work-area maximized geometry is not fullscreen");
        check(!ReminderPolicy.CoversMonitor(screen,screen,true,true),"captioned maximized windows are not geometrically classified as fullscreen");
        check(ReminderPolicy.CoversMonitor(new Rectangle(-1920,-120,1920,1080),new Rectangle(-1920,-120,1920,1080),false,false),"fullscreen detection supports negative-coordinate monitors");
        check(!ReminderPolicy.CoversMonitor(Rectangle.Empty,screen,false,false)&&!ReminderPolicy.CoversMonitor(screen,Rectangle.Empty,false,false),"empty bounds are not fabricated as fullscreen");
        var area=new Rectangle(0,0,1920,1040);var size=new Size(330,180);
        check(ReminderPolicy.Place(area,size,Rectangle.Empty,false)==new Point(1590,0),"reminder has zero top and right margin by default");
        var panel=new Rectangle(1690,0,230,600);
        check(ReminderPolicy.Place(area,size,panel,false)==new Point(1590,0),"disabled avoidance keeps the reminder flush even when the hardware panel overlaps");
        check(!new Rectangle(ReminderPolicy.Place(area,size,panel,true),size).IntersectsWith(panel),"optional avoidance moves beside an overlapping hardware panel");
        check(ReminderPolicy.Place(area,size,new Rectangle(1920,0,230,600),true)==new Point(1590,0),"panel on a secondary display never displaces the primary reminder");
        check(ReminderPolicy.Place(new Rectangle(-1920,-200,1920,1080),size,Rectangle.Empty,false)==new Point(-330,-200),"flush reminder placement supports a primary display at negative coordinates");
        check(ReminderPolicy.Place(new Rectangle(0,0,2560,1440),new Size(420,245),Rectangle.Empty,false)==new Point(2140,0),"scaled reminder size retains zero margin");
        Point small=ReminderPolicy.Place(new Rectangle(-800,0,800,600),size,new Rectangle(-800,0,800,250),true);
        check(small.X>=-800&&small.X+size.Width<=0&&small.Y>=250&&small.Y+size.Height<=600,"narrow screen places reminder beneath the panel and remains inside work area");
        var c=new AppConfig();
        check(!c.BreakReminderEnabled&&c.BreakReminderMinutes==30&&c.BreakReminderDeferFullscreen&&!c.BreakReminderAvoidPanel,"reminders remain opt-in with avoidance off and fullscreen deferral on");
        c.BreakReminderMinutes=0;c.Normalize();check(c.BreakReminderMinutes==1,"reminder minimum interval is one minute");
        c.BreakReminderMinutes=999999;c.Normalize();check(c.BreakReminderMinutes==480,"reminder maximum interval is eight hours");
        string path=Path.Combine(Path.GetTempPath(),"QuietMonitor-reminder-tests-"+Guid.NewGuid().ToString("N"));
        try{
            Directory.CreateDirectory(path);var store=new ConfigStore(path);
            File.WriteAllText(store.FilePath,"{\"SchemaVersion\":2,\"FontSize\":9,\"Visible\":{\"CpuTemp\":false}}",new UTF8Encoding(false));
            byte[] before=File.ReadAllBytes(store.FilePath);var migrated=store.Load();
            check(!migrated.BreakReminderEnabled&&migrated.BreakReminderMinutes==30&&migrated.BreakReminderDeferFullscreen&&!migrated.BreakReminderAvoidPanel&&migrated.FontSize==9&&!migrated.Visible["CpuTemp"],"actual old settings load preserves choices and adds flush reminder defaults");
            check(Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(store.FilePath)),"loading reminder defaults does not rewrite old settings");
            migrated.BreakReminderEnabled=true;migrated.BreakReminderMinutes=45;migrated.BreakReminderDeferFullscreen=false;migrated.BreakReminderAvoidPanel=true;store.Save(migrated);var loaded=store.Load();
            check(loaded.BreakReminderEnabled&&loaded.BreakReminderMinutes==45&&!loaded.BreakReminderDeferFullscreen&&loaded.BreakReminderAvoidPanel,"explicit save persists avoidance and reminder options independently of countdown state");
        }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
