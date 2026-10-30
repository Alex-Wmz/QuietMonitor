using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace QuietMonitor {
    public sealed class LayoutConfig {
        public int X=int.MinValue,Y=int.MinValue;
        public string Monitor="";
    }
    public sealed class AppConfig {
        public int SchemaVersion=2,IntervalMs=1000,FontSize=11,BackgroundOpacity=80,Width=232,SlowIntervalSeconds=10;
        public string FontFamily="Microsoft YaHei UI";
        public bool AutoDock=true;
        public string StorageOrder="DriveLetter";
        public bool BreakReminderEnabled=false,BreakReminderDeferFullscreen=true;
        public bool BreakReminderAvoidPanel=false;
        public int BreakReminderMinutes=30;
        public bool BoldText=true,HideMissingFans=true,EnableMemory=false,EnableStorage=false,EnableController=false,EnablePsu=false,EnableBattery=false;
        public bool RoundNumbers=false,AutoShowTemperatures=true,TaskManagerVramCapacity=false;
        public List<string> ExtraSensorIds=new List<string>();
        public Dictionary<string,string> ExtraLabels=new Dictionary<string,string>();
        public string Foreground="#F0F3F6",Background="#11151C",Accent="#58D6B0",Mode="Desktop",NetworkId="";
        public bool TopMost=true,ClickThrough=false,ShowMemory=true,ShowNetwork=true,Units=true;
        public LayoutConfig Desktop=new LayoutConfig(),GameBar=new LayoutConfig();
        public Dictionary<string,string> Bindings=new Dictionary<string,string>();
        public Dictionary<string,bool> Visible=new Dictionary<string,bool>();
        public AppConfig(){foreach(string key in Fields.Keys){Bindings[key]="";Visible[key]=key!="CpuEffectiveClock"&&key!="GpuShared";}}
        public AppConfig Clone(){return new JavaScriptSerializer().Deserialize<AppConfig>(new JavaScriptSerializer().Serialize(this));}
        public void Normalize(){
            bool upgrade=SchemaVersion<2;
            IntervalMs=Math.Max(1000,Math.Min(10000,IntervalMs));FontSize=Math.Max(9,Math.Min(22,FontSize));BackgroundOpacity=Math.Max(15,Math.Min(100,BackgroundOpacity));Width=Math.Max(210,Math.Min(480,Width));SlowIntervalSeconds=Math.Max(5,Math.Min(60,SlowIntervalSeconds));SchemaVersion=2;
            if(String.IsNullOrWhiteSpace(FontFamily)||FontFamily.Length>100)FontFamily="Microsoft YaHei UI";
            if(ExtraSensorIds==null)ExtraSensorIds=new List<string>();ExtraSensorIds=ExtraSensorIds.FindAll(id=>!String.IsNullOrEmpty(id));
            if(ExtraLabels==null)ExtraLabels=new Dictionary<string,string>();
            if(Mode!="Desktop"&&Mode!="GameBar")Mode="Desktop";
            if(StorageOrder!="DriveLetter"&&StorageOrder!="Physical")StorageOrder="DriveLetter";
            BreakReminderMinutes=Math.Max(1,Math.Min(480,BreakReminderMinutes));
            if(Desktop==null)Desktop=new LayoutConfig();if(GameBar==null)GameBar=new LayoutConfig();
            if(Bindings==null)Bindings=new Dictionary<string,string>();if(Visible==null)Visible=new Dictionary<string,bool>();
            if(upgrade)Visible["GpuDedicated"]=true;
            foreach(string key in Fields.Keys){if(!Bindings.ContainsKey(key))Bindings[key]="";if(!Visible.ContainsKey(key))Visible[key]=key!="CpuEffectiveClock"&&key!="GpuShared";}
            if(!ColorValid(Foreground))Foreground="#F0F3F6";if(!ColorValid(Background))Background="#11151C";if(!ColorValid(Accent))Accent="#58D6B0";
            if(NetworkId==null)NetworkId="";
        }
        static bool ColorValid(string value){int ignored;return value!=null&&value.Length==7&&value[0]=='#'&&Int32.TryParse(value.Substring(1),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out ignored);}
    }
    public sealed class ConfigStore {
        public readonly string DirectoryPath,FilePath;
        public string LoadWarning;
        public ConfigStore(string directory){DirectoryPath=directory;FilePath=Path.Combine(directory,"settings.json");}
        public AppConfig Load(){
            try{if(!File.Exists(FilePath))return new AppConfig();var config=new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FilePath,Encoding.UTF8));if(config==null)throw new InvalidDataException();config.Normalize();return config;}
            catch{LoadWarning="设置文件无法读取，已使用默认值。原文件保留至手动保存。";return new AppConfig();}
        }
        public void Save(AppConfig config){
            config.Normalize();Directory.CreateDirectory(DirectoryPath);
            string temporary=Path.Combine(DirectoryPath,"settings.new.json");
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(config),new UTF8Encoding(false));
            if(File.Exists(FilePath))File.Replace(temporary,FilePath,Path.Combine(DirectoryPath,"settings.previous.json"));else File.Move(temporary,FilePath);
        }
    }
}
