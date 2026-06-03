using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MaiChartManager.Utils;

public static class AquaMaiSignatureV2
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AquaMaiSignatureBlock
    {
        public PubKeyId KeyId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 132)]
        public byte[] Signature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public byte[] Magic;
        public byte Version;
    }

    private static AquaMaiSignatureBlock? parseFromBytes(byte[] data)
    {
        var size = Marshal.SizeOf<AquaMaiSignatureBlock>();
        if (data.Length < size)
        {
            return null;
        }

        var block = data.AsSpan(data.Length - size);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(block.ToArray(), 0, ptr, size);
            var stru = Marshal.PtrToStructure<AquaMaiSignatureBlock>(ptr);
            if (!stru.Magic.AsSpan().SequenceEqual("AquaMaiSig"u8))
            {
                return null;
            }
            if (stru.Version != 1)
            {
                return null;
            }
            return stru;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public enum PubKeyId : byte
    {
        None,
        Local,
        CI,
    }

    private static readonly Dictionary<PubKeyId, byte[]> pubKeys = new()
    {
        {
            PubKeyId.Local,
            Convert.FromBase64String(
                "MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQBVoScU915wnWeDOvLsQd3uWh9NwclPhup8TT+cqiV3SB683JgQTpLYv2XRCGfH/3zIwTU2KDIXwNPsDPlOpH0QIkB0aVIDo9g6mus7cTMphq/7yjQQEPnsBQO5KbtcNxcy7mSnhykSea2Gv+iOKu1C4FOaO39zNe0HULVoqMrcCNLRkg=")
        },
        {
            PubKeyId.CI,
            Convert.FromBase64String(
                "MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQAvi9gtqbPF0g7K52lumBRiztMb5lVKbTwhzwVSVsMBUo5wXp9w86CnIh3/VErXtyneP1BBMLFDEtd4Cb11eQmxBMBuPjY61oca4gZhIxgQ8e0ki/pUhtUQwIQ48AN/gba/lq0GWBaPrwEyhSvHArHsPo2WxFczdsOO0mTgwq0bAw/tTw=")
        },
    };

    private static readonly HashSet<string> oldHashes = File.ReadAllLines(Path.Combine(StaticSettings.exeDir, "oldAquaMaiHashes.txt")).ToHashSet();

    public enum VerifyStatus
    {
        NotFound,
        InvalidKeyId,
        InvalidSignature,
        Valid,
    }

    public record VerifyResult(VerifyStatus Status, PubKeyId KeyId);

    public static VerifyResult VerifySignature(byte[] data)
    {
#if DEBUG // (wine) 0314:fixme:ncrypt:NCryptImportKey Parameter information not implemented
        try
        {
#endif
            var block = parseFromBytes(data);
            if (block == null)
            {
                var sha256 = SHA256.HashData(data);
                var hashString = Convert.ToHexString(sha256).ToLowerInvariant();
                var oldValid = oldHashes.Contains(hashString);
                return new VerifyResult(oldValid ? VerifyStatus.Valid : VerifyStatus.NotFound, PubKeyId.None);
            }

            if (!pubKeys.TryGetValue(block.Value.KeyId, out var pubKey))
            {
                return new VerifyResult(VerifyStatus.InvalidKeyId, block.Value.KeyId);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(pubKey, out _);

            var size = Marshal.SizeOf<AquaMaiSignatureBlock>();
            var dataToVerify = data.AsSpan(0, data.Length - size);
            var isValid = ecdsa.VerifyData(dataToVerify, block.Value.Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return new VerifyResult(isValid ? VerifyStatus.Valid : VerifyStatus.InvalidSignature, block.Value.KeyId);
#if DEBUG
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return new VerifyResult(VerifyStatus.Valid, PubKeyId.Local);
        }
#endif
    }
}