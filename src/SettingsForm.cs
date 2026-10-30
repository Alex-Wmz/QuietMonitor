using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuietMonitor {
    public sealed class SettingsForm:Form {
        sealed class Choice{public string Id,Text;public override string ToString(){return Text;}}
        readonly Sampler sampler;readonly ConfigStore store;
        readonly Func<string> reminderState;
        AppConfig draft;readonly Action<AppConfig,bool> apply;readonly Action elevate;
        readonly Dictionary<string,ComboBox> sources=new Dictionary<string,ComboBox>();
        readonly Dictionary<string,CheckBox> visibleFields=new Dictionary<string,CheckBox>();
        readonly Dictionary<string,TextBox> colors=new Dictionary<string,TextBox>();
        NumericUpDown fontSize,opacity,width,interval,slowInterval,reminderMinutes;
        CheckBox reminderEnabled,reminderDefer,reminderAvoidPanel;Label reminderStatus;
        CheckBox topMost,clickThrough,memory,network,units,bold,hideFans,roundNumbers,taskManagerVram,autoTemperatures,enableMemory,enableStorage,enableController,enablePsu,enableBattery,autoDock;
        ComboBox mode,nic,fontFamily,storageOrder;
        CheckedListBox extras;
        TextBox extraLabel;
        string extrasSignature;
        Label status,result,extraStatus;
        DataGridView inventory;
        TabControl tabs;
        readonly Timer timer=new Timer{Interval=1000};
        public SettingsForm(Sampler sampler,AppConfig current,ConfigStore store,Action<AppConfig,bool> apply,Action elevate,Func<string> reminderState){
            this.sampler=sampler;this.store=store;this.apply=apply;this.elevate=elevate;this.reminderState=reminderState;draft=current.Clone();
            Text="QuietMonitor 设置";Font=new Font("Microsoft YaHei UI",9.5f);AutoScaleMode=AutoScaleMode.Dpi;KeyPreview=true;
            Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.S){Commit(true);e.Handled=true;e.SuppressKeyPress=true;}else if(e.Control&&e.KeyCode==Keys.Enter){Commit(false);e.Handled=true;e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.F5){RememberSources();RememberExtras();RefreshSources();RefreshExtras();e.Handled=true;}else if(e.KeyCode==Keys.Escape){Close();e.Handled=true;}};
            StartPosition=FormStartPosition.CenterScreen;MinimumSize=new Size(760,620);ClientSize=new Size(870,670);
            tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);
            var appearance=new TabPage("外观与运行");var bindings=new TabPage("传感器来源");var devices=new TabPage("设备状态 / 适配");var more=new TabPage("更多参数");var breaks=new TabPage("久坐提醒");tabs.TabPages.AddRange(new[]{appearance,bindings,devices,more,breaks});
            BuildAppearance(appearance);BuildSources(bindings);BuildDevices(devices);BuildExtras(more);BuildReminders(breaks);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
            Controls.Add(bottom);tabs.BringToFront();
            Button close=Button("关闭",delegate{Close();});close.DialogResult=DialogResult.Cancel;CancelButton=close;
            bottom.Controls.Add(close);bottom.Controls.Add(Button("保存并应用",delegate{Commit(true);}));bottom.Controls.Add(Button("应用（仅本次）",delegate{Commit(false);}));
            result=new Label{AutoSize=false,Width=350,Height=40,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(50,95,80)};bottom.Controls.Add(result);
            timer.Tick+=delegate{if(tabs.SelectedIndex==2)UpdateInventory();if(tabs.SelectedIndex==3){UpdateExtraStatus();if(extrasSignature!=String.Join("|",sampler.Latest.Readings.Select(r=>r.Id))){RememberExtras();RefreshExtras();}}if(tabs.SelectedIndex==4)reminderStatus.Text=reminderState();};timer.Start();
            FormClosed+=delegate{timer.Stop();timer.Dispose();Icon.Dispose();};RefreshSources();RefreshExtras();UpdateInventory();
            if(store.LoadWarning!=null)result.Text=store.LoadWarning;
        }
        static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=32,MinimumSize=new Size(90,32)};b.Click+=delegate{action();};return b;}
        static TableLayoutPanel Table(Control parent,int columns){var t=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=columns,Padding=new Padding(14)};parent.Controls.Add(t);return t;}
        static void Row(TableLayoutPanel table,string label,Control control){int row=table.RowCount++;table.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(3,9,12,8)},0,row);control.Margin=new Padding(3,5,3,5);table.Controls.Add(control,1,row);}
        NumericUpDown Number(int value,int min,int max){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,Width=110};}
        static CheckBox Check(string text,bool value){return new CheckBox{Text=text,Checked=value,AutoSize=true};}
        void BuildReminders(TabPage page){
            page.AutoScroll=true;var t=Table(page,2);
            reminderEnabled=Check("开启久坐提醒",draft.BreakReminderEnabled);Row(t,"开关",reminderEnabled);
            reminderMinutes=Number(draft.BreakReminderMinutes,1,480);Row(t,"间隔（分钟）",reminderMinutes);
            reminderDefer=Check("全屏游戏 / 视频时暂缓，退出全屏后补提醒",draft.BreakReminderDeferFullscreen);Row(t,"全屏时",reminderDefer);
            reminderAvoidPanel=Check("避让硬件监控面板（会离开右上角）",draft.BreakReminderAvoidPanel);Row(t,"位置避让",reminderAvoidPanel);
            reminderStatus=new Label{AutoSize=true,MaximumSize=new Size(570,0),Text=reminderState()};Row(t,"当前已应用状态",reminderStatus);
            Row(t,"提醒方式",new Label{AutoSize=true,MaximumSize=new Size(570,0),Text="默认紧贴主显示器右上角，外边距为零；接入多屏后仍在主屏，不跟随前台窗口。可勾选避让硬件面板，重叠时才向左或向下让开。\n不抢键盘焦点，也不会被硬件面板的鼠标穿透锁住。不点击就保留。点“知道了，重新计时”或小窗的关闭按钮，从点击时开始下一轮。进入全屏时暂时隐藏，退出后继续保留同一条提醒。"});
            Row(t,"计时与保存",new Label{AutoSize=true,MaximumSize=new Size(570,0),Text="默认关闭，间隔默认 30 分钟。开启或修改间隔后重新计时；其它外观设置的应用不会重置倒计时。全屏时间继续累计，只补一条，不累积弹窗。\n退出软件后倒计时不保留，重启从头开始；睡眠时间计入间隔，恢复后若已到期会补提醒。这里只按时间提醒，不记录坐姿、输入活动或历史。\n修改后点“应用（仅本次）”或“保存并应用”；只有手动保存才写配置。"});
        }
        void BuildAppearance(TabPage page){
            page.AutoScroll=true;var t=Table(page,2);
            mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=240};mode.Items.AddRange(new object[]{"桌面纵向面板","游戏顶部横条（手动切换）"});mode.SelectedIndex=draft.Mode=="GameBar"?1:0;Row(t,"显示布局",mode);
            autoDock=Check("自动贴边吸顶（桌面靠右 / 游戏居中，无外边距）",draft.AutoDock);Row(t,"窗口位置",autoDock);
            fontSize=Number(draft.FontSize,9,22);Row(t,"字号",fontSize);width=Number(draft.Width,210,480);Row(t,"桌面面板宽度",width);
            fontFamily=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=240};foreach(string family in new[]{"Microsoft YaHei UI","Microsoft YaHei","Segoe UI","Arial",draft.FontFamily}.Distinct())fontFamily.Items.Add(family);fontFamily.SelectedItem=draft.FontFamily;Row(t,"面板字体",fontFamily);
            bold=Check("加粗（小字号更清晰）",draft.BoldText);Row(t,"字重",bold);
            opacity=Number(draft.BackgroundOpacity,15,100);Row(t,"背景不透明度 %",opacity);interval=Number(draft.IntervalMs/1000,1,10);Row(t,"采样间隔（秒）",interval);
            ColorRow(t,"文字颜色","Foreground",draft.Foreground);ColorRow(t,"背景颜色","Background",draft.Background);ColorRow(t,"强调颜色","Accent",draft.Accent);
            var checks=new FlowLayoutPanel{AutoSize=true,Width=570,WrapContents=true};
            topMost=Check("窗口置顶",draft.TopMost);clickThrough=Check("鼠标穿透 / 锁定",draft.ClickThrough);memory=Check("显示内存",draft.ShowMemory);network=Check("显示网络",draft.ShowNetwork);units=Check("显示单位",draft.Units);hideFans=Check("隐藏缺少来源的风扇",draft.HideMissingFans);checks.Controls.AddRange(new Control[]{topMost,clickThrough,memory,network,units,hideFans});Row(t,"显示选项",checks);
            roundNumbers=Check("温度、功耗、内存、显存、网速四舍五入为整数",draft.RoundNumbers);Row(t,"数值显示",roundNumbers);
            taskManagerVram=Check("按任务管理器样式估算总量（含推算保留）",draft.TaskManagerVramCapacity);Row(t,"显存容量口径",taskManagerVram);
            Row(t,"显存说明",new Label{AutoSize=true,MaximumSize=new Size(570,0),Text="默认显示 Windows 接口返回的可管理容量；勾选后总量按显示规则向上规整。差额是推算的保留部分，不能当成额外测得的物理容量。清单一直保留原始值。"});
            Row(t,"容量 / 网速单位",new Label{AutoSize=true,MaximumSize=new Size(570,0),Text="使用 KB / MB / GB，内存等二进制读数仍按 1024 换算。整数选项只改变显示，不降低采样精度。"});
            var note=new Label{AutoSize=true,MaximumSize=new Size(740,0),Text="解锁后可拖到另一块屏幕，松手即在那块屏幕吸顶。关闭自动吸顶可自由摆放，仍限制在屏幕内。\nCtrl+Alt+M 显示/隐藏 · Ctrl+Alt+B 切换布局 · Ctrl+Alt+L 锁定 · Ctrl+Alt+S 设置\n设置与位置仅在手动保存时写入。游戏横条使用紧凑行距，不显示硬盘温度。无注入；独占全屏需逐游戏验证。",Margin=new Padding(3,14,3,10)};t.Controls.Add(note,0,t.RowCount);t.SetColumnSpan(note,2);t.RowCount++;
        }
        void ColorRow(TableLayoutPanel t,string label,string key,string value){var flow=new FlowLayoutPanel{AutoSize=true};var input=new TextBox{Text=value,Width=100,MaxLength=7};colors[key]=input;flow.Controls.Add(input);flow.Controls.Add(Button("选择…",delegate{using(var d=new ColorDialog{Color=SafeColor(input.Text),FullOpen=true})if(d.ShowDialog(this)==DialogResult.OK)input.Text="#"+d.Color.R.ToString("X2")+d.Color.G.ToString("X2")+d.Color.B.ToString("X2");}));Row(t,label,flow);}
        static Color SafeColor(string text){try{return ColorTranslator.FromHtml(text);}catch{return Color.White;}}
        void BuildSources(TabPage page){
            page.AutoScroll=true;var t=Table(page,2);t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(string key in Fields.Keys){var check=Check(Fields.Labels[key],draft.Visible[key]);visibleFields[key]=check;var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=630,DropDownWidth=1000};sources[key]=combo;int row=t.RowCount++;t.Controls.Add(check,0,row);t.Controls.Add(combo,1,row);combo.Margin=new Padding(3,5,3,5);check.Anchor=AnchorStyles.Left;}
            nic=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=630,DropDownWidth=700};Row(t,"网络接口",nic);
            Row(t,"刷新可选项",Button("重新枚举来源",delegate{RememberSources();RefreshSources();result.Text="已刷新当前采样中的来源。";}));
            var note=new Label{AutoSize=true,MaximumSize=new Size(780,0),Text="CPU 占用优先使用 Windows 利用率（任务管理器口径）；回退旧来源时标为“占用（时间）”。两种口径不保证相等。\n核心平均频率与有效频率分开；后端没有有效频率来源时只能显示 —，调整设置不能产生读数。\nGPU 核心温度、热点温度、显存温度分别读取独立来源；没有热点支持时显示 —，不会复制核心或显存温度。显存时钟 MHz 不等于显存有效数据率。\nAMD GFX、SoC、ASIC 功耗域不同；温度不会用 SoC / VR / 热点冒充核心或显存温度。风扇有效 0 RPM 仍显示。",Margin=new Padding(3,12,3,8)};t.Controls.Add(note,0,t.RowCount);t.SetColumnSpan(note,2);t.RowCount++;
        }
        void BuildExtras(TabPage page){
            var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=215,Padding=new Padding(10),AutoScroll=true};page.Controls.Add(top);
            top.Controls.Add(new Label{AutoSize=false,Width=800,Height=36,Text="勾选硬件类别后点应用，温度会在读到后自动显示。游戏横条不显示硬盘温度；这不等于关闭硬盘采集。硬件不提供功耗时不会推算。"});
            enableMemory=Check("内存传感器",draft.EnableMemory);enableStorage=Check("硬盘传感器",draft.EnableStorage);enableController=Check("USB 控制器 / 散热器",draft.EnableController);enablePsu=Check("支持的电源",draft.EnablePsu);enableBattery=Check("电池",draft.EnableBattery);
            top.Controls.AddRange(new Control[]{enableMemory,enableStorage,enableController,enablePsu,enableBattery});
            autoTemperatures=Check("自动显示已启用的内存 / 硬盘温度",draft.AutoShowTemperatures);top.Controls.Add(autoTemperatures);top.SetFlowBreak(autoTemperatures,true);
            top.Controls.Add(new Label{Text="额外类别采样（秒）",AutoSize=true,Margin=new Padding(3,9,3,0)});slowInterval=Number(draft.SlowIntervalSeconds,5,60);top.Controls.Add(slowInterval);
            top.Controls.Add(Button("刷新清单",delegate{RememberExtras();RefreshExtras();}));
            top.Controls.Add(new Label{Text="硬盘排序",AutoSize=true,Margin=new Padding(12,9,3,0)});storageOrder=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=240};storageOrder.Items.AddRange(new object[]{"盘符（C、D、E…；无盘符最后）","物理磁盘编号（0、1、2…）"});storageOrder.SelectedIndex=draft.StorageOrder=="Physical"?1:0;top.Controls.Add(storageOrder);
            extraStatus=new Label{AutoSize=false,Width=800,Height=64};top.Controls.Add(extraStatus);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=68,Padding=new Padding(8)};page.Controls.Add(bottom);
            bottom.Controls.Add(new Label{Text="所选项目显示名称",AutoSize=true,Margin=new Padding(3,8,3,0)});extraLabel=new TextBox{Width=300,MaxLength=70};bottom.Controls.Add(extraLabel);bottom.Controls.Add(Button("使用此名称",delegate{Choice choice=extras.SelectedItem as Choice;if(choice!=null){draft.ExtraLabels[choice.Id]=extraLabel.Text.Trim();result.Text="名称已暂存；点应用或保存生效。";}}));
            bottom.Controls.Add(new Label{AutoSize=true,Text="配置速率来自 SMBIOS，属于配置值；温度等需传感器支持。选项较多时可解锁悬浮窗后用滚轮翻页。"});
            extras=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true,IntegralHeight=false};page.Controls.Add(extras);extras.BringToFront();
            extras.SelectedIndexChanged+=delegate{Choice choice=extras.SelectedItem as Choice;if(choice==null)return;string label;if(!draft.ExtraLabels.TryGetValue(choice.Id,out label)){Reading r=sampler.Latest.Readings.FirstOrDefault(x=>x.Id==choice.Id);label=r==null?choice.Id:OverlayPainter.DefaultExtraLabel(r);}extraLabel.Text=label;};
        }
        void RememberExtras(){if(extras==null)return;draft.ExtraSensorIds=extras.CheckedItems.Cast<Choice>().Select(c=>c.Id).ToList();}
        void UpdateExtraStatus(){
            Snapshot s=sampler.Latest;var lines=new List<string>();
            foreach(string kind in new[]{"Memory","Storage"}){
                bool enabled=kind=="Memory"?draft.EnableMemory:draft.EnableStorage;
                string title=kind=="Memory"?"内存":"硬盘";
                if(!enabled){lines.Add(title+"采集未应用开启");continue;}
                if(s.WarmingUp){lines.Add(title+"：正在枚举 / 预热…");continue;}
                int temperatures=s.Readings.Count(r=>DisplayNumbers.LiveTemperature(r,kind)&&Selection.Usable(r,s,draft.IntervalMs));
                int powers=s.Readings.Count(r=>r.HardwareKind==kind&&r.Type=="Power"&&Selection.Usable(r,s,draft.IntervalMs));
                lines.Add(title+"：温度 "+temperatures+" 项；功耗 "+(powers>0?powers+" 项":"无可读来源"));
            }
            lines.Add(!s.IsAdmin?"当前为普通权限：DIMM 温度可能需在“设备状态 / 适配”中以管理员身份重启。":!s.PawnIoInstalled?"未检测到 PawnIO，部分底层温度不可用。":"温度以传感器支持为准；自动项不会在面板重复显示。可关闭自动显示，改为逐项勾选。");
            extraStatus.Text=String.Join("\n",lines);
        }
        void RefreshExtras(){Snapshot s=sampler.Latest;extrasSignature=String.Join("|",s.Readings.Select(r=>r.Id));extras.BeginUpdate();extras.Items.Clear();
            foreach(Reading r in s.Readings.OrderBy(r=>r.HardwareKind).ThenBy(r=>r.Hardware).ThenBy(r=>r.Type).ThenBy(r=>r.Name))extras.Items.Add(new Choice{Id=r.Id,Text=r.Hardware+" / "+r.Name+" ["+r.Unit+" · "+r.Source+" · "+StatusText(r.Status)+(String.IsNullOrEmpty(r.Scope)?"":" · "+r.Scope)+"] · "+r.Id},draft.ExtraSensorIds.Contains(r.Id));
            foreach(string id in draft.ExtraSensorIds.Where(id=>!s.Readings.Any(r=>r.Id==id)))extras.Items.Add(new Choice{Id=id,Text="来源暂缺（保留选择）："+id},true);extras.EndUpdate();
        }
        void RememberSources(){foreach(string key in Fields.Keys){Choice c=sources[key].SelectedItem as Choice;if(c!=null)draft.Bindings[key]=c.Id;}Choice n=nic.SelectedItem as Choice;if(n!=null)draft.NetworkId=n.Id;}
        void RefreshSources(){Snapshot s=sampler.Latest;
            foreach(string key in Fields.Keys){ComboBox c=sources[key];c.BeginUpdate();c.Items.Clear();c.Items.Add(new Choice{Id="",Text="自动选择（保守匹配核心 / 封装传感器）"});
                foreach(Reading r in s.Readings.Where(r=>r.Type==Fields.TypeFor(key)&&(key!="CpuEffectiveClock"||r.HardwareKind=="Cpu"&&(r.Scope=="effective-clock"||r.Name.IndexOf("Effective",StringComparison.OrdinalIgnoreCase)>=0))).OrderBy(r=>r.Hardware).ThenBy(r=>r.Name).ThenBy(r=>r.Source))c.Items.Add(new Choice{Id=r.Id,Text=r.ToString()+" · "+r.Id+(String.IsNullOrEmpty(r.Scope)?"":" · "+r.Scope)});
                string binding=draft.Bindings[key];Choice selected=c.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==binding);if(selected==null){selected=new Choice{Id=binding,Text="来源暂缺（保留绑定）："+binding};c.Items.Add(selected);}c.SelectedItem=selected;c.EndUpdate();
            }
            nic.Items.Clear();nic.Items.Add(new Choice{Id="",Text="自动选择一个活动接口（不累加多个接口）"});foreach(AdapterChoice n in s.Adapters)nic.Items.Add(new Choice{Id=n.Id,Text=n.Name});
            Choice chosen=nic.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==draft.NetworkId);if(chosen==null){chosen=new Choice{Id=draft.NetworkId,Text="接口暂缺（保留绑定）"};nic.Items.Add(chosen);}nic.SelectedItem=chosen;
        }
        void BuildDevices(Control page){
            var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=138,Padding=new Padding(10),AutoScroll=true};page.Controls.Add(top);
            status=new Label{Width=800,Height=65,AutoEllipsis=true};top.Controls.Add(status);
            top.Controls.Add(Button("以管理员身份重启",delegate{elevate();}));
            top.Controls.Add(Button("PawnIO 官方网站",delegate{Process.Start(new ProcessStartInfo("https://pawnio.eu/"){UseShellExecute=true});}));
            top.Controls.Add(Button("导出适配清单…",Export));
            inventory=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
            foreach(string title in new[]{"硬件","传感器","读数","状态","来源","功耗域","ID"})inventory.Columns.Add(title,title);
            page.Controls.Add(inventory);inventory.BringToFront();
        }
        static string StatusText(string value){switch(value){case "ok":return "有读数";case "warming-up":return "预热中";case "stale":return "已过期";case "inconsistent":return "与系统容量不一致";case "unsupported":return "不支持";case "needs-admin":return "需管理员";case "needs-driver":return "需驱动";case "invalid":return "无效值";case "read-failed":return "读取失败";default:return "不可用";}}
        void UpdateInventory(){Snapshot s=sampler.Latest;status.Text="CPU: "+s.CpuName+"\nGPU: "+s.GpuName+"\n权限："+(s.IsAdmin?"管理员":"普通用户")+" · PawnIO："+(s.PawnIoInstalled?"已安装":"未检测到")+" · "+s.Message;
            bool rebuild=inventory.Rows.Count!=s.Readings.Count;
            if(!rebuild)for(int i=0;i<s.Readings.Count;i++)if(!Object.Equals(inventory.Rows[i].Cells[6].Value,s.Readings[i].Id)){rebuild=true;break;}
            if(rebuild){inventory.Rows.Clear();foreach(Reading r in s.Readings)inventory.Rows.Add(r.Hardware,r.Name,"—",StatusText(r.Status),r.Source,r.Scope,r.Id);}
            for(int i=0;i<s.Readings.Count;i++){Reading r=s.Readings[i];string value=Selection.Usable(r,s,draft.IntervalMs)?r.Value.Value.ToString("0.##")+" "+r.Unit:"—";if(!Object.Equals(inventory.Rows[i].Cells[2].Value,value))inventory.Rows[i].Cells[2].Value=value;string state=StatusText(Selection.State(r,s,draft.IntervalMs));if(!Object.Equals(inventory.Rows[i].Cells[3].Value,state))inventory.Rows[i].Cells[3].Value=state;}
        }
        void Export(){using(var dialog=new SaveFileDialog{Title="导出传感器适配清单（包含硬件型号，请自行保管）",FileName="quietmonitor-inventory.json",Filter="JSON 文件|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK){
                Snapshot s=sampler.Latest;var export=new {SchemaVersion=3,Application="QuietMonitor "+Application.ProductVersion,Cpu=s.CpuName,Gpu=s.GpuName,IsAdmin=s.IsAdmin,PawnIoInstalled=s.PawnIoInstalled,WarmingUp=s.WarmingUp,Readings=s.Readings.Select(r=>new {r.Id,r.Name,r.Hardware,r.HardwareKind,r.Type,r.Unit,r.Source,r.Scope,r.Status,DisplayStatus=Selection.State(r,s,draft.IntervalMs),r.Value,r.UpdatedUtc,r.PollIntervalMs,r.DiskNumber,r.DriveLetters}).ToArray()};
                try{File.WriteAllText(dialog.FileName,new JavaScriptSerializer().Serialize(export),new UTF8Encoding(false));result.Text="已导出；清单包含硬件型号，未自动上传。";}catch(Exception error){MessageBox.Show(this,"导出失败："+error.GetType().Name,"QuietMonitor");}
            }}
        void Commit(bool save){try{
            RememberSources();RememberExtras();draft.Mode=mode.SelectedIndex==1?"GameBar":"Desktop";draft.FontSize=(int)fontSize.Value;draft.Width=(int)width.Value;draft.BackgroundOpacity=(int)opacity.Value;draft.IntervalMs=(int)interval.Value*1000;
            draft.FontFamily=Convert.ToString(fontFamily.SelectedItem);draft.BoldText=bold.Checked;draft.HideMissingFans=hideFans.Checked;draft.RoundNumbers=roundNumbers.Checked;draft.TaskManagerVramCapacity=taskManagerVram.Checked;draft.AutoShowTemperatures=autoTemperatures.Checked;draft.EnableMemory=enableMemory.Checked;draft.EnableStorage=enableStorage.Checked;draft.EnableController=enableController.Checked;draft.EnablePsu=enablePsu.Checked;draft.EnableBattery=enableBattery.Checked;draft.SlowIntervalSeconds=(int)slowInterval.Value;
            draft.TopMost=topMost.Checked;draft.ClickThrough=clickThrough.Checked;draft.ShowMemory=memory.Checked;draft.ShowNetwork=network.Checked;draft.Units=units.Checked;draft.AutoDock=autoDock.Checked;draft.StorageOrder=storageOrder.SelectedIndex==1?"Physical":"DriveLetter";
            draft.BreakReminderEnabled=reminderEnabled.Checked;draft.BreakReminderMinutes=(int)reminderMinutes.Value;draft.BreakReminderDeferFullscreen=reminderDefer.Checked;
            draft.BreakReminderAvoidPanel=reminderAvoidPanel.Checked;
            draft.Foreground=colors["Foreground"].Text;draft.Background=colors["Background"].Text;draft.Accent=colors["Accent"].Text;foreach(string key in Fields.Keys)draft.Visible[key]=visibleFields[key].Checked;
            draft.Normalize();apply(draft.Clone(),save);result.Text=save?"已保存并应用。":"已应用；退出后不保留本次修改。";
        }catch(Exception error){MessageBox.Show(this,"应用或保存失败："+error.GetType().Name,"QuietMonitor",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    }
}
