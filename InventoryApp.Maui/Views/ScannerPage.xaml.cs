using InventoryApp.Maui.ViewModels;
using ZXing.Net.Maui;

namespace InventoryApp.Maui.Views
{
    /// <summary>
    /// The main UI view for scanning barcodes.
    /// Acts as a bridge between the camera hardware events and the ScannerViewModel.
    /// </summary>
    public partial class ScannerPage : ContentPage
    {
        public ScannerPage(ScannerViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;

            // Configure the scanner to read all standard barcode formats
            barcodeReader.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormats.All,
                AutoRotate = true,
                Multiple = false
            };
        }

        /// <summary>
        /// Handles the hardware event when the camera detects a barcode.
        /// Extracts the raw string and safely routes it to the ViewModel.
        /// </summary>
        private void CameraBarcodeReaderView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
        {
            var first = e.Results?.FirstOrDefault();
            if (first == null) return;

            var vm = (ScannerViewModel)BindingContext;

            // Ensure we only process one barcode at a time to prevent duplicate API calls
            if (vm.IsDetecting)
            {
                // The camera event fires on a background thread. 
                // We must marshal the command execution back to the main UI thread.
                Dispatcher.Dispatch(() =>
                {
                    vm.ProcessBarcodeCommand.Execute(first.Value);
                });
            }
        }
    }
}
