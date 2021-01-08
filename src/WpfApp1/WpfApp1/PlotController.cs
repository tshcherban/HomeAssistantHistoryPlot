using ScottPlot;

namespace WpfApp1
{
    internal class PlotController
    {
        private PlottableScatter _boilerTempPlot;

        public PlotController()
        {
            _boilerTempPlot = new PlottableScatter(new double[0], new double[0]);
        }
    }
}