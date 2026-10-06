// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CraftUnifiedBooter.Services;

/// <summary>
/// 账户头像：离线账户使用像素风头部头像（偶数序号 = 史蒂夫，奇数序号 = 艾利克斯）。
/// 正版账户后续接入真实皮肤后替换。
/// </summary>
public static class AvatarService
{
    /// <summary>史蒂夫配色（深棕头发 / 小麦肤色 / 深蓝眼睛）</summary>
    private static readonly Dictionary<char, Color> StevePalette = new()
    {
        ['H'] = Color.FromRgb(0x4A, 0x37, 0x28),
        ['S'] = Color.FromRgb(0xC6, 0x9D, 0x7A),
        ['W'] = Colors.White,
        ['P'] = Color.FromRgb(0x3A, 0x3A, 0x8A),
    };

    /// <summary>艾利克斯配色（橙棕头发 / 白皙肤色 / 绿色眼睛）</summary>
    private static readonly Dictionary<char, Color> AlexPalette = new()
    {
        ['A'] = Color.FromRgb(0xC6, 0x7C, 0x3D),
        ['S'] = Color.FromRgb(0xF9, 0xDC, 0xC4),
        ['W'] = Colors.White,
        ['P'] = Color.FromRgb(0x3A, 0x66, 0x3A),
    };

    /// <summary>史蒂夫 8x8 面部像素（H=头发 S=皮肤 W=眼白 P=瞳孔）</summary>
    private static readonly string[] SteveFace =
    {
        "HHHHHHHH",
        "HHHHHHHH",
        "HHSSSSHH",
        "HWPSSPWH",
        "HSSSSSSH",
        "HSSSSSSH",
        "HSSSSSSH",
        "HSSSSSSH",
    };

    /// <summary>艾利克斯 8x8 面部像素（A=头发 S=皮肤 W=眼白 P=瞳孔）</summary>
    private static readonly string[] AlexFace =
    {
        "AAAAAAAA",
        "AAAAAAAA",
        "AASSSSAA",
        "AWPSSPWA",
        "ASSSSSSA",
        "ASSSSSSA",
        "AASSSSAA",
        "AASSSSAA",
    };

    /// <summary>按离线账户序号生成头像：偶数 → 史蒂夫，奇数 → 艾利克斯</summary>
    public static ImageSource CreateOfflineHead(int ordinal)
    {
        var isSteve = ordinal % 2 == 0;
        return BuildFace(
            isSteve ? SteveFace : AlexFace,
            isSteve ? StevePalette : AlexPalette);
    }

    private static BitmapSource BuildFace(string[] rows, Dictionary<char, Color> palette)
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var row = rows[y];
            for (var x = 0; x < size; x++)
            {
                var color = palette.TryGetValue(row[x], out var c) ? c : Colors.Transparent;
                var i = (y * size + x) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = color.A;
            }
        }

        var bitmap = BitmapSource.Create(
            size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>生成可用于 Border.Background 的头像画笔（方块像素放大，不做平滑）</summary>
    public static ImageBrush CreateOfflineHeadBrush(int ordinal)
    {
        var brush = new ImageBrush(CreateOfflineHead(ordinal)) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        return brush;
    }

    /// <summary>加载皮肤并统一转为 Bgra32。Indexed8 等索引色 PNG 的 alpha 通道只有转为 Bgra32 后才能被 CopyPixels 正确读取。</summary>
    private static BitmapSource? LoadBgra32(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.EndInit();
        bmp.Freeze();
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        conv.Freeze();
        return conv;
    }

    /// <summary>从皮肤 PNG 裁出头部正面（8,8 起 8×8），正版账户头像用</summary>
    public static BitmapSource? CreateHeadFromSkin(string? skinPath)
    {
        if (string.IsNullOrEmpty(skinPath) || !File.Exists(skinPath)) return null;
        try
        {
            var bmp = LoadBgra32(skinPath);
            if (bmp == null || bmp.PixelWidth < 16 || bmp.PixelHeight < 16) return null;
            var crop = new CroppedBitmap(bmp, new Int32Rect(8, 8, 8, 8));
            crop.Freeze();
            return crop;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>正版账户头像画笔（皮肤缺失时返回 null）</summary>
    public static ImageBrush? CreateSkinHeadBrush(string? skinPath)
    {
        var head = CreateHeadFromSkin(skinPath);
        if (head == null) return null;
        var brush = new ImageBrush(head) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        return brush;
    }

    // ===================== 正方形头像（头部正面 + 第二层帽子叠加） =====================
    /// <summary>
    /// 按目标尺寸生成正方形平面头像：从皮肤裁出头部正面 (8,8,8,8)，整数像素放大；
    /// 并叠加第二层（帽子层 (40,8,8,8)），呈现 Minecraft 皮肤的第二层效果。64×32 老皮肤无第二层则只用基础层。
    /// </summary>
    public static BitmapSource? CreateSkinHeadBitmap(string? skinPath, int size)
    {
        if (string.IsNullOrEmpty(skinPath) || !File.Exists(skinPath)) return null;
        try
        {
            var bmp = LoadBgra32(skinPath);
            if (bmp == null || bmp.PixelWidth < 16 || bmp.PixelHeight < 16) return null;

            // 基础层头部正面 (8,8,8,8)
            var face = new byte[8 * 8 * 4];
            CopyRegion(bmp, 8, 8, 8, 8, face, 8);

            // 第二层（帽子层）头部正面 (40,8,8,8)，仅 64×64 皮肤有
            if (bmp.PixelHeight >= 64)
            {
                var hat = new byte[8 * 8 * 4];
                CopyRegion(bmp, 40, 8, 8, 8, hat, 8);
                BlendOverlay(face, hat, 8 * 8);
            }

            return ScalePixels(face, 8, 8, size);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>裁取皮肤指定区域到字节数组（Bgra32）</summary>
    private static void CopyRegion(BitmapSource src, int x, int y, int w, int h, byte[] dst, int stride)
    {
        var crop = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
        crop.CopyPixels(dst, stride * 4, 0);
    }

    /// <summary>把第二层贴图按 alpha 混合到基础层上（标准 alpha 合成）</summary>
    private static void BlendOverlay(byte[] dst, byte[] overlay, int pixelCount)
    {
        for (var i = 0; i < pixelCount; i++)
        {
            var o = i * 4;
            var sa = overlay[o + 3] / 255.0;
            if (sa <= 0) continue;
            var da = dst[o + 3] / 255.0;
            var outA = sa + da * (1 - sa);
            if (outA <= 0) continue;
            for (var c = 0; c < 3; c++)
                dst[o + c] = (byte)((overlay[o + c] * sa + dst[o + c] * da * (1 - sa)) / outA);
            dst[o + 3] = (byte)(outA * 255);
        }
    }

    /// <summary>离线账户清晰头像：偶数 → 史蒂夫，奇数 → 艾利克斯</summary>
    public static BitmapSource CreateOfflineHeadBitmap(int ordinal, int size)
    {
        var isSteve = ordinal % 2 == 0;
        var rows = isSteve ? SteveFace : AlexFace;
        var palette = isSteve ? StevePalette : AlexPalette;
        var src = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        {
            var row = rows[y];
            for (var x = 0; x < 8; x++)
            {
                var color = palette.TryGetValue(row[x], out var c) ? c : Colors.Transparent;
                var i = (y * 8 + x) * 4;
                src[i] = color.B;
                src[i + 1] = color.G;
                src[i + 2] = color.R;
                src[i + 3] = color.A;
            }
        }
        return ScalePixels(src, 8, 8, size);
    }

    // ===================== PCL2 风格头像呈现（脸层 + 第二层帽子，两层叠加） =====================

    /// <summary>PCL2 风格头像位图：Face 为基础层（脸），Hat 为第二层（帽子/头发），无第二层时为 null。</summary>
    public sealed record SkinAvatarBits(BitmapSource Face, BitmapSource? Hat);

    /// <summary>
    /// 按 PCL2 的呈现方法从皮肤裁出头像：
    /// 脸层取 (8,8,8,8)×Scale，帽子层取 (40,8,8,8)×Scale（仅当皮肤包含第二层时返回）。
    /// 位图已最近邻放大到目标尺寸，显示时帽子层应比脸层大 7/6 倍并居中叠加。
    /// </summary>
    public static SkinAvatarBits? CreateSkinAvatarBits(string? skinPath, int size)
    {
        if (string.IsNullOrEmpty(skinPath) || !File.Exists(skinPath)) return null;
        try
        {
            var bmp = LoadBgra32(skinPath);
            if (bmp == null || bmp.PixelWidth < 32 || bmp.PixelHeight < 32) return null;

            // PCL2：Scale = 图片宽度 \ 64（整数除法）
            var scale = Math.Max(1, bmp.PixelWidth / 64);
            var region = scale * 8;

            // 脸层（基础层）：(8,8,8,8)×Scale
            var facePx = new byte[region * region * 4];
            CopyRegion(bmp, scale * 8, scale * 8, region, region, facePx, region);
            var face = ScalePixels(facePx, region, region, size);

            // 帽子层（第二层）：PCL2 检测到有第二层才裁
            BitmapSource? hat = null;
            if (bmp.PixelWidth >= 64 && bmp.PixelHeight >= 32 && HasHatLayer(bmp, scale))
            {
                var hatPx = new byte[region * region * 4];
                CopyRegion(bmp, scale * 40, scale * 8, region, region, hatPx, region);
                if (!IsAllTransparent(hatPx))
                    hat = ScalePixels(hatPx, region, region, size);
            }

            return new SkinAvatarBits(face, hat);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>离线账户的 PCL2 风格头像（程序绘制皮肤，无第二层，Hat 为 null）</summary>
    public static SkinAvatarBits CreateOfflineAvatarBits(int ordinal, int size)
    {
        var face = CreateOfflineHeadBitmap(ordinal, size);
        return new SkinAvatarBits(face, null);
    }

    /// <summary>PCL2 第二层检测：皮肤含透明像素，或三个采样点颜色均不同于帽子参考点 (41,9)×Scale。</summary>
    private static bool HasHatLayer(BitmapSource bmp, int scale)
    {
        var w = bmp.PixelWidth;
        var h = bmp.PixelHeight;
        if (w < 64 || h < 32) return false;

        var p1 = GetPixel(bmp, 1, 1);
        var p2 = GetPixel(bmp, w - 1, h - 1);
        var p3 = GetPixel(bmp, w - 2, h / 2 - 2);
        var hatRef = GetPixel(bmp, scale * 41, scale * 9);

        var hasTransparent = p1.A == 0 || p2.A == 0 || p3.A == 0;
        var hatDiffers = p1 != hatRef && p2 != hatRef && p3 != hatRef;
        return hasTransparent || hatDiffers;
    }

    /// <summary>读取单个像素 BGRA（越界返回全 0）</summary>
    private static (byte B, byte G, byte R, byte A) GetPixel(BitmapSource bmp, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bmp.PixelWidth || y >= bmp.PixelHeight)
            return (0, 0, 0, 0);
        var pixel = new byte[4];
        var crop = new CroppedBitmap(bmp, new Int32Rect(x, y, 1, 1));
        crop.CopyPixels(pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    /// <summary>判断 BGRA 区域是否全透明（无实际第二层内容）</summary>
    private static bool IsAllTransparent(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 0) return false;
        return true;
    }

    /// <summary>最近邻整数放大（每个源像素映射为等宽方块，保证硬边不糊）</summary>
    private static BitmapSource ScalePixels(byte[] src, int sw, int sh, int size)
    {
        if (size < 1) size = 1;
        var dst = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var sy = y * sh / size;
            for (var x = 0; x < size; x++)
            {
                var sx = x * sw / size;
                var si = (sy * sw + sx) * 4;
                var di = (y * size + x) * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, dst, size * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>把清晰头像位图封装为画笔（保持硬边）</summary>
    public static ImageBrush CreateHeadBrush(BitmapSource bitmap)
    {
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        return brush;
    }
}