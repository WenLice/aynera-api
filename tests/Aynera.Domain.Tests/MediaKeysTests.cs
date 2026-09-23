using Aynera.Domain.Media.Statics;

namespace Aynera.Domain.Tests;

public sealed class MediaKeysTests
{
    private static readonly Guid User = Guid.Parse("3f6c1a2e-0000-4000-8000-000000000001");

    [Theory]
    [InlineData(1, "3f6c1a2e-0000-4000-8000-000000000001/photo_1.jpg")]
    [InlineData(2, "3f6c1a2e-0000-4000-8000-000000000001/photo_2.jpg")]
    [InlineData(5, "3f6c1a2e-0000-4000-8000-000000000001/photo_5.jpg")]
    public void Photo_LivesInTheMembersFolder_WithItsSlotAsSuffix(int index, string expected) =>
        Assert.Equal(expected, MediaKeys.Photo(User, index));

    [Theory]
    [InlineData("video/mp4", "intro_video.mp4")]
    [InlineData("video/quicktime", "intro_video.mov")]
    [InlineData("video/webm", "intro_video.webm")]
    [InlineData("VIDEO/MP4; codecs=avc1", "intro_video.mp4")]
    public void IntroVideo_HasNoIndex_AndKeepsItsRealExtension(string contentType, string file) =>
        Assert.Equal($"{User:D}/{file}", MediaKeys.IntroVideo(User, contentType));

    [Fact]
    public void LivenessSelfie_SitsBesideThePhotos() =>
        Assert.Equal($"{User:D}/liveness.jpg", MediaKeys.Liveness(User));

    [Fact]
    public void EveryKey_StartsWithTheMembersFolder()
    {
        var folder = MediaKeys.Folder(User) + "/";
        Assert.StartsWith(folder, MediaKeys.Photo(User, 3));
        Assert.StartsWith(folder, MediaKeys.IntroVideo(User, "video/mp4"));
        Assert.StartsWith(folder, MediaKeys.Liveness(User));
    }
}
