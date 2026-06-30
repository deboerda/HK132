# Chat Conversation

Note: _This is purely the output of the chat conversation and does not contain any raw data, codebase snippets, etc. used to generate the output._

### User Input

这是一个飞行器仪表的qt项目，我现在想要在unity中也组建一个飞行器仪表，想复用这个项目中的仪表盘资源和相关代码逻辑，请帮我梳理并输出

*Listed directory [qtfiles](file:///c:/qtfiles) *

*Listed directory [qtcpp_demo-master-qflightinstruments](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments) *

*Listed directory [qtcpp_demo-master-qflightinstruments](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments) *

*Listed directory [qflightinstruments](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments/qflightinstruments) *

*Listed directory [src](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments/qflightinstruments/src) *

*Listed directory [images](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments/qflightinstruments/src/images) *

*Listed directory [adi](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments/qflightinstruments/src/images/adi) *

*Viewed [qfi_ADI.cpp](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments/qflightinstruments/src/qfi_ADI.cpp) *

*Viewed [qfi_ALT.cpp](file:///c:/qtfiles/qtcpp_demo-master-qflightinstruments/qtcpp_demo-master-qflightinstruments/qflightinstruments/src/qfi_ALT.cpp) *

### Planner Response

我帮你梳理了这个 Qt 项目（`qflightinstruments`）的结构，并为你总结了如何在 Unity 中复用这些仪表盘的资源和代码逻辑。

### 1. 资源与代码结构梳理

该 Qt 项目的核心概念是通过对**分层的 SVG 矢量图**进行旋转和平移操作，来模拟出真实的仪表盘数据变化。

*   **仪表盘种类：**
    *   `ADI`：姿态指引仪 (Attitude Director Indicator) - 控制俯仰 (Pitch) 和横滚 (Roll)
    *   `ALT`：高度表 (Altimeter) - 控制高度和气压设定
    *   `ASI`：空速表 (Airspeed Indicator) - 控制空速
    *   `HSI`：水平状态指示仪 (Horizontal Situation Indicator) - 控制航向
    *   `TC`：转弯协调仪 (Turn Coordinator)
    *   `VSI`：垂直速度表 (Vertical Speed Indicator) - 控制升降速率
    *   `PFD/NAV`：多功能主飞行显示器和导航显示器
*   **图像资源路径：**
    位于 `src/images/` 目录下。每个仪表都有独立的文件夹（如 `adi`, `alt`）。**所有资源都是 `.svg` 格式**，并且按照渲染层级拆分了背景、表盘、指针和外壳等（如 `adi_back.svg`, `adi_face.svg`, `adi_case.svg`）。
*   **核心逻辑原理：**
    源码中没有复杂的 3D 逻辑，全部基于 Qt 的 `QGraphicsView`（2D 渲染）。通过不断调整 2D 图形图层（`QGraphicsSvgItem`）的 **Z-Index (渲染层级)**，**Rotation (旋转角度)** 以及 **Translation (相对于原点的平移)** 达成视觉目的。

---

### 2. Unity 复用方案设计

在 Unity 里，我们要利用其原生 UGUI 系统（`Canvas` 和 `Image` 组件）替代 Qt 的 `QGraphicsView`。

#### A. 资源导入与处理
Unity 无法直接将 SVG 作为普通图片放入 Image 组件，你有两种方案：
1.  **推荐方案（无损缩放）**：在 Unity 的 Package Manager 中安装官方的 `Vector Graphics` 插件。它能直接读取 SVG 资源，并在引入时将其转换为适用于 UGUI 渲染的精灵体 (Sprite)，且不失真。
2.  **备用方案**：通过 Illustrator 等工具批量将其导出为高分辨率的 PNG 图片（带有 Alpha 空白通道）然后再导入 Unity。

#### B. UI 层级搭建
以 `ADI` 姿态仪为例，在 Unity Canvas 下创建一个空的 `RectTransform` 命名为 `ADI_Instrument`，下面挂载四张 `Image` 节点，按**从上到下的顺序（对应 Z-Index的从底层到顶层）**排列：
1.  `Back` (对应 `adi_back.svg`)
2.  `Face` (对应 `adi_face.svg`)
3.  `Ring` (对应 `adi_ring.svg`)
4.  `Case` (对应 `adi_case.svg`)

---

### 3. 具体代码映射举例 (Qt 转换至 Unity C#)

我们可以提取 Qt 源码中的数学转换公式，并将其映射到 Unity C# 中。Unity 这里的坐标系 Y 轴是朝上的，与 Qt 略微不同（Qt 左上角为 0,0，Y朝下），所以映射时可能需要做少量的符号反转。

**案例 1：ADI 姿态仪 (俯仰与横滚)**
```csharp
using UnityEngine;

public class Unity_ADI : MonoBehaviour
{
    [Header("UI Layers")]
    public RectTransform itemBack;
    public RectTransform itemFace;
    public RectTransform itemRing;

    [Header("Flight Data")]
    [Range(-180f, 180f)] public float roll = 0.0f;     // 横滚角 (deg)
    [Range(-25f, 25f)] public float pitch = 0.0f;    // 俯仰角 (deg)

    // Qt 源码测算出的比例系数
    private const float originalPixPerDeg = 1.7f; 
    
    void Update()
    {
        // 1. 同步横滚角 (Roll) - 旋转表盘
        // UnityUI Z轴负方向是顺时针，Qt 中写的是 -roll
        float uiRollAngle = -roll;
        Vector3 rot = new Vector3(0, 0, uiRollAngle);
        
        itemBack.localEulerAngles = rot;
        itemFace.localEulerAngles = rot;
        itemRing.localEulerAngles = rot;

        // 2. 同步俯仰角 (Pitch) - 限制在球形面具内的位移
        // 这一步是将 Pitch的度数 转换为 像素偏移，并结合当前横滚角计算 X 和 Y 偏移量
        float rollRad = roll * Mathf.Deg2Rad;
        float delta = originalPixPerDeg * pitch;

        // 若你的UI非 240x240 大小，这里需要乘上宽高的缩放比例 scaleX/Y
        // Qt公式 translation: deltaX = delta * sin(Roll), deltaY = delta * cos(Roll)
        float deltaX = delta * Mathf.Sin(rollRad);
        
        // 注意：Unity 坐标系 Y 轴与 Qt 刚好相反，因此这里计算出的 Y 需进行负反转
        float deltaY = -1.0f * (delta * Mathf.Cos(rollRad)); 

        itemFace.anchoredPosition = new Vector2(deltaX, deltaY);
    }
}
```

**案例 2：ALT 高度表 (双指针与刻度盘)**
从它的 `qfi_ALT.cpp` 解析可知它的驱动逻辑非常单纯，全靠乘法驱动旋转：
```csharp
    public RectTransform itemHand_1; // 长针：万英尺针
    public RectTransform itemHand_2; // 短针：千英尺针
    public RectTransform itemFace_1; // 气压表盘

    public float altitude = 0f;      // 高度 [ft]
    public float pressure = 28.0f;   // 气压 [inHg]

    void Update()
    {
        // Qt 比例测算逻辑：
        // altitude 每增加 1000英尺，H2 (百英尺/千英尺长分针) 转一圈 360 度 -> 乘 0.36
        // altitude 每增加 10000英尺，H1 (万英尺时针) 转一圈 360 度 -> 乘 0.036
        float angleH1 = altitude * 0.036f;
        float angleH2 = (altitude % 1000f) * 0.36f;
        
        // 28.0 是气压基准值
        float angleF1 = (pressure - 28.0f) * 100.0f; 
        
        // 赋值（Unity下需要反向Z轴来匹配正逆时针）
        itemHand_1.localEulerAngles = new Vector3(0, 0, -angleH1);
        itemHand_2.localEulerAngles = new Vector3(0, 0, -angleH2);
        itemFace_1.localEulerAngles = new Vector3(0, 0, angleF1); // 这里的符号通过比对正逆时针需求来定
    }
```

### 总结建议
1. **移植难度极低**：由于原 Qt 项目并没有采用非常硬核的自定义渲染管线或者底层代码截断，仅通过对 `.svg` 素材进行数学偏移来驱动 UI，这完美匹配 Unity 的 `RectTransform` 操作逻辑。
2. **重点在资源适配和层级遮罩**：PFD 和 ADI 中的 `Face` 层在屏幕边界处可能需要使用 Unity 的 `Mask` 或者是 `RectMask2D` 组件裁剪一下（模拟 `adi_case.svg` 外壳对内容的遮挡）。
3. 按照这两个逻辑案例，你只需要把所有 `src/images/` 里的图分批弄进 Unity，然后在你的 `PlaneDisplayController.cs` 或者新的脚本中建立映射即可复刻非常平滑纯正的仪表盘UI。