using Lab.Client.Maui.Services;
using Lab.Client.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lab.Client.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        builder.Services.AddSingleton<IClientSessionStore, SecureClientSessionStore>();
        builder.Services.AddSingleton(provider => new LaboratoryViewModel(
            provider.GetRequiredService<HttpClient>(), provider.GetRequiredService<IClientSessionStore>(), FileSystem.AppDataDirectory));
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
