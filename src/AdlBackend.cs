// AMD ADL getter ABI. Published ADL header definitions are MIT licensed; see THIRD_PARTY.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace QuietMonitor {
    public sealed class AdlBackend:IDisposable {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Allocate(int bytes);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] struct Adapter {
            public int Size,Index;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Udid;
            public int Bus,Device,Function,Vendor;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Name;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string DisplayName;
            public int Present,Exist;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string DriverPathEx;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Pnp;
            public int DisplayIndex;
        }
        [StructLayout(LayoutKind.Sequential)]struct NativeValue{public int Supported,Value;}
        [StructLayout(LayoutKind.Sequential)]struct NativeData{
            public int Size;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=256)]public NativeValue[] Values;
        }
        [DllImport("atiadlxx.dll",CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int ADL2_Main_Control_Create(Allocate allocate,int connected,ref IntPtr context);
        [DllImport("atiadlxx.dll",CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int ADL2_Main_Control_Destroy(IntPtr context);
        [DllImport("atiadlxx.dll",CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context,ref int count);
        [DllImport("atiadlxx.dll",CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context,IntPtr info,int size);
        [DllImport("atiadlxx.dll",CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int ADL2_New_QueryPMLogData_Get(IntPtr context,int adapter,ref NativeData data);
        IntPtr context;
        readonly Allocate allocate=Marshal.AllocHGlobal;
        readonly List<Adapter> adapters=new List<Adapter>();
        static readonly int[] Ids={1,8,14,17,19,23,24,27,28,29,30};
        static readonly string[] Names={"GPU Clock","GPU Temperature","GPU Fan","SoC Power","GPU Usage","ASIC Power","VR SoC Temperature","GPU Hot Spot","GFX Temperature","SoC Temperature","GFX Power"};
        static readonly string[] Types={"Clock","Temperature","Fan","Power","Load","Power","Temperature","Temperature","Temperature","Temperature","Power"};
        static readonly string[] Units={"MHz","°C","RPM","W","%","W","°C","°C","°C","°C","W"};
        public AdlBackend(){
            try{
                Check(ADL2_Main_Control_Create(allocate,1,ref context));int count=0;Check(ADL2_Adapter_NumberOfAdapters_Get(context,ref count));
                if(count<1||count>64)throw new InvalidOperationException("ADL adapter count");
                int element=Marshal.SizeOf(typeof(Adapter));IntPtr buffer=Marshal.AllocHGlobal(element*count);
                try{
                    Check(ADL2_Adapter_AdapterInfo_Get(context,buffer,element*count));var seen=new HashSet<string>();
                    for(int i=0;i<count;i++){
                        Adapter item=(Adapter)Marshal.PtrToStructure(IntPtr.Add(buffer,i*element),typeof(Adapter));
                        if(item.Name==null||item.Pnp==null||item.Pnp.IndexOf("VEN_1002",StringComparison.OrdinalIgnoreCase)<0)continue;
                        string key=item.Bus+":"+item.Device+":"+item.Function;
                        if(seen.Add(key))adapters.Add(item);
                    }
                }finally{Marshal.FreeHGlobal(buffer);}
            }catch{Dispose();throw;}
        }
        static void Check(int status){if(status!=0)throw new InvalidOperationException("AMD ADL status "+status);}
        public List<Reading> Read(){
            var readings=new List<Reading>();DateTime now=DateTime.UtcNow;
            foreach(Adapter adapter in adapters){
                NativeData data=new NativeData{Values=new NativeValue[256]};int status=ADL2_New_QueryPMLogData_Get(context,adapter.Index,ref data);
                Match device=Regex.Match(adapter.Pnp,"DEV_([0-9A-F]{4})",RegexOptions.IgnoreCase);
                string key="adl/1002/"+(device.Success?device.Groups[1].Value:"unknown")+"/"+adapter.Bus+":"+adapter.Device+"."+adapter.Function+"/";
                for(int i=0;i<Ids.Length;i++){
                    NativeValue field=data.Values[Ids[i]];bool supported=status==0&&field.Supported!=0;
                    string scope=Ids[i]==30?"gfx-driver":Ids[i]==17?"soc-driver":Ids[i]==23?"asic-driver":"";
                    double? value=supported?(double?)field.Value:null;
                    string validity=supported?"ok":status==0?"unsupported":"read-failed";
                    if(value.HasValue&&Types[i]=="Temperature"&&(value.Value<=0||value.Value>150)){validity="invalid";value=null;}
                    readings.Add(new Reading{Id=key+Ids[i],Name=Names[i],Hardware=adapter.Name.Trim(),HardwareKind="GpuAmd",Type=Types[i],Unit=Units[i],Value=value,Status=validity,Source="AMD ADL",Scope=scope,UpdatedUtc=now});
                }
            }
            return readings;
        }
        public void Dispose(){if(context!=IntPtr.Zero){ADL2_Main_Control_Destroy(context);context=IntPtr.Zero;}GC.KeepAlive(allocate);}
    }
}
