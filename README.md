# dualsense-bt-haptics

> 🎮 让 DualSense（PS5 手柄）在 **蓝牙模式下启用音圈马达（Haptics）震动反馈** 的工具。

## 当前问题
1. 能感知到延迟（大约200ms? 可能是虚拟音频设备影响的）
2. 代码可读性与健壮性待提升

## 简介

本项目旨在解决 DualSense 手柄在通过 **蓝牙连接 Windows 时无法使用高级触觉反馈（Haptics）** 的问题。通过结合虚拟手柄模拟与真实蓝牙指令注入，实现在支持的游戏和应用中启用 DualSense 独特的自适应扳机与音圈震动效果。

- ✅ 蓝牙连接下启用 Haptics 震动
- ✅ 无需有线连接或额外硬件
- ⚠️ 需要安装 [ViGEmBus (Fork)](https://github.com/awalol/ViGEmBus/tree/simple_ds5_support) 驱动（只做了 DualSense5 HID的支持，在`dev_try2`分支正在努力实现USBAudio）

---

## 工作原理

1. 使用 [ViGEmBus](https://github.com/awalol/ViGEmBus/tree/simple_ds5_support) 创建一个虚拟的 DualShock 5 控制器，供游戏识别。
2. 同时监听真实 DualSense 手柄的输入，将报文转发给虚拟手柄。
3. 创建虚拟音频设备，接收来自游戏的震动音频流
4. 基于 [egormanga/SAxense](https://github.com/egormanga/SAxense) 逆向分析出的 **蓝牙 Haptics 控制报文**，向真实手柄推送音频流。

## 使用教程
1. 创建个虚拟的音频设备，设置为 4 通道 48KHz 24bit （当然其他也可以，主要是为了减少杂音），我个人感觉使用 Steam Streaming 的虚拟设备效果最佳（这设备怎么装上去的不记得了）
2. 把设备名称改成 `DualSense Wireless Controller`
3. 启动 dualsense-bt-haptics.exe，之后正常进游戏即可
4. 您可能需要搭配 hidhide 避免重复输入，当然你也可以尝试修改代码禁用虚拟手柄的创建，看看是否正常工作。因为我在原神测试的时候，蓝牙手柄被其他应用占用以后游戏内就无法正常工作了

目前项目还处于开发阶段，您需要自行编译 VIGemBus 驱动并安装，然后将编译出来的 ViGEmClient.dll 复制到程序的运行目录

---

## 致谢

- 感谢 [@nefarius](https://github.com/nefarius) 开发 [ViGEmBus](https://github.com/nefarius/ViGEmBus) —— 虚拟手柄模拟的基石。
- 感谢 [@egormanga](https://github.com/egormanga) 在 [SAxense](https://github.com/egormanga/SAxense) 项目中逆向并公开了 **DualSense 蓝牙 Haptics 报文格式**，使本项目成为可能。

---

## 免责声明

本软件仅供学习与研究用途。使用本工具可能违反某些平台的服务条款。作者不对因使用本软件导致的任何设备损坏、系统不稳定或账号封禁承担责任。

---

## 贡献 & 问题反馈

欢迎提交 Issue 或 Pull Request！  
如遇问题，请提供：
- Windows 版本
- DualSense 固件版本
- 是否已正确安装 ViGEmBus
- 日志输出（如有）

---

## License

MIT License © 2026 awalol
