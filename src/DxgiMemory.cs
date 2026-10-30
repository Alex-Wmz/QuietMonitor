// Read-only adapter metadata; no D3D device, swap chain, game handle or hook is created.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace QuietMonitor {
    public static class DxgiMemory {
        [StructLayout(LayoutKind.Sequential)]struct Luid{public uint Low;public int High;}
        [StructLayout(LayoutKind.Sequential)]struct OpenAdapter{public Luid Luid;public uint Handle;}
        [StructLayout(LayoutKind.Sequential)]struct CloseAdapter{public uint Handle;}
        [StructLayout(LayoutKind.Sequential)]struct QueryInfo{public uint Handle,Type;public IntPtr Data;public uint Size;}
        [StructLayout(LayoutKind.Sequential)]struct SegmentSizes{public ulong Video,System,Shared;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Description{
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Name;
            public uint Vendor,Device,Subsystem,Revision;
            public UIntPtr DedicatedVideo,DedicatedSystem,SharedSystem;
            public Luid Luid;public uint Flags;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Enumerate(IntPtr factory,uint index,out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetDescription(IntPtr adapter,out Description description);
        [DllImport("dxgi.dll",ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int CreateDXGIFactory1(ref Guid iid,out IntPtr factory);
        [DllImport("gdi32.dll",ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
        [DllImport("gdi32.dll",ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int D3DKMTQueryAdapterInfo(ref QueryInfo info);
        [DllImport("gdi32.dll",ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int D3DKMTCloseAdapter(ref CloseAdapter adapter);
        static T Method<T>(IntPtr instance,int index)where T:class{return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),index*IntPtr.Size),typeof(T)) as T;}
        public static List<Reading> Read(){
            var result=new List<Reading>();IntPtr factory=IntPtr.Zero;Guid iid=new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            try{
                if(CreateDXGIFactory1(ref iid,out factory)<0||factory==IntPtr.Zero)return result;
                Enumerate enumerate=Method<Enumerate>(factory,12);var seen=new HashSet<string>();
                for(uint index=0;index<32;index++){
                    IntPtr adapter;if(enumerate(factory,index,out adapter)<0||adapter==IntPtr.Zero)break;
                    try{Description info;if(Method<GetDescription>(adapter,10)(adapter,out info)<0||(info.Flags&2)!=0)continue;
                        string id="dxgi/"+info.Luid.High.ToString("x8")+info.Luid.Low.ToString("x8")+"/";
                        if(!seen.Add(id))continue;
                        foreach(bool dedicated in new[]{true,false}){ulong bytes=(dedicated?info.DedicatedVideo:info.SharedSystem).ToUInt64();result.Add(new Reading{Id=id+(dedicated?"dedicated-capacity":"shared-capacity"),Name=dedicated?"DXGI Dedicated Memory Capacity":"DXGI Shared Memory Capacity",Hardware=info.Name.Trim(),HardwareKind=info.Vendor==0x1002?"GpuAmd":info.Vendor==0x10de?"GpuNvidia":"GpuIntel",Type="SmallData",Unit="MiB",Value=bytes/1048576.0,Status=bytes>0?"ok":"unavailable",Source="Windows DXGI",Scope=dedicated?"adapter-dedicated-capacity":"adapter-shared-limit",UpdatedUtc=DateTime.UtcNow,PollIntervalMs=30000});}
                        AddSegments(result,info,id);
                    }finally{Marshal.Release(adapter);}
                }
            }catch{}finally{if(factory!=IntPtr.Zero)Marshal.Release(factory);}return result;
        }
        static void AddSegments(List<Reading> readings,Description info,string id){
            var opened=new OpenAdapter{Luid=info.Luid};IntPtr data=IntPtr.Zero;
            try{
                if(D3DKMTOpenAdapterFromLuid(ref opened)!=0||opened.Handle==0)return;
                int size=Marshal.SizeOf(typeof(SegmentSizes));data=Marshal.AllocHGlobal(size);Marshal.StructureToPtr(new SegmentSizes(),data,false);
                var query=new QueryInfo{Handle=opened.Handle,Type=3,Data=data,Size=(uint)size};if(D3DKMTQueryAdapterInfo(ref query)!=0)return;
                SegmentSizes segment=(SegmentSizes)Marshal.PtrToStructure(data,typeof(SegmentSizes));ulong bytes=segment.Video+segment.System;
                if(bytes>0)readings.Add(new Reading{Id=id+"dedicated-segments",Name="D3DKMT Dedicated Memory Capacity",Hardware=info.Name.Trim(),HardwareKind=info.Vendor==0x1002?"GpuAmd":info.Vendor==0x10de?"GpuNvidia":"GpuIntel",Type="SmallData",Unit="MiB",Value=bytes/1048576.0,Status="ok",Source="Windows D3DKMT",Scope="wddm-dedicated-segments",UpdatedUtc=DateTime.UtcNow,PollIntervalMs=30000});
            }catch{}finally{if(data!=IntPtr.Zero)Marshal.FreeHGlobal(data);if(opened.Handle!=0){var close=new CloseAdapter{Handle=opened.Handle};D3DKMTCloseAdapter(ref close);}}
        }
    }
}
