# Third-party notices

`TrayTemps.exe` is our own code (MIT). The sensor libraries are **not** part of this
repository: they are taken from the upstream [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
release (`v0.9.6`, asset `LibreHardwareMonitor.zip`) when building and are redistributed
inside the release archive as-is, with this notice.

| Component | License | Project |
|---|---|---|
| LibreHardwareMonitorLib | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit, RAMSPDToolkit-NDD, BlackSharp.Core, Aga.Controls | as published by the LibreHardwareMonitor release | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| OxyPlot / OxyPlot.WindowsForms | MIT | https://github.com/oxyplot/oxyplot |
| HidSharp | Apache-2.0 | https://github.com/IntergatedCircuits/HidSharp |
| Microsoft.Bcl.AsyncInterfaces, Microsoft.Bcl.HashCode | MIT | https://github.com/dotnet/runtime |
| Microsoft.Win32.TaskScheduler | MIT | https://github.com/dahall/TaskScheduler |
| System.Buffers, System.IO.Pipelines, System.Memory, System.Numerics.Vectors, System.Reflection.Metadata | MIT | https://github.com/dotnet/runtime |
| System.CodeDom, System.Collections.Immutable, System.Formats.Nrbf, System.Resources.Extensions | MIT | https://github.com/dotnet/runtime |
| System.Runtime.CompilerServices.Unsafe, System.Threading.Tasks.Extensions, System.Threading.AccessControl | MIT | https://github.com/dotnet/runtime |
| System.Security.AccessControl, System.Security.Principal.Windows | MIT | https://github.com/dotnet/runtime |
| System.Text.Encodings.Web, System.Text.Json | MIT | https://github.com/dotnet/runtime |

The full license texts are available in the corresponding upstream projects.

LibreHardwareMonitor is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR
PURPOSE. See the MPL-2.0 for details: https://www.mozilla.org/en-US/MPL/2.0/

Reading CPU and motherboard sensors requires the PawnIO kernel driver, which is installed
by LibreHardwareMonitor (or by the user) and is not bundled in this repository.
