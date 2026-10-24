using System.Windows;
using System.Windows.Controls;
using HumbleBundleAssets.ViewModels;

namespace HumbleBundleAssets
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && slider.IsMouseCaptureWithin && DataContext is MainViewModel vm)
            {
                vm.SeekAudio(e.NewValue);
            }
        }
    }
}