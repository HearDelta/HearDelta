using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"heardelta-settings-{Guid.NewGuid():N}");

    [Fact]
    public void EveryDetectedOutputIsOfferedForExplicitSelection()
    {
        var endpoints = new[]
        {
            new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24),
            new AudioEndpointDescriptor("endpoint-b", "Ausgang B", 2, 44_100, 16),
            new AudioEndpointDescriptor("endpoint-c", "Ausgang C", 8, 96_000, 32)
        };

        var viewModel = CreateViewModel(new MutableAudioEndpointService(endpoints));

        Assert.Equal(endpoints, viewModel.AudioEndpoints);
        Assert.Null(viewModel.SelectedEndpoint);
        Assert.Contains("3 aktive Audioausgänge", viewModel.StatusMessage);
    }

    [Fact]
    public void RefreshKeepsTheExplicitSelectionWhenTheEndpointStillExists()
    {
        var service = new MutableAudioEndpointService(
            new AudioEndpointDescriptor("endpoint-a", "Alter Name", 2, 48_000, 24));
        var viewModel = CreateViewModel(service);
        viewModel.SelectedEndpoint = viewModel.AudioEndpoints.Single();
        service.Endpoints =
        [
            new AudioEndpointDescriptor("endpoint-a", "Aktueller Name", 2, 48_000, 24),
            new AudioEndpointDescriptor("endpoint-b", "Weiterer Ausgang", 2, 48_000, 24)
        ];

        viewModel.RefreshEndpointsCommand.Execute(null);

        Assert.Equal(2, viewModel.AudioEndpoints.Count);
        Assert.Equal("endpoint-a", viewModel.SelectedEndpoint?.Id);
        Assert.Equal("Aktueller Name", viewModel.SelectedEndpoint?.Name);
        Assert.Equal("Aktueller Name", viewModel.AmplifierOutput);
    }

    [Fact]
    public void EmptyProfileListShowsHintUntilFirstProfileIsSaved()
    {
        var viewModel = CreateViewModel(new MutableAudioEndpointService(
            new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24)));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.True(viewModel.HasNoProfiles);

        viewModel.SelectedEndpoint = viewModel.AudioEndpoints.Single();
        viewModel.ProfileName = "Testprofil";
        viewModel.Manufacturer = "Sennheiser";
        viewModel.Model = "HD 600";
        viewModel.Design = "Ohrumschließend, offen";
        viewModel.Gain = "Low";
        viewModel.SaveProfileCommand.Execute(null);

        Assert.Single(viewModel.Profiles);
        Assert.False(viewModel.HasNoProfiles);
        Assert.Contains(nameof(SettingsViewModel.HasNoProfiles), changed);
    }

    [Fact]
    public void CatalogEqualizationIsSelectedSavedAndRestoredWithTheProfile()
    {
        var endpoints = new MutableAudioEndpointService(new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24));
        var viewModel = CreateViewModel(endpoints, new HeadphoneEqualizationCatalogService());
        viewModel.SelectedEndpoint = viewModel.AudioEndpoints.Single();

        viewModel.EqualizationSearchText = "sennheiser hd 600";
        viewModel.SelectedEqualizationMatch = viewModel.EqualizationMatches.Single(entry => entry.Name == "Sennheiser HD 600");
        viewModel.SaveProfileCommand.Execute(null);

        Assert.Equal("Sennheiser", viewModel.Manufacturer);
        Assert.Equal("HD 600", viewModel.Model);
        Assert.Equal("", viewModel.Design);
        Assert.Contains("10 Filter", viewModel.EqualizationDetails);

        var reloaded = CreateViewModel(endpoints, new HeadphoneEqualizationCatalogService());
        var equalization = reloaded.SelectedProfile?.Headphone.Equalization;
        Assert.NotNull(equalization);
        Assert.Equal("oratory1990/over-ear/Sennheiser HD 600", equalization.SourceId);
        Assert.Equal(equalization.SourceSha256, reloaded.SelectedProfile!.CreateSnapshot().HeadphoneEqualization?.SourceSha256);
        Assert.Same(equalization, reloaded.Equalization);

        reloaded.ClearEqualizationCommand.Execute(null);
        Assert.False(reloaded.HasEqualization);
    }

    [Fact]
    public void BroadSearchShowsOnlyTheFirstHundredMatches()
    {
        var viewModel = CreateViewModel(
            new MutableAudioEndpointService(new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24)),
            new HeadphoneEqualizationCatalogService());

        viewModel.EqualizationSearchText = "over-ear";

        Assert.Equal(100, viewModel.EqualizationMatches.Count);
        Assert.Contains("die ersten 100", viewModel.EqualizationSearchStatus);
    }

    [Fact]
    public void SaveIsOnlyPossibleWhenSomethingWouldChange()
    {
        var endpoints = new MutableAudioEndpointService(new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24));
        var viewModel = CreateViewModel(endpoints, new HeadphoneEqualizationCatalogService());
        viewModel.SelectedEndpoint = viewModel.AudioEndpoints.Single();
        viewModel.Manufacturer = "Sennheiser";
        viewModel.Model = "HD 600";
        viewModel.Design = "Ohrumschließend, offen";

        Assert.True(viewModel.SaveProfileCommand.CanExecute(null));
        Assert.Equal("Neues Messprofil, noch nicht gespeichert", viewModel.SaveStateText);

        viewModel.SaveProfileCommand.Execute(null);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveProfileCommand.CanExecute(null));
        Assert.Equal("Alle Änderungen sind gespeichert", viewModel.SaveStateText);

        viewModel.Gain = "High";
        Assert.True(viewModel.SaveProfileCommand.CanExecute(null));
        Assert.Equal("Nicht gespeicherte Änderungen", viewModel.SaveStateText);
        viewModel.Gain = "";
        Assert.False(viewModel.SaveProfileCommand.CanExecute(null));

        viewModel.Model = "HD 600 ";
        Assert.False(viewModel.HasUnsavedChanges);

        viewModel.EqualizationSearchText = "sennheiser hd 600";
        viewModel.SelectedEqualizationMatch = viewModel.EqualizationMatches.Single(entry => entry.Name == "Sennheiser HD 600");
        Assert.True(viewModel.SaveProfileCommand.CanExecute(null));
        viewModel.ClearEqualizationCommand.Execute(null);
        Assert.False(viewModel.HasUnsavedChanges);

        viewModel.StartVolumeDb = -50m;
        Assert.True(viewModel.HasUnsavedChanges);
        viewModel.StartVolumeDb = -60m;
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Discard, "B", "")]
    [InlineData(UnsavedChangesDecision.Cancel, "A", "")]
    [InlineData(UnsavedChangesDecision.Save, "B", "High")]
    public void SwitchingProfilesWithUnsavedChangesAsksFirst(UnsavedChangesDecision decision, string expectedProfile, string expectedSavedGain)
    {
        var prompt = new RecordingPrompt(decision);
        var viewModel = CreateViewModelWithTwoProfiles(prompt);
        var profileA = viewModel.Profiles.Single(profile => profile.Name == "A");
        var profileB = viewModel.Profiles.Single(profile => profile.Name == "B");
        viewModel.ProfileListSelection = profileA;
        Assert.Equal(0, prompt.Calls);

        viewModel.Gain = "High";
        viewModel.ProfileListSelection = profileB;

        Assert.Equal(1, prompt.Calls);
        Assert.Equal(expectedProfile, viewModel.SelectedProfile?.Name);
        Assert.Equal(expectedProfile, viewModel.ProfileListSelection?.Name);
        Assert.Equal(expectedSavedGain, viewModel.Profiles.Single(profile => profile.Name == "A").Gain);
        if (decision == UnsavedChangesDecision.Cancel)
            Assert.Equal("High", viewModel.Gain);
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Save, true, "High")]
    [InlineData(UnsavedChangesDecision.Discard, true, "")]
    [InlineData(UnsavedChangesDecision.Cancel, false, "")]
    public void ClosingTheAppAsksOnlyWithUnsavedChanges(UnsavedChangesDecision decision, bool expectedClose, string expectedSavedGain)
    {
        var prompt = new RecordingPrompt(decision);
        var viewModel = CreateViewModelWithTwoProfiles(prompt);

        Assert.True(viewModel.ConfirmClose());
        Assert.Equal(0, prompt.Calls);

        viewModel.Gain = "High";

        Assert.Equal(expectedClose, viewModel.ConfirmClose());
        Assert.Equal(1, prompt.Calls);
        Assert.Contains("Anwendung schließen", prompt.LastMessage);
        Assert.Equal(expectedSavedGain, viewModel.SelectedProfile!.Gain);
    }

    [Fact]
    public void ClosingStaysOpenWhenSavingFails()
    {
        var prompt = new RecordingPrompt(UnsavedChangesDecision.Save);
        var viewModel = CreateViewModelWithTwoProfiles(prompt);
        viewModel.NewProfileCommand.Execute(null);
        viewModel.SelectedEndpoint = null;
        viewModel.ProfileName = "Ohne Ausgang";

        Assert.False(viewModel.ConfirmClose());
        Assert.Contains("Audioausgang", viewModel.StatusMessage);
    }

    [Fact]
    public void NewProfileAsksOnlyWhenEditsWouldBeLost()
    {
        var prompt = new RecordingPrompt(UnsavedChangesDecision.Cancel);
        var viewModel = CreateViewModelWithTwoProfiles(prompt);

        viewModel.NewProfileCommand.Execute(null);
        Assert.Null(viewModel.SelectedProfile);
        viewModel.ProfileListSelection = viewModel.Profiles[0];
        Assert.Equal(0, prompt.Calls);

        viewModel.Model = "Geändert";
        viewModel.NewProfileCommand.Execute(null);
        Assert.Equal(1, prompt.Calls);
        Assert.NotNull(viewModel.SelectedProfile);

        prompt.Decision = UnsavedChangesDecision.Discard;
        viewModel.NewProfileCommand.Execute(null);
        viewModel.ProfileName = "Unfertig";
        viewModel.ProfileListSelection = viewModel.Profiles[0];
        Assert.Equal(3, prompt.Calls);
        Assert.NotNull(viewModel.SelectedProfile);
    }

    [Fact]
    public void DesignOffersFixedChoicesKeepsOldFreeTextAndWarnsForOnEar()
    {
        var viewModel = CreateViewModel(new MutableAudioEndpointService(new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24)));

        Assert.Equal(["", .. SettingsViewModel.StandardDesigns], viewModel.DesignOptions);
        Assert.DoesNotContain(viewModel.DesignOptions, option => option.Contains("In-Ear"));

        viewModel.Design = "Ohraufliegend";
        Assert.True(viewModel.HasDesignHint);
        Assert.Contains("Hinter-dem-Ohr", viewModel.DesignHint);

        viewModel.Design = "Offen";
        Assert.Contains("Offen", viewModel.DesignOptions);
        Assert.Contains("Freitextwert", viewModel.DesignHint);

        viewModel.Design = null!;
        Assert.Equal("", viewModel.Design);
        Assert.False(viewModel.HasDesignHint);
    }

    private SettingsViewModel CreateViewModelWithTwoProfiles(RecordingPrompt prompt)
    {
        var viewModel = CreateViewModel(
            new MutableAudioEndpointService(new AudioEndpointDescriptor("endpoint-a", "Ausgang A", 2, 48_000, 24)),
            unsavedChangesPrompt: prompt);
        viewModel.SelectedEndpoint = viewModel.AudioEndpoints.Single();
        viewModel.Manufacturer = "Sennheiser";
        viewModel.Model = "HD 600";
        foreach (var name in new[] { "A", "B" })
        {
            viewModel.NewProfileCommand.Execute(null);
            viewModel.ProfileName = name;
            viewModel.SaveProfileCommand.Execute(null);
        }
        prompt.Reset();
        return viewModel;
    }

    private SettingsViewModel CreateViewModel(
        IAudioEndpointService service,
        IHeadphoneEqualizationCatalogService? equalizationCatalog = null,
        IUnsavedChangesPrompt? unsavedChangesPrompt = null)
    {
        Directory.CreateDirectory(directory);
        var repository = new ProfileRepository(TestDatabase.Initialize(Path.Combine(directory, "test.db")));
        return new SettingsViewModel(service, repository, equalizationCatalog, unsavedChangesPrompt);
    }

    private sealed class RecordingPrompt(UnsavedChangesDecision decision) : IUnsavedChangesPrompt
    {
        public UnsavedChangesDecision Decision { get; set; } = decision;
        public int Calls { get; private set; }
        public string LastMessage { get; private set; } = "";

        public UnsavedChangesDecision Ask(string title, string message)
        {
            Calls++;
            LastMessage = message;
            return Decision;
        }

        public void Reset() => Calls = 0;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class MutableAudioEndpointService(params AudioEndpointDescriptor[] endpoints) : IAudioEndpointService
    {
        public IReadOnlyList<AudioEndpointDescriptor> Endpoints { get; set; } = endpoints;

        public IReadOnlyList<AudioEndpointDescriptor> GetActiveOutputs() => Endpoints;

        public Task PlayChannelTestAsync(
            string endpointId,
            bool exclusive,
            int channel,
            decimal digitalAttenuationDb,
            decimal maximumVolumeDb,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
