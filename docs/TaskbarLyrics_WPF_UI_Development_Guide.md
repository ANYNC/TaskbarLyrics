# TaskbarLyrics 设置页 WPF 开发手册

## 1. 目标效果

实现一个 **深色、半透明、Windows 11 Fluent 风格** 的桌面设置页。

核心特征：

```text
布局：左侧导航 + 右侧内容区
风格：深色、低饱和、蓝色统一强调色
背景：窗口半透明 / Acrylic / Mica 感
卡片：圆角、弱描边、轻微内发光
控件：现代 Toggle、图标按钮、播放器卡片、拖拽排序列表
```

视觉参考为最后一次生成图：

- 不使用彩色穿透背景作为主设计语言
- 背景以深蓝黑为主
- 强调色统一为蓝色
- 播放器品牌色只保留在图标中
- 所有 Switch、选中态、边框高亮使用统一蓝色

---

## 2. 推荐技术栈

### 基础框架

```text
.NET 8 / .NET 9
WPF
MVVM
CommunityToolkit.Mvvm
```

### 推荐库

```text
CommunityToolkit.Mvvm
Wpf.Ui 或 ModernWpf
MaterialDesignThemes 可选，不建议重度使用
Hardcodet.NotifyIcon.Wpf 可用于托盘
Microsoft.Web.WebView2 仅在需要内嵌网页时使用
```

### 建议架构

```text
TaskbarLyrics
├── Views
│   ├── MainWindow.xaml
│   ├── SettingsPage.xaml
│   └── Components
│       ├── Sidebar.xaml
│       ├── SettingCard.xaml
│       ├── PlayerItem.xaml
│       └── ToggleSwitch.xaml
├── ViewModels
│   ├── MainViewModel.cs
│   ├── SettingsViewModel.cs
│   └── PlayerIntegrationViewModel.cs
├── Models
│   ├── PlayerIntegration.cs
│   └── SettingItem.cs
├── Services
│   ├── SettingsService.cs
│   ├── ThemeService.cs
│   └── WindowBackdropService.cs
└── Resources
    ├── Colors.xaml
    ├── Typography.xaml
    ├── Controls.xaml
    └── Icons.xaml
```

---

## 3. 页面整体布局

### 窗口尺寸

建议默认尺寸：

```text
Width: 1280
Height: 860
MinWidth: 1080
MinHeight: 720
```

最终窗口比例应接近：

```text
左侧导航：240px ~ 260px
右侧内容：剩余宽度
顶部标题栏：48px
内容区左右边距：48px
```

---

## 4. 页面结构

```text
Window
├── Custom TitleBar
│   ├── App Title
│   └── Window Buttons
│
├── Root Grid
│   ├── Sidebar
│   │   ├── Logo
│   │   ├── Navigation Items
│   │   └── Bottom Settings
│   │
│   └── Content Area
│       ├── Page Header
│       ├── Music Icon Button
│       ├── Player Integration Card
│       └── Lyrics Settings Card
```

---

## 5. 视觉标准

### 5.1 主色板

建议统一使用以下色彩。

```xaml
<Color x:Key="Color.WindowBackground">#0B1220</Color>
<Color x:Key="Color.Surface">#121A2A</Color>
<Color x:Key="Color.SurfaceElevated">#172235</Color>
<Color x:Key="Color.SurfaceCard">#151E2F</Color>
<Color x:Key="Color.SurfaceItem">#101827</Color>

<Color x:Key="Color.Border">#2A3650</Color>
<Color x:Key="Color.BorderSoft">#1F2A40</Color>
<Color x:Key="Color.BorderActive">#4F8CFF</Color>

<Color x:Key="Color.Accent">#3B82F6</Color>
<Color x:Key="Color.AccentHover">#60A5FA</Color>
<Color x:Key="Color.AccentPressed">#2563EB</Color>

<Color x:Key="Color.TextPrimary">#F8FAFC</Color>
<Color x:Key="Color.TextSecondary">#CBD5E1</Color>
<Color x:Key="Color.TextTertiary">#94A3B8</Color>
<Color x:Key="Color.TextDisabled">#64748B</Color>

<Color x:Key="Color.SwitchOff">#334155</Color>
<Color x:Key="Color.SwitchThumb">#FFFFFF</Color>
```

### 5.2 背景原则

不要使用强烈彩色渐变。

推荐：

```text
主背景：深蓝黑
局部光晕：低透明度蓝色
窗口背后：Mica / Acrylic / Blur
内容层：半透明深色卡片
```

背景可以使用非常弱的径向光：

```xaml
<RadialGradientBrush x:Key="RootBackgroundBrush" Center="0.2,0.1" RadiusX="0.9" RadiusY="0.9">
    <GradientStop Color="#18233A" Offset="0"/>
    <GradientStop Color="#0B1220" Offset="0.55"/>
    <GradientStop Color="#070B12" Offset="1"/>
</RadialGradientBrush>
```

---

## 6. 字体规范

### 推荐字体

```text
中文：Microsoft YaHei UI / PingFang SC
英文：Segoe UI / SF Pro
```

### 字号

```text
页面标题：32px / Semibold
一级卡片标题：22px / Semibold
二级标题：16px / Semibold
正文：14px / Regular
辅助说明：13px / Regular
导航文字：16px / Medium
```

### 字重

```text
标题：600
重要文本：500
正文：400
辅助文字：400
```

---

## 7. 间距系统

使用 8pt Grid。

```text
4px   极小间距
8px   控件内部小间距
12px  图标与文字间距
16px  普通组件间距
24px  卡片内边距
32px  区块间距
40px  页面边距
48px  大页面边距
```

建议：

```text
Sidebar Padding: 24px
Content Padding Left/Right: 48px
Content Top Padding: 40px
Card Padding: 24px
Card Gap: 24px
Player Item Gap: 16px
```

---

## 8. 圆角规范

```text
窗口圆角：16px
Sidebar 内部卡片圆角：14px
主卡片圆角：16px
播放器项圆角：12px
按钮圆角：10px
Switch 圆角：999px
图标容器圆角：12px
```

---

## 9. 阴影与描边

### 卡片描边

```xaml
BorderBrush="#2A3650"
BorderThickness="1"
```

### 卡片背景

```xaml
Background="#B3151E2F"
```

透明度不要太高，否则背景穿透会干扰内容。

### 阴影

WPF 原生阴影建议谨慎使用。

推荐：

```xaml
<DropShadowEffect
    BlurRadius="24"
    ShadowDepth="8"
    Opacity="0.22"
    Color="#000000"/>
```

只给主窗口或主卡片使用，不要每个小组件都加阴影。

---

## 10. 窗口效果

### 推荐窗口属性

```xaml
WindowStyle="None"
AllowsTransparency="False"
Background="Transparent"
ResizeMode="CanResize"
```

如果需要 Mica / Acrylic，建议通过 DWM API 设置，而不是 `AllowsTransparency=True`。

`AllowsTransparency=True` 会导致：

```text
性能下降
文本渲染变差
阴影异常
DPI 问题
窗口动画不自然
```

### 建议实现方式

```text
Windows 11：优先使用 Mica / Desktop Acrylic
Windows 10：降级为深色半透明背景 + Blur
旧系统：纯深色背景
```

---

## 11. TitleBar 设计

### 高度

```text
48px
```

### 内容

左上角：

```text
TaskbarLyrics - 设置
```

右上角：

```text
最小化
最大化
关闭
```

### 按钮尺寸

```text
46 x 40
```

### 交互

```text
Hover：#FFFFFF12
Pressed：#FFFFFF1A
Close Hover：#E81123
```

---

## 12. Sidebar 设计

### 宽度

```text
256px
```

### 背景

```xaml
Background="#99101A2B"
BorderBrush="#25334D"
BorderThickness="0,0,1,0"
```

### Logo 区

```text
高度：80px
图标：36x36
文字：20px / Semibold
```

图标颜色使用 Accent：

```text
#3B82F6
```

### 导航项

尺寸：

```text
Height: 56px
Margin: 20px 6px
Padding: 16px 0
Radius: 12px
```

普通态：

```text
文字：#CBD5E1
图标：#CBD5E1
背景：Transparent
```

Hover：

```text
背景：#FFFFFF0A
文字：#F8FAFC
```

选中态：

```text
背景：#1E3A8A55
边框：#3B82F655
文字：#EAF2FF
图标：#60A5FA
```

可选增强：

```text
左侧 3px 蓝色竖条
```

---

## 13. 主内容区设计

### 右侧内容 Margin

```text
Left: 48
Top: 48
Right: 48
Bottom: 48
```

### Header

```text
Title: 设置
Subtitle: 调整播放器识别、歌词显示、外观和任务栏布局。
```

标题：

```text
FontSize: 32
FontWeight: SemiBold
Color: #F8FAFC
```

副标题：

```text
FontSize: 14
Color: #94A3B8
```

右上角图标按钮：

```text
Size: 56x56
Radius: 14
Border: #3B82F655
Icon: #3B82F6
Background: #1E293B99
```

---

## 14. 主卡片 SettingCard

### 卡片尺寸

宽度：

```text
Stretch
```

内边距：

```text
24
```

间距：

```text
Card Gap: 24
```

背景：

```xaml
Background="#99151E2F"
BorderBrush="#2A3650"
BorderThickness="1"
CornerRadius="16"
```

### 标题区

```text
Title: 22px / Semibold / #F8FAFC
Description: 14px / #94A3B8
Title 与 Description 间距：4px
标题区与内容区间距：24px
```

---

## 15. 播放器集成卡片

### 卡片内容结构

```text
播放器集成
说明文字

播放器启用网格
├── QQ音乐
├── 网易云音乐
├── 酷狗音乐
└── Spotify

拖拽排序列表
├── QQ音乐
├── 网易云音乐
├── Spotify
└── 酷狗音乐
```

---

## 16. 播放器启用项 PlayerToggleItem

### 布局

```text
两列 Grid
每个 Item 高度：64px
列间距：16px
行间距：16px
```

每个 Item：

```text
Height: 64
Padding: 16,0
CornerRadius: 12
Background: #AA101827
Border: #26344D
```

内部：

```text
Icon: 36x36
Text: 16px / Medium
Switch: 44x24
```

### 品牌色

仅图标使用品牌色：

```text
QQ音乐：#FACC15
网易云音乐：#EF4444
酷狗音乐：#3B82F6
Spotify：#22C55E
```

不要让品牌色污染卡片背景。

可使用轻微品牌色边缘光，但透明度必须低：

```text
品牌色透明度：8% ~ 12%
```

推荐更统一：

```text
所有播放器项背景一致
所有 Switch 一律蓝色
只有 Logo 保留品牌色
```

---

## 17. ToggleSwitch 规范

### 尺寸

```text
Width: 44
Height: 24
Thumb: 20
Padding: 2
```

### ON

```text
Track: #3B82F6
Thumb: #FFFFFF
Glow: #3B82F655
```

### OFF

```text
Track: #334155
Thumb: #CBD5E1
```

### 动画

```text
Duration: 160ms
Easing: CubicEase Out
```

状态变化：

```text
Thumb TranslateX: 0 → 20
Track Color: Off → Accent
```

---

## 18. 拖拽排序列表

### 容器

```text
MarginTop: 24
CornerRadius: 12
Background: #88101827
Border: #26344D
Padding: 8,8
```

### 行

```text
Height: 52
Padding: 16,0
```

内容：

```text
DragIcon: 18x18
Text: 15px / Medium
```

分割线：

```text
Height: 1
Color: #FFFFFF0D
MarginLeft: 44
```

### 拖拽反馈

```text
Dragging Background: #1E3A8A44
Dragging Border: #3B82F688
Cursor: SizeAll
Opacity: 0.92
Scale: 1.01
```

---

## 19. 歌词设置卡片

结构：

```text
歌词设置
控制歌词窗口启动行为、翻译与显示辅助项。

设置项
└── 启动时显示歌词
    应用启动后自动显示歌词悬浮窗。
    Toggle
```

设置项高度：

```text
72px
```

背景：

```text
#AA101827
```

圆角：

```text
12px
```

内边距：

```text
16px 20px
```

---

## 20. XAML 资源示例

### Colors.xaml

```xaml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Color x:Key="WindowBackgroundColor">#0B1220</Color>
    <Color x:Key="SurfaceColor">#121A2A</Color>
    <Color x:Key="SurfaceElevatedColor">#172235</Color>
    <Color x:Key="SurfaceCardColor">#151E2F</Color>
    <Color x:Key="SurfaceItemColor">#101827</Color>

    <Color x:Key="BorderColor">#2A3650</Color>
    <Color x:Key="BorderSoftColor">#1F2A40</Color>
    <Color x:Key="AccentColor">#3B82F6</Color>
    <Color x:Key="AccentHoverColor">#60A5FA</Color>

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource WindowBackgroundColor}" />
    <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}" />
    <SolidColorBrush x:Key="SurfaceElevatedBrush" Color="{StaticResource SurfaceElevatedColor}" />
    <SolidColorBrush x:Key="SurfaceCardBrush" Color="{StaticResource SurfaceCardColor}" Opacity="0.78" />
    <SolidColorBrush x:Key="SurfaceItemBrush" Color="{StaticResource SurfaceItemColor}" Opacity="0.78" />

    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}" />
    <SolidColorBrush x:Key="BorderSoftBrush" Color="{StaticResource BorderSoftColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="AccentHoverBrush" Color="{StaticResource AccentHoverColor}" />

    <SolidColorBrush x:Key="TextPrimaryBrush" Color="#F8FAFC" />
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="#CBD5E1" />
    <SolidColorBrush x:Key="TextTertiaryBrush" Color="#94A3B8" />
    <SolidColorBrush x:Key="TextDisabledBrush" Color="#64748B" />

</ResourceDictionary>
```

---

## 21. 主窗口布局示例

```xaml
<Window
    x:Class="TaskbarLyrics.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Width="1280"
    Height="860"
    MinWidth="1080"
    MinHeight="720"
    WindowStyle="None"
    ResizeMode="CanResize"
    Background="{StaticResource WindowBackgroundBrush}">

    <Border
        CornerRadius="16"
        BorderThickness="1"
        BorderBrush="#334155"
        Background="{StaticResource WindowBackgroundBrush}">

        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="48"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <Grid Grid.Row="0">
                <TextBlock
                    Text="TaskbarLyrics - 设置"
                    VerticalAlignment="Center"
                    Margin="24,0,0,0"
                    Foreground="{StaticResource TextSecondaryBrush}"
                    FontSize="14"/>

                <StackPanel
                    Orientation="Horizontal"
                    HorizontalAlignment="Right">
                    <Button Style="{StaticResource TitleBarButtonStyle}" Content="—"/>
                    <Button Style="{StaticResource TitleBarButtonStyle}" Content="□"/>
                    <Button Style="{StaticResource CloseTitleBarButtonStyle}" Content="×"/>
                </StackPanel>
            </Grid>

            <Grid Grid.Row="1">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="256"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <!-- Sidebar -->
                <Border
                    Grid.Column="0"
                    Background="#99101A2B"
                    BorderBrush="#25334D"
                    BorderThickness="0,0,1,0">
                    <!-- Sidebar content -->
                </Border>

                <!-- Content -->
                <ScrollViewer
                    Grid.Column="1"
                    VerticalScrollBarVisibility="Auto"
                    HorizontalScrollBarVisibility="Disabled">
                    <Grid Margin="48,40,48,48">
                        <!-- Content here -->
                    </Grid>
                </ScrollViewer>
            </Grid>
        </Grid>
    </Border>
</Window>
```

---

## 22. Content 区示例布局

```xaml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="24"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="24"/>
        <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <Grid Grid.Row="0">
        <StackPanel>
            <TextBlock
                Text="设置"
                FontSize="32"
                FontWeight="SemiBold"
                Foreground="{StaticResource TextPrimaryBrush}"/>

            <TextBlock
                Text="调整播放器识别、歌词显示、外观和任务栏布局。"
                FontSize="14"
                Margin="0,8,0,0"
                Foreground="{StaticResource TextTertiaryBrush}"/>
        </StackPanel>

        <Border
            Width="56"
            Height="56"
            CornerRadius="14"
            HorizontalAlignment="Right"
            VerticalAlignment="Top"
            BorderBrush="#553B82F6"
            BorderThickness="1"
            Background="#991E293B">
            <TextBlock
                Text="♫"
                FontSize="28"
                Foreground="{StaticResource AccentBrush}"
                HorizontalAlignment="Center"
                VerticalAlignment="Center"/>
        </Border>
    </Grid>

    <Border
        Grid.Row="2"
        Style="{StaticResource SettingCardStyle}">
        <!-- 播放器集成 -->
    </Border>

    <Border
        Grid.Row="4"
        Style="{StaticResource SettingCardStyle}">
        <!-- 歌词设置 -->
    </Border>
</Grid>
```

---

## 23. SettingCard Style

```xaml
<Style x:Key="SettingCardStyle" TargetType="Border">
    <Setter Property="CornerRadius" Value="16"/>
    <Setter Property="Padding" Value="24"/>
    <Setter Property="Background" Value="#99151E2F"/>
    <Setter Property="BorderBrush" Value="#2A3650"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Effect">
        <Setter.Value>
            <DropShadowEffect
                BlurRadius="22"
                ShadowDepth="8"
                Opacity="0.16"
                Color="#000000"/>
        </Setter.Value>
    </Setter>
</Style>
```

---

## 24. Player Item Style

```xaml
<Style x:Key="PlayerItemStyle" TargetType="Border">
    <Setter Property="Height" Value="64"/>
    <Setter Property="CornerRadius" Value="12"/>
    <Setter Property="Padding" Value="16,0"/>
    <Setter Property="Background" Value="#AA101827"/>
    <Setter Property="BorderBrush" Value="#26344D"/>
    <Setter Property="BorderThickness" Value="1"/>
</Style>
```

---

## 25. Sidebar 视觉结构

```xaml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="80"/>
        <RowDefinition Height="*"/>
        <RowDefinition Height="72"/>
    </Grid.RowDefinitions>

    <StackPanel
        Grid.Row="0"
        Orientation="Horizontal"
        Margin="24,0"
        VerticalAlignment="Center">
        <TextBlock
            Text="♪"
            FontSize="30"
            Foreground="{StaticResource AccentBrush}"
            Margin="0,0,12,0"/>
        <TextBlock
            Text="TaskbarLyrics"
            FontSize="20"
            FontWeight="SemiBold"
            Foreground="{StaticResource TextPrimaryBrush}"
            VerticalAlignment="Center"/>
    </StackPanel>

    <StackPanel Grid.Row="1" Margin="20,24,20,0">
        <!-- NavigationItem -->
    </StackPanel>

    <StackPanel
        Grid.Row="2"
        Orientation="Horizontal"
        Margin="24,0"
        VerticalAlignment="Center">
        <TextBlock Text="⚙" FontSize="20" Foreground="{StaticResource TextSecondaryBrush}" Margin="0,0,12,0"/>
        <TextBlock Text="设置" FontSize="15" Foreground="{StaticResource TextSecondaryBrush}"/>
    </StackPanel>
</Grid>
```

---

## 26. Navigation Item 规范

```xaml
<RadioButton
    Height="56"
    Margin="0,4"
    Padding="16,0"
    Style="{StaticResource SidebarNavItemStyle}">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="▧" FontSize="20" Margin="0,0,14,0"/>
        <TextBlock Text="外观" FontSize="16" FontWeight="Medium"/>
    </StackPanel>
</RadioButton>
```

选中态：

```text
背景：#334F8CFF
边框：#554F8CFF
文字：#EAF2FF
```

---

## 27. 交互状态标准

### Hover

```text
背景亮度提升 4% ~ 8%
描边透明度提升
不要强烈变色
```

### Pressed

```text
轻微缩小 0.98
背景降低亮度
```

### Selected

```text
统一蓝色
不要使用紫色、青色混合
```

### Disabled

```text
Opacity: 0.45
```

---

## 28. 动画标准

推荐统一动画时长：

```text
快速反馈：120ms
普通过渡：180ms
页面切换：220ms
```

缓动：

```text
CubicEase EaseOut
```

可动画属性：

```text
Opacity
TranslateTransform.X/Y
ScaleTransform
Background Color
Border Color
```

避免动画：

```text
Width
Height
Margin
复杂 BlurRadius
```

---

## 29. 图标规范

### 建议

使用统一线性图标库。

可选：

```text
Segoe Fluent Icons
Lucide
FontAwesome
Material Symbols Rounded
```

### 规范

```text
普通导航图标：20px
Logo 图标：30px
Header 图标：28px
拖拽图标：18px
线宽：1.75px ~ 2px
```

图标颜色：

```text
普通：#CBD5E1
选中：#60A5FA
弱化：#94A3B8
```

---

## 30. 数据模型建议

```csharp
public sealed partial class PlayerIntegration : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string IconText { get; init; } = string.Empty;

    public string BrandColor { get; init; } = "#3B82F6";

    [ObservableProperty]
    private bool isEnabled = true;

    [ObservableProperty]
    private int priority;
}
```

ViewModel：

```csharp
public sealed partial class SettingsViewModel : ObservableObject
{
    public ObservableCollection<PlayerIntegration> Players { get; } = new();

    [ObservableProperty]
    private bool showLyricsOnStartup = true;

    public SettingsViewModel()
    {
        Players.Add(new PlayerIntegration
        {
            Id = "qqmusic",
            Name = "QQ音乐",
            IconText = "Q",
            BrandColor = "#FACC15",
            Priority = 0
        });

        Players.Add(new PlayerIntegration
        {
            Id = "netease",
            Name = "网易云音乐",
            IconText = "♬",
            BrandColor = "#EF4444",
            Priority = 1
        });

        Players.Add(new PlayerIntegration
        {
            Id = "kugou",
            Name = "酷狗音乐",
            IconText = "K",
            BrandColor = "#3B82F6",
            Priority = 2
        });

        Players.Add(new PlayerIntegration
        {
            Id = "spotify",
            Name = "Spotify",
            IconText = "S",
            BrandColor = "#22C55E",
            Priority = 3
        });
    }
}
```

---

## 31. 拖拽排序实现建议

简单实现：

```text
ItemsControl/ListBox
PreviewMouseLeftButtonDown
MouseMove
DragDrop.DoDragDrop
Drop 后调整 ObservableCollection 顺序
```

推荐封装成：

```text
SortableListBehavior
```

交互要求：

```text
拖拽时显示半透明浮层
落点显示蓝色指示线
排序后立即保存 Priority
```

---

## 32. 设置保存

建议保存到 JSON：

```text
%AppData%/TaskbarLyrics/settings.json
```

结构：

```json
{
  "theme": "dark",
  "accentColor": "#3B82F6",
  "showLyricsOnStartup": true,
  "playerIntegrations": [
    {
      "id": "qqmusic",
      "enabled": true,
      "priority": 0
    },
    {
      "id": "netease",
      "enabled": true,
      "priority": 1
    }
  ]
}
```

---

## 33. 可访问性要求

必须保证：

```text
正文与背景对比度足够
按钮有 Keyboard Focus
Toggle 支持 Space 切换
拖拽排序提供上移/下移备用按钮或键盘操作
不要只依赖颜色表达状态
```

Focus 样式：

```text
蓝色描边：#60A5FA
描边厚度：2px
```

---

## 34. DPI 与适配

必须支持：

```text
100%
125%
150%
175%
200%
```

注意：

```text
不要使用固定图片尺寸作为背景
图标尽量用 Vector / Path / FontIcon
文本不要写死高度
ScrollViewer 必须可滚动
```

---

## 35. 最终验收标准

Codex 实现后可以按这个清单检查。

### 视觉

```text
整体为统一深蓝黑色系
主强调色只有蓝色
品牌色只出现在播放器 Icon
卡片有清晰层级但不过度发光
Sidebar 与 Content 分区明确
页面标题、卡片标题、正文层级清晰
所有圆角风格一致
```

### 布局

```text
左侧导航固定宽度
右侧内容可滚动
播放器集成卡片两列排列
拖拽排序列表位于播放器开关区域下方
歌词设置卡片在第二块
窗口缩小时内容不溢出
```

### 交互

```text
导航 Hover / Selected 正常
Switch 有动画
播放器启用状态可切换
排序列表可拖拽
窗口可拖动、最小化、最大化、关闭
```

### 技术

```text
MVVM 清晰
颜色集中在 ResourceDictionary
控件样式集中管理
没有大量硬编码颜色
设置可持久化
DPI 显示正常
```

---

## 36. 给 Codex 的实现提示词

可以直接把下面这段交给 Codex：

```text
请用 WPF 实现一个 TaskbarLyrics 设置页，风格参考 Windows 11 Fluent 深色桌面应用。

要求：
1. 使用 .NET WPF + MVVM。
2. 页面布局为左侧 Sidebar + 右侧设置内容区。
3. 整体使用深蓝黑色系，强调色统一为 #3B82F6。
4. 背景使用深色半透明质感，可模拟 Mica/Acrylic，但不要使用强烈彩色渐变。
5. 左侧导航宽度 256px，包含 Logo、播放器、歌词、外观、布局、调试、底部设置入口。
6. 当前选中项为“外观”，选中态使用蓝色半透明背景。
7. 右侧 Header 标题为“设置”，副标题为“调整播放器识别、歌词显示、外观和任务栏布局。”，右上角有音乐图标按钮。
8. 第一张主卡片标题为“播放器集成”，包含四个播放器开关项：QQ音乐、网易云音乐、酷狗音乐、Spotify。
9. 播放器开关项两列布局，每项高 64px，圆角 12px，深色背景，右侧统一蓝色 Toggle。
10. 播放器品牌色只用于图标，不用于整体背景。
11. 播放器区域下方有拖拽排序列表，包含 QQ音乐、网易云音乐、Spotify、酷狗音乐。
12. 第二张卡片标题为“歌词设置”，包含“启动时显示歌词”设置项和 Toggle。
13. 所有卡片圆角 16px，背景 #151E2F 约 75% 透明，描边 #2A3650。
14. 使用统一 ResourceDictionary 管理颜色、字体、控件样式。
15. ToggleSwitch 需要有平滑动画。
16. 窗口自定义标题栏，右上角有最小化、最大化、关闭按钮。
17. 支持窗口拖动、缩放、DPI 缩放和内容滚动。
18. 代码结构清晰，尽量组件化：Sidebar、SettingCard、PlayerItem、ToggleSwitch。
```

这份手册可直接作为 Codex 实现 WPF 设置页的视觉与技术参考。
