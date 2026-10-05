using CommunityToolkit.Mvvm.ComponentModel;

namespace HearDelta.App.ViewModels;

public sealed class PersonsViewModel : ObservableObject
{
    public PersonsViewModel(PersonListViewModel list, PersonDetailViewModel detail)
    {
        List = list;
        Detail = detail;
        List.SelectionChanged += Detail.SetPerson;
        List.HearingAidsChanged += Detail.RefreshHearingAids;
        Detail.SetPerson(List.SelectedPerson);
    }

    public PersonListViewModel List { get; }
    public PersonDetailViewModel Detail { get; }
}
