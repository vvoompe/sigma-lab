using Lab.Client.Maui.ViewModels;
namespace Lab.Client.Maui;

/// <summary>View лише передає дії моделі представлення та запитує явне рішення щодо конфлікту.</summary>
public partial class MainPage : ContentPage
{
    private readonly LaboratoryViewModel model;
    private bool initialized;
    public MainPage(LaboratoryViewModel model)
    {
        InitializeComponent();
        this.model = model;
        BindingContext = model;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (initialized) return;
        initialized = true;
        await model.InitializeAsync();
    }
    private async void OnLogin(object? sender, EventArgs e) => await model.LoginAsync();
    private async void OnLogout(object? sender, EventArgs e) => await model.LogoutAsync();
    private async void OnLocal(object? sender, EventArgs e) => await model.RefreshLocalAsync();
    private async void OnDownload(object? sender, EventArgs e) => await model.DownloadServerAsync();
    private async void OnCreate(object? sender, EventArgs e) => await model.CreateExperimentAsync();
    private async void OnAdd(object? sender, EventArgs e) => await model.AddOfflineMeasurementAsync();
    private async void OnSync(object? sender, EventArgs e) => await model.SynchronizeAsync();
    private async void OnRetry(object? sender, EventArgs e) => await model.RetryAsync();
    private async void OnStatistics(object? sender, EventArgs e) => await model.ShowStatisticsAsync();
    private async void OnRebase(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Конфлікт версій", "Повторити додавання вибраного виміру до поточної чернетки сервера? Перевірка діапазону та статусу залишається чинною.", "Повторити", "Скасувати"))
            await model.RebaseAsync();
    }
    private async void OnDiscard(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Відкинути локальну команду?", "Вибрану невдалу операцію буде видалено з черги. Серверні дані не зміняться.", "Відкинути", "Скасувати"))
            await model.DiscardAsync();
    }
}
