<p align="center"><img src="assets/cover.svg" alt="QuietMonitor — 安静观察，按需提醒" width="100%"></p>

# QuietMonitor

轻量 Windows 桌面硬件监测与久坐提醒。桌面面板、顶部横条可以切换；
把支持的传感器读数放在容易看见的位置，并在设置的时间提醒休息。

**0.4.1 · 预发布 · Windows 10/11 x64 · 源码可见／非商业许可**

本项目不注入游戏、不挂钩图形 API，不提供超频、调速或风扇控制。
未知、无权限与缺少传感器的数据保留相应状态，不用零值代替。

## 下载与试用

到 [Releases](https://github.com/Alex-Wmz/QuietMonitor/releases) 下载
`QuietMonitor-v0.4.1-win-x64.zip`，完整解压后运行 `QuietMonitor.exe`。
需要 .NET Framework 4.8；旁边的 DLL、配置和许可文件须一并保留。
程序未做代码签名；请核对发布页与 SHA-256，不通过关闭安全软件来绕过警告。

首次使用先打开设置、检查数据来源，再按需选择显示项。需要部分 CPU/主板数据时，
可由本人决定是否以管理员权限重启；某些硬件需要单独安装上游 PawnIO。
本包不包含驱动安装器，不自动安装或启动第三方驱动。

## 已有功能

- 桌面面板与顶部横条、拖动和贴边、锁定穿透、置顶和字体设置。
- CPU、GPU、内存与磁盘的已支持数据；GPU 核心、热点及显存温度分别表达。
- 可选久坐提醒：默认关闭；全屏暂缓、退出后补提醒，确认后重新计时。
- 默认 1 Hz 核心采样；内存快照、显式保存设置，不做周期遥测或自动更新检查。

| 快捷键 | 功能 |
| --- | --- |
| Ctrl+Alt+M | 显示／隐藏 |
| Ctrl+Alt+B | 切换布局 |
| Ctrl+Alt+L | 锁定／解除穿透 |
| Ctrl+Alt+S | 打开设置 |

采集支持取决于硬件、驱动和权限。普通置顶窗不能保证覆盖所有独占全屏或反作弊环境。
目前没有 FPS、帧时间或游戏自动识别。休息提醒根据时间触发，不识别真实坐姿或离席。

## 从源码构建

本机需要 Windows PowerShell 和 .NET Framework 4.x x64 编译器。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Package
```

首次构建从官方发布位置下载锁定依赖，并校验归档及逐个 DLL 的 SHA-256。
构建默认运行核心合成测试，发布包按明确文件清单生成；不打包整个工作目录。
使用本地已下载依赖时可传 `-DependencyArchive`。校验记录见 [验证范围](docs/VALIDATION.md)。

## 许可、隐私与反馈

本仓库自有应用代码采用 [PolyForm Noncommercial 1.0.0](LICENSE)。
符合许可的非商业用途免费；商业使用需另行书面授权，见 [商业授权导读](COMMERCIAL_LICENSE.md)。
这是源码可见许可，不是 OSI 定义的开源。第三方 DLL 与 ABI 声明保留原许可，
见 [THIRD_PARTY.md](THIRD_PARTY.md)；本项目不会收回上游或旧许可已经赋予的权利。

设置和硬件导出只保存在本机。不要把真实配置、硬件标识或诊断原文发到公开 Issue。
普通问题可在 Issues 留下合成复现；安全问题使用 [私密报告](SECURITY.md)。

需求、功能取舍、交互和验收由维护者持续核对，代码实现使用 AI 辅助。
本仓库为经过整理的公开发布版本，不包含维护者的私人配置、历史接力资料或真实测量样本。
