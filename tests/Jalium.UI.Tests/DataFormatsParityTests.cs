using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class DataFormatsParityTests
{
    [Fact]
    public void PredefinedFieldsExposeWpfNamesAndAreReadonly()
    {
        Assert.Equal("EnhancedMetafile", DataFormats.EnhancedMetafile);
        Assert.Equal("MetaFilePict", DataFormats.MetafilePicture);
        Assert.Equal("SymbolicLink", DataFormats.SymbolicLink);
        Assert.Equal("DataInterchangeFormat", DataFormats.Dif);
        Assert.Equal("TaggedImageFileFormat", DataFormats.Tiff);
        Assert.Equal("Palette", DataFormats.Palette);
        Assert.Equal("PenData", DataFormats.PenData);
        Assert.Equal("RiffAudio", DataFormats.Riff);
        Assert.Equal("WaveAudio", DataFormats.WaveAudio);

        FieldInfo field = typeof(DataFormats).GetField(nameof(DataFormats.Text))!;
        Assert.True(field.IsInitOnly);
        Assert.False(field.IsLiteral);
    }

    [Fact]
    public void GetDataFormatCachesByNameAndId()
    {
        DataFormat textByName = DataFormats.GetDataFormat(DataFormats.Text);
        DataFormat textById = DataFormats.GetDataFormat(1);
        DataFormat custom = DataFormats.GetDataFormat("Jalium.UI.Tests.Custom");

        Assert.Same(textByName, textById);
        Assert.Equal(1, textByName.Id);
        Assert.Same(custom, DataFormats.GetDataFormat(custom.Id));
        Assert.Same(custom, DataFormats.GetDataFormat("jalium.ui.tests.custom"));
    }

    [Fact]
    public void GetDataFormatRejectsInvalidNames()
    {
        Assert.Throws<ArgumentNullException>(() => DataFormats.GetDataFormat(null!));
        Assert.Throws<ArgumentException>(() => DataFormats.GetDataFormat(string.Empty));
    }
}
