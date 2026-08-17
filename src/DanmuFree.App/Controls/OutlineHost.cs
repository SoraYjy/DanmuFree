using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DanmuFree.App.Controls;

/// <summary>
/// 描边容器：给内容（弹幕文字）套一圈硬描边。原理：8 层嵌套 Grid，各挂一个 BlurRadius=0 的
/// DropShadowEffect（ShadowDepth=Thickness、Color=Stroke、Direction=8 个方向）——每层把
/// 「内容剪影 + 已累积的影子」朝自己方向平移一次，8 方向叠加 = 膨胀描边环；效果先画影子再画内容，
/// 原文字最终画在最上、颜色/绑定不受影响（只复制 alpha 剪影着色）。
///
/// 实现为 Decorator + **代码自建**视觉树（构造器里搭 8 层 Grid + ContentPresenter），**不走
/// ControlTemplate/隐式 Style/Freezable 绑定**——首版用 App.xaml 隐式 Style 模板 + TemplatedParent
/// 绑定效果属性，实测勾选后无任何视觉变化（模板/绑定疑似未生效且无报错），改为 DPCallback 直改
/// 效果属性，更新路径确定、可单步。Thickness=0（描边关）时效果直接置 null（0 渲染开销、像素级无变化）。
/// 注：效果会把文本栅格化（灰度 AA）——描边本为复杂背景可读性，代价可接受。
/// </summary>
[ContentProperty(nameof(Content))]
public class OutlineHost : Decorator
{
    private static readonly int[] Directions = { 0, 45, 90, 135, 180, 225, 270, 315 };

    private readonly ContentPresenter _presenter = new();
    private readonly Grid[] _layers = new Grid[Directions.Length];      // [0]=最外层
    private readonly DropShadowEffect[] _effects = new DropShadowEffect[Directions.Length];

    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(OutlineHost),
        new PropertyMetadata(null, (d, e) => ((OutlineHost)d)._presenter.Content = e.NewValue));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Color), typeof(OutlineHost),
        new FrameworkPropertyMetadata(Colors.Black, (d, _) => ((OutlineHost)d).SyncEffects()));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(OutlineHost),
        new FrameworkPropertyMetadata(1.5, (d, _) => ((OutlineHost)d).SyncEffects()));

    /// <summary>要描边的内容（XAML 直接写子元素即可）。</summary>
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    /// <summary>描边颜色（默认黑）。</summary>
    public Color Stroke { get => (Color)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>描边粗细（px）；0 = 关闭描边（效果置 null，零开销）。</summary>
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public OutlineHost()
    {
        // 由内向外搭 8 层：最内 ContentPresenter，每层 Grid 挂一个方向投影。
        UIElement inner = _presenter;
        for (int i = Directions.Length - 1; i >= 0; i--)
        {
            var grid = new Grid();
            _effects[i] = new DropShadowEffect
            { BlurRadius = 0, Opacity = 1, Direction = Directions[i] };
            grid.Effect = _effects[i];
            grid.Children.Add(inner);
            _layers[i] = grid;
            inner = grid;
        }
        Child = inner;
        SyncEffects();
    }

    // 属性回调直改效果（不走绑定）：开→挂效果并同步深度/颜色；关→置 null（像素级等同无描边）。
    private void SyncEffects()
    {
        var on = Thickness > 0;
        for (int i = 0; i < _effects.Length; i++)
        {
            _effects[i].ShadowDepth = Thickness;
            _effects[i].Color = Stroke;
            _layers[i].Effect = on ? _effects[i] : null;
        }
    }
}
