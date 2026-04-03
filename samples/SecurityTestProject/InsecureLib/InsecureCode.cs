using System.Security.Cryptography;

namespace InsecureLib;

/// <summary>
/// Intentional security anti-patterns for integration test validation.
/// DO NOT use this code as a reference — every method here is deliberately insecure.
/// </summary>
public static class InsecureCode
{
    /// <summary>Weak hashing: SHA1 (triggers CA5350 / SCS0006).</summary>
    public static byte[] WeakHash(byte[] data)
    {
        using var sha1 = SHA1.Create();
        return sha1.ComputeHash(data);
    }

    /// <summary>Broken cipher: DES (triggers CA5351 / SCS0010).</summary>
    public static byte[] BrokenEncrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var des = DES.Create();
        des.Key = key;
        des.IV = iv;
        using var encryptor = des.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>Weak hashing: MD5 (triggers SCS0006).</summary>
    public static byte[] AlsoWeakHash(byte[] data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data);
    }
}
