<img src="icon.png" width="88" align="right" alt="墨白图标" />

# 墨白 · InkWhite

**仅适配 ClassIsland 2.1.0.1。插件代码由 GPT 生成。**

让课堂组件，随背景清晰呈现。

ClassIsland 插件。背景稳定后，文字、天气图标和分割线会缓慢统一变为黑色或白色，减少频繁变色带来的干扰。

- 默认背景稳定 **3 秒**后，用 **2 秒**完成渐变，时间可调。
- 深色背景用白字，中灰、浅灰和浅色背景用黑字。
- 描边 / 柔光独立开关，**默认关闭**。

**搭配建议：配合 ClassIsland 的 Liquid Glass 主题并开启高透明度，效果更好。**

## 安装

在仓库的 **Releases** 下载 `.cipx`，打开 ClassIsland → 设置 → 插件 → 从本地安装，安装后重启。

适配 **ClassIsland 2.1.0.1 / Windows 10 2004+ 或 Windows 11**。其他版本需另行验证。

> 启用期间，ClassIsland 主窗口可能不会出现在截图、录屏或屏幕共享中；停用插件功能后恢复。自定义位图图标、自绘文字暂不支持。

## 构建

安装 .NET 8 SDK 后，在 PowerShell 中运行：

```powershell
./build.ps1
```

输出 `InkWhite-0.1.7-ci2.1.0.1.cipx`。项目保留原插件 ID，旧版用户可直接更新。

作者：[苏泊林 / subolin](https://github.com/subolin)

QQ：1570130637 · 邮箱：[subolinChina@outlook.com](mailto:subolinChina@outlook.com)

