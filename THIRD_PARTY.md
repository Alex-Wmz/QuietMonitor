# Third-party components

QuietMonitor's original application source in this public release is licensed under PolyForm Noncommercial 1.0.0; see LICENSE and NOTICE. Earlier valid licenses remain unaffected. The portable package contains unmodified DLLs extracted from the official LibreHardwareMonitor v0.9.6 .NET Framework release. Each file and the original archive are SHA-256 pinned in `dependencies.lock.json`. No GamePP code, AMD driver DLL, ADLX SDK or PawnIO installer is redistributed.

| Component | Version | License / source |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0, [source at release commit](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701) |
| BlackSharp.Core | 1.0.7 | MPL-2.0, [source](https://github.com/Blacktempel/BlackSharp/tree/1.0.7) |
| DiskInfoToolkit | 1.1.2 | MPL-2.0, [source](https://github.com/Blacktempel/DiskInfoToolkit/tree/1.1.2) |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0, [source](https://github.com/Blacktempel/RAMSPDToolkit/tree/1.4.2) |
| HidSharp | 2.6.4 | Apache-2.0 with upstream notices, [package and project](https://www.nuget.org/packages/HidSharp/2.6.4) |
| Microsoft support assemblies | see hash lock | MIT and third-party notices, [.NET runtime source](https://github.com/dotnet/runtime), [NuGet package source links](https://www.nuget.org/profiles/Microsoft) |
| PawnIO modules embedded by LHM | as shipped by LHM 0.9.6 | LGPL-2.1; included in LHM upstream notices; [module source](https://github.com/namazso/PawnIO.Modules) |
| AMD ADL header ABI definitions | commit 84c881cb2136f04287eb3af12de00a9e2867aa9e | MIT, [ADL SDK source](https://github.com/GPUOpen-LibrariesAndSDKs/display-library/tree/84c881cb2136f04287eb3af12de00a9e2867aa9e) |

`third-party/` includes upstream license texts and notices. The official LHM notice includes an Aga.Controls notice even though the Aga.Controls GUI DLL is not part of this package. Libraries remain separate replaceable DLLs; source and build instructions for this application are included in the repository. The above source links provide the corresponding upstream projects and release versions. The application does not restrict reverse engineering for debugging modifications to LGPL components.

Microsoft DLLs in this build: Microsoft.Bcl.AsyncInterfaces, Microsoft.Bcl.HashCode, System.Buffers, System.CodeDom, System.Collections.Immutable, System.Formats.Nrbf, System.IO.Pipelines, System.Memory, System.Numerics.Vectors, System.Reflection.Metadata, System.Resources.Extensions, System.Runtime.CompilerServices.Unsafe, System.Security.AccessControl, System.Security.Principal.Windows, System.Text.Encodings.Web, System.Text.Json, System.Threading.AccessControl, System.Threading.Tasks.Extensions. They are taken from the locked LHM release, not from the development machine's installed runtime.

The AMD ADL import definitions are implemented in `src/AdlBackend.cs`; the native AMD DLL is resolved from the installed system display driver. Its binary is not copied into the release. Installing PawnIO, if needed, is a separate explicit user action and is subject to its own upstream license and system requirements.
