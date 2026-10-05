using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class TestPlanViewModelTests
{
    [Fact]
    public void PlanRecommendsFirstOpenStepButEveryStepCanBeStarted()
    {
        var person = new PersonProfile(Guid.NewGuid(), "Anna", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var history = new HistoryViewModel(
            new NoWordTests(),
            new NoThresholdTests(),
            new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory()),
            new AlwaysConfirm());
        var detail = new PersonDetailViewModel(history, loadPlanStatus: (_, ear) => ear == TestedEar.Left
            ? [new TestPlanStatus(TestPlanStep.HearingThreshold, DateTimeOffset.Parse("2026-10-01T10:00:00Z"), "Tonmittel 0,5–2 kHz -73 dBFS")]
            : []);
        var requested = new List<(TestPlanStep, TestedEar)>();
        detail.PlanStepRequested += (step, ear) => requested.Add((step, ear));

        detail.SetPerson(person);

        Assert.Equal(5, detail.PlanSteps.Count);
        Assert.True(detail.PlanSteps[0].IsDone);
        Assert.Equal("✓", detail.PlanSteps[0].NumberText);
        Assert.Equal(TestPlanStep.NumbersQuiet, Assert.Single(detail.PlanSteps, step => step.IsRecommended).Step);
        Assert.Contains("Schritt 2", detail.PlanHint);

        detail.StartPlanStepCommand.Execute(detail.PlanSteps[4]);
        detail.PlanEar = detail.PlanEarOptions[1];
        Assert.Equal(TestPlanStep.HearingThreshold, Assert.Single(detail.PlanSteps, step => step.IsRecommended).Step);
        detail.StartPlanStepCommand.Execute(detail.PlanSteps[0]);

        Assert.Equal([(TestPlanStep.PhonemesNoise, TestedEar.Left), (TestPlanStep.HearingThreshold, TestedEar.Right)], requested);
    }

    private sealed class NoWordTests : IMeasurementSessionRepository
    {
        public IReadOnlyList<PairedMeasurementSession> LoadAll() => [];
        public IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId) => [];
        public PairedMeasurementSession? Load(Guid id) => null;
        public void Save(Guid personId, PairedMeasurementSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class NoThresholdTests : IHearingThresholdSessionRepository
    {
        public IReadOnlyList<HearingThresholdSession> LoadAll() => [];
        public IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId) => [];
        public HearingThresholdSession? Load(Guid id) => null;
        public void Save(Guid personId, HearingThresholdSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class AlwaysConfirm : IUserConfirmationService
    {
        public bool Confirm(string title, string message) => true;
    }
}
