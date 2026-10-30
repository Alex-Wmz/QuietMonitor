using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuietMonitor {
    public sealed class Reading {
        public string Id,Name,Hardware,HardwareKind,Type,Unit,Source,Scope,Status;
        public double? Value;
        public int PollIntervalMs=1000;
        public int? DiskNumber;
        public string DriveLetters="";
        public DateTime UpdatedUtc;
        public override string ToString(){return Hardware+" / "+Name+" ["+Source+", "+Unit+"]";}
    }
    public sealed class Snapshot {
        public DateTime Utc;
        public bool IsAdmin,PawnIoInstalled,WarmingUp;
        public string CpuName="CPU",GpuName="GPU",Message="正在准备传感器…";
        public List<Reading> Readings=new List<Reading>();
        public List<AdapterChoice> Adapters=new List<AdapterChoice>();
        public double? MemoryUsedGiB,MemoryTotalGiB,DownloadBps,UploadBps;
        public string NetworkName;
    }
    public sealed class AdapterChoice {
        public string Id,Name;
        public override string ToString(){return Name;}
    }
    public static class Fields {
        public static readonly string[] Keys={"CpuLoad","CpuClock","CpuEffectiveClock","CpuTemp","CpuPower","CpuFan","GpuLoad","GpuClock","GpuTemp","GpuHotSpotTemp","GpuMemoryTemp","GpuMemoryClock","GpuPower","GpuFan","GpuDedicated","GpuShared"};
        public static readonly Dictionary<string,string> Labels=new Dictionary<string,string>{
            {"CpuLoad","CPU 占用"},{"CpuClock","CPU 核心平均频率"},{"CpuEffectiveClock","CPU 平均有效频率"},{"CpuTemp","CPU 温度"},{"CpuPower","CPU 封装功耗"},{"CpuFan","CPU 风扇"},
            {"GpuLoad","GPU 占用"},{"GpuClock","GPU 频率"},{"GpuTemp","GPU 核心温度"},{"GpuHotSpotTemp","GPU 热点温度"},{"GpuMemoryTemp","GPU 显存温度"},{"GpuMemoryClock","GPU 显存频率"},{"GpuPower","GPU 功耗"},{"GpuFan","GPU 风扇"},{"GpuDedicated","GPU 专用显存（已用/容量）"},{"GpuShared","GPU 共享内存"}};
        public static string TypeFor(string key){if(key.EndsWith("Temp"))return "Temperature";if(key.EndsWith("Power"))return "Power";if(key.EndsWith("Fan"))return "Fan";if(key.EndsWith("Clock"))return "Clock";if(key.EndsWith("Load"))return "Load";return "SmallData";}
    }
    public static class Selection {
        public static Reading Resolve(string key,Snapshot snapshot,AppConfig config){
            string binding;
            if(config.Bindings.TryGetValue(key,out binding)&&!String.IsNullOrEmpty(binding))return snapshot.Readings.FirstOrDefault(r=>r.Id==binding&&(key!="CpuEffectiveClock"||r.HardwareKind=="Cpu"&&r.Type=="Clock"&&(r.Scope=="effective-clock"||r.Name.IndexOf("Effective",StringComparison.OrdinalIgnoreCase)>=0))&&(key!="GpuHotSpotTemp"||r.Hardware==snapshot.GpuName&&IsHotSpot(r)));
            string type=Fields.TypeFor(key);IEnumerable<Reading> candidates=snapshot.Readings.Where(r=>r.Type==type);
            bool cpu=key.StartsWith("Cpu",StringComparison.Ordinal);
            if(cpu){
                if(key=="CpuFan")return candidates.OrderBy(r=>r.Status=="ok"?0:1).FirstOrDefault(r=>r.Name.IndexOf("CPU",StringComparison.OrdinalIgnoreCase)>=0);
                candidates=candidates.Where(r=>r.HardwareKind=="Cpu");
            }else{
                string gpu=snapshot.GpuName;
                candidates=candidates.Where(r=>r.Hardware==gpu);
            }
            Reading[] all=candidates.ToArray();
            string[] names;
            switch(key){
                case "CpuLoad":names=new[]{"CPU Utility (Windows)","CPU Total","CPU Package"};break;
                case "CpuClock":names=new[]{"Cores (Average)","Core Average","CPU Core"};break;
                case "CpuEffectiveClock":names=new[]{"Cores (Average Effective)","Core Average (Effective)"};break;
                case "CpuTemp":names=new[]{"CPU Package","Core (Tctl/Tdie)","Core (Tdie)","Core Max","Core Average"};break;
                case "CpuPower":names=new[]{"Package","CPU Package"};break;
                case "GpuLoad":names=new[]{"GPU Core","GPU Usage","GPU D3D 3D"};break;
                case "GpuClock":names=new[]{"GPU Core","GPU Clock"};break;
                case "GpuTemp":names=new[]{"GFX Temperature","GPU Core","GPU Temperature"};break;
                case "GpuHotSpotTemp":names=new[]{"GPU Hot Spot","GPU Hotspot","GPU Junction","GPU Junction Temperature"};break;
                case "GpuMemoryTemp":names=new[]{"GPU Memory Junction","GPU Memory Temperature","GPU Memory"};break;
                case "GpuMemoryClock":names=new[]{"GPU Memory","GPU Memory Clock"};break;
                case "GpuPower":names=new[]{"GFX Power","GPU Package","GPU Power","GPU Core"};break;
                case "GpuFan":names=new[]{"GPU Fan","GPU Fan 1"};break;
                case "GpuDedicated":names=new[]{"D3D Dedicated Memory Used","GPU Memory Used"};break;
                case "GpuShared":names=new[]{"D3D Shared Memory Used"};break;
                default:return null;
            }
            foreach(string name in names){Reading reading=all.FirstOrDefault(r=>Usable(r,snapshot,config.IntervalMs)&&String.Equals(r.Name,name,StringComparison.OrdinalIgnoreCase));if(reading!=null)return reading;}
            foreach(string name in names){Reading reading=all.FirstOrDefault(r=>String.Equals(r.Name,name,StringComparison.OrdinalIgnoreCase));if(reading!=null)return reading;}
            // A missing named temperature must never fall back to an unrelated VR/SoC/hotspot sensor.
            if(key=="CpuTemp")return all.FirstOrDefault(r=>r.Name.StartsWith("Core #",StringComparison.Ordinal)&&r.Type=="Temperature");
            if(key=="CpuClock"){
                Reading[] cores=all.Where(r=>IsCoreClockName(r.Name)).ToArray();
                if(cores.Length>0){
                    string state=cores.Select(r=>State(r,snapshot,config.IntervalMs)).FirstOrDefault(value=>value!="ok")??"ok";
                    return new Reading{Id="derived/cpu/average-core-clock",Name="Core Average",Hardware=cores[0].Hardware,HardwareKind="Cpu",Type="Clock",Unit="MHz",Value=state=="ok"?(double?)cores.Average(r=>r.Value.Value):null,Source="LHM mean",Scope="core-clock",Status=state,UpdatedUtc=cores.Min(r=>r.UpdatedUtc),PollIntervalMs=cores.Min(r=>r.PollIntervalMs)};
                }
            }
            return null;
        }
        static bool IsHotSpot(Reading r){return r.Type=="Temperature"&&r.HardwareKind!=null&&r.HardwareKind.StartsWith("Gpu",StringComparison.Ordinal)&&new[]{"GPU Hot Spot","GPU Hotspot","GPU Junction","GPU Junction Temperature"}.Any(name=>String.Equals(r.Name,name,StringComparison.OrdinalIgnoreCase));}
        static bool IsCoreClockName(string name){
            foreach(string prefix in new[]{"Core #","CPU Core #"}){int index;if(name!=null&&name.StartsWith(prefix,StringComparison.Ordinal)&&Int32.TryParse(name.Substring(prefix.Length),out index)&&index>0)return true;}
            return false;
        }
        public static string State(Reading reading,Snapshot snapshot,int intervalMs){
            if(reading==null)return "unavailable";
            if(reading.Status!="ok")return String.IsNullOrEmpty(reading.Status)?"unavailable":reading.Status;
            if(!reading.Value.HasValue)return "unavailable";
            if(Double.IsNaN(reading.Value.Value)||Double.IsInfinity(reading.Value.Value))return "invalid";
            if(snapshot.WarmingUp)return "warming-up";
            if((DateTime.UtcNow-snapshot.Utc).TotalMilliseconds>Math.Max(5000,intervalMs*3)||
                (DateTime.UtcNow-reading.UpdatedUtc).TotalMilliseconds>Math.Max(5000,Math.Max((double)intervalMs*3,(double)reading.PollIntervalMs*3)))return "stale";
            return "ok";
        }
        public static bool Usable(Reading reading,Snapshot snapshot,int intervalMs){
            return State(reading,snapshot,intervalMs)=="ok";
        }
        public static string Format(string key,Reading reading,Snapshot snapshot,AppConfig config){
            if(!Usable(reading,snapshot,config.IntervalMs))return "—";
            double number=reading.Value.Value;string value;
            if(key=="GpuDedicated"){
                Reading capacity=MemoryCapacity(snapshot,reading.Hardware);
                bool validTotal=Usable(capacity,snapshot,config.IntervalMs);
                double totalNumber=validTotal?capacity.Value.Value:0;
                if(validTotal&&config.TaskManagerVramCapacity&&(capacity.Name.StartsWith("D3D",StringComparison.Ordinal)||capacity.Name.StartsWith("DXGI",StringComparison.Ordinal)))totalNumber=DisplayNumbers.TaskManagerTotal(totalNumber);
                double divisor=(validTotal?totalNumber:number)>=1024?1024:1;
                string total=validTotal?DisplayNumbers.Number(totalNumber/divisor,config,"0.#"):"—";
                return DisplayNumbers.Number(number/divisor,config,"0.#")+" / "+total+(config.Units?(divisor==1024?" GB":" MB"):"");
            }
            if(key=="GpuShared")return DisplayNumbers.Number(number/1024,config,"0.0")+(config.Units?" GB":"");
            if(reading.Type=="Load")value=number.ToString("0",CultureInfo.InvariantCulture);
            else if(reading.Type=="Clock"||reading.Type=="Fan")value=number.ToString("0",CultureInfo.InvariantCulture);
            else value=DisplayNumbers.Number(number,config,reading.Source=="AMD ADL"?"0":"0.0");
            return value+(config.Units?" "+DisplayNumbers.Unit(reading.Unit):"");
        }
        static Reading MemoryCapacity(Snapshot snapshot,string hardware){
            foreach(string name in new[]{"D3D Dedicated Memory Total","D3DKMT Dedicated Memory Capacity","DXGI Dedicated Memory Capacity","GPU Memory Total"}){
                Reading[] found=snapshot.Readings.Where(r=>r.Hardware==hardware&&r.Name==name&&r.Status=="ok"&&r.Value>0).ToArray();
                if(found.Length==0)continue;
                // Identically named adapters with different capacities need an explicit future adapter mapping.
                if(found.Select(r=>r.Value.Value).Distinct().Count()>1)return null;
                return found[0];
            }return null;
        }
        public static string Label(string key,Reading selected){
            if(key=="CpuLoad"&&selected!=null&&selected.Scope=="processor-time")return "占用（时间）";
            if(key=="GpuMemoryTemp"&&selected!=null&&selected.Name=="GPU Memory Junction")return "显存结温";
            if(key=="CpuClock")return selected!=null&&selected.Name.IndexOf("Effective",StringComparison.OrdinalIgnoreCase)>=0?"有效频率":"频率";
            if(key=="CpuEffectiveClock")return "有效频率";
            if(key=="GpuDedicated")return "专用显存";
            if(key=="GpuPower"&&selected!=null&&selected.Scope=="gfx-driver")return "GFX 功耗";
            if(key=="GpuPower"&&selected!=null&&selected.Scope=="asic-driver")return "ASIC 功耗";
            if(key=="GpuPower"&&selected!=null&&selected.Scope=="soc-driver")return "SoC 功耗";
            return Fields.Labels[key].Replace("CPU ","").Replace("GPU ","");
        }
        public static bool DisplayField(string key,Reading reading,AppConfig config){return !key.EndsWith("Fan",StringComparison.Ordinal)||!config.HideMissingFans||reading!=null&&(reading.Status=="ok"||reading.Status=="needs-admin"||reading.Status=="needs-driver");}
        public static string ExtraValue(Reading reading,Snapshot snapshot,AppConfig config){
            if(!Usable(reading,snapshot,config.IntervalMs))return "—";
            double value=reading.Value.Value;string unit=reading.Unit;
            if(reading.Type=="Throughput")return DisplayNumbers.Rate(value,config);
            bool rounded=reading.Type=="Temperature"||reading.Type=="Power"||reading.Type=="Data"||reading.Type=="SmallData";
            string text=rounded?DisplayNumbers.Number(value,config,"0.##"):value.ToString(reading.Type=="Clock"||reading.Type=="Fan"||reading.Type=="DataRate"?"0":"0.##",CultureInfo.InvariantCulture);
            return text+(config.Units&&!String.IsNullOrEmpty(unit)?" "+DisplayNumbers.Unit(unit):"");
        }
    }
    // Familiar Windows-style labels, with 1024 scaling retained for binary readings.
    // Original backend units/precision remain untouched in Reading and inventory exports.
    public static class DisplayNumbers {
        public static string Number(double value,AppConfig config,string pattern){return config.RoundNumbers?Math.Round(value,0,MidpointRounding.AwayFromZero).ToString("0",CultureInfo.InvariantCulture):value.ToString(pattern,CultureInfo.InvariantCulture);}
        public static string Unit(string unit){return (unit??"").Replace("KiB","KB").Replace("MiB","MB").Replace("GiB","GB").Replace("GB (LHM)","GB");}
        public static string Rate(double bytes,AppConfig config){
            double value=bytes/1024;string unit="KB/s";
            if(value>=1024){value/=1024;unit="MB/s";}if(value>=1024){value/=1024;unit="GB/s";}
            return Number(value,config,"0.0")+(config.Units?" "+unit:"");
        }
        // Display estimate, matching the policy verified in the local Windows 10 Task Manager.
        // This is not a physical-capacity sensor; never overwrite the underlying Reading.
        public static double TaskManagerTotal(double mib){
            if(Double.IsNaN(mib)||Double.IsInfinity(mib)||mib<=0||mib>1099511627776)return mib;
            ulong bytes=(ulong)Math.Round(mib*1048576,MidpointRounding.AwayFromZero),step=1;
            ulong target=bytes/10;while(step<target&&step<536870912)step<<=1;
            return ((bytes+step-1)/step*step)/1048576.0;
        }
        public static IEnumerable<Reading> Temperatures(Snapshot snapshot,AppConfig config,string kind){
            if(!config.AutoShowTemperatures||kind=="Memory"&&!config.EnableMemory||kind=="Storage"&&!config.EnableStorage)return Enumerable.Empty<Reading>();
            var readings=snapshot.Readings.Where(r=>LiveTemperature(r,kind));
            if(kind=="Storage")return readings.GroupBy(r=>{int at=r.Id.LastIndexOf("/temperature/",StringComparison.OrdinalIgnoreCase);return at>=0?r.Id.Substring(0,at):r.Hardware;}).Select(g=>g.OrderBy(r=>r.Name=="Composite Temperature"?0:r.Name=="Temperature"?1:2).ThenBy(r=>r.Id,StringComparer.Ordinal).First()).OrderBy(StorageNumber);
            return readings;
        }
        public static int StorageNumber(Reading r){
            if(r.DiskNumber.HasValue&&r.DiskNumber.Value>=0)return r.DiskNumber.Value;
            string[] parts=(r.Id??"").Split('/');int number;return parts.Length>2&&Int32.TryParse(parts[2],out number)&&number>=0?number:Int32.MaxValue;
        }
        public static string FirstDriveLetter(Reading r){
            string[] letters=(r.DriveLetters??"").Split(',').Select(v=>v.Trim().ToUpperInvariant()).Where(v=>v.Length>=2&&v[0]>='A'&&v[0]<='Z'&&v[1]==':').OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            return letters.Length==0?"{":letters[0].Substring(0,1);
        }
        public static IEnumerable<string> OrderExtras(IEnumerable<string> ids,Snapshot snapshot,string order="Physical"){
            var selected=ids.Distinct().ToArray();var readings=snapshot.Readings.GroupBy(r=>r.Id).ToDictionary(g=>g.Key,g=>g.First());
            var ordered=new Queue<string>(selected.Where(id=>readings.ContainsKey(id)&&readings[id].HardwareKind=="Storage").OrderBy(id=>order=="DriveLetter"?FirstDriveLetter(readings[id]):"",StringComparer.Ordinal).ThenBy(id=>StorageNumber(readings[id])));
            foreach(string id in selected)yield return readings.ContainsKey(id)&&readings[id].HardwareKind=="Storage"?ordered.Dequeue():id;
        }
        public static bool LiveTemperature(Reading r,string kind){return r.HardwareKind==kind&&r.Type=="Temperature"&&r.Scope!="configured-temperature-limit"&&r.Scope!="temperature-sensor-resolution"&&r.Name.IndexOf("Warning",StringComparison.OrdinalIgnoreCase)<0&&r.Name.IndexOf("Critical",StringComparison.OrdinalIgnoreCase)<0&&r.Name.IndexOf("Resolution",StringComparison.OrdinalIgnoreCase)<0;}
    }
}
