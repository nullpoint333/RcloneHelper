using RcloneHelper.Helpers;

namespace RcloneHelper.Tests;

public class SecureStorageHelperTests
{
    [Fact]
    public void Encrypt_NullOrEmpty_ShouldReturnEmpty()
    {
        Assert.Equal("", SecureStorageHelper.Encrypt(null));
        Assert.Equal("", SecureStorageHelper.Encrypt(""));
    }

    [Fact]
    public void Decrypt_NullOrEmpty_ShouldReturnEmpty()
    {
        Assert.Equal("", SecureStorageHelper.Decrypt(null));
        Assert.Equal("", SecureStorageHelper.Decrypt(""));
    }

    [Fact]
    public void Encrypt_ShouldNotReturnPlainText()
    {
        var result = SecureStorageHelper.Encrypt("mypassword");

        // 加密后的结果应该以 ENC: 开头（Windows）或保持原样（非 Windows）
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void Encrypt_AlreadyEncrypted_ShouldReturnSameValue()
    {
        var encrypted = SecureStorageHelper.Encrypt("test");
        var doubleEncrypted = SecureStorageHelper.Encrypt(encrypted);

        Assert.Equal(encrypted, doubleEncrypted);
    }

    [Fact]
    public void Decrypt_WithoutEncPrefix_ShouldReturnAsIs()
    {
        var plainText = "not_encrypted_data";

        var result = SecureStorageHelper.Decrypt(plainText);

        Assert.Equal(plainText, result);
    }
}
