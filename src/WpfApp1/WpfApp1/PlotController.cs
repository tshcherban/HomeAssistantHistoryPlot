using ScottPlot.Plottable;

namespace WpfApp1
{
    internal class PlotController
    {
        private ScatterPlot _boilerTempPlot;

        public PlotController()
        {
            _boilerTempPlot = new ScatterPlot(new double[0], new double[0]);
        }
    }
}