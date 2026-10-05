using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class PersonWorkflowViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-13T11:00:00Z");

    [Fact]
    public void TwoPeopleStaySeparatedAndSearchUsesNameOrLocalId()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        var first = new PersonProfile(Guid.NewGuid(), "Anna Beispiel", null, Now, Now);
        var second = new PersonProfile(Guid.NewGuid(), "Berta Beispiel", null, Now, Now);
        repository.Save(first);
        repository.Save(second);
        var viewModel = new PersonListViewModel(repository, () => Now);

        viewModel.SearchText = "Berta";
        Assert.Equal(second.Id, Assert.Single(viewModel.People).Id);

        viewModel.SearchText = first.Id.ToString("N")[..8];
        Assert.Equal(first.Id, Assert.Single(viewModel.People).Id);
    }

    [Fact]
    public void CreatedPersonIsAvailableAfterViewModelRestart()
    {
        using var fixture = new DatabaseFixture();
        var people = new PersonRepository(fixture.Connections);
        var list = new PersonListViewModel(people, () => Now)
        {
            EditDisplayName = "  Beispielperson  ",
            EditDateOfBirth = new DateTime(1981, 4, 12),
            EditOtherInformation = "lokal"
        };
        Assert.Equal("Person anlegen", list.SaveButtonText);
        list.SavePersonCommand.Execute(null);
        var selected = Assert.IsType<PersonProfile>(list.SelectedPerson);
        Assert.Equal("Änderungen speichern", list.SaveButtonText);
        var restartedList = new PersonListViewModel(new PersonRepository(fixture.Connections), () => Now.AddHours(1));
        Assert.Equal("Beispielperson", restartedList.SelectedPerson?.DisplayName);
        Assert.Equal(new DateOnly(1981, 4, 12), restartedList.SelectedPerson?.DateOfBirth);
        Assert.Equal("lokal", restartedList.SelectedPerson?.Notes);
    }

    [Fact]
    public void SelectingPersonEditsSameRecordAndNewPersonReturnsToCreateMode()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        var original = new PersonProfile(
            Guid.NewGuid(), "Alter Name", "Alte Info", Now, Now,
            DateOfBirth: new DateOnly(1975, 2, 3));
        repository.Save(original);
        var viewModel = new PersonListViewModel(repository, () => Now.AddHours(1));

        Assert.Equal(original.Id, viewModel.SelectedPerson?.Id);
        Assert.Equal("Alter Name", viewModel.EditDisplayName);
        Assert.Equal(new DateTime(1975, 2, 3), viewModel.EditDateOfBirth);
        Assert.Equal("Alte Info", viewModel.EditOtherInformation);
        Assert.Equal("Änderungen speichern", viewModel.SaveButtonText);

        viewModel.EditDisplayName = "Neuer Name";
        viewModel.EditDateOfBirth = null;
        viewModel.EditOtherInformation = "Neue Info";
        viewModel.SavePersonCommand.Execute(null);

        var updated = Assert.IsType<PersonProfile>(repository.Load(original.Id));
        Assert.Equal("Neuer Name", updated.DisplayName);
        Assert.Null(updated.DateOfBirth);
        Assert.Equal("Neue Info", updated.Notes);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal(Now.AddHours(1), updated.UpdatedAt);
        Assert.Single(repository.LoadAll());

        viewModel.NewPersonCommand.Execute(null);
        Assert.Null(viewModel.SelectedPerson);
        Assert.Equal("Person anlegen", viewModel.SaveButtonText);
        Assert.Empty(viewModel.EditDisplayName);
        Assert.Null(viewModel.EditDateOfBirth);
        Assert.Empty(viewModel.EditOtherInformation);
    }

    [Fact]
    public void EditorOpensOnlyOnRequestAndCancelRestoresPreviousPerson()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        var existing = new PersonProfile(Guid.NewGuid(), "Anna Beispiel", null, Now, Now);
        repository.Save(existing);
        var viewModel = new PersonListViewModel(repository, () => Now);

        Assert.False(viewModel.IsEditorOpen);
        Assert.True(viewModel.IsSummaryVisible);

        viewModel.NewPersonCommand.Execute(null);
        Assert.True(viewModel.IsEditorOpen);
        Assert.False(viewModel.IsSummaryVisible);
        Assert.Null(viewModel.SelectedPerson);

        viewModel.CancelEditCommand.Execute(null);
        Assert.False(viewModel.IsEditorOpen);
        Assert.Equal(existing.Id, viewModel.SelectedPerson?.Id);

        viewModel.EditPersonCommand.Execute(null);
        Assert.True(viewModel.IsEditorOpen);
        viewModel.EditDisplayName = "Anna Neu";
        viewModel.SavePersonCommand.Execute(null);
        Assert.False(viewModel.IsEditorOpen);
        Assert.Equal("Anna Neu", viewModel.SelectedPerson?.DisplayName);
    }

    [Theory]
    [InlineData("Anna Beispiel", "AB")]
    [InlineData("  otto  ", "OT")]
    [InlineData("Maria von der Heide", "MH")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void InitialsUseFirstAndLastWord(string? name, string expected) =>
        Assert.Equal(expected, PersonInitials.From(name));

    [Fact]
    public void HearingAidsCanBeAddedAndRemovedWithoutTouchingOtherPeople()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        var anna = new PersonProfile(Guid.NewGuid(), "Anna Beispiel", null, Now, Now);
        var berta = new PersonProfile(Guid.NewGuid(), "Berta Beispiel", null, Now, Now);
        repository.Save(anna);
        repository.Save(berta);
        var viewModel = new PersonListViewModel(repository, () => Now);
        var changes = 0;
        viewModel.HearingAidsChanged += () => changes++;
        viewModel.SelectedPerson = viewModel.People.Single(person => person.Id == anna.Id);

        viewModel.NewAidManufacturer = " Phonak ";
        viewModel.NewAidModel = "Audéo Sphere";
        viewModel.NewAidEar = viewModel.EarOptions.Single(option => option.Value == TestedEar.Right);
        viewModel.AddHearingAidCommand.Execute(null);

        var item = Assert.Single(viewModel.HearingAids);
        Assert.Equal("Phonak Audéo Sphere", item.Aid.DisplayName);
        Assert.Equal(BadgeTone.EarRight, item.EarTone);
        Assert.Empty(viewModel.NewAidManufacturer);
        Assert.Empty(repository.LoadHearingAids(berta.Id));

        viewModel.ArchiveHearingAidCommand.Execute(item);

        Assert.Empty(viewModel.HearingAids);
        Assert.True(viewModel.HasNoHearingAids);
        Assert.Single(repository.LoadHearingAids(anna.Id, includeArchived: true));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void HearingAidWithoutEarIsRejected()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        repository.Save(new PersonProfile(Guid.NewGuid(), "Anna Beispiel", null, Now, Now));
        var viewModel = new PersonListViewModel(repository, () => Now)
        {
            NewAidManufacturer = "Phonak",
            NewAidModel = "Audéo Sphere"
        };

        viewModel.AddHearingAidCommand.Execute(null);

        Assert.Empty(viewModel.HearingAids);
        Assert.Contains("Ohr", viewModel.StatusMessage);
    }

    [Fact]
    public void HearingAidCanBeEditedInPlaceWithOwnDisplayName()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        var anna = new PersonProfile(Guid.NewGuid(), "Anna Beispiel", null, Now, Now);
        repository.Save(anna);
        var viewModel = new PersonListViewModel(repository, () => Now);
        viewModel.SelectedPerson = viewModel.People.Single();
        viewModel.NewAidManufacturer = "Phonak";
        viewModel.NewAidModel = "Audéo Sphere";
        viewModel.NewAidEar = viewModel.EarOptions.Single(option => option.Value == TestedEar.Left);
        viewModel.AddHearingAidCommand.Execute(null);
        var original = Assert.Single(viewModel.HearingAids).Aid;

        viewModel.EditHearingAidCommand.Execute(viewModel.HearingAids[0]);
        Assert.True(viewModel.IsEditingHearingAid);
        Assert.Equal("Phonak", viewModel.NewAidManufacturer);
        Assert.Empty(viewModel.NewAidDisplayName);

        viewModel.NewAidModel = "Audéo Infinio";
        viewModel.NewAidDisplayName = " Alltagsgerät ";
        viewModel.NewAidEar = viewModel.EarOptions.Single(option => option.Value == TestedEar.Right);
        viewModel.AddHearingAidCommand.Execute(null);

        var edited = Assert.Single(repository.LoadHearingAids(anna.Id, includeArchived: true));
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal("Audéo Infinio", edited.Model);
        Assert.Equal("Alltagsgerät", edited.DisplayName);
        Assert.Equal(TestedEar.Right, edited.Ear);
        Assert.False(viewModel.IsEditingHearingAid);
        Assert.Empty(viewModel.NewAidModel);
    }

    [Fact]
    public void DeletingHearingAidRequiresConfirmation()
    {
        using var fixture = new DatabaseFixture();
        var repository = new PersonRepository(fixture.Connections);
        var anna = new PersonProfile(Guid.NewGuid(), "Anna Beispiel", null, Now, Now);
        repository.Save(anna);
        repository.SaveHearingAid(new PersonHearingAid(Guid.NewGuid(), anna.Id, "Phonak", "Audéo Sphere", "Phonak Audéo Sphere", TestedEar.Left));
        var confirmation = new FixedConfirmation(false);
        var viewModel = new PersonListViewModel(repository, () => Now, confirmation);
        viewModel.SelectedPerson = viewModel.People.Single();

        viewModel.ArchiveHearingAidCommand.Execute(viewModel.HearingAids[0]);
        Assert.Single(viewModel.HearingAids);

        confirmation.Result = true;
        viewModel.ArchiveHearingAidCommand.Execute(viewModel.HearingAids[0]);
        Assert.Empty(viewModel.HearingAids);
        Assert.Equal(2, confirmation.Calls);
    }

    private sealed class FixedConfirmation(bool result) : IUserConfirmationService
    {
        public bool Result { get; set; } = result;
        public int Calls { get; private set; }

        public bool Confirm(string title, string message)
        {
            Calls++;
            return Result;
        }
    }

    private sealed class DatabaseFixture : IDisposable
    {
        public DatabaseFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"heardelta-person-ui-{Guid.NewGuid():N}");
            Connections = new DatabaseConnectionFactory(Path.Combine(DirectoryPath, "sessions.db"));
            new DatabaseSchemaInitializer(Connections).Initialize();
        }

        public string DirectoryPath { get; }
        public DatabaseConnectionFactory Connections { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
