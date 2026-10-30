using System;
using System.Runtime.InteropServices;

namespace QuietMonitor {
    // One persistent, language-neutral Windows counter. No WMI loop or log session.
    public sealed class WindowsCpuLoad:IDisposable {
        [StructLayout(LayoutKind.Explicit,Size=16)]struct CounterValue {
            [FieldOffset(0)]public uint Status;
            [FieldOffset(8)]public double Value;
        }
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint PdhOpenQuery(string source,UIntPtr user,out IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr user,out IntPtr counter);
        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out CounterValue value);
        [DllImport("pdh.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint PdhCloseQuery(IntPtr query);
        IntPtr query,counter;bool primed;DateTime lastAttempt=DateTime.MinValue;
        public Reading Read(string cpu,int intervalMs,DateTime now){
            var r=new Reading{Id="windows/cpu/utility",Name="CPU Utility (Windows)",Hardware=cpu,HardwareKind="Cpu",Type="Load",Unit="%",Source="Windows PDH",Scope="processor-utility-capped-at-100",Status="unavailable",UpdatedUtc=now,PollIntervalMs=intervalMs};
            try{
                if(query==IntPtr.Zero){
                    if((now-lastAttempt).TotalSeconds<60)return r;lastAttempt=now;
                    if(PdhOpenQuery(null,UIntPtr.Zero,out query)!=0||PdhAddEnglishCounter(query,@"\Processor Information(_Total)\% Processor Utility",UIntPtr.Zero,out counter)!=0){Dispose();return r;}
                }
                if(PdhCollectQueryData(query)!=0){r.Status="read-failed";primed=false;return r;}
                if(!primed){primed=true;r.Status="warming-up";return r;}
                uint type;CounterValue value;
                if(PdhGetFormattedCounterValue(counter,0x200,out type,out value)!=0||value.Status>1){r.Status="read-failed";return r;}
                if(Double.IsNaN(value.Value)||Double.IsInfinity(value.Value)||value.Value<0||value.Value>100){r.Status="invalid";return r;}
                r.Value=value.Value;r.Status="ok";return r;
            }catch{Dispose();r.Status="read-failed";return r;}
        }
        public void Dispose(){if(query!=IntPtr.Zero)PdhCloseQuery(query);query=counter=IntPtr.Zero;primed=false;}
    }
}
