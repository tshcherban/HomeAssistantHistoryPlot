using System;
using System.IO;
using System.Windows;

namespace WpfApp1
{
    public partial class App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "config.txt");
            var lines = File.ReadAllLines(cfgPath);

            var rdr = DataReader.GetNew(lines[0], lines[1], lines[2]);

            MainWindow = new ScottPlotWindow(rdr);
            MainWindow.Show();
        }
    }
}