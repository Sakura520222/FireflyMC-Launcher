using System.Text;
using FluentAssertions;
using Xunit;

namespace FireflyMC.Launcher.Tests.Update;

/// <summary>
/// 验证 Updater 的纯 C# Ed25519 验签实现。
/// 测试向量由 Python cryptography 库（已知正确实现）用与 appsettings 相同的密钥生成：
///   公钥 69aBvg...KT0= 与 SelfUpdate.PublicKey 一致。
/// </summary>
public class Ed25519VerifierTests
{
    private const string PublicKeyB64 = "69aBvgTl2ZInogiFQ+qogD+W6ZxOmEL4Gz22Zyt/KT0=";

    private const string SignatureB64 =
        "tFrmPt5T3mTZSYF9GVSl7K04v7eoQGoXpPE2x9q0WLvndQpG5dZ3uDdkmBfMqZDcRlB3F3b1aYNsD/5yY7HMAw==";

    private static readonly byte[] Message =
        Encoding.UTF8.GetBytes("FireflyMC self-update test vector");

    [Fact]
    public void Verify_AcceptsValidSignature()
    {
        var pub = Convert.FromBase64String(PublicKeyB64);
        var sig = Convert.FromBase64String(SignatureB64);

        Ed25519Verifier.Verify(Message, sig, pub).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsTamperedMessage()
    {
        var pub = Convert.FromBase64String(PublicKeyB64);
        var sig = Convert.FromBase64String(SignatureB64);
        var tampered = Encoding.UTF8.GetBytes("FireflyMC self-update test vector TAMPERED");

        Ed25519Verifier.Verify(tampered, sig, pub).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsTamperedSignature()
    {
        var pub = Convert.FromBase64String(PublicKeyB64);
        var sig = Convert.FromBase64String(SignatureB64);
        sig[0] ^= 0xFF;

        Ed25519Verifier.Verify(Message, sig, pub).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsWrongPublicKey()
    {
        var sig = Convert.FromBase64String(SignatureB64);
        var wrongPub = new byte[32];

        Ed25519Verifier.Verify(Message, sig, wrongPub).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsMalformedSignatureLength()
    {
        var pub = Convert.FromBase64String(PublicKeyB64);
        var badSig = new byte[63];

        Ed25519Verifier.Verify(Message, badSig, pub).Should().BeFalse();
    }
}
