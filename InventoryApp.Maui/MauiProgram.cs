using InventoryApp.Maui.Services;
using InventoryApp.Maui.ViewModels;
using InventoryApp.Maui.Views;
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

            // Load embedded configuration (appsettings.json)
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("InventoryApp.Maui.appsettings.json");

            if (stream != null)
            {
                var config = new ConfigurationBuilder()
                    .AddJsonStream(stream)
                    .Build();
                builder.Configuration.AddConfiguration(config);
            }

            // Configure app, UI components, and plugins
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Register Services (Singleton: Shared instance across the app)
            builder.Services.AddSingleton<ApiService>();

            // Register Views and ViewModels (Transient: Fresh instance per navigation)
            builder.Services.AddTransient<ScannerViewModel>();
            builder.Services.AddTransient<ScannerPage>();

            builder.Services.AddTransient<InventoryListViewModel>();
            builder.Services.AddTransient<InventoryListPage>();

            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddTransient<LoginPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
