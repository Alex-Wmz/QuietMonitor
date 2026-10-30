using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace QuietMonitor {
    public sealed class Sampler:IDisposable {
        [StructLayout(LayoutKind.Sequential)]sealed class MemoryInfo{
            public uint Length=(uint)Marshal.SizeOf(typeof(MemoryInfo));public uint Load;
            public ulong TotalPhys,AvailPhys,TotalPage,AvailPage,TotalVirtual,AvailVirtual,AvailExtended;
        }
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx([In,Out]MemoryInfo info);
        readonly ManualResetEvent stop=new ManualResetEvent(false);
        readonly Thread thread;
        volatile AppConfig config;
        volatile Snapshot latest=new Snapshot();
        Computer computer;AdlBackend adl;WindowsCpuLoad cpuLoad;
        readonly bool admin;
        bool pawn;
        string lhmError,adlError;
        DateTime lastAttempt=DateTime.MinValue,lastAdapters=DateTime.MinValue;
        NetworkInterface[] adapters=new NetworkInterface[0];
        string previousNic;long received,sent,networkTicks;
        int cycles;
        string enabledGroups;
        readonly Dictionary<string,List<Reading>> slowCache=new Dictionary<string,List<Reading>>();
        readonly Dictionary<string,DateTime> slowUpdated=new Dictionary<string,DateTime>();
        List<Reading> capacity=new List<Reading>(),memoryConfiguration=new List<Reading>();
        DateTime capacityUpdated=DateTime.MinValue;
        public Snapshot Latest{get{return latest;}}
        public Sampler(AppConfig config){
            this.config=config;admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            thread=new Thread(Loop){IsBackground=true,Name="QuietMonitor sampler",Priority=ThreadPriority.BelowNormal};thread.Start();
        }
        public void Configure(AppConfig updated){config=updated;}
        void Initialize(){
            if(computer==null){
                try{
                    pawn=LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
                    AppConfig c=config;
                    computer=new Computer{IsCpuEnabled=true,IsGpuEnabled=true,IsMotherboardEnabled=true,IsMemoryEnabled=c.EnableMemory,IsStorageEnabled=c.EnableStorage,IsNetworkEnabled=false,IsControllerEnabled=c.EnableController,IsPsuEnabled=c.EnablePsu,IsBatteryEnabled=c.EnableBattery};
                    computer.Open();lhmError=null;
                    memoryConfiguration=c.EnableMemory?ReadMemoryConfiguration():new List<Reading>();
                    enabledGroups=Groups(c);
                }catch(Exception error){lhmError=error.GetType().Name;try{if(computer!=null)computer.Close();}catch{}computer=null;}
            }
            if(adl==null){try{adl=new AdlBackend();adlError=null;}catch(Exception error){adlError=error.GetType().Name;}}
            lastAttempt=DateTime.UtcNow;
        }
        void Loop(){
            try{
                while(!stop.WaitOne(0)){
                    Stopwatch elapsed=Stopwatch.StartNew();AppConfig current=config;
                    try{
                        if(computer!=null&&enabledGroups!=Groups(current)){computer.Close();computer=null;slowCache.Clear();slowUpdated.Clear();cycles=0;lastAttempt=DateTime.MinValue;}
                        if((computer==null||adl==null)&&(DateTime.UtcNow-lastAttempt).TotalSeconds>=30)Initialize();
                        var snapshot=new Snapshot{Utc=DateTime.UtcNow,IsAdmin=admin,PawnIoInstalled=pawn,WarmingUp=cycles<3};
                        if(computer!=null){
                            foreach(IHardware hardware in computer.Hardware)Visit(hardware,snapshot,current,false);
                            snapshot.CpuName=computer.Hardware.Where(h=>h.HardwareType==HardwareType.Cpu).Select(h=>h.Name).FirstOrDefault()??"CPU";
                            IHardware gpu=computer.Hardware.Where(h=>h.HardwareType.ToString().StartsWith("Gpu")).OrderBy(h=>h.HardwareType==HardwareType.GpuNvidia?0:h.HardwareType==HardwareType.GpuAmd?1:2).FirstOrDefault();
                            snapshot.GpuName=gpu==null?"GPU":gpu.Name;
                        }
                        if(cpuLoad==null)cpuLoad=new WindowsCpuLoad();snapshot.Readings.Add(cpuLoad.Read(snapshot.CpuName,current.IntervalMs,snapshot.Utc));
                        if(adl!=null){try{snapshot.Readings.AddRange(adl.Read());}catch(Exception error){adlError=error.GetType().Name;adl.Dispose();adl=null;}}
                        if(snapshot.GpuName=="GPU")snapshot.GpuName=snapshot.Readings.Where(r=>r.HardwareKind.StartsWith("Gpu")).Select(r=>r.Hardware).FirstOrDefault()??"GPU";
                        bool needCapacity=!snapshot.Readings.Any(r=>r.Hardware==snapshot.GpuName&&r.Name=="D3D Dedicated Memory Total"&&r.Status=="ok"&&r.Value>0);
                        if(needCapacity){if((DateTime.UtcNow-capacityUpdated).TotalSeconds>=30){capacity=DxgiMemory.Read();capacityUpdated=DateTime.UtcNow;}snapshot.Readings.AddRange(capacity);}
                        foreach(Reading r in memoryConfiguration)snapshot.Readings.Add(new Reading{Id=r.Id,Name=r.Name,Hardware=r.Hardware,HardwareKind=r.HardwareKind,Type=r.Type,Unit=r.Unit,Value=r.Value,Source=r.Source,Scope=r.Scope,Status=r.Status,UpdatedUtc=snapshot.Utc});
                        var memory=new MemoryInfo();if(GlobalMemoryStatusEx(memory)){snapshot.MemoryTotalGiB=memory.TotalPhys/1073741824.0;snapshot.MemoryUsedGiB=(memory.TotalPhys-memory.AvailPhys)/1073741824.0;}
                        Network(snapshot,current);
                        snapshot.Message=snapshot.WarmingUp?"传感器准备中…":!admin?"CPU 部分数据需管理员权限":!pawn?"CPU 部分数据需安装 PawnIO":lhmError!=null?"LHM 不可用："+lhmError:"1 秒采样 · 数据保留在内存";
                        if(current.IntervalMs!=1000&&!snapshot.WarmingUp&&admin&&pawn&&lhmError==null)snapshot.Message=(current.IntervalMs/1000)+" 秒采样 · 数据保留在内存";
                        latest=snapshot;cycles++;
                    }catch(Exception error){latest=new Snapshot{Utc=DateTime.UtcNow,IsAdmin=admin,PawnIoInstalled=pawn,Message="读取失败："+error.GetType().Name};}
                    int wait=Math.Max(20,current.IntervalMs-(int)elapsed.ElapsedMilliseconds);if(stop.WaitOne(wait))break;
                }
            }finally{try{if(cpuLoad!=null)cpuLoad.Dispose();}catch{}try{if(adl!=null)adl.Dispose();}catch{}try{if(computer!=null)computer.Close();}catch{}}
        }
        static string Groups(AppConfig c){return c.EnableMemory+"/"+c.EnableStorage+"/"+c.EnableController+"/"+c.EnablePsu+"/"+c.EnableBattery;}
        void Visit(IHardware hardware,Snapshot snapshot,AppConfig current,bool inheritedSlow){
            string hardwareKind=hardware.HardwareType.ToString();
            bool slow=inheritedSlow||hardwareKind!="Cpu"&&!hardwareKind.StartsWith("Gpu",StringComparison.Ordinal)&&hardwareKind!="Motherboard"&&hardwareKind!="SuperIO"&&hardwareKind!="EmbeddedController";
            string cacheKey=hardware.Identifier.ToString();DateTime last;
            if(slow&&slowUpdated.TryGetValue(cacheKey,out last)&&(DateTime.UtcNow-last).TotalSeconds<current.SlowIntervalSeconds){snapshot.Readings.AddRange(slowCache[cacheKey]);foreach(IHardware child in hardware.SubHardware)Visit(child,snapshot,current,true);return;}
            int start=snapshot.Readings.Count;
            bool ok=true;try{hardware.Update();}catch{ok=false;}
            int? diskNumber=null;string driveLetters="";
            var disk=hardware as LibreHardwareMonitor.Hardware.Storage.StorageDevice;
            if(disk!=null){try{
                // Reuse already enumerated metadata; these getters perform no additional device query.
                if(disk.Storage.DriveNumber>=0)diskNumber=disk.Storage.DriveNumber;
                if(disk.Storage.Partitions!=null)driveLetters=String.Join(", ",disk.Storage.Partitions.Where(p=>p.DriveLetter.HasValue).Select(p=>Char.ToUpperInvariant(p.DriveLetter.Value)).Where(letter=>letter>='A'&&letter<='Z').Distinct().OrderBy(letter=>letter).Select(letter=>letter+":"));
            }catch{}}
            foreach(ISensor sensor in hardware.Sensors){
                string kind=hardware.HardwareType.ToString(),type=sensor.SensorType.ToString();
                float? raw=sensor.Value;double? value=raw.HasValue&&!Single.IsNaN(raw.Value)&&!Single.IsInfinity(raw.Value)?(double?)raw.Value:null;
                string status=ok?(value.HasValue?"ok":"unavailable"):"read-failed";
                bool privileged=kind=="Cpu"&&(type=="Temperature"||type=="Power"||type=="Clock"||type=="Voltage"||type=="Factor")||(kind=="Motherboard"||kind=="SuperIO"||kind=="EmbeddedController")&&type=="Fan";
                if(privileged&&!admin){status="needs-admin";value=null;}else if(privileged&&!pawn){status="needs-driver";value=null;}
                bool temperatureLimit=type=="Temperature"&&(sensor.Name.IndexOf("Limit",StringComparison.OrdinalIgnoreCase)>=0||sensor.Name.IndexOf("Offset",StringComparison.OrdinalIgnoreCase)>=0||sensor.Name.IndexOf("Warning",StringComparison.OrdinalIgnoreCase)>=0||sensor.Name.IndexOf("Critical",StringComparison.OrdinalIgnoreCase)>=0);
                if(type=="Temperature"&&value.HasValue&&(!temperatureLimit&&value.Value<=0||value.Value>150)){status="invalid";value=null;}
                if(kind=="Cpu"&&type=="Clock"&&sensor.Name.IndexOf("Effective",StringComparison.OrdinalIgnoreCase)<0&&value.HasValue&&value.Value<=0){status="invalid";value=null;}
                string name=sensor.Name,scope="";
                if(kind=="Cpu"&&type=="Load")scope="processor-time";
                if(kind=="GpuAmd"&&type=="Temperature"&&sensor.Identifier.ToString().EndsWith("/temperature/4",StringComparison.Ordinal)){name="SoC Temperature";scope="soc-driver";}
                if(type=="Power")scope=kind=="Cpu"?"cpu-package-or-core":kind=="GpuAmd"&&name=="GPU Core"?"gfx-driver":kind=="GpuAmd"&&name=="GPU SoC"?"soc-driver":kind.StartsWith("Gpu",StringComparison.Ordinal)?"gpu-driver":"device-telemetry";
                if(type=="Power"&&kind=="Cpu")scope=name=="CPU Package"||name=="Package"?"cpu-package":name=="CPU Cores"?"cpu-cores":name=="CPU Memory"?"cpu-memory-domain-not-dimm-power":name=="CPU Platform"?"cpu-platform-domain-not-wall-power":scope;
                if(type=="Power"&&kind=="GpuNvidia"&&name=="GPU Package")scope="gpu-and-associated-circuitry-nvml";
                if(kind=="Cpu"&&type=="Clock")scope=name.IndexOf("Effective",StringComparison.OrdinalIgnoreCase)>=0?"effective-clock":name=="Bus Speed"?"bus-clock":"core-clock";
                if(type=="Timing"||kind=="Memory"&&name=="Capacity")scope="spd-configuration-not-live";
                if(kind=="Memory"&&type=="Data"&&name=="Capacity"&&value.HasValue){
                    Reading[] installed=memoryConfiguration.Where(r=>r.Scope=="installed-memory-capacity").ToArray();
                    if(installed.Length>0&&!installed.Any(r=>Math.Abs(r.Value.Value-value.Value)<0.01)){status="inconsistent";scope="spd-capacity-conflicts-with-smbios";}
                }
                if(temperatureLimit)scope="configured-temperature-limit";
                if(type=="Temperature"&&name.IndexOf("Resolution",StringComparison.OrdinalIgnoreCase)>=0)scope="temperature-sensor-resolution";
                if(type=="Voltage")scope=name.IndexOf("VID",StringComparison.OrdinalIgnoreCase)>=0?"requested-voltage-vid":"voltage-telemetry-not-calibrated";
                if(name=="D3D Dedicated Memory Total")scope="wddm-video-memory-capacity";
                if(kind.StartsWith("Gpu",StringComparison.Ordinal)&&name=="GPU Memory Total")scope="driver-reported-memory-total";
                snapshot.Readings.Add(new Reading{Id=sensor.Identifier.ToString(),Name=name,Hardware=hardware.Name,HardwareKind=kind,Type=type,Unit=Unit(type),Value=value,Status=status,Source=scope=="gpu-and-associated-circuitry-nvml"?"LHM / NVIDIA NVML":"LHM",Scope=scope,UpdatedUtc=snapshot.Utc,PollIntervalMs=slow?current.SlowIntervalSeconds*1000:current.IntervalMs,DiskNumber=diskNumber,DriveLetters=driveLetters});
            }
            if(slow){slowCache[cacheKey]=snapshot.Readings.Skip(start).ToList();slowUpdated[cacheKey]=snapshot.Utc;}
            foreach(IHardware child in hardware.SubHardware)Visit(child,snapshot,current,slow);
        }
        static string Unit(string type){switch(type){case "Temperature":return "°C";case "Power":return "W";case "Fan":return "RPM";case "Clock":return "MHz";case "Load":case "Control":case "Level":return "%";case "SmallData":return "MiB";case "Data":return "GB (LHM)";case "Voltage":return "V";case "Current":return "A";case "Throughput":return "B/s";case "Factor":return "×";case "Energy":return "mWh";case "Frequency":return "Hz";case "Flow":return "L/h";case "Humidity":return "%";case "Timing":return "ns";case "TimeSpan":return "s";case "Noise":return "dBA";case "Conductivity":return "µS/cm";default:return type;}}
        static List<Reading> ReadMemoryConfiguration(){
            var result=new List<Reading>();
            try{using(var query=new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed, SMBIOSMemoryType, Capacity FROM Win32_PhysicalMemory"))using(var modules=query.Get()){
                int index=0;foreach(ManagementObject module in modules){using(module){index++;uint configured=Convert.ToUInt32(module["ConfiguredClockSpeed"]??0);uint speed=Convert.ToUInt32(module["Speed"]??0);uint kind=Convert.ToUInt32(module["SMBIOSMemoryType"]??0);uint rate=configured>0?configured:speed;
                    bool ddr=new uint[]{18,19,20,24,26,27,28,29,30,34,35}.Contains(kind);
                    if(rate>0)result.Add(new Reading{Id="smbios/memory/"+index+"/configured-speed",Name="Memory Configured Rate #"+index,Hardware="System Memory",HardwareKind="Memory",Type="DataRate",Unit=ddr?"MT/s":"MHz (reported)",Source="Windows SMBIOS",Scope="configuration-not-live-clock",Value=rate,Status="ok",UpdatedUtc=DateTime.UtcNow});
                    ulong bytes=Convert.ToUInt64(module["Capacity"]??0);if(bytes>0)result.Add(new Reading{Id="smbios/memory/"+index+"/installed-capacity",Name="Memory Installed Capacity #"+index,Hardware="System Memory",HardwareKind="Memory",Type="Data",Unit="GiB",Source="Windows SMBIOS",Scope="installed-memory-capacity",Value=bytes/1073741824.0,Status="ok",UpdatedUtc=DateTime.UtcNow});
                }}
            }}catch{}return result;
        }
        void Network(Snapshot snapshot,AppConfig current){
            if((DateTime.UtcNow-lastAdapters).TotalSeconds>30){try{adapters=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).ToArray();}catch{adapters=new NetworkInterface[0];}lastAdapters=DateTime.UtcNow;}
            snapshot.Adapters=adapters.Select(n=>new AdapterChoice{Id=n.Id,Name=n.Name}).ToList();
            if(!current.ShowNetwork)return;
            NetworkInterface selected=adapters.FirstOrDefault(n=>n.Id==current.NetworkId);
            if(selected==null&&String.IsNullOrEmpty(current.NetworkId))selected=adapters.Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)).OrderBy(n=>IsVirtualName(n.Name)?1:0).FirstOrDefault();
            if(selected==null)return;
            try{
                IPInterfaceStatistics stats=selected.GetIPStatistics();long now=Stopwatch.GetTimestamp();
                if(previousNic==selected.Id&&now>networkTicks){double seconds=(now-networkTicks)/(double)Stopwatch.Frequency;snapshot.DownloadBps=Math.Max(0,(stats.BytesReceived-received)/seconds);snapshot.UploadBps=Math.Max(0,(stats.BytesSent-sent)/seconds);}
                previousNic=selected.Id;received=stats.BytesReceived;sent=stats.BytesSent;networkTicks=now;snapshot.NetworkName=selected.Name;
            }catch{}
        }
        static bool IsVirtualName(string name){return new[]{"virtual","vethernet","tap","tun","vmware","loopback"}.Any(word=>name.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);}
        public void Dispose(){stop.Set();thread.Join(2500);}
    }
}
