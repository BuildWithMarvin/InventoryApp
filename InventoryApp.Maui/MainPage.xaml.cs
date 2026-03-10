using InventoryApp.Maui.ViewModels;
using ZXing.Net.Maui;

namespace InventoryApp.Maui
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainViewModel viewModel)
        {
            InitializeComponent();

            // We tell the page who its ‘brain’ is
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

            // We check whether the scanner is currently active and forward the code to the ViewModel.
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
