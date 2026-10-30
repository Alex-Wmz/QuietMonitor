using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using QuietMonitor;
static class LayoutPreview {
    static void Add(Snapshot s,string id,string name,string kind,string type,double value,string unit,string scope=""){
        s.Readings.Add(new Reading{Id=id,Name=name,Hardware=kind=="Cpu"?"Sample CPU":"Sample GPU",HardwareKind=kind,Type=type,Value=value,Unit=unit,Source="Fixture",Scope=scope,Status="ok",UpdatedUtc=s.Utc});
    }
    static int Main(string[] args){
        string directory=args[0];Directory.CreateDirectory(directory);var s=new Snapshot{Utc=DateTime.UtcNow,CpuName="Sample CPU",GpuName="Sample GPU",MemoryUsedGiB=22.5,MemoryTotalGiB=31.2,DownloadBps=28000,UploadBps=420000};
        Add(s,"c-load","CPU Total","Cpu","Load",5,"%");Add(s,"c-clock","Cores (Average)","Cpu","Clock",3718,"MHz");Add(s,"c-eff","Cores (Average Effective)","Cpu","Clock",205,"MHz");Add(s,"c-temp","Core (Tctl/Tdie)","Cpu","Temperature",64.9,"°C");Add(s,"c-power","Package","Cpu","Power",15.3,"W");
        Add(s,"g-load","GPU Core","GpuAmd","Load",7,"%");Add(s,"g-clock","GPU Core","GpuAmd","Clock",800,"MHz");Add(s,"g-temp","GFX Temperature","GpuAmd","Temperature",63,"°C");Add(s,"g-power","GFX Power","GpuAmd","Power",8,"W","gfx-driver");Add(s,"g-used","D3D Dedicated Memory Used","GpuAmd","SmallData",420.9,"MiB");Add(s,"g-total","D3D Dedicated Memory Total","GpuAmd","SmallData",475.2,"MiB");
        foreach(int size in new[]{9,11}){var c=new AppConfig{FontSize=size};using(Bitmap bitmap=OverlayPainter.Paint(OverlayPainter.Build(s,c),c,1.25f,1500,1000,0))bitmap.Save(Path.Combine(directory,"desktop-"+size+"pt.png"),ImageFormat.Png);}
        var extra=new AppConfig{FontSize=9};extra.ExtraSensorIds.Add("c-eff");extra.ExtraLabels["c-eff"]="平均有效频率";using(Bitmap bitmap=OverlayPainter.Paint(OverlayPainter.Build(s,extra),extra,1.25f,1500,1000,0))bitmap.Save(Path.Combine(directory,"desktop-with-extra.png"),ImageFormat.Png);
        Add(s,"/memory/0/temperature/0","DIMM #0","Memory","Temperature",56.5,"°C");Add(s,"/memory/1/temperature/0","DIMM #1","Memory","Temperature",51,"°C");
        Add(s,"/nvme/1/temperature/0","Composite Temperature","Storage","Temperature",40,"°C");Add(s,"/nvme/0/temperature/0","Composite Temperature","Storage","Temperature",47,"°C");
        foreach(Reading r in s.Readings)if(r.HardwareKind=="Storage")r.DriveLetters=r.Id.StartsWith("/nvme/0/")?"C:":"D:";
        var rounded=new AppConfig{FontSize=9,RoundNumbers=true,TaskManagerVramCapacity=true,EnableMemory=true,EnableStorage=true};using(Bitmap bitmap=OverlayPainter.Paint(OverlayPainter.Build(s,rounded),rounded,1.25f,1500,1000,0))bitmap.Save(Path.Combine(directory,"desktop-integers.png"),ImageFormat.Png);
        return 0;
    }
}
