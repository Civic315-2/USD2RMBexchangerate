# 中行美元牌价 v12

在 Windows 上解压后，用 PowerShell 运行 .\build.ps1。生成的「中行美元牌价.exe」位于同一目录。构建使用 Windows 自带的 .NET Framework C# 编译器。

- RateStrip.cs 包含抓取、报价显示、托盘菜单及开机启动逻辑。
- boc.ico 是构建使用的多尺寸图标。
- DrawIcon.cs 是图标绘制源代码。它生成当前目录下 work\boc.ico，运行前需有 work 目录。日常构建直接使用随包提供的 boc.ico。
