using AeroGL.Data;
using System;
using System.Windows;

namespace AeroGL
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Pastikan |DataDirectory| menunjuk ke folder exe
            AppDomain.CurrentDomain.SetData("DataDirectory", AppDomain.CurrentDomain.BaseDirectory);
            
            MasterSchema.Init();

            var gate = new ProjectGateWindow();
            bool? result = gate.ShowDialog();

            if (result == true && AeroGL.Core.CurrentCompany.IsLoaded)
            {
                try
                {
                    // Inisialisasi DB Perusahaan yang dipilih
                    // Sekarang Db.Open() di dalam Schema.Init() akan otomatis menggunakan path dinamis
                    AeroGL.Data.Schema.Init();

                    // Muat konfigurasi akun khusus perusahaan tersebut
                    AeroGL.Data.AccountConfig.Reload();

                    // 4. Buka Window Utama
                    var main = new MainWindow();
                    main.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal memuat database perusahaan: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                }
            }
            else
            {
                // Jika user klik Cancel atau menutup jendela pemilihan, matikan aplikasi
                Shutdown();
            }

        }
    }
}
