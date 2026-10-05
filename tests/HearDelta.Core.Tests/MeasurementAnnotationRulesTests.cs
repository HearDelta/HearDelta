using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class MeasurementAnnotationRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T09:00:00Z");

    [Fact]
    public void DefaultNameIsHearingAidOrWithoutHearingAid()
    {
        Assert.Equal("Phonak Audéo", MeasurementAnnotationRules.DefaultName(" Phonak Audéo "));
        Assert.Equal(MeasurementAnnotationRules.WithoutHearingAidName, MeasurementAnnotationRules.DefaultName((HearingAidSnapshot?)null));
    }

    [Fact]
    public void CreateTrimsNameAndDropsBlankComment()
    {
        var annotation = MeasurementAnnotationRules.Create(Guid.NewGuid(), "  Programm 2 ", " \n ", Now);

        Assert.Equal("Programm 2", annotation.Name);
        Assert.Null(annotation.Comment);
        Assert.Empty(MeasurementAnnotationRules.Validate(annotation));
    }

    [Fact]
    public void ValidateRejectsMissingIdBlankAndOverlongValues()
    {
        Assert.NotEmpty(MeasurementAnnotationRules.Validate(MeasurementAnnotationRules.Create(Guid.Empty, "Name", null, Now)));
        Assert.NotEmpty(MeasurementAnnotationRules.Validate(MeasurementAnnotationRules.Create(Guid.NewGuid(), " ", null, Now)));
        Assert.NotEmpty(MeasurementAnnotationRules.Validate(MeasurementAnnotationRules.Create(
            Guid.NewGuid(), new string('x', MeasurementAnnotationRules.MaximumNameLength + 1), null, Now)));
        Assert.NotEmpty(MeasurementAnnotationRules.Validate(MeasurementAnnotationRules.Create(
            Guid.NewGuid(), "Name", new string('x', MeasurementAnnotationRules.MaximumCommentLength + 1), Now)));
    }
}
