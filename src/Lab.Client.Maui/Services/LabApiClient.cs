using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lab.Contracts;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.Security;
using Lab.Contracts.Sites;

namespace Lab.Client.Maui.Services;

/// <summary>Помилка API без розкриття токена чи пароля.</summary>
public sealed class ClientApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Типізований клієнт наявних контрактів сервера. Токен передається лише в заголовку.</summary>
public sealed class LabApiClient(HttpClient http, Uri server, string? token = null)
{
    public Task<AccessTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AccessTokenResponse>(HttpMethod.Post, "api/auth/login", request, cancellationToken);

    public Task<IReadOnlyList<SiteDto>> SitesAsync(CancellationToken cancellationToken = default) =>
        PagesAsync<SiteDto>("api/sites", cancellationToken);

    public Task<IReadOnlyList<InstrumentDto>> InstrumentsAsync(CancellationToken cancellationToken = default) =>
        PagesAsync<InstrumentDto>("api/instruments", cancellationToken);

    public Task<IReadOnlyList<ExperimentDto>> ExperimentsAsync(CancellationToken cancellationToken = default) =>
        PagesAsync<ExperimentDto>("api/experiments", cancellationToken);

    public Task<IReadOnlyList<MeasurementDto>> MeasurementsAsync(int id, CancellationToken cancellationToken = default) =>
        PagesAsync<MeasurementDto>($"api/experiments/{id}/measurements", cancellationToken);

    public Task<ExperimentDto> ExperimentAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<ExperimentDto>(HttpMethod.Get, $"api/experiments/{id}", null, cancellationToken);

    public Task<ExperimentDto> CreateExperimentAsync(CreateExperimentRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ExperimentDto>(HttpMethod.Post, "api/experiments", request, cancellationToken);

    public Task<MeasurementMutationDto> AddMeasurementAsync(int id, AddMeasurementRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MeasurementMutationDto>(HttpMethod.Post, $"api/experiments/{id}/measurements", request, cancellationToken);

    public Task<ExperimentStatisticsDto> StatisticsAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<ExperimentStatisticsDto>(HttpMethod.Get, $"api/experiments/{id}/statistics", null, cancellationToken);

    private async Task<IReadOnlyList<T>> PagesAsync<T>(string path, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        for (var page = 1; ; page++)
        {
            var result = await SendAsync<PagedResult<T>>(HttpMethod.Get, $"{path}?page={page}&pageSize=100", null, cancellationToken).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (items.Count >= result.TotalCount) return items;
            if (result.Items.Count == 0) throw new InvalidDataException("API повернув неповну сторінку; повторіть завантаження.");
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(server, path));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = $"Сервер повернув {(int)response.StatusCode}.";
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
                if (problem.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    message = detail.GetString() ?? message;
                else if (problem.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    message = title.GetString() ?? message;
            }
            catch (JsonException) { /* Не-JSON відповідь проксі: залишаємо HTTP-статус. */ }
            throw new ClientApiException(response.StatusCode, message);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("API повернув порожню відповідь.");
    }
}
