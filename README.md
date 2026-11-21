# VirtualStickPro（TouchKeys）

VirtualStickPro 是一款面向 Windows 触屏设备的屏幕虚拟摇杆与自定义按键工具。它将触屏上的摇杆和按钮操作映射为键盘输入，适合需要触屏操作的游戏及应用。

支持拖动和缩放摇杆、按钮，添加并配置自定义按键，通过系统托盘控制显示与开机启动，并使用快捷键快速切换控件显示。覆盖控件之外的屏幕区域保持透明穿透，方便在目标应用上方使用。

无驱动、无全局钩子、无网络、无进程注入。开机自启动通过 Windows 启动文件夹快捷方式实现。

---

## 下载

[VirtualStickPro V0.3（Windows 压缩包）](VirtualStickProV0.3.zip)包含程序、配置、启动脚本和源码。

---

## 快速开始

双击 **`启动.bat`**，或直接运行 `TouchKeys.exe`。

屏幕上会出现：左下角摇杆和右侧若干圆形按键。程序也会显示在 Windows 通知区域（系统托盘）。摇杆外圈始终隐藏。

| 操作 | 做法 |
| --- | --- |
| 切换 编辑/游玩 模式 | **Ctrl + Alt + F2** |
| 隐藏/显示虚拟按钮 | **Ctrl + Alt + F3**（托盘菜单可修改） |
| 移动控件 | 进编辑模式，直接拖动；松开后自动保存 |
| 缩放控件 | 编辑模式下，在控件上滚动鼠标滚轮 |
| 加新按键 | 编辑模式下空白处 **右键** → 在这里添加按键 |
| 改按键 / 改显示文字 | 编辑模式下右键按钮 → 设置按键 |
| 改摇杆方向 | 编辑模式下右键摇杆 → 设置摇杆映射 |
| 调整体透明度 | 右键 → 设置透明度，或改 ini 的 `opacity` |
| 保存并重启 | 编辑模式下右键 → 保存配置并重启 |
| 设置自启动 / 新增按键 / 退出 | 右键点击通知区域里的 TouchKeys 图标 |

**游玩模式下**：空白区域点下去直接传到游戏，一个像素都不挡。

---

## 锁定按钮（一直前进）

摇杆正上方有个小锁扣。

| 动作 | 结果 |
| --- | --- |
| **未锁定时直接点击**锁定按钮 | **无效**（防误触，符合设计） |
| 把摇杆**往上拖到**锁扣上 | 🔒 锁定，持续发送 `keyLock`（默认 W） |
| 锁定后**松开手指** | 保持锁定，一直前进 |
| 锁定后点击绿色锁扣，或再**按住摇杆任意位置** | 解除锁定，摇杆回到中心 |

锁扣点按带缩放回弹动画。锁定后摇杆整体泛绿、锁扣变绿，一眼能看出当前状态。

---

## 配置（`touchkeys.ini`）

纯文本，记事本能直接改。改完右键 →「重新载入」，不用重启。

### 摇杆

```ini
[stick]
cx=200          ; 圆心 X
cy=700          ; 圆心 Y
radius=130      ; 半径
deadzone=0.28   ; 死区，越大越防误触
keyUp=W
keyDown=S
keyLeft=A
keyRight=D
```

斜向推会**同时按下两个键**（左上 = W+A），这是游戏需要的正常行为。

### 锁定按钮

```ini
[stick]
keyLock=W        ; 锁定后持续发送的键（W = 一直前进）
lockRadius=34    ; 锁扣半径
lockGap=26       ; 锁扣与摇杆的间距
```

### 自定义按键

想加几个就复制几段：

```ini
[button.0]
x=1100        ; 左上角 X
y=700         ; 左上角 Y
w=110         ; 宽
h=110         ; 高
label=跳       ; 显示的文字（中文/符号/emoji 都行）
key=SPACE     ; 按下去发送的键
```

### 全局透明度

```ini
[app]
opacity=150     ; 30-255，越小越透
```

### 支持的按键名

```
A-Z   0-9   F1-F12
SPACE ENTER ESC TAB BACK DELETE INSERT HOME END
UP DOWN LEFT RIGHT PAGEUP PAGEDOWN
SHIFT CTRL ALT  LSHIFT RSHIFT LCTRL RCTRL LALT RALT
CAPSLOCK  NUM0-NUM9  MULTIPLY ADD SUBTRACT DECIMAL DIVIDE
```

也可以直接写虚拟键码：`key=0x41`

---

## 摇杆手感

- **大圈只在按下时变亮**，平时只是一道淡淡的轮廓，不抢视线
- **小圈（摇杆帽）很小**，跟着手指走
- **小圈圆心永远被限制在大圈内** —— 手指拖出去再远，方向仍然有效，
  只是幅度封顶（不会"跑出圈外"）
- 按下的瞬间记录抓取点，所以按在边缘也不会突然弹满，手感是渐进的
- **斜向自动双键**：左上 = W+A，右下 = S+D，切换方向时旧键会自动松开

### 多指同时触发

- 摇杆占用一根手指，按钮可以同时按**多个**
- 同一个按钮被两根手指按住时，用引用计数管理，一根抬起不会误释放另一个
- 摇杆推杆 + 按钮同时按下，两个键会同时生效

**崩溃修复（重要）**：早期版本调用 `GetPointerType` 时报
`NullReferenceException` 直接崩溃。原因是 P/Invoke 到某些 API 在本机解析失败。
现在改为统一走 `GetPointerInfo`（已确认本机存在），并且：

- 整个 `HandlePointer` 包在 try/catch 里
- 加了 `Application.ThreadException` + `AppDomain.UnhandledException` 全局兜底
- **任何输入处理失败都不会再弹出 .NET 崩溃对话框**

`tests/PointerApiCheck.exe` 可以检查本机哪些指针 API 真实存在。

**事件来源用了双通道**：

1. `WM_POINTER`（Windows 8+）—— 每根手指有独立的 pointerId，最可靠
2. `WM_TOUCH`（后备）—— 老设备的兼容路径

**关键修正**：早期版本用"1.2 秒内发生过触摸就压掉所有鼠标事件"来过滤合成鼠标，
结果把第二根手指也一起压掉了。现在改成**要求时间 <400ms 且位置 <20px 重合**
才判定为合成事件，真正的多指输入不再被误伤。

**关于 SetCapture**：早期用 `SetCapture` 保证能收到抬起事件，但它会把所有
指针都揽到这个窗口，是多指串扰的元凶之一，已全部移除。

### 方向判定用「轴投影」而不是「角度扇区」

这是踩过的坑：角度扇区在 45° 斜向时正好落在 22.5° / 157.5° 的边界上，
稍一偏移就只剩一个方向（实测 -135° 只出了 W，A 被吃掉）。

改成看单位方向向量在 x / y 上的分量，各自跟一个阈值（0.30）比较 ——
斜向天然同时超过两个轴，两个键都会按下。

## 四个关键设计

### 1. 为什么不抢焦点 —— `WS_EX_NOACTIVATE`

窗口必须带 `WS_EX_NOACTIVATE` 扩展样式。少了它，一按摇杆焦点就被抢走，
游戏立刻切到后台，表现就是「按下摇杆画面跳到桌面/别的界面」。

配合 `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE` 双保险。

实测（前台开记事本，按住摇杆拖动）：

```
拖动前前台: [Notepad] "无标题 - Notepad"
拖动中前台: [Notepad] "*w - Notepad"     ← 标题多了 *w，W 键打进去了
释放后前台: [Notepad] "*w - Notepad"
★ 前台始终是记事本，没有被抢走
```

### 2. 空白区域为什么不会挡触摸

**分层窗口 + UpdateLayeredWindow 逐像素 alpha**：

- 画了控件的地方 alpha > 0 → Windows 认为"有东西"，点击交给程序
- 没画东西的地方 alpha = 0 → 直接穿透给下层窗口（游戏）

实测：

| | 占比 |
| --- | --- |
| 完全透明（穿透） | 93.41% |
| 有内容（吃触摸） | 6.59% |

命中测试：

```
(   5,   5) → Chrome     ✅ 穿透
( 768, 480) → Chrome     ✅ 穿透
( 200, 700) → TouchKeys  ✅ 摇杆位置，挡住
```

### 3. 为什么必须用 CreateDIBSection

`Bitmap.GetHbitmap()` **会丢掉 alpha 通道**，整屏变成不透明，
空白处再也穿不过去。必须走 `CreateDIBSection` 并**手工预乘 alpha**
（`RGB *= A/255`），因为 `UpdateLayeredWindow` 要的是预乘格式。

### 4. 为什么故意不设 DPI 感知

实测 125% 缩放的机器：

| 模式 | GetSystemMetrics | 窗口实际 |
| --- | --- | --- |
| 不设 DPI 感知 | 1536×960 | 1536×960 ✅ 一致 |
| 设 System DPI Aware | **1920×1200** | 1536×960 ❌ 错位 |

DPI 感知一开，画布比窗口大，右下角被裁掉 —— **那一片 alpha=0，摇杆直接失灵**。
所以程序**故意不设**。双显卡笔记本（物理 3840×2160 + 250% 缩放）尤其要注意。

---

## 性能（多指同时操作不卡）

早期版本每收到一条触点消息就整屏重绘一次，多指时消息翻倍直接卡死。
现在做了四层优化：

| 优化 | 效果 |
| --- | --- |
| **脏矩形** | 摇杆动只重画摇杆那块（约 290×290），按钮动只重画那个按钮 |
| **重绘节流** | 消息进来只标记"待重绘"，由 16ms 计时器合并刷新（~60fps） |
| **复用位图** | 画布 `Bitmap` 只创建一次，不再每帧 new 1536×960 |
| **复用 GDI 资源** | DIB 段 / DC / HBITMAP 全部持久化，不再每帧创建销毁 |
| **预乘用指针** | `unsafe` 逐像素，只算脏区，比数组下标快 |

实测（连续拖动摇杆 3 秒，约 185 次移动）：

```
优化前：CPU 35.3%   每帧 6.62 ms
优化后：CPU 10.8%   每帧 1.94 ms      ← 快 3.4 倍
```

`tests/PerfTest.exe` 可以随时复测。

**注意**：编译需要 `/unsafe` 开关（因为预乘那段用了指针）：

```bat
csc /unsafe /target:winexe /out:TouchKeys.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    TouchKeys.cs
```

## 自己编译

不需要 Visual Studio，Windows 自带的 csc 就够：

```bat
csc /target:winexe /out:TouchKeys.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    TouchKeys.cs
```

`csc.exe` 一般在 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`。

---

## 自检工具（`tests/`）

| 文件 | 作用 |
| --- | --- |
| `FocusTest.cs` | **验证不抢焦点**（前台进程是否被夺走） |
| `LockTest.cs` | 锁定按钮四项行为（点击无效 / 拖入锁定 / 松手保持 / 触摸解锁） |
| `FullTest.cs` | 摇杆四方向 + 按钮的按键实测 |
| `SelfTest.cs` | 统计渲染结果 alpha 分布 |
| `WinInspect.cs` | 窗口几何 + WindowFromPoint 穿透验证 |
| `KeyProbe.cs` | 摇杆推上是否发出 W |
| `DpiProbe.cs` | 不同 DPI 模式的坐标对比 |
| `Verify.cs` | **主验收脚本**：读 ini 里的坐标，11 项全流程 |
| `MultiKey.cs` | 多键同时触发（斜向双键、方向切换、按钮独立） |
| `StyleCheck.cs` | 检查窗口 EXSTYLE（NOACTIVATE 是否生效） |
| `PointerProbe.cs` | 探测触摸注入能产生哪些消息 |
| `TouchInject.cs` | 用 Touch Injection API 模拟多点触摸 |
| `StickRange.cs` | 小圈超出大圈后方向是否仍正确 |
| `Preview.cs` | 把控件叠到深色背景上预览观感 |

编译：`csc /target:exe /out:xxx.exe /r:System.dll /r:System.Drawing.dll xxx.cs`
运行前先让 TouchKeys 跑起来。

实测结果：

```
=== 焦点保持 ===
★ 通过：拖动摇杆期间前台进程没有变（没有抢焦点）

=== 锁定功能 ===
1) 直接点锁定按钮 -> W=False   ✓ 无效（符合设计）
2) 拖到锁定区 -> 拖动中出现 W=True，松手前 W=True
3) 松手后仍 W=True             ✓ 保持锁定（一直前进）
4) 再按住摇杆 -> W=False       ✓ 已解锁
★ 全部通过

=== 摇杆 + 按钮（11 项）===
  OK  推W(上) / S(下) / A(左) / D(右) 全部正确发键并复位
  OK  按钮 跳(SPACE) / 攻(1) / 技(2) 全部正确
  OK  直接点击锁定按钮无效
  OK  拖入锁定区 -> 锁定
  OK  松手后保持（一直前进）
  OK  触摸摇杆解锁

=== 多键同时触发（3 项）===
  OK  斜向 左上 = W+A 同时按下
  OK  斜向切向 左上->右下，旧键正确松开
  OK  按钮独立按下/释放

=== 多指状态管理（4 项，直接调用触点函数，不依赖硬件）===
  OK  摇杆(W) 与 按钮 同时按下
  OK  松开摇杆后按钮仍按住
  OK  全部松开后无残留按键
  OK  同一按钮两指同按互不干扰

=== 多键同时触发（4 项）===
  OK  斜向 左上 = W+A
  OK  按钮独立按下/释放
  OK  斜向切换 左上->右下，旧键正确松开
  OK  结束后无残留按键

=== 摇杆范围（2 项）===
  OK  左上 45° 拖到远超半径，A+W 仍同时生效
  OK  松手后完全复位
```

---

## 已知限制

- **不要用于带内核级反作弊的游戏**。ACE、EAC、GameGuard 这类会检测输入模拟，
  轻则输入被忽略，重则封号。这和软件本身无关，任何按键模拟都一样。
  那种游戏请用手柄（系统标准输入通道）或官方云版。
- 单显示器。多显示器虚拟屏坐标换算没测试。
- 只能发键盘事件，不能发鼠标移动/点击。
- 触摸需设备支持 Windows Touch 消息（绝大多数 Windows 平板支持）。
- **如果某个按钮位置点不动**：可能是被 Windows 自己的浮层挡住了
  （`TextInputHost` / 输入法 / 触摸键盘）。换个位置就行，
  用 `tests/WinInspect.exe` 可以查那个坐标到底归哪个窗口。

---

## 遇到问题

**摇杆推了没反应**
1. 确认不在编辑模式（编辑模式不产生按键）
2. 跑 `tests/FullTest.exe` 看哪项失败
3. 加 `--debug` 启动会生成 `touchkeys.log`，里面有每条鼠标消息和按键计算


**配置文件在哪**
程序依次尝试：exe 同目录 → `%APPDATA%\TouchKeys\` → 临时目录。
哪个能写用哪个。右键菜单「保存配置」会提示实际路径。
