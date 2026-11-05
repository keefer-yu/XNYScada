# XNY SCADA

基于 .NET 8 的新能源单机 SCADA。设备通讯、流程、权限和生产业务放在与界面无关的类库中，WPF 与 WinForms 共用同一套 ViewModel。机型差异通过 `configs/machine-templates` 中的模板描述。

## 运行

需要 .NET 8 SDK。同一台机器上 WPF 与 WinForms 不能同时运行，二者共用单实例互斥量。

```powershell
dotnet run --project src/XNY.Scada.Wpf
dotnet run --project src/XNY.Scada.WinForms
dotnet test tests/XNY.Scada.Tests
```

首次启动会创建 SQLite 数据库 `data/scada.db`，并写入超级管理员 `admin`。默认密码来自 `configs/appsettings.json` 的 `Security:DefaultAdminPassword`（出厂值为 `Admin@123`）。数据库里还没有该用户时才会创建，请在现场改掉默认密码。

## 发布

```powershell
./build/publish.ps1
./build/publish.ps1 -Ui Wpf -SelfContained
./build/publish.ps1 -SkipTests -Installer
```

安装包需要本机已安装 Inno Setup 6。

## 文档

详细说明在 [docs](docs/README.md)：

- [项目概述](docs/项目概述.md)
- [架构与模块](docs/架构与模块.md)
- [配置与运行](docs/配置与运行.md)
- [流程引擎](docs/流程引擎.md)
- [数据与权限](docs/数据与权限.md)
