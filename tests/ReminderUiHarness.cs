// Optional developer UI fixture, never included in a release package.
// Compile against BreakReminder.cs, BreakReminderWindow.cs, Settings.cs and Model.cs.
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using QuietMonitor;
static class ReminderUiHarness {
    sealed class Harness:Form {
        [DllImport("shell32.dll")]static extern int SHQueryUserNotificationState(out int state);
        readonly BreakReminderRuntime reminder;
        readonly Timer timer=new Timer{Interval=1000};
        readonly Label state;
        readonly AppConfig config=new AppConfig{BreakReminderEnabled=true,BreakReminderMinutes=1};
        long now;bool fullscreen;
        public Harness(){
            Text="QuietMonitor Reminder QA";ClientSize=new Size(650,230);StartPosition=FormStartPosition.CenterScreen;KeyPreview=true;
            Controls.Add(new Label{AutoSize=true,Location=new Point(15,15),Text="F6: advance one interval   F7: toggle fixture fullscreen\nF8: disable / enable   F9: acknowledge   Esc: exit fullscreen or close"});
            state=new Label{Location=new Point(15,80),Size=new Size(620,100)};Controls.Add(state);
            reminder=new BreakReminderRuntime(delegate{return Rectangle.Empty;},delegate{return now;},ReminderEnvironmentProbe.Read);
            reminder.Configure(config);timer.Tick+=delegate{reminder.Tick();UpdateStatus();};timer.Start();
            KeyDown+=delegate(object sender,KeyEventArgs e){
                if(e.KeyCode==Keys.F6)now+=60000;
                else if(e.KeyCode==Keys.F7)ToggleFullscreen();
                else if(e.KeyCode==Keys.F8){config.BreakReminderEnabled=!config.BreakReminderEnabled;reminder.Configure(config);}
                else if(e.KeyCode==Keys.F9)reminder.Acknowledge();
                else if(e.KeyCode==Keys.Escape){if(fullscreen)ToggleFullscreen();else Close();}
                reminder.Tick();UpdateStatus();
            };
        }
        void UpdateStatus(){int shell;int hr=SHQueryUserNotificationState(out shell);state.Text=reminder.Status+"\nFixture fullscreen: "+fullscreen+"; Shell state: "+shell+"; HRESULT: "+hr+"\nVisible reminder windows: "+Application.OpenForms.Cast<Form>().Count(f=>f is BreakReminderWindow&&f.Visible)+"; Fixture has focus: "+ContainsFocus;}
        void ToggleFullscreen(){fullscreen=!fullscreen;WindowState=FormWindowState.Normal;FormBorderStyle=fullscreen?FormBorderStyle.None:FormBorderStyle.Sizable;if(fullscreen)Bounds=Screen.FromControl(this).Bounds;else{Size=new Size(666,270);CenterToScreen();}}
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();reminder.Dispose();}base.Dispose(disposing);}
    }
    [STAThread]static void Main(){Application.EnableVisualStyles();using(var form=new Harness())Application.Run(form);}
}
