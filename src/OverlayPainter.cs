using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
namespace QuietMonitor {
    public sealed class OverlayRow {
        public string Kind,Label,Value;
        public OverlayRow(string kind,string label,string value=""){Kind=kind;Label=label;Value=value;}
    }
    public sealed class OverlayContent {
        public readonly List<OverlayRow> BaseRows=new List<OverlayRow>(),ExtraRows=new List<OverlayRow>();
        public string Signature;
    }
    // Shared by the real layered window and offline bitmap layout validation.
    public static class OverlayPainter {
        public static int Recenter(int left,int oldWidth,int newWidth){return left+oldWidth/2-newWidth/2;}
        public static Point PlaceWithin(Rectangle area,Size size,Point requested,string mode,bool dock,int margin){
            int left=area.Left+margin,top=area.Top+margin,right=Math.Max(left,area.Right-margin-size.Width),bottom=Math.Max(top,area.Bottom-margin-size.Height);
            if(dock)return new Point(mode=="GameBar"?Math.Max(left,area.Left+(area.Width-size.Width)/2):right,top);
            return new Point(Math.Max(left,Math.Min(requested.X,right)),Math.Max(top,Math.Min(requested.Y,bottom)));
        }
        public static string DefaultExtraLabel(Reading r){
            if(r==null)return "来源暂缺";
            if(r.Scope=="configuration-not-live-clock")return "内存配置速率 "+r.Name.Substring(r.Name.LastIndexOf('#')+1);
            if(r.HardwareKind=="Memory"&&r.Type=="Temperature"&&r.Name.StartsWith("DIMM #",StringComparison.Ordinal))return r.Name+" 温度";
            if(r.HardwareKind=="Storage"){
                int index=DisplayNumbers.StorageNumber(r);string slot=index==Int32.MaxValue?"":index.ToString();
                string name=r.Name=="Composite Temperature"?"温度":r.Name.StartsWith("Temperature #",StringComparison.Ordinal)?r.Name.Replace("Temperature #","温度 #"):r.Name;
                return "硬盘 "+slot+(String.IsNullOrEmpty(r.DriveLetters)?"":"（"+r.DriveLetters+"）")+" "+name;
            }
            return r.Hardware+" / "+r.Name;
        }
        static string Rate(double? n,Snapshot s,AppConfig c){return n.HasValue&&!s.WarmingUp&&(DateTime.UtcNow-s.Utc).TotalMilliseconds<=Math.Max(5000,c.IntervalMs*3)?DisplayNumbers.Rate(n.Value,c):"—";}
        public static OverlayContent Build(Snapshot s,AppConfig c){
            var content=new OverlayContent();
            foreach(string prefix in new[]{"Cpu","Gpu"}){
                var rows=new List<OverlayRow>();
                foreach(string key in Fields.Keys.Where(k=>k.StartsWith(prefix,StringComparison.Ordinal))){
                    bool visible;if(!c.Visible.TryGetValue(key,out visible)||!visible)continue;Reading r=Selection.Resolve(key,s,c);if(!Selection.DisplayField(key,r,c))continue;
                    rows.Add(new OverlayRow("pair",Selection.Label(key,r),Selection.Format(key,r,s,c)));
                }
                if(rows.Count==0)continue;
                if(c.Mode=="GameBar")content.BaseRows.Add(new OverlayRow("line",(prefix=="Cpu"?"CPU":"GPU")+"   "+String.Join("    ",rows.Select(r=>r.Label+" "+r.Value))));
                else{content.BaseRows.Add(new OverlayRow("heading",prefix=="Cpu"?"CPU":"GPU"));content.BaseRows.AddRange(rows);}
            }
            bool fresh=(DateTime.UtcNow-s.Utc).TotalMilliseconds<=Math.Max(5000,c.IntervalMs*3);
            string ram=fresh&&!s.WarmingUp&&s.MemoryUsedGiB.HasValue&&s.MemoryTotalGiB.HasValue?DisplayNumbers.Number(s.MemoryUsedGiB.Value,c,"0.0")+" / "+DisplayNumbers.Number(s.MemoryTotalGiB.Value,c,"0.0")+(c.Units?" GB":""):"—";
            string down=Rate(s.DownloadBps,s,c),up=Rate(s.UploadBps,s,c);
            if(c.Mode=="GameBar"){
                if(c.ShowMemory||c.ShowNetwork)content.BaseRows.Add(new OverlayRow("line",(c.ShowMemory?"RAM  "+ram+"    ":"")+(c.ShowNetwork?"NET  ↓ "+down+"   ↑ "+up:"")));
            }else{
                if(c.ShowMemory)content.BaseRows.Add(new OverlayRow("pair","RAM",ram));
                if(c.ShowNetwork){content.BaseRows.Add(new OverlayRow("pair","NET ↓",down));content.BaseRows.Add(new OverlayRow("pair","NET ↑",up));}
            }
            var automatic=new List<Reading>();
            foreach(string kind in new[]{"Memory","Storage"}){
                if(c.Mode=="GameBar"&&kind=="Storage")continue;
                Reading[] temperatures=DisplayNumbers.Temperatures(s,c,kind).ToArray();automatic.AddRange(temperatures);
                if(c.AutoShowTemperatures&&(kind=="Memory"?c.EnableMemory:c.EnableStorage)&&temperatures.Length==0)content.ExtraRows.Add(new OverlayRow("pair",kind=="Memory"?"内存温度":"硬盘温度","—"));
            }
            foreach(string id in DisplayNumbers.OrderExtras(automatic.Select(r=>r.Id).Concat(c.ExtraSensorIds),s,c.StorageOrder)){
                Reading r=s.Readings.FirstOrDefault(x=>x.Id==id);string label;
                if(c.Mode=="GameBar"&&IsStorageTemperature(r,id))continue;
                if(!c.ExtraLabels.TryGetValue(id,out label)||String.IsNullOrWhiteSpace(label))label=r==null?id:DefaultExtraLabel(r);
                content.ExtraRows.Add(new OverlayRow("pair",label,Selection.ExtraValue(r,s,c)));
            }
            content.Signature=String.Join(";",content.BaseRows.Concat(content.ExtraRows).Select(r=>r.Kind+"|"+r.Label+"|"+r.Value));return content;
        }
        static bool IsStorageTemperature(Reading r,string id){
            if(r!=null)return r.HardwareKind=="Storage"&&r.Type=="Temperature";
            // Retain a temporarily missing disk selection in settings, without adding a game row.
            return id!=null&&(id.StartsWith("/hdd/",StringComparison.Ordinal)||id.StartsWith("/nvme/",StringComparison.Ordinal))&&id.IndexOf("/temperature/",StringComparison.Ordinal)>=0;
        }
        public static Bitmap Paint(OverlayContent content,AppConfig c,float scale,int maxWidth,int maxHeight,int extraOffset){
            using(var font=new Font(c.FontFamily,c.FontSize,c.BoldText?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Point))
            using(var heading=new Font(c.FontFamily,c.FontSize,FontStyle.Bold,GraphicsUnit.Point))
            using(var measure=new Bitmap(1,1)){
                bool compact=c.Mode=="GameBar";
                measure.SetResolution(96*scale,96*scale);int row,pad=(int)Math.Round(12*scale),padY=(int)Math.Round((compact?4:12)*scale),width=(int)Math.Round(c.Width*scale);
                using(Graphics g=Graphics.FromImage(measure)){
                    row=(int)Math.Ceiling(font.GetHeight(g))+Math.Max(1,(int)Math.Round((compact?1:5)*scale));
                    if(c.Mode=="GameBar"){
                        float need=content.BaseRows.Select(r=>g.MeasureString(r.Label,font).Width).DefaultIfEmpty(480*scale).Max();
                        width=Math.Max((int)(480*scale),(int)Math.Ceiling(need)+pad*2+6);
                    }
                }
                width=Math.Min(maxWidth,width);
                var rows=new List<OverlayRow>(content.BaseRows);
                int capacity=Math.Max(1,(maxHeight-padY*2)/row-rows.Count-1);
                if(content.ExtraRows.Count>0){
                    rows.Add(new OverlayRow("heading","更多"));int count=Math.Min(capacity,content.ExtraRows.Count),start=((extraOffset%content.ExtraRows.Count)+content.ExtraRows.Count)%content.ExtraRows.Count;
                    for(int i=0;i<count;i++)rows.Add(content.ExtraRows[(start+i)%content.ExtraRows.Count]);
                    if(content.ExtraRows.Count>count){if(rows.Count*row+padY*2+row>maxHeight)rows.RemoveAt(rows.Count-1);rows.Add(new OverlayRow("line","还有更多项目 · 解锁后滚轮切换"));}
                }
                if(rows.Count==0)rows.Add(new OverlayRow("line","请在设置中选择显示项目"));
                int height=Math.Min(maxHeight,padY*2+row*rows.Count);
                var bitmap=new Bitmap(Math.Max(1,width),Math.Max(1,height),PixelFormat.Format32bppPArgb);bitmap.SetResolution(96*scale,96*scale);
                using(Graphics g=Graphics.FromImage(bitmap)){
                    g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;g.TextContrast=4;
                    Color bg=ColorTranslator.FromHtml(c.Background),fg=ColorTranslator.FromHtml(c.Foreground),accent=ColorTranslator.FromHtml(c.Accent);
                    using(var brush=new SolidBrush(Color.FromArgb(c.BackgroundOpacity*255/100,bg)))g.FillRectangle(brush,0,0,width,height);
                    using(var foreground=new SolidBrush(fg))using(var highlight=new SolidBrush(accent)){
                        int y=padY;
                        foreach(OverlayRow item in rows){
                            if(item.Kind=="pair"){
                                int valueWidth=Math.Min(width-pad*2,(int)Math.Ceiling(g.MeasureString(item.Value,font).Width)+3);
                                Draw(g,item.Label,font,foreground,pad,y,Math.Max(1,width-pad*2-valueWidth-(int)(8*scale)),row);
                                Draw(g,item.Value,font,foreground,pad,y,width-pad*2,row,true);
                            }else Draw(g,item.Label,item.Kind=="heading"?heading:font,item.Kind=="heading"?highlight:foreground,pad,y,width-pad*2,row);
                            y+=row;
                        }
                    }
                }return bitmap;
            }
        }
        static void Draw(Graphics g,string text,Font font,Brush brush,int x,int y,int width,int height,bool right=false){
            // Align glyph origins to physical pixels; fractional vertical centering blurs small text.
            int top=y+(int)Math.Floor((height-font.GetHeight(g))/2);
            using(var format=new StringFormat{Alignment=right?StringAlignment.Far:StringAlignment.Near,LineAlignment=StringAlignment.Near,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,new RectangleF(x,top,width,height),format);
        }
    }
}
