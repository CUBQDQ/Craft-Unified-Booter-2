// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Security.Cryptography;
using System.Text;

namespace CraftUnifiedBooter.Services;

/// <summary>字符串保护：密钥由字符串运行时派生（字符串可被 Obfuscar HideStrings 加密）+ 字节置换 + XOR，增加静态逆向难度。</summary>
public static class Protect
{
    private static byte[]? _key;

    private static byte[] Key => _key ??= BuildKey();

    /// <summary>
    /// 运行时派生密钥：由固定种子字符串经 SHA256 派生。
    /// 该种子是字符串字面量，发布时会被 Obfuscar 的 HideStrings 加密，
    /// 静态反编译只能看到密文，无法直接还原密钥（不再暴露数字魔数）。
    /// </summary>
    private static byte[] BuildKey()
    {
        var seed = "CUB-Protect-Key-v2-7F3A9C2E-5B4D8F01-A1B2C3D4";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16];
    }

    /// <summary>解密：Base64 → 逆置换（整体反转）→ XOR → UTF8，并校验结果为合法可打印文本。</summary>
    public static string D(string data)
    {
        try
        {
            var bytes = Convert.FromBase64String(data);
            // 加密时先 XOR 再整体反转，解密时先反转再 XOR
            Array.Reverse(bytes);
            var key = Key;
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] ^= key[i % key.Length];

            var s = Encoding.UTF8.GetString(bytes);
            // 合法性校验：正常密文解出的是可打印字符串；被篡改/替换后会出现不可见控制字符，此时返回空
            foreach (var c in s)
            {
                if (c < 0x20 && c is not ('\n' or '\r' or '\t')) return "";
            }
            return s;
        }
        catch
        {
            return "";
        }
    }

#if DEBUG
    /// <summary>加密（仅开发调试用；发布版不包含此方法，避免给攻击者提供现成的加密工具）。</summary>
    public static string E(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        var key = Key;
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= key[i % key.Length];
        Array.Reverse(bytes);
        return Convert.ToBase64String(bytes);
    }
#endif
}