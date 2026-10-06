// Craft Unified Booter (CUB)
// Copyright (c) 2026 方块筑界 (Craft Unified Booter Project). 保留所有权利。
// SPDX-License-Identifier: LicenseRef-Proprietary
//
// 本文件仅限阅读与借鉴，禁止复制、克隆、使用与分发。
// 详见项目根目录下的 LICENSE 文件。

using System.Security.Cryptography;

namespace CraftUnifiedBooter.Services;

/// <summary>账户存储（离线账户增删选；微软登录预留，对应原版 electron/ipc/accounts.ts）</summary>
public static class AccountsStore
{
    private static readonly object Lock = new();
    public static List<Account> Accounts { get; private set; } = Load();

    public static event Action? Changed;

    private static List<Account> Load() => Store.ReadJson<List<Account>>("accounts.json") ?? new();

    public static void Save()
    {
        lock (Lock) Store.WriteJson("accounts.json", Accounts);
        Changed?.Invoke();
    }

    /// <summary>当前选中账户（无则第一个，仍无返回 null）</summary>
    public static Account? GetSelected()
    {
        return Accounts.FirstOrDefault(a => a.Selected) ?? Accounts.FirstOrDefault();
    }

    public static Account AddOffline(string name)
    {
        lock (Lock)
        {
            var account = new Account
            {
                Uuid = NewUuid(),
                Name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim(),
                Type = "offline",
                Selected = Accounts.Count == 0,
            };
            Accounts.Add(account);
            Save();
            return account;
        }
    }

    public static Account AddMicrosoft(Account account)
    {
        lock (Lock)
        {
            Accounts.RemoveAll(a => a.Uuid == account.Uuid);
            foreach (var a in Accounts) a.Selected = false;
            account.Selected = true;
            Accounts.Add(account);
            Save();
            return account;
        }
    }

    public static void Remove(string uuid)
    {
        lock (Lock)
        {
            var removed = Accounts.FirstOrDefault(a => a.Uuid == uuid);
            Accounts.RemoveAll(a => a.Uuid == uuid);
            if (removed?.Selected == true && Accounts.Count > 0)
                Accounts[0].Selected = true;
            Save();
        }
    }

    public static void Select(string uuid)
    {
        lock (Lock)
        {
            foreach (var a in Accounts) a.Selected = a.Uuid == uuid;
            Save();
        }
    }

    /// <summary>离线账户序号（1 起，仅统计离线账户；偶数=史蒂夫，奇数=艾利克斯）</summary>
    public static int OfflineOrdinal(Account account)
    {
        var n = 0;
        foreach (var a in Accounts)
        {
            if (a.Type == "microsoft") continue;
            n++;
            if (a.Uuid == account.Uuid) return n;
        }
        return 1;
    }

    public static string NewUuid()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex[20..]}";
    }
}
