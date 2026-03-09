using InventoryApp.Maui.ViewModels;
using ZXing.Net.Maui;

namespace InventoryApp.Maui
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainViewModel viewModel)
        {
            InitializeComponent();

            // Wir sagen der Seite, wer ihr "Gehirn" ist
            BindingContext = viewModel;

            barcodeReader.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormats.All,
                AutoRotate = true,
                Multiple = false
            };
        }

        private void CameraBarcodeReaderView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
        {
            var first = e.Results?.FirstOrDefault();
            if (first == null) return;

            var vm = (MainViewModel)BindingContext;

            // Wir prüfen, ob der Scanner gerade aktiv ist, und leiten den Code ans ViewModel
            if (vm.IsDetecting)
            {
                Dispatcher.Dispatch(() =>
                {
                    vm.ProcessBarcodeCommand.Execute(first.Value);
                });
            }
        }
    }
}
