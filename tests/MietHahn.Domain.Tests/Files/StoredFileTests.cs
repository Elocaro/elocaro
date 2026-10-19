using MietHahn.Domain.Files;

namespace MietHahn.Domain.Tests.Files;

public class StoredFileTests
{
    private const string Sha = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";

    [Fact]
    public void Hash_is_stored_in_lowercase()
    {
        var file = new StoredFile("receipt.pdf", "application/pdf", 10, Sha);

        Assert.Equal(Sha.ToLowerInvariant(), file.Sha256);
        Assert.False(file.IsImage);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Invalid_hash_is_rejected(string sha)
    {
        Assert.Throws<ArgumentException>(() => new StoredFile("x.pdf", "application/pdf", 1, sha));
    }

    [Fact]
    public void Too_long_name_is_truncated()
    {
        var file = new StoredFile(new string('a', 300) + ".jpg", "image/jpeg", 1, Sha);

        Assert.Equal(StoredFile.MaxNameLength, file.Name.Length);
        Assert.True(file.IsImage);
    }

    [Fact]
    public void Empty_file_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredFile("x.pdf", "application/pdf", 0, Sha));
    }
}
