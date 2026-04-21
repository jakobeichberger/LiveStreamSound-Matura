using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.Shared.Tests;

public class ClassroomLaptopNameTests
{
    [Theory]
    [InlineData("HP-KB-017", LaptopCategory.Klassenraum, "017", null)]
    [InlineData("HP-KB-018-2", LaptopCategory.Klassenraum, "018", 2)]
    [InlineData("LEN-KB-065-2", LaptopCategory.Klassenraum, "065", 2)]
    [InlineData("HP-WERK-038", LaptopCategory.Werkstatt, "038", null)]
    [InlineData("R072", LaptopCategory.Raum, "072", null)]
    [InlineData("DESKTOP-A1B2C3", LaptopCategory.Sonstige, "", null)]
    [InlineData("PC-UNKNOWN", LaptopCategory.Sonstige, "", null)]
    [InlineData("", LaptopCategory.Sonstige, "", null)]
    public void Classify_MatchesExpectedPattern(
        string hostname, LaptopCategory cat, string room, int? device)
    {
        var p = ClassroomLaptopName.Classify(hostname);
        Assert.Equal(cat, p.Category);
        Assert.Equal(room, p.Room);
        Assert.Equal(device, p.DeviceIndex);
    }

    [Theory]
    [InlineData("HP-KB-017", "Raum 017")]
    [InlineData("HP-KB-018-2", "Raum 018 (Gerät 2)")]
    [InlineData("HP-WERK-038", "Werkstatt 038")]
    [InlineData("R072", "Raum 072")]
    [InlineData("DESKTOP-A1B2C3", "DESKTOP-A1B2C3")]
    public void FriendlyName_GermanRendering(string hostname, string expected) =>
        Assert.Equal(expected, ClassroomLaptopName.FriendlyName(hostname));

    [Theory]
    [InlineData("HP-KB-017", "Room 017")]
    [InlineData("HP-KB-018-2", "Room 018 (device 2)")]
    [InlineData("HP-WERK-038", "Workshop 038")]
    [InlineData("R072", "Room 072")]
    public void FriendlyNameEnglish_Rendering(string hostname, string expected) =>
        Assert.Equal(expected, ClassroomLaptopName.FriendlyNameEnglish(hostname));
}
