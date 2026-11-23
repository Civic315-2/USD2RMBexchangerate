# 中行美元牌价 v12

在 Windows 上解压后，用 PowerShell 运行 .\build.ps1。生成的「中行美元牌价.exe」位于同一目录。构建使用 Windows 自带的 .NET Framework C# 编译器。

- RateStrip.cs 包含抓取、报价显示、托盘菜单及开机启动逻辑。
- boc.ico 是构建使用的多尺寸图标。
- DrawIcon.cs 是图标绘制源代码。它生成当前目录下 work\boc.ico，运行前需有 work 目录。日常构建直接使用随包提供的 boc.ico。
<img width="560" height="281" alt="2187b469da2b99ef0683c9f12e2b97a0" src="https://github.com/user-attachments/assets/de90b525-138b-46f9-ba5a-ad19e0ccfbbd" />
