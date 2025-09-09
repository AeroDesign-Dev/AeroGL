using System;
using System.Windows;

namespace AeroGL
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Pastikan |DataDirectory| menunjuk ke folder exe
            AppDomain.CurrentDomain.SetData("DataDirectory",
                AppDomain.CurrentDomain.BaseDirectory);

            // Buat schema + trigger jika belum ada
            AeroGL.Data.Schema.Init();
            //await AeroGL.Data.DbfImporter.RunOnce(@"D:\GLData"); // ganti path

        }
    }
}
