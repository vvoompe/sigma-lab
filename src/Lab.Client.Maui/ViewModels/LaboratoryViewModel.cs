using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lab.Client.Maui.Data;
using Lab.Client.Maui.Services;
using Lab.Client.Maui.Sync;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.Security;
using Lab.Contracts.Sites;

namespace Lab.Client.Maui.ViewModels;

/// <summary>Платформне захищене сховище облікового запису.</summary>
public interface IClientSessionStore
{
    Task<ClientAccount?> LoadAsync();
    Task SaveAsync(ClientAccount account);
    void Clear();
}

/// <summary>Стан екрану. Мережева недоступність не блокує читання локальних серій.</summary>
public sealed class LaboratoryViewModel(HttpClient http, IClientSessionStore sessions, string root) : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private ClientAccount? account;
    private bool busy;
    private string message = "Увійдіть для першого завантаження; потім дані доступні офлайн.";
    private string statistics = string.Empty;
    private SyncQueueItem? selectedOperation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Server { get; set; } = "https://localhost:7443";
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string NewExperimentName { get; set; } = string.Empty;
    public string MeasurementValue { get; set; } = string.Empty;
    public ExperimentDto? SelectedExperiment { get; set; }
    public InstrumentDto? SelectedInstrument { get; set; }
    public SiteDto? SelectedSite { get; set; }
    public SyncQueueItem? SelectedOperation
    {
        get => selectedOperation;
        set { selectedOperation = value; Notify(); Notify(nameof(OperationDetails)); }
    }
    public string OperationDetails
    {
        get
        {
            if (selectedOperation is null) return string.Empty;
            try
            {
                var item = JsonSerializer.Deserialize<PendingMeasurement>(selectedOperation.Payload, JsonOptions);
                return item is null ? string.Empty : $"Вимір {item.Value:G6}; час UTC {item.MeasuredAt:u}; очікувана версія {item.ExpectedVersion}";
            }
            catch (JsonException) { return "Пошкоджений запис операції; потрібне відновлення журналу."; }
        }
    }
    public bool IsIdle => !busy;
    public string Message { get => message; private set { message = value; Notify(); } }
    public string Statistics { get => statistics; private set { statistics = value; Notify(); } }
    public ObservableCollection<ExperimentDto> Experiments { get; } = [];
    public ObservableCollection<InstrumentDto> Instruments { get; } = [];
    public ObservableCollection<SiteDto> Sites { get; } = [];
    public ObservableCollection<SyncQueueItem> Operations { get; } = [];
    public ObservableCollection<MeasurementDto> Measurements { get; } = [];

    public Task InitializeAsync() => RunAsync(async () =>
    {
        account = await sessions.LoadAsync();
        if (account is null) return;
        Server = account.Server;
        Email = account.Email;
        Notify(nameof(Server));
        Notify(nameof(Email));
        await LoadLocalAsync();
        Message = $"Локальні дані: {account.Name}. Для мережевих дій потрібен чинний токен.";
    });

    public Task LoginAsync() => RunAsync(async () =>
    {
        var server = ClientAccount.ValidateServer(Server);
        var response = await new LabApiClient(http, server).LoginAsync(new LoginRequest { Email = Email, Password = Password });
        var next = ClientAccount.FromToken(server, Email, response);
        await sessions.SaveAsync(next);
        account = next;
        Password = string.Empty;
        Notify(nameof(Password));
        ClearDisplay();
        await LoadLocalAsync();
        await DownloadAsync();
    });

    public Task LogoutAsync() => RunAsync(() =>
    {
        sessions.Clear();
        account = null;
        Password = string.Empty;
        Notify(nameof(Password));
        ClearDisplay();
        Message = "Вихід виконано. Локальні дані ізольовані за акаунтом і збережені для наступного входу.";
        return Task.CompletedTask;
    });

    public Task RefreshLocalAsync() => RunAsync(LoadLocalAsync);
    public Task DownloadServerAsync() => RunAsync(DownloadAsync);

    public Task CreateExperimentAsync() => RunAsync(async () =>
    {
        var api = Api();
        var site = SelectedSite ?? throw new InvalidOperationException("Оберіть майданчик.");
        var instrument = SelectedInstrument ?? throw new InvalidOperationException("Оберіть прилад.");
        await api.CreateExperimentAsync(new CreateExperimentRequest
        {
            Name = NewExperimentName, SiteId = site.Id, InstrumentId = instrument.Id,
        });
        await DownloadAsync();
    });

    public Task AddOfflineMeasurementAsync() => RunAsync(async () =>
    {
        var current = Account();
        var series = SelectedExperiment ?? throw new InvalidOperationException("Оберіть серію.");
        if (!double.TryParse(MeasurementValue, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            && !double.TryParse(MeasurementValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            throw new ArgumentException("Введіть числове значення виміру.");
        await new ExperimentCache(current, root).ValidateMeasurementAsync(series.Id, value);
        var queue = Queue();
        var items = await queue.GetAllAsync();
        var unsent = items.Count(item => item.ResourceId == series.Id.ToString(CultureInfo.InvariantCulture) && item.Status != SyncQueueStatus.Sent);
        await MeasurementSynchronizer.EnqueueAsync(queue, new PendingMeasurement(series.Id, Guid.NewGuid(), value,
            DateTime.UtcNow, checked(series.Version + unsent)));
        await LoadOperationsAsync();
        Message = "Вимір збережено в локальному журналі. Він доступний без мережі та очікує синхронізації.";
    });

    public Task SynchronizeAsync() => RunAsync(async () =>
    {
        var report = await new MeasurementSynchronizer(Queue(), Api()).SynchronizeAsync();
        await LoadOperationsAsync();
        Message = $"Черга: Sent {report.Sent}, Failed {report.Failed}, Pending {report.Pending}.";
        // Перечитуємо серверну версію перед наступними локальними командами.
        if (report.Failed == 0) await DownloadAsync();
    });

    public Task RetryAsync() => RunAsync(async () =>
    {
        var operation = SelectedOperation ?? throw new InvalidOperationException("Оберіть операцію черги.");
        if (operation.Status != SyncQueueStatus.Failed) throw new InvalidOperationException("Повтор доступний лише для Failed-операції.");
        await Queue().RetryAsync(operation.OperationId);
        await LoadOperationsAsync();
        Message = "Операція повернена в Pending зі старою версією. Натисніть синхронізацію.";
    });

    /// <summary>Рішення «залишити серверні дані» після підтвердження у View.</summary>
    public Task DiscardAsync() => RunAsync(async () =>
    {
        var operation = SelectedOperation ?? throw new InvalidOperationException("Оберіть невдалу операцію.");
        await Queue().DiscardFailedAsync(operation.OperationId);
        SelectedOperation = null;
        await LoadOperationsAsync();
        Message = "Невдалу локальну операцію відкинуто. Наступні команди зберігають перевірку своїх версій.";
    });

    /// <summary>Викликається тільки після явного підтвердження користувачем у View.</summary>
    public Task RebaseAsync() => RunAsync(async () =>
    {
        var operation = SelectedOperation ?? throw new InvalidOperationException("Оберіть невдалу операцію.");
        var command = JsonSerializer.Deserialize<PendingMeasurement>(operation.Payload, JsonOptions)
            ?? throw new InvalidDataException("Пошкоджена операція.");
        var series = await Api().ExperimentAsync(command.ExperimentId);
        if (series.Status != "Draft") throw new InvalidOperationException("Підписану серію змінювати не можна.");
        var payload = JsonSerializer.Serialize(command with { ExpectedVersion = series.Version }, JsonOptions);
        await Queue().RebaseAsync(operation.OperationId, payload);
        await LoadOperationsAsync();
        Message = $"Застосовано рішення користувача: повторити для версії {series.Version}. Дані сервера не перезаписано.";
    });

    public Task ShowStatisticsAsync() => RunAsync(async () =>
    {
        var series = SelectedExperiment ?? throw new InvalidOperationException("Оберіть серію.");
        var api = Api();
        var result = await api.StatisticsAsync(series.Id);
        Statistics = $"n={result.Count}; середнє={result.Mean:G6} {result.Unit}; СКО={result.SampleStandardDeviation:G6}; "
            + $"похибка={result.StandardError:G6}; ДІ=[{result.ConfidenceInterval.Lower:G6}; {result.ConfidenceInterval.Upper:G6}]";
        Measurements.Clear();
        foreach (var item in await api.MeasurementsAsync(series.Id)) Measurements.Add(item);
    });

    private ClientAccount Account() => account ?? throw new InvalidOperationException("Спочатку увійдіть.");
    private LabApiClient Api()
    {
        var current = Account();
        if (current.ExpiresAtUtc <= DateTime.UtcNow) throw new InvalidOperationException("Токен сплив. Увійдіть знову; офлайн-черга збережена.");
        return new(http, ClientAccount.ValidateServer(current.Server), current.Token);
    }
    private SyncQueueStore Queue() => new(Path.Combine(Account().WorkspacePath(root), "sync-queue.json"));

    private async Task DownloadAsync()
    {
        var api = Api();
        var sites = await api.SitesAsync();
        var instruments = await api.InstrumentsAsync();
        var experiments = await api.ExperimentsAsync();
        await new ExperimentCache(Account(), root).ReplaceAsync(sites, instruments, experiments);
        Sites.Clear();
        Instruments.Clear();
        foreach (var item in sites) Sites.Add(item);
        foreach (var item in instruments) Instruments.Add(item);
        await LoadLocalAsync();
        Message = $"Завантажено {Experiments.Count} серій у SQLite. Тепер їх можна читати без мережі.";
    }

    private async Task LoadLocalAsync()
    {
        var selectedId = SelectedExperiment?.Id;
        var items = await new ExperimentCache(Account(), root).ListAsync();
        Experiments.Clear();
        foreach (var item in items) Experiments.Add(item);
        SelectedExperiment = Experiments.FirstOrDefault(item => item.Id == selectedId);
        Notify(nameof(SelectedExperiment));
        await LoadOperationsAsync();
    }

    private async Task LoadOperationsAsync()
    {
        Operations.Clear();
        foreach (var item in await Queue().GetAllAsync()) Operations.Add(item);
    }

    private void ClearDisplay()
    {
        Experiments.Clear(); Instruments.Clear(); Sites.Clear(); Measurements.Clear(); Operations.Clear();
        SelectedExperiment = null; SelectedInstrument = null; SelectedSite = null; SelectedOperation = null;
        Notify(nameof(SelectedExperiment)); Notify(nameof(SelectedInstrument)); Notify(nameof(SelectedSite)); Notify(nameof(SelectedOperation));
        Statistics = string.Empty;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        Notify(nameof(IsIdle));
        try { await action(); }
        catch (Exception exception) when (exception is ClientApiException or HttpRequestException or IOException
            or InvalidOperationException or ArgumentException or JsonException or Microsoft.Data.Sqlite.SqliteException
            or Microsoft.EntityFrameworkCore.DbUpdateException or OperationCanceledException or KeyNotFoundException
            or System.Security.Cryptography.CryptographicException or NotSupportedException)
        {
            Message = exception is HttpRequestException or OperationCanceledException
                ? "Мережа недоступна. Локальні серії та черга збережені; повторіть пізніше." : exception.Message;
        }
        finally { busy = false; Notify(nameof(IsIdle)); }
    }

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
