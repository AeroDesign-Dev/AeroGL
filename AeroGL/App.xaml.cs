using AeroGL.Data;
using Dapper;
using System;
using System.Windows;

namespace AeroGL
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Pastikan |DataDirectory| menunjuk ke folder exe
            AppDomain.CurrentDomain.SetData("DataDirectory", AppDomain.CurrentDomain.BaseDirectory);
            
            MasterSchema.Init();

            var gate = new ProjectGateWindow();
            bool? result = gate.ShowDialog();

            if (result == true && AeroGL.Core.CurrentCompany.IsLoaded)
            {
                try
                {
                    AeroGL.Data.Schema.Init();
                    AeroGL.Data.AccountConfig.Reload();

                    var main = new MainWindow();

                    // Kasih tahu WPF kalau ini jendela utama sekarang
                    this.MainWindow = main;
                    main.Show();

                    // 2. KEMBALIKAN KE NORMAL: Sekarang kalau Main ditutup, aplikasi ikut mati
                    this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal memuat database perusahaan: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Shutdown();
                }
            }
            else
            {
                // Jika user klik Cancel atau menutup jendela pemilihan, matikan aplikasi
                this.Shutdown();
            }

        }
    }
}
