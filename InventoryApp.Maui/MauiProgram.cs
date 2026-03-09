using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;
// WICHTIG: Passe diese beiden Usings an deine Ordnerstruktur an, falls nötig!
using InventoryApp.Maui.Services;
using InventoryApp.Maui.ViewModels;

namespace InventoryApp.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseBarcodeReader() // <--- Verhindert den nativen Kamera-Absturz!
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Hier sagen wir der App, wo sie das Gehirn und den API-Briefträger findet
            builder.Services.AddSingleton<ApiService>();
            builder.Services.AddTransient<MainViewModel>();
            builder.Services.AddTransient<MainPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}