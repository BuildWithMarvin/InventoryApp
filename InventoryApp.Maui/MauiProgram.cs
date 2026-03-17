using InventoryApp.Maui.Services;
using InventoryApp.Maui.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using ZXing.Net.Maui.Controls;

namespace InventoryApp.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            // 1. LOAD CONFIGURATION (appsettings.json)

            var assembly = Assembly.GetExecutingAssembly();

            // Gotcha: The appsettings.json MUST be marked as an 
            // “Embedded Resource” in Visual Studio, otherwise the stream will remain null.
            using var stream = assembly.GetManifestResourceStream("InventoryApp.Maui.appsettings.json");

            if (stream != null)
            {
                var config = new ConfigurationBuilder()
                    .AddJsonStream(stream)
                    .Build();
                builder.Configuration.AddConfiguration(config);
            }


            // 2. INITIALISE MAUI APP & PLUGINS

            builder
             .UseMauiApp<App>()
              // Registers the native camera handlers (iOS/Android) from ZXing. 
              // Without this line, the app crashes immediately when accessing the hardware.
              .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                            {
                                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                            });


            // 3. DEPENDENCY INJECTION (IoC-Container)

          
            builder.Services.AddSingleton<ApiService>();
            // Transient: Every time we call up a page, we want a fresh instance.
            // This prevents ‘stuck’ UI states or memory leaks from previous scans.
            builder.Services.AddTransient<MainViewModel>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<InventoryListViewModel>();
            builder.Services.AddTransient<InventoryListPage>();
            // ... deine anderen builder.Services stehen hier ...
            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddTransient<LoginPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
        