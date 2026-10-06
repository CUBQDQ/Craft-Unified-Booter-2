// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 皮肤贴图解析：2D 正面立绘 + 3D 可旋转模型（WPF 原生 3D，无外部依赖）。
/// 坐标均以标准 64×64 皮肤贴图像素为单位。
/// </summary>
public static class SkinService
{
    private readonly record struct Uv(int X, int Y, int W, int H);

    private readonly record struct PartBox(
        double X0, double Y0, double Z0, double X1, double Y1, double Z1,
        Uv Top, Uv Bottom, Uv Right, Uv Front, Uv Left, Uv Back);

    // ===================== 贴图载入 =====================
    /// <summary>读取皮肤贴图（Bgra32），失败返回 null</summary>
    public static BitmapSource? LoadTexture(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            conv.Freeze();
            return conv;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>贴图整数倍放大倍数：WPF 3D 纹理固定使用双线性过滤，先做最近邻放大可大幅减轻模糊</summary>
    private const int TextureScale = 8;

    /// <summary>读取贴图并做最近邻整数倍放大（3D 专用，保持像素硬边）</summary>
    public static BitmapSource? LoadTextureScaled(string? path)
    {
        var tex = LoadTexture(path);
        return tex == null ? null : Upscale(tex, TextureScale);
    }

    /// <summary>最近邻整数倍放大：每个源像素复制为等宽方块</summary>
    private static BitmapSource Upscale(BitmapSource src, int factor)
    {
        var sw = src.PixelWidth;
        var sh = src.PixelHeight;
        var srcPx = new byte[sw * sh * 4];
        src.CopyPixels(srcPx, sw * 4, 0);

        var dw = sw * factor;
        var dh = sh * factor;
        var dstPx = new byte[dw * dh * 4];
        for (var y = 0; y < dh; y++)
        {
            var sy = y / factor;
            for (var x = 0; x < dw; x++)
            {
                var si = (sy * sw + x / factor) * 4;
                var di = (y * dw + x) * 4;
                dstPx[di] = srcPx[si];
                dstPx[di + 1] = srcPx[si + 1];
                dstPx[di + 2] = srcPx[si + 2];
                dstPx[di + 3] = srcPx[si + 3];
            }
        }

        var result = BitmapSource.Create(dw, dh, 96, 96, PixelFormats.Bgra32, null, dstPx, dw * 4);
        result.Freeze();
        return result;
    }

    /// <summary>表示皮肤是否为纤细（Alex）模型（微软接口返回的是大写 SLIM）</summary>
    public static bool IsSlim(string? variant) =>
        string.Equals(variant, "slim", StringComparison.OrdinalIgnoreCase);

    // ===================== 贴图裁切 =====================
    /// <summary>披风贴图裁出外层区域 (1,1,10,16)，用于皮肤库缩略图</summary>
    public static BitmapSource? RenderCapeThumb(string? capePath)
    {
        var tex = LoadTexture(capePath);
        if (tex == null || tex.PixelWidth < 64 || tex.PixelHeight < 32) return null;
        try
        {
            var crop = new CroppedBitmap(tex, new Int32Rect(1, 1, 10, 16));
            crop.Freeze();
            return crop;
        }
        catch
        {
            return null;
        }
    }

    // ===================== 3D 模型 =====================
    /// <summary>构建可旋转的 3D 皮肤模型（Y 轴为中心，脚底 y=0，身高 32）</summary>
    public static Model3DGroup? BuildModel(string? skinPath, string? capePath, bool slim)
    {
        var tex = LoadTextureScaled(skinPath);
        if (tex == null) return null;
        // UV 区域用的是原始皮肤像素坐标（64×64 基准），这里必须换算回原始尺寸，
        // 否则会按放大后的尺寸做归一化，导致贴图整体偏移
        var tw = (double)tex.PixelWidth / TextureScale;
        var th = (double)tex.PixelHeight / TextureScale;

        var group = new Model3DGroup();
        // 高环境光 + 一盏弱平行光：贴图颜色基本保持原样，仅保留轻微立体感（避免像素被洗白）
        group.Children.Add(new AmbientLight(Color.FromRgb(0xCC, 0xCC, 0xCC)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x3C, 0x3C, 0x3C),
            new Vector3D(0.25, -0.85, -0.7)));

        // 注意：WPF 3D 材质用 ImageBrush 时，默认 ViewportUnits=RelativeToBoundingBox 会把整张
        // 贴图拉伸到「网格 UV 的包围盒」上。我们的 UV 只用到一部分区间，会导致整图被放大并偏移
        // （这正是"贴图偏移/对不齐"的根因）。解决办法是在网格里放一个零面积标记面，把 UV 包围盒
        // 精确撑满到 [0,1]×[0,1]（见 AddUvBoundsMarker），映射即为 1:1。
        var brush = new ImageBrush(tex) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        var material = new DiffuseMaterial(brush);

        var armW = slim ? 3.0 : 4.0;
        var aw = (int)armW;
        var legacy = th < 64;   // 64×32 老皮肤：左臂左腿复用右侧贴图

        // ---- 基础层 ----
        var mesh = new MeshGeometry3D { };
        // 头 8×8×8
        AddBox(mesh, -4, 24, -4, 4, 32, 4, tw, th,
            new Uv(8, 0, 8, 8), new Uv(16, 0, 8, 8), new Uv(0, 8, 8, 8),
            new Uv(8, 8, 8, 8), new Uv(16, 8, 8, 8), new Uv(24, 8, 8, 8));
        // 身体 8×12×4
        AddBox(mesh, -4, 12, -2, 4, 24, 2, tw, th,
            new Uv(20, 16, 8, 4), new Uv(28, 16, 8, 4), new Uv(16, 20, 4, 12),
            new Uv(20, 20, 8, 12), new Uv(28, 20, 4, 12), new Uv(32, 20, 8, 12));
        // 右臂（宽 4 时：40/44/48/52；纤细宽 3 时：40/43/46/49）
        AddBox(mesh, -4 - armW, 12, -2, -4, 24, 2, tw, th,
            new Uv(44, 16, aw, 4), new Uv(44 + aw, 16, aw, 4),
            new Uv(40, 20, aw, 12), new Uv(40 + aw, 20, aw, 12),
            new Uv(40 + aw * 2, 20, aw, 12), new Uv(40 + aw * 3, 20, aw, 12));
        // 左臂（64×32 老皮肤复用右臂贴图）
        AddBox(mesh, 4, 12, -2, 4 + armW, 24, 2, tw, th,
            legacy
                ? new Uv(44, 16, aw, 4)
                : new Uv(36, 48, aw, 4),
            legacy
                ? new Uv(44 + aw, 16, aw, 4)
                : new Uv(36 + aw, 48, aw, 4),
            legacy
                ? new Uv(40 + aw * 2, 20, aw, 12)
                : new Uv(32 + aw * 2, 52, aw, 12),
            legacy
                ? new Uv(40 + aw, 20, aw, 12)
                : new Uv(32 + aw, 52, aw, 12),
            legacy
                ? new Uv(40, 20, aw, 12)
                : new Uv(32, 52, aw, 12),
            legacy
                ? new Uv(40 + aw * 3, 20, aw, 12)
                : new Uv(32 + aw * 3, 52, aw, 12));
        // 右腿
        AddBox(mesh, -4, 0, -2, 0, 12, 2, tw, th,
            new Uv(4, 16, 4, 4), new Uv(8, 16, 4, 4), new Uv(0, 20, 4, 12),
            new Uv(4, 20, 4, 12), new Uv(8, 20, 4, 12), new Uv(12, 20, 4, 12));
        // 左腿（64×32 老皮肤复用右腿贴图）
        AddBox(mesh, 0, 0, -2, 4, 12, 2, tw, th,
            legacy
                ? new Uv(4, 16, 4, 4)
                : new Uv(20, 48, 4, 4),
            legacy
                ? new Uv(8, 16, 4, 4)
                : new Uv(24, 48, 4, 4),
            legacy
                ? new Uv(8, 20, 4, 12)
                : new Uv(24, 52, 4, 12),
            legacy
                ? new Uv(4, 20, 4, 12)
                : new Uv(20, 52, 4, 12),
            legacy
                ? new Uv(0, 20, 4, 12)
                : new Uv(16, 52, 4, 12),
            legacy
                ? new Uv(12, 20, 4, 12)
                : new Uv(28, 52, 4, 12));
        AddUvBoundsMarker(mesh);   // 撑满 UV 包围盒，避免贴图被整体放大偏移
        group.Children.Add(new GeometryModel3D(mesh, material));

        // ---- 覆盖层（第二层：帽 / 外套 / 袖子 / 裤子）----
        // 第二层在 Minecraft 里是画在一个略大 0.25~0.5 的"壳"上的，让模型更有立体感。
        // 它的贴图区域与基础层完全独立（帽子在 32~40、外套在 36~56 等），
        // 配合 AddUvBoundsMarker 撑满 UV 包围盒后是 1:1 精确映射，不会错位。
        const bool RenderSecondLayer = true;
        if (RenderSecondLayer && th >= 64)
        {
            const double eH = 1.0;    // 头部（帽层），明显大于基础层，避免 Z-fighting 线条
            const double eB = 0.5;    // 身体 / 手臂 / 腿
            var ov = new MeshGeometry3D();
            AddBoxAlphaClipped(ov, -4 - eH, 24 - eH, -4 - eH, 4 + eH, 32 + eH, 4 + eH, tw, th, tex,
                new Uv(40, 0, 8, 8), new Uv(48, 0, 8, 8), new Uv(32, 8, 8, 8),
                new Uv(40, 8, 8, 8), new Uv(48, 8, 8, 8), new Uv(56, 8, 8, 8));
            AddBoxAlphaClipped(ov, -4 - eB, 12 - eB, -2 - eB, 4 + eB, 24 + eB, 2 + eB, tw, th, tex,
                new Uv(20, 32, 8, 4), new Uv(28, 32, 8, 4), new Uv(16, 36, 4, 12),
                new Uv(20, 36, 8, 12), new Uv(28, 36, 4, 12), new Uv(32, 36, 8, 12));
            AddBoxAlphaClipped(ov, -4 - armW - eB, 12 - eB, -2 - eB, -4 + eB, 24 + eB, 2 + eB, tw, th, tex,
                new Uv(44, 32, aw, 4), new Uv(44 + aw, 32, aw, 4),
                new Uv(40, 36, aw, 12), new Uv(40 + aw, 36, aw, 12),
                new Uv(40 + aw * 2, 36, aw, 12), new Uv(40 + aw * 3, 36, aw, 12));
            AddBoxAlphaClipped(ov, 4 - eB, 12 - eB, -2 - eB, 4 + armW + eB, 24 + eB, 2 + eB, tw, th, tex,
                new Uv(52, 48, aw, 4), new Uv(52 + aw, 48, aw, 4),
                new Uv(48 + aw * 2, 52, aw, 12), new Uv(48 + aw, 52, aw, 12),
                new Uv(48, 52, aw, 12), new Uv(48 + aw * 3, 52, aw, 12));
            AddBoxAlphaClipped(ov, -4 - eB, -eB, -2 - eB, 0 + eB, 12 + eB, 2 + eB, tw, th, tex,
                new Uv(4, 32, 4, 4), new Uv(8, 32, 4, 4), new Uv(0, 36, 4, 12),
                new Uv(4, 36, 4, 12), new Uv(8, 36, 4, 12), new Uv(12, 36, 4, 12));
            AddBoxAlphaClipped(ov, 0 - eB, -eB, -2 - eB, 4 + eB, 12 + eB, 2 + eB, tw, th, tex,
                new Uv(4, 48, 4, 4), new Uv(8, 48, 4, 4), new Uv(8, 52, 4, 12),
                new Uv(4, 52, 4, 12), new Uv(0, 52, 4, 12), new Uv(12, 52, 4, 12));
            AddUvBoundsMarker(ov);   // 同理撑满 UV 包围盒
            group.Children.Add(new GeometryModel3D(ov, material));
        }

        // ---- 披风（挂在身后，10×16×1，官方几何锚点 y=8~24） ----
        var capeTex = LoadTextureScaled(capePath);
        if (capeTex != null)
        {
            var cw = (double)capeTex.PixelWidth / TextureScale;
            var ch = (double)capeTex.PixelHeight / TextureScale;
            var capeMesh = new MeshGeometry3D();
            // 标准 64×32 披风贴图：外层可见面在 (1,1,10,16)，-Z 面朝外（背对镜头一侧）
            AddBox(capeMesh, -5, 8, -3.6, 5, 24, -2.6, cw, ch,
                new Uv(1, 0, 10, 1), new Uv(11, 0, 10, 1), new Uv(11, 1, 1, 16),
                new Uv(12, 1, 10, 16), new Uv(0, 1, 1, 16), new Uv(1, 1, 10, 16));
            AddUvBoundsMarker(capeMesh);   // 同理撑满 UV 包围盒
            group.Children.Add(new GeometryModel3D(capeMesh,
                new DiffuseMaterial(CapeBrush(capeTex))));
        }

        group.Freeze();
        return group;
    }

    /// <summary>皮肤动画类型：静止 / 走路 / 跑步（参照 Minecraft 原版动作节奏）</summary>
    public enum SkinAnimation { Idle, Walk, Run }

    /// <summary>可动画的 3D 皮肤模型：包含模型组和各个部位的关节旋转句柄（含披风飘动）</summary>
    public sealed class AnimatedSkin
    {
        public Model3DGroup Group { get; }
        private readonly RotateTransform3D _head;
        private readonly RotateTransform3D _body;
        private readonly RotateTransform3D _leftArm;
        private readonly RotateTransform3D _rightArm;
        private readonly RotateTransform3D _leftLeg;
        private readonly RotateTransform3D _rightLeg;
        private readonly RotateTransform3D _capeX;
        private readonly RotateTransform3D _capeY;

        internal AnimatedSkin(Model3DGroup group,
            RotateTransform3D head, RotateTransform3D body,
            RotateTransform3D leftArm, RotateTransform3D rightArm,
            RotateTransform3D leftLeg, RotateTransform3D rightLeg,
            RotateTransform3D capeX, RotateTransform3D capeY)
        {
            Group = group;
            _head = head; _body = body;
            _leftArm = leftArm; _rightArm = rightArm;
            _leftLeg = leftLeg; _rightLeg = rightLeg;
            _capeX = capeX; _capeY = capeY;
        }

        /// <summary>
        /// 按时间推进播放动作。参照 Minecraft 原版动画：
        /// 走路 ~2 步/秒（腿/臂摆幅约 35°），跑步 ~4 步/秒（摆幅约 55°，身体轻微前倾）。
        /// 手臂与同侧腿摆动方向相反，左右交替。披风随步伐上下飘动。
        /// </summary>
        public void ApplyAnimation(SkinAnimation animation, double time)
        {
            switch (animation)
            {
                case SkinAnimation.Idle:
                    SetAngle(_head, 0); SetAngle(_body, 0);
                    SetAngle(_leftArm, 0); SetAngle(_rightArm, 0);
                    SetAngle(_leftLeg, 0); SetAngle(_rightLeg, 0);
                    SetAngle(_capeX, 0); SetAngle(_capeY, 0);
                    break;
                case SkinAnimation.Walk:
                {
                    var s = Math.Sin(time * Math.PI * 2);   // 每秒一步
                    SetAngle(_leftLeg, s * 35);
                    SetAngle(_rightLeg, -s * 35);
                    SetAngle(_leftArm, -s * 25);
                    SetAngle(_rightArm, s * 25);
                    SetAngle(_body, 2);                      // 走路仅轻微前倾
                    SetAngle(_head, 0);
                    // 披风轻微飘动（角度很小，避免穿模）
                    SetAngle(_capeX, Math.Abs(s) * 3);
                    SetAngle(_capeY, s * 2);
                    break;
                }
                case SkinAnimation.Run:
                {
                    var s = Math.Sin(time * Math.PI * 4);   // 每秒两步
                    SetAngle(_leftLeg, s * 55);
                    SetAngle(_rightLeg, -s * 55);
                    SetAngle(_leftArm, -s * 40);
                    SetAngle(_rightArm, s * 40);
                    SetAngle(_body, 7);                      // 跑步轻微前倾（不夸张）
                    SetAngle(_head, 3);
                    // 披风小幅飘动（角度小，避免穿模）
                    SetAngle(_capeX, Math.Abs(s) * 5);
                    SetAngle(_capeY, s * 3);
                    break;
                }
            }
        }

        private static void SetAngle(RotateTransform3D rotate, double angle)
        {
            if (rotate.Rotation is AxisAngleRotation3D r) r.Angle = angle;
        }
    }

    /// <summary>构建可旋转、可播放走路/跑步动画的 3D 皮肤模型（Y 轴为中心，脚底 y=0，身高 32）</summary>
    public static AnimatedSkin? BuildAnimatedModel(string? skinPath, string? capePath, bool slim)
    {
        var tex = LoadTextureScaled(skinPath);
        if (tex == null) return null;
        var tw = (double)tex.PixelWidth / TextureScale;
        var th = (double)tex.PixelHeight / TextureScale;

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(0xCC, 0xCC, 0xCC)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x3C, 0x3C, 0x3C),
            new Vector3D(0.25, -0.85, -0.7)));

        var brush = new ImageBrush(tex) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        var material = new DiffuseMaterial(brush);

        var armW = slim ? 3.0 : 4.0;
        var aw = (int)armW;
        var legacy = th < 64;

        // 各部位关节旋转句柄（绕 X 轴前后摆动）
        var headRot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));
        var bodyRot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));
        var leftArmRot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));
        var rightArmRot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));
        var leftLegRot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));
        var rightLegRot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));

        var headTransform = JointTransform(24, headRot);
        var bodyTransform = JointTransform(12, bodyRot);
        var leftArmTransform = JointTransform(24, leftArmRot);
        var rightArmTransform = JointTransform(24, rightArmRot);
        var leftLegTransform = JointTransform(12, leftLegRot);
        var rightLegTransform = JointTransform(12, rightLegRot);

        // ---- 基础层（每个部位独立网格，便于关节动画） ----
        var headMesh = new MeshGeometry3D();
        AddBox(headMesh, -4, 24, -4, 4, 32, 4, tw, th,
            new Uv(8, 0, 8, 8), new Uv(16, 0, 8, 8), new Uv(0, 8, 8, 8),
            new Uv(8, 8, 8, 8), new Uv(16, 8, 8, 8), new Uv(24, 8, 8, 8));
        AddUvBoundsMarker(headMesh);
        group.Children.Add(new GeometryModel3D(headMesh, material) { Transform = headTransform });

        var bodyMesh = new MeshGeometry3D();
        AddBox(bodyMesh, -4, 12, -2, 4, 24, 2, tw, th,
            new Uv(20, 16, 8, 4), new Uv(28, 16, 8, 4), new Uv(16, 20, 4, 12),
            new Uv(20, 20, 8, 12), new Uv(28, 20, 4, 12), new Uv(32, 20, 8, 12));
        AddUvBoundsMarker(bodyMesh);
        group.Children.Add(new GeometryModel3D(bodyMesh, material) { Transform = bodyTransform });

        var rightArmMesh = new MeshGeometry3D();
        AddBox(rightArmMesh, -4 - armW, 12, -2, -4, 24, 2, tw, th,
            new Uv(44, 16, aw, 4), new Uv(44 + aw, 16, aw, 4),
            new Uv(40, 20, aw, 12), new Uv(40 + aw, 20, aw, 12),
            new Uv(40 + aw * 2, 20, aw, 12), new Uv(40 + aw * 3, 20, aw, 12));
        AddUvBoundsMarker(rightArmMesh);
        group.Children.Add(new GeometryModel3D(rightArmMesh, material) { Transform = rightArmTransform });

        var leftArmMesh = new MeshGeometry3D();
        AddBox(leftArmMesh, 4, 12, -2, 4 + armW, 24, 2, tw, th,
            legacy ? new Uv(44, 16, aw, 4) : new Uv(36, 48, aw, 4),
            legacy ? new Uv(44 + aw, 16, aw, 4) : new Uv(36 + aw, 48, aw, 4),
            legacy ? new Uv(40 + aw * 2, 20, aw, 12) : new Uv(32 + aw * 2, 52, aw, 12),
            legacy ? new Uv(40 + aw, 20, aw, 12) : new Uv(32 + aw, 52, aw, 12),
            legacy ? new Uv(40, 20, aw, 12) : new Uv(32, 52, aw, 12),
            legacy ? new Uv(40 + aw * 3, 20, aw, 12) : new Uv(32 + aw * 3, 52, aw, 12));
        AddUvBoundsMarker(leftArmMesh);
        group.Children.Add(new GeometryModel3D(leftArmMesh, material) { Transform = leftArmTransform });

        var rightLegMesh = new MeshGeometry3D();
        AddBox(rightLegMesh, -4, 0, -2, 0, 12, 2, tw, th,
            new Uv(4, 16, 4, 4), new Uv(8, 16, 4, 4), new Uv(0, 20, 4, 12),
            new Uv(4, 20, 4, 12), new Uv(8, 20, 4, 12), new Uv(12, 20, 4, 12));
        AddUvBoundsMarker(rightLegMesh);
        group.Children.Add(new GeometryModel3D(rightLegMesh, material) { Transform = rightLegTransform });

        var leftLegMesh = new MeshGeometry3D();
        AddBox(leftLegMesh, 0, 0, -2, 4, 12, 2, tw, th,
            legacy ? new Uv(4, 16, 4, 4) : new Uv(20, 48, 4, 4),
            legacy ? new Uv(8, 16, 4, 4) : new Uv(24, 48, 4, 4),
            legacy ? new Uv(8, 20, 4, 12) : new Uv(24, 52, 4, 12),
            legacy ? new Uv(4, 20, 4, 12) : new Uv(20, 52, 4, 12),
            legacy ? new Uv(0, 20, 4, 12) : new Uv(16, 52, 4, 12),
            legacy ? new Uv(12, 20, 4, 12) : new Uv(28, 52, 4, 12));
        AddUvBoundsMarker(leftLegMesh);
        group.Children.Add(new GeometryModel3D(leftLegMesh, material) { Transform = leftLegTransform });

        // ---- 覆盖层（第二层：帽/外套/袖子/裤子，附着在对应部位随动） ----
        const bool RenderSecondLayer = true;
        if (RenderSecondLayer && th >= 64)
        {
            const double eH = 1.0;    // 头部（帽层），明显大于基础层，避免 Z-fighting 线条
            const double eB = 0.5;    // 身体 / 手臂 / 腿

            var hatMesh = new MeshGeometry3D();
            AddBoxAlphaClipped(hatMesh, -4 - eH, 24 - eH, -4 - eH, 4 + eH, 32 + eH, 4 + eH, tw, th, tex,
                new Uv(40, 0, 8, 8), new Uv(48, 0, 8, 8), new Uv(32, 8, 8, 8),
                new Uv(40, 8, 8, 8), new Uv(48, 8, 8, 8), new Uv(56, 8, 8, 8));
            AddUvBoundsMarker(hatMesh);
            group.Children.Add(new GeometryModel3D(hatMesh, material) { Transform = headTransform });

            var jacketMesh = new MeshGeometry3D();
            AddBoxAlphaClipped(jacketMesh, -4 - eB, 12 - eB, -2 - eB, 4 + eB, 24 + eB, 2 + eB, tw, th, tex,
                new Uv(20, 32, 8, 4), new Uv(28, 32, 8, 4), new Uv(16, 36, 4, 12),
                new Uv(20, 36, 8, 12), new Uv(28, 36, 4, 12), new Uv(32, 36, 8, 12));
            AddUvBoundsMarker(jacketMesh);
            group.Children.Add(new GeometryModel3D(jacketMesh, material) { Transform = bodyTransform });

            var rightSleeveMesh = new MeshGeometry3D();
            AddBoxAlphaClipped(rightSleeveMesh, -4 - armW - eB, 12 - eB, -2 - eB, -4 + eB, 24 + eB, 2 + eB, tw, th, tex,
                new Uv(44, 32, aw, 4), new Uv(44 + aw, 32, aw, 4),
                new Uv(40, 36, aw, 12), new Uv(40 + aw, 36, aw, 12),
                new Uv(40 + aw * 2, 36, aw, 12), new Uv(40 + aw * 3, 36, aw, 12));
            AddUvBoundsMarker(rightSleeveMesh);
            group.Children.Add(new GeometryModel3D(rightSleeveMesh, material) { Transform = rightArmTransform });

            var leftSleeveMesh = new MeshGeometry3D();
            AddBoxAlphaClipped(leftSleeveMesh, 4 - eB, 12 - eB, -2 - eB, 4 + armW + eB, 24 + eB, 2 + eB, tw, th, tex,
                new Uv(52, 48, aw, 4), new Uv(52 + aw, 48, aw, 4),
                new Uv(48 + aw * 2, 52, aw, 12), new Uv(48 + aw, 52, aw, 12),
                new Uv(48, 52, aw, 12), new Uv(48 + aw * 3, 52, aw, 12));
            AddUvBoundsMarker(leftSleeveMesh);
            group.Children.Add(new GeometryModel3D(leftSleeveMesh, material) { Transform = leftArmTransform });

            var rightPantMesh = new MeshGeometry3D();
            AddBoxAlphaClipped(rightPantMesh, -4 - eB, -eB, -2 - eB, 0 + eB, 12 + eB, 2 + eB, tw, th, tex,
                new Uv(4, 32, 4, 4), new Uv(8, 32, 4, 4), new Uv(0, 36, 4, 12),
                new Uv(4, 36, 4, 12), new Uv(8, 36, 4, 12), new Uv(12, 36, 4, 12));
            AddUvBoundsMarker(rightPantMesh);
            group.Children.Add(new GeometryModel3D(rightPantMesh, material) { Transform = rightLegTransform });

            var leftPantMesh = new MeshGeometry3D();
            AddBoxAlphaClipped(leftPantMesh, 0 - eB, -eB, -2 - eB, 4 + eB, 12 + eB, 2 + eB, tw, th, tex,
                new Uv(4, 48, 4, 4), new Uv(8, 48, 4, 4), new Uv(8, 52, 4, 12),
                new Uv(4, 52, 4, 12), new Uv(0, 52, 4, 12), new Uv(12, 52, 4, 12));
            AddUvBoundsMarker(leftPantMesh);
            group.Children.Add(new GeometryModel3D(leftPantMesh, material) { Transform = leftLegTransform });
        }

        // ---- 披风（挂在身后，10×16×1，官方几何锚点 y=8~24，可飘动） ----
        var capeRotX = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0));
        var capeRotY = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
        var capeTransform = new Transform3DGroup();
        capeTransform.Children.Add(new TranslateTransform3D(0, -24, 0));
        capeTransform.Children.Add(capeRotX);
        capeTransform.Children.Add(capeRotY);
        capeTransform.Children.Add(new TranslateTransform3D(0, 24, 0));

        var capeTex = LoadTextureScaled(capePath);
        if (capeTex != null)
        {
            var cw = (double)capeTex.PixelWidth / TextureScale;
            var ch = (double)capeTex.PixelHeight / TextureScale;
            var capeMesh = new MeshGeometry3D();
            AddBox(capeMesh, -5, 8, -4.4, 5, 24, -3.4, cw, ch,
                new Uv(1, 0, 10, 1), new Uv(11, 0, 10, 1), new Uv(11, 1, 1, 16),
                new Uv(12, 1, 10, 16), new Uv(0, 1, 1, 16), new Uv(1, 1, 10, 16));
            AddUvBoundsMarker(capeMesh);
            group.Children.Add(new GeometryModel3D(capeMesh,
                new DiffuseMaterial(CapeBrush(capeTex))) { Transform = capeTransform });
        }

        // 注意：不能 group.Freeze()——动画需要实时修改各部位关节旋转角度，冻结后无法播放动作
        return new AnimatedSkin(group, headRot, bodyRot, leftArmRot, rightArmRot, leftLegRot, rightLegRot, capeRotX, capeRotY);
    }

    /// <summary>构建绕关节旋转的变换组：先平移到关节 → 旋转 → 移回（模拟部位绕关节摆动）</summary>
    private static Transform3DGroup JointTransform(double jointY, RotateTransform3D rotate)
    {
        var group = new Transform3DGroup();
        group.Children.Add(new TranslateTransform3D(0, -jointY, 0));
        group.Children.Add(rotate);
        group.Children.Add(new TranslateTransform3D(0, jointY, 0));
        return group;
    }

    /// <summary>披风贴图画笔（关闭平滑，保持像素硬边）</summary>
    private static ImageBrush CapeBrush(BitmapSource tex)
    {
        var brush = new ImageBrush(tex) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        return brush;
    }

    /// <summary>
    /// 在网格末尾加入一个零面积（不可见）标记面，把网格的 UV 包围盒精确撑满到 [0,1]×[0,1]。
    /// WPF 3D 的 ImageBrush 默认把整张贴图拉伸到「网格 UV 包围盒」，若不撑满会导致
    /// 贴图整体被放大并偏移（表现为"贴图偏了/对不齐"）。
    /// </summary>
    private static void AddUvBoundsMarker(MeshGeometry3D mesh)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(-4, 0, -2));
        mesh.Positions.Add(new Point3D(-4, 0, -2));
        mesh.Positions.Add(new Point3D(-4, 0, -2));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        // 三个顶点重合 → 三角形面积为 0，不会渲染出任何像素
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 1);
        mesh.TriangleIndices.Add(i + 2);
    }

    /// <summary>添加一个长方体：6 个面各自的贴图区域</summary>
    private static void AddBox(MeshGeometry3D mesh,
        double x0, double y0, double z0, double x1, double y1, double z1,
        double texW, double texH,
        Uv top, Uv bottom, Uv right, Uv front, Uv left, Uv back)
    {
        // +Y 顶面：贴图 v 方向对应 z（v_min↔背面 z0，v_max↔正面 z1）。
        // 依据皮肤展开图折叠规则：顶面贴图区正好在正面区上方，两者的接缝是"正面顶边"，
        // 所以顶面贴图的下边缘必须落在正面一侧，否则头顶花纹与侧面接不上（错位）。
        AddFace(mesh, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), top, texW, texH, true);
        // -Y 底面：贴图 v 方向对应 x（v_min↔x0，v_max↔x1），u 方向对应 z（u_min↔正面 z1）
        AddFace(mesh, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), bottom, texW, texH, true);
        // -X 面（角色右侧）→ 贴图 right 区域
        AddFace(mesh, new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), right, texW, texH, true);
        // +X 面（角色左侧）→ 贴图 left 区域
        AddFace(mesh, new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), left, texW, texH, true);
        // +Z 正面
        AddFace(mesh, new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), front, texW, texH, true);
        // -Z 背面
        AddFace(mesh, new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), back, texW, texH, true);
    }

    /// <summary>
    /// 按贴图 alpha 裁剪的盒子：每个面只生成「不透明像素」对应的微型四边形。
    /// 用于第二层（帽/外套/袖/裤），透明区域不生成网格，从根本上消除 WPF 3D
    /// 中透明像素参与深度混合导致的"线条/孔洞"（等价于 Minecraft 的 alpha 测试）。
    /// </summary>
    private static void AddBoxAlphaClipped(MeshGeometry3D mesh,
        double x0, double y0, double z0, double x1, double y1, double z1,
        double texW, double texH, BitmapSource tex,
        Uv top, Uv bottom, Uv right, Uv front, Uv left, Uv back)
    {
        var scale = (int)Math.Max(1, tex.PixelWidth / texW);
        var texPixels = new byte[tex.PixelWidth * tex.PixelHeight * 4];
        tex.CopyPixels(texPixels, tex.PixelWidth * 4, 0);

        AddFaceAlphaClipped(mesh, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), top, texW, texH, texPixels, tex.PixelWidth, scale);
        AddFaceAlphaClipped(mesh, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), bottom, texW, texH, texPixels, tex.PixelWidth, scale);
        AddFaceAlphaClipped(mesh, new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), right, texW, texH, texPixels, tex.PixelWidth, scale);
        AddFaceAlphaClipped(mesh, new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), left, texW, texH, texPixels, tex.PixelWidth, scale);
        AddFaceAlphaClipped(mesh, new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), front, texW, texH, texPixels, tex.PixelWidth, scale);
        AddFaceAlphaClipped(mesh, new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), back, texW, texH, texPixels, tex.PixelWidth, scale);
    }

    /// <summary>按像素生成不透明区域的微型四边形（flip=true 环绕，与 AddFace 一致）。</summary>
    private static void AddFaceAlphaClipped(MeshGeometry3D mesh,
        Point3D tl, Point3D tr, Point3D br, Point3D bl,
        Uv uv, double texW, double texH,
        byte[] texPixels, int texPixelWidth, int scale)
    {
        var pw = uv.W;
        var ph = uv.H;

        // 该面完全没有不透明像素 → 整体跳过（不产生任何顶点）
        var anyVisible = false;
        for (var j = 0; j < ph && !anyVisible; j++)
            for (var i = 0; i < pw; i++)
            {
                if (PixelAlpha(texPixels, texPixelWidth, (uv.X + i) * scale, (uv.Y + j) * scale) > 0)
                {
                    anyVisible = true;
                    break;
                }
            }
        if (!anyVisible) return;

        for (var j = 0; j < ph; j++)
        {
            for (var i = 0; i < pw; i++)
            {
                var alpha = PixelAlpha(texPixels, texPixelWidth, (uv.X + i) * scale, (uv.Y + j) * scale);
                if (alpha <= 0) continue;

                var fu0 = (double)i / pw;
                var fu1 = (double)(i + 1) / pw;
                var fv0 = (double)j / ph;
                var fv1 = (double)(j + 1) / ph;
                var p00 = LerpQuad(tl, tr, bl, br, fu0, fv0);
                var p10 = LerpQuad(tl, tr, bl, br, fu1, fv0);
                var p11 = LerpQuad(tl, tr, bl, br, fu1, fv1);
                var p01 = LerpQuad(tl, tr, bl, br, fu0, fv1);

                var px0 = (uv.X + i) / texW;
                var py0 = (uv.Y + j) / texH;
                var px1 = (uv.X + i + 1) / texW;
                var py1 = (uv.Y + j + 1) / texH;

                var idx = mesh.Positions.Count;
                mesh.Positions.Add(p00);
                mesh.Positions.Add(p10);
                mesh.Positions.Add(p11);
                mesh.Positions.Add(p01);
                mesh.TextureCoordinates.Add(new Point(px0, py0));
                mesh.TextureCoordinates.Add(new Point(px1, py0));
                mesh.TextureCoordinates.Add(new Point(px1, py1));
                mesh.TextureCoordinates.Add(new Point(px0, py1));
                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 2);
                mesh.TriangleIndices.Add(idx + 1);
                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 3);
                mesh.TriangleIndices.Add(idx + 2);
            }
        }
    }

    /// <summary>读取放大贴图中某像素的 alpha（texPixels 为 Bgra32）。</summary>
    private static byte PixelAlpha(byte[] texPixels, int texPixelWidth, int x, int y)
    {
        var i = (y * texPixelWidth + x) * 4 + 3;
        return i < texPixels.Length ? texPixels[i] : (byte)0;
    }

    /// <summary>在由 tl/tr/br/bl 定义的四边形上按 (fu,fv) 双线性插值。</summary>
    private static Point3D LerpQuad(Point3D tl, Point3D tr, Point3D bl, Point3D br, double fu, double fv)
    {
        var top = tl + (tr - tl) * fu;
        var bottom = bl + (br - bl) * fu;
        return top + (bottom - top) * fv;
    }

    /// <summary>
    /// 添加一个四边形面。WPF 默认背面剔除，顶点环绕方向必须是逆时针（从面外侧看），
    /// 否则正面会被剔除、显示出模型的背面（即贴图错位）。<paramref name="flip"/> 用于反转环绕方向。
    /// </summary>
    private static void AddFace(MeshGeometry3D mesh,
        Point3D tl, Point3D tr, Point3D br, Point3D bl, Uv uv, double texW, double texH, bool flip)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(tl);
        mesh.Positions.Add(tr);
        mesh.Positions.Add(br);
        mesh.Positions.Add(bl);

        var u0 = uv.X / texW;
        var v0 = uv.Y / texH;
        var u1 = (uv.X + uv.W) / texW;
        var v1 = (uv.Y + uv.H) / texH;
        mesh.TextureCoordinates.Add(new Point(u0, v0));
        mesh.TextureCoordinates.Add(new Point(u1, v0));
        mesh.TextureCoordinates.Add(new Point(u1, v1));
        mesh.TextureCoordinates.Add(new Point(u0, v1));

        if (flip)
        {
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 3);
            mesh.TriangleIndices.Add(i + 2);
        }
        else
        {
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }
    }

    // ===================== 默认皮肤（9 个官方角色，启动时自动下载） =====================

    /// <summary>一个官方默认皮肤的定义。</summary>
    public sealed record DefaultSkinInfo(
        string Name,       // 皮肤库显示名
        string FileName,   // 存到 CUB/skins 的文件名（小写）
        string Variant,    // classic | slim
        string WikiFile,   // Minecraft Wiki 上的贴图文件名（用于 API 查询真实下载地址）
        string? OfficialUrl); // Mojang 官方直链（仅 Steve/Alex 有）

    /// <summary>9 个官方默认皮肤（Steve/Alex 用官方直链，其余通过 Minecraft Wiki API 解析下载地址）。</summary>
    public static readonly IReadOnlyList<DefaultSkinInfo> DefaultSkins = new[]
    {
        new DefaultSkinInfo("史蒂夫", "steve.png", "classic", "Steve (classic texture) JE6.png", "https://assets.mojang.com/SkinTemplates/steve.png"),
        new DefaultSkinInfo("艾利克斯", "alex.png", "slim", "Alex (classic texture) JE2.png", "https://assets.mojang.com/SkinTemplates/alex.png"),
        new DefaultSkinInfo("Ari", "ari.png", "classic", "Ari (classic texture) JE1.png", null),
        new DefaultSkinInfo("Kai", "kai.png", "classic", "Kai (classic texture) JE1.png", null),
        new DefaultSkinInfo("Noor", "noor.png", "slim", "Noor (classic texture) JE1.png", null),
        new DefaultSkinInfo("Sunny", "sunny.png", "classic", "Sunny (classic texture) JE1.png", null),
        new DefaultSkinInfo("Zuri", "zuri.png", "classic", "Zuri (classic texture) JE1.png", null),
        new DefaultSkinInfo("Efe", "efe.png", "slim", "Efe (classic texture) JE1.png", null),
        new DefaultSkinInfo("Makena", "makena.png", "slim", "Makena (classic texture) JE1.png", null),
    };

    /// <summary>CUB/skins 下官方默认皮肤文件路径</summary>
    public static string DefaultSkinPath(string fileName) =>
        Path.Combine(Store.CubSkinsDir, fileName);

    /// <summary>CUB/skins 下官方原版皮肤文件路径（英文命名）</summary>
    public static string DefaultSkinPath(bool steve) =>
        DefaultSkinPath(steve ? "steve.png" : "alex.png");

    /// <summary>
    /// 确保官方默认皮肤就绪。为避免卡住界面，这里只同步写入内置回退贴图；
    /// 官方原版文件由 EnsureDefaultsAsync 在后台下载并覆盖（首次启动时执行）。
    /// </summary>
    public static string EnsureDefaultSkin(bool steve) =>
        EnsureDefaultSkin(steve ? "steve.png" : "alex.png");

    /// <summary>确保指定文件名的默认皮肤存在（缺失时复制内置资源，否则写入程序绘制的回退图）。</summary>
    public static string EnsureDefaultSkin(string fileName)
    {
        Store.EnsureDir(Store.CubSkinsDir);
        var path = DefaultSkinPath(fileName);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

        var info = DefaultSkins.FirstOrDefault(d => d.FileName == fileName);
        if (info != null)
        {
            try
            {
                if (TryExtractBuiltinSkin(info, path))
                {
                    File.WriteAllText(path + FallbackMarker, "");
                    return path;
                }
            }
            catch { /* 提取失败时继续走绘制回退 */ }
        }

        try
        {
            // 内置资源也缺失时，用程序绘制的默认皮肤占位（Steve/Alex 用各自贴图，其他角色用 Steve 占位），保证文件始终存在
            if (fileName == "alex.png") WriteFallbackSkin(false, path);
            else WriteFallbackSkin(true, path);
            File.WriteAllText(path + FallbackMarker, "");
        }
        catch { /* 写入失败时由调用方回退占位 */ }
        return path;
    }

    /// <summary>首次启动时后台下载 9 个官方默认皮肤到 CUB/skins：先复制内置资源保证可用，再尝试网络下载官方版本覆盖。</summary>
    public static async Task EnsureDefaultsAsync()
    {
        foreach (var info in DefaultSkins)
        {
            var path = DefaultSkinPath(info.FileName);
            var isFallback = File.Exists(path + FallbackMarker);
            if (File.Exists(path) && new FileInfo(path).Length > 0 && !isFallback) continue;

            try
            {
                Store.EnsureDir(Store.CubSkinsDir);

                // 1) 文件缺失时先提取嵌入回退资源，保证离线也能用
                if (!File.Exists(path))
                {
                    if (TryExtractBuiltinSkin(info, path))
                        File.WriteAllText(path + FallbackMarker, "");
                }

                // 2) 解析下载链接（官方直链或 Minecraft Wiki API）并下载覆盖
                var url = await ResolveSkinUrlAsync(info);
                if (string.IsNullOrEmpty(url)) continue;
                var bytes = await Downloader.Client.GetByteArrayAsync(url);
                if (bytes.Length == 0) continue;
                await File.WriteAllBytesAsync(path, bytes);
                if (File.Exists(path + FallbackMarker)) File.Delete(path + FallbackMarker);
            }
            catch { /* 离线时保留内置回退贴图 */ }
        }
    }

    /// <summary>标记文件：存在说明当前皮肤是内置回退版本，等待官方文件覆盖</summary>
    private const string FallbackMarker = ".fallback";

    /// <summary>从嵌入资源提取内置默认皮肤到目标路径（单文件发布后皮肤资源内嵌于 exe）。</summary>
    private static bool TryExtractBuiltinSkin(DefaultSkinInfo info, string dest)
    {
        var localName = char.ToUpperInvariant(info.FileName[0]) + info.FileName.Substring(1); // Steve.png
        var resourceName = $"CraftUnifiedBooter.Skins.{localName}";
        using var stream = typeof(SkinService).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return false;
        using var fs = File.Create(dest);
        stream.CopyTo(fs);
        return true;
    }

    /// <summary>解析皮肤下载链接：优先官方直链；否则通过 Minecraft Wiki API 查询贴图真实地址。</summary>
    private static async Task<string?> ResolveSkinUrlAsync(DefaultSkinInfo info)
    {
        if (!string.IsNullOrEmpty(info.OfficialUrl)) return info.OfficialUrl;
        try
        {
            var wikiTitle = info.WikiFile.Replace(" ", "_");
            var api = $"https://minecraft.wiki/api.php?action=query&titles=File:{Uri.EscapeDataString(wikiTitle)}&prop=imageinfo&iiprop=url&format=json";
            var json = await Downloader.Client.GetStringAsync(api);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("query", out var query) &&
                query.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    if (page.Value.TryGetProperty("imageinfo", out var imageInfo) &&
                        imageInfo.GetArrayLength() > 0 &&
                        imageInfo[0].TryGetProperty("url", out var url))
                    {
                        return url.GetString();
                    }
                }
            }
        }
        catch { /* 解析失败由调用方回退 */ }
        return null;
    }

    /// <summary>把内置生成的默认皮肤写入指定文件</summary>
    private static void WriteFallbackSkin(bool steve, string path)
    {
        var bmp = BuildDefaultSkin(steve);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>生成经典史蒂夫 / 艾利克斯默认皮肤（64×64，仅作离线回退）</summary>
    private static BitmapSource BuildDefaultSkin(bool steve)
    {
        const int S = 64;
        var px = new byte[S * S * 4];

        var skin = steve ? Color.FromRgb(0xC6, 0x9D, 0x7A) : Color.FromRgb(0xF9, 0xDC, 0xC4);
        var hair = steve ? Color.FromRgb(0x4A, 0x37, 0x28) : Color.FromRgb(0xC6, 0x7C, 0x3D);
        var shirt = steve ? Color.FromRgb(0x2E, 0x7D, 0x8F) : Color.FromRgb(0x4C, 0xAF, 0x7D);
        var pants = steve ? Color.FromRgb(0x3B, 0x4A, 0x8C) : Color.FromRgb(0x8B, 0x6B, 0x4A);
        var shoes = steve ? Color.FromRgb(0x4A, 0x4A, 0x4A) : Color.FromRgb(0x6B, 0x4A, 0x2E);
        var eye = Color.FromRgb(0x3A, 0x3A, 0x8A);
        var mouth = Color.FromRgb(0x8B, 0x5E, 0x3C);

        void Rect(int x, int y, int w, int h, Color c)
        {
            for (var yy = y; yy < y + h; yy++)
                for (var xx = x; xx < x + w; xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= S || yy >= S) continue;
                    var i = (yy * S + xx) * 4;
                    px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255;
                }
        }

        var aw = steve ? 4 : 3;

        // 头：顶/侧/后为头发，正面为脸
        Rect(8, 0, 8, 8, hair);
        Rect(16, 0, 8, 8, skin);
        Rect(0, 8, 8, 8, hair);
        Rect(16, 8, 8, 8, hair);
        Rect(24, 8, 8, 8, hair);
        Rect(8, 8, 8, 8, skin);
        Rect(8, 8, 8, 2, hair);            // 刘海
        Rect(9, 12, 1, 1, Colors.White);   // 左眼白
        Rect(10, 12, 1, 1, eye);           // 左瞳
        Rect(13, 12, 1, 1, Colors.White);  // 右眼白
        Rect(14, 12, 1, 1, eye);           // 右瞳
        Rect(11, 14, 2, 1, mouth);         // 嘴

        // 身体：上衣
        Rect(20, 16, 8, 4, shirt);
        Rect(28, 16, 8, 4, shirt);
        Rect(16, 20, 4, 12, shirt);
        Rect(20, 20, 8, 12, shirt);
        Rect(28, 20, 4, 12, shirt);
        Rect(32, 20, 8, 12, shirt);

        // 手臂：短袖
        foreach (var ax in new[] { 44, 44 + aw, 44 + aw * 2 }) Rect(ax, 16, aw, 4, shirt);
        Rect(44, 16, aw, 4, shirt);
        foreach (var ax in new[] { 40, 44, 44 + aw, 44 + aw * 2 })
        {
            Rect(ax, 20, aw, 4, shirt);
            Rect(ax, 24, aw, 8, skin);
        }
        foreach (var ax in new[] { 36, 36 + aw, 36 + aw * 2 }) Rect(ax, 48, aw, 4, shirt);
        foreach (var ax in new[] { 32, 36, 36 + aw, 36 + aw * 2 })
        {
            Rect(ax, 52, aw, 4, shirt);
            Rect(ax, 56, aw, 8, skin);
        }

        // 腿：裤子 + 鞋
        Rect(4, 16, 4, 4, pants);
        Rect(8, 16, 4, 4, shoes);
        foreach (var lx in new[] { 0, 4, 8, 12 })
        {
            Rect(lx, 20, 4, 10, pants);
            Rect(lx, 30, 4, 2, shoes);
        }
        Rect(20, 48, 4, 4, pants);
        Rect(24, 48, 4, 4, shoes);
        foreach (var lx in new[] { 16, 20, 24, 28 })
        {
            Rect(lx, 52, 4, 10, pants);
            Rect(lx, 62, 4, 2, shoes);
        }

        var result = BitmapSource.Create(S, S, 96, 96, PixelFormats.Bgra32, null, px, S * 4);
        result.Freeze();
        return result;
    }
}