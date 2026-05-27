using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SoundVibe
{
    public partial class ToastWindow : Window
    {
        public ToastWindow(SongModel song)
        {
            InitializeComponent();

            // 1. DİNAMİK TEMA EŞLEŞTİRMESİ
            // Ana pencerede (MainWindow) hangi renkler (Neon, Aero vs) aktifse, bildirim penceresine kopyalıyoruz.
            try
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    this.Resources["ThemeAccentBrush"] = mainWindow.FindResource("ThemeAccentBrush");
                    this.Resources["ThemeGlassBrush"] = mainWindow.FindResource("ThemeGlassBrush");
                    this.Resources["ThemeDarkBgBrush"] = mainWindow.FindResource("ThemeDarkBgBrush");
                }
            }
            catch { }

            // 2. VERİLERİ ARAYÜZE BAS
            TxtToastTitle.Text = song.Title;
            TxtToastArtist.Text = song.Artist;
            if (song.CoverImage != null)
            {
                ImgToastCover.Source = song.CoverImage;
            }

            // 3. CAM EFEKTİNİ AKTİF ET
            WindowsServices.EnableAcrylicBlur(this);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Ekranın sağ alt köşesinin X ve Y koordinatlarını buluyoruz
            // SystemParameters.WorkArea görev çubuğunu hesaba katarak güvenli alanı verir.
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Right - this.Width;
            this.Top = workArea.Bottom - this.Height;

            await ShowToastAnimationAsync();
        }

        private async Task ShowToastAnimationAsync()
        {
            // 1. Fade In (Yumuşak Giriş Animasyonu)
            for (double i = 0; i <= 1.0; i += 0.08)
            {
                this.Opacity = i;
                await Task.Delay(15);
            }

            this.Opacity = 1.0;

            // 2. Ekranda Kalma Süresi (3 Saniye)
            await Task.Delay(3000);

            // 3. Fade Out (Yumuşak Çıkış Animasyonu)
            for (double i = 1.0; i >= 0; i -= 0.08)
            {
                this.Opacity = i;
                await Task.Delay(15);
            }

            // Animasyon bittiğinde pencereyi bellekten sil ve kapat
            this.Close();
        }
    }
}