# Craft Unified Booter (CUB)

方块筑界出品的 Minecraft Java 版统一启动器（Windows 桌面端），基于 .NET 8 + WPF 开发。

## 功能特性

- **版本管理**：整合原版 / Forge / Fabric / NeoForge / Quilt 等，一键下载与安装，内置 BMCLAPI 国内镜像加速
- **账户系统**：支持离线账户与微软正版账户登录
- **联机服务**：集成红石联机，快速创建 / 加入房间
- **资源生态**：整合包、模组、光影、资源包、数据包的检索与下载（Modrinth 等来源），模组中文名翻译
- **个性化**：官方皮肤库、界面主题与个性化设置
- **辅助功能**：AI 对话助手、托盘常驻、下载管理、启动状态面板、自我修复与更新

## 构建

环境要求：

- Windows 10 / 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022（可选，需带“.NET 桌面开发”工作负载）

命令行构建：

```powershell
# 调试构建
dotnet build CraftUnifiedBooter.csproj -c Debug

# 发布单文件（框架依赖，目标机器需安装 .NET 8 Desktop Runtime）
dotnet publish CraftUnifiedBooter.csproj -c Release
```

## 目录结构

```
Assets/      图标等静态资源
Controls/    自定义控件
Pages/       各功能页面（XAML + 代码后置）
Resources/   模组中文名等数据资源
Services/    核心服务（下载、启动、账户、联机等）
Skins/       内置皮肤资源
Themes/      样式与图标资源
```

## 声明

- 本项目为第三方启动器，与 Mojang Studios / Microsoft 无任何隶属或合作关系。
- Minecraft 是 Mojang Studios 的商标，游戏本体版权归其所有，本项目不附带任何游戏本体文件。
- 请勿使用本项目从事任何违反 Minecraft 最终用户许可协议（EULA）的行为。

## 使用许可

本项目**不是开源软件**。源码公开仅为阅读与技术交流之便，采用自定义的「仅限借鉴」许可协议：

- **允许**：阅读、浏览源码；为学习研究之目的借鉴其设计思路与实现方法，并在自己独立完成的原创作品中体现。
- **禁止**：复制、下载、克隆（clone / fork）、转载、镜像；运行、部署、使用本作品；修改、改编、创作衍生作品；打包嵌入或整合到其他项目；任何商业用途。

详见 [LICENSE](LICENSE)。