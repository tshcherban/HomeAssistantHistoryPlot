using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottable;

namespace WpfApp1
{
    public partial class ScottPlotWindow
    {
        private readonly List<Interval> _flameActiveIntervals;
        private readonly Color _boilerTempColor;
        private readonly Color _boilerTargetTempColor;
        private readonly Color _flameActiveColor;
        private readonly DataReader _haConnector;

        private bool _closed;
        private bool _bypassAxisChange;
        private bool _bypassScrollChange;
        private DateTime _minDate;
        private DateTime _maxDate;
        private ScatterPlot _boilerTempPlot;
        private ScatterPlot _integralErrorPLot;

        private static Color ColorFromRGBString(string rgbHex)
        {
            var colorObj = System.Windows.Media.ColorConverter.ConvertFromString(rgbHex);
            if (colorObj == null)
                throw new ApplicationException($"Failed to convert '{rgbHex}' to color");

            var mediaColor = (System.Windows.Media.Color) colorObj;
            var ret = Color.FromArgb(mediaColor.R, mediaColor.G, mediaColor.B);

            return ret;
        }

        public ScottPlotWindow(DataReader rdr = null)
        {
            _flameActiveIntervals = new List<Interval>();

            _boilerTargetTempColor = ColorFromRGBString("#AE1313");
            _boilerTempColor = ColorFromRGBString("#13AEAE");
            _flameActiveColor = Color.FromArgb(25, Color.IndianRed);

            _haConnector = rdr;

            InitializeComponent();

            WpfPlotBoiler.Configure(lowQualityWhileDragging: false, lowQualityOnScrollWheel: false, lowQualityAlways: false);
            WpfPlotRoom.Configure(lowQualityWhileDragging: false, lowQualityOnScrollWheel: false, lowQualityAlways: false);
            WpfPlotGasData.Configure(lowQualityWhileDragging: false, lowQualityOnScrollWheel: false, lowQualityAlways: false);

            DatePickerFrom.DisplayDateStart = new DateTime(2021, 1, 5, 0, 30, 0);
            DatePickerFrom.SelectedDate = DateTime.Today;
            DatePickerTo.SelectedDate = DateTime.Today;

            DatePickerFrom.SelectedDateChanged += DatePicker_OnSelectedDateChanged;
            DatePickerTo.SelectedDateChanged += DatePicker_OnSelectedDateChanged;

            InitPlot(true);
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;

            base.OnClosed(e);
        }

        private async Task<TimeSpan> DrawFlameStatus(double maxX)
        {
            var dd = await _haConnector.GetItems("sensor.ot_flame_enable", _minDate, _maxDate);

            var dataX = dd.Select(x => x.last_updated.ToOADate()).ToList();
            var dataYb = dd.Select(x => x.state == "1").ToList();

            if (dataYb.Last() && dataX.Last() < maxX)
            {
                dataYb.Add(false);
                dataX.Add(maxX);
            }

            //wpfPlot1.plt.AddScatter(dataX, dataY).color = Color.Red;
            //wpfPlot1.plt.PlotFill(dataX, dataY);

            var prev = false;
            double? xPrev = null;

            _flameActiveIntervals.Clear();

            var total = TimeSpan.Zero;

            for (var i = 0; i < dataX.Count; ++i)
            {
                var x = dataX[i];
                var y = dataYb[i];

                if (xPrev.HasValue)
                {
                    if (prev && !y)
                    {
                        var interval = new Interval
                        {
                            Start = DateTime.FromOADate(xPrev.Value),
                            End = DateTime.FromOADate(x)
                        };
                        _flameActiveIntervals.Add(interval);

                        total += interval.End - interval.Start;

                        WpfPlotBoiler.plt.AddHorizontalSpan(xPrev.Value, x, _flameActiveColor);
                    }
                }

                xPrev = x;
                prev = y;
            }

            return total;
        }

        private static ScatterPlot PlotStep(Plot plot, double[] x, double[]y, Color? color = null)
        {
             var scatter = plot.AddScatter(x, y, color, markerSize: 0F, markerShape:MarkerShape.none);
            scatter.StepDisplay = true;

            return scatter;
        }

        private async void InitPlot(bool reset = false)
        {
            if (!DatePickerFrom.SelectedDate.HasValue || !DatePickerTo.SelectedDate.HasValue)
                return;

            _minDate = DatePickerFrom.SelectedDate.Value;
            _maxDate = DatePickerTo.SelectedDate.Value;

            await Task.Yield();

            WpfPlotBoilerLoadingBlock.Visibility = Visibility.Visible;
            WpfPlotBoiler.Effect = new BlurEffect {KernelType = KernelType.Gaussian, Radius = 5};

            WpfPlotRoomLoadingBlock.Visibility = Visibility.Visible;
            WpfPlotRoom.Effect = new BlurEffect { KernelType = KernelType.Gaussian, Radius = 5 };

            WpfPlotGasLoadingBlock.Visibility = Visibility.Visible;
            WpfPlotGasData.Effect = new BlurEffect { KernelType = KernelType.Gaussian, Radius = 5 };

            await Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { }));

            if (reset)
            {
                WpfPlotBoiler.Reset();
                WpfPlotRoom.Reset();
            }

            var spans = WpfPlotBoiler.plt.GetPlottables().OfType<HSpan>().ToList();
            foreach (var sp in spans)
                WpfPlotBoiler.plt.Remove(sp);

            /*var counterData = GetGasConsumption();
            if (counterData.Count > 0)
            {
                wpfPlot1.Reset();

                var dx = counterData.Select(x => x.date.ToOADate()).ToArray();
                var dy = counterData.Select(x => x.value).ToArray();
                wpfPlot1.plt.PlotScatter(dx, dy);
                wpfPlot1.plt.Ticks(dateTimeX: true);
                wpfPlot1.Render();
                return;
            }*/

            var dd = (await _haConnector.GetItems("sensor.ot_integral_error", _minDate, _maxDate)).Where(x => x.state != null && x.state != "unknown").ToList();

            var dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            var dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            var minX = dataX.Min();
            var maxX = dataX.Max();
            var minY = dataY.Min();
            var maxY = dataY.Max();

            _integralErrorPLot = PlotStep(WpfPlotBoiler.plt, dataX, dataY);
            _integralErrorPLot.IsVisible = false;

            dd = (await _haConnector.GetItems("sensor.boiler_temperature", _minDate, _maxDate)).Where(x => x.state != null && x.state != "unknown" && x.state != "0.00").ToList();

            dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            minX = Math.Min(dataX.Min(), minX);
            maxX = Math.Max(dataX.Max(), maxX);
            minY = Math.Min(dataY.Min(), minY);
            maxY = Math.Max(dataY.Max(), maxY);

            if (!reset)
            {
                _boilerTempPlot.Update(dataX, dataY);
            }
            else
            {
                _boilerTempPlot = PlotStep(WpfPlotBoiler.plt, dataX, dataY, _boilerTempColor);
            }

            dd = (await _haConnector.GetItems("sensor.boiler_target_temperature", _minDate, _maxDate)).Where(x => x.state != null && x.state != "unknown" && x.state != "0.00").ToList();

            if (dd.Last().last_updated.ToOADate() < maxX)
            {
                dd.Add(new DataPoint
                {
                    last_updated = DateTime.FromOADate(maxX),
                    state = dd.Last().state,
                });
            }

            dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            minX = Math.Min(dataX.Min(), minX);
            maxX = Math.Max(dataX.Max(), maxX);
            minY = Math.Min(dataY.Min(), minY);
            maxY = Math.Max(dataY.Max(), maxY);

            PlotStep(WpfPlotBoiler.plt, dataX, dataY, _boilerTargetTempColor);

            var burnerActive = await DrawFlameStatus(maxX);
            var totalDuration = DateTime.FromOADate(maxX) - DateTime.FromOADate(minX);
            var burnerActiveTotalSeconds = 100d / totalDuration.TotalSeconds * burnerActive.TotalSeconds;

            InfoTextBlock.Text = $"Burner active for {FormatTimeSpan(burnerActive)} of {FormatTimeSpan(totalDuration)} ({burnerActiveTotalSeconds:F0} %)";

            var marginX = 0.01 * (maxX - minX);
            var marginY = 0.05 * (maxY - minY);

            WpfPlotBoiler.plt.XAxis.Dims.SetBounds(minX - marginX, maxX + marginX);
            WpfPlotBoiler.plt.YAxis.Dims.SetBounds(minY - marginY, maxY + marginY);

            WpfPlotBoiler.plt.XAxis.TickLabelFormat(null, true);
            WpfPlotBoiler.Render();

            PlotScroll.Minimum = minX - marginX;
            PlotScroll.Maximum = maxX + marginX;

            WpfPlotBoilerLoadingBlock.Visibility = Visibility.Collapsed;
            WpfPlotBoiler.Effect = null;
            await Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { }));

            WpfPlotRoom.plt.Clear();

            dd = (await _haConnector.GetItems("sensor.living_temp", _minDate, _maxDate)).Where(x => x.state!= null && x.state != "unknown").ToList();

            if (dd.Last().last_updated.ToOADate() < maxX)
            {
                dd.Add(new DataPoint
                {
                    last_updated = DateTime.FromOADate(maxX),
                    state = dd.Last().state,
                });
            }

            dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            PlotStep(WpfPlotRoom.plt, dataX, dataY);

            var dd1 = (await _haConnector.GetItems("climate.test_ot", _minDate, _maxDate)).Where(x => x.attributes.temperature != null).Select(x => (last_updated: x.last_updated, state: (double) x.attributes.temperature)).ToList();

            if (dd1.Last().last_updated.ToOADate() < maxX)
            {
                dd1.Add((last_updated: DateTime.FromOADate(maxX), state: dd1.Last().state));
            }

            dataX = dd1.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd1.Select(x => x.state).ToArray();
            PlotStep(WpfPlotRoom.plt, dataX, dataY);

            WpfPlotRoom.plt.XAxis.Dims.SetBounds(minX - marginX, maxX + marginX);
            WpfPlotRoom.plt.XAxis.TickLabelFormat(null, true);
            WpfPlotRoom.plt.SetAxisLimits(WpfPlotBoiler.plt.XAxis.Dims.Min, WpfPlotBoiler.plt.XAxis.Dims.Max);
            WpfPlotRoom.Render();

            WpfPlotRoomLoadingBlock.Visibility = Visibility.Collapsed;
            WpfPlotRoom.Effect = null;

            await Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { }));

            WpfPlotGasData.plt.Clear();

            var gasData = _haConnector.GetGasConsumption(_minDate, _maxDate.AddDays(1).AddTicks(-1));

            double? prevAmount = null;
            double? prevDate = null;
            foreach (var g in gasData)
            {
                WpfPlotGasData.plt.AddVerticalLine(g.date.ToOADate(), Color.Blue);
                //WpfPlotBoiler.plt.AddVerticalLine(g.date.ToOADate(), Color.Blue);

                if (prevAmount.HasValue)
                {
                    var amount = g.value - prevAmount.Value;
                    var rate = amount / (g.date - DateTime.FromOADate(prevDate.Value)).TotalDays;
                    //WpfPlotGasData.plt.AddHorizontalSpan(prevDate.Value, g.date.ToOADate(), Color.Transparent, $"{amount:F1}");
                    WpfPlotGasData.plt.AddText($"{amount:F1} ({rate:F1})", prevDate.Value, 0, color: Color.Blue).Rotation = 270;
                }

                prevAmount = g.value;
                prevDate = g.date.ToOADate();
            }

            WpfPlotGasData.plt.XAxis.Dims.SetBounds(minX - marginX, maxX + marginX);
            WpfPlotGasData.plt.XAxis.TickLabelFormat(null, true);
            WpfPlotGasData.plt.SetAxisLimits(WpfPlotBoiler.plt.XAxis.Dims.Min, WpfPlotBoiler.plt.XAxis.Dims.Max, 0, 20);
            WpfPlotGasData.Render();

            WpfPlotGasLoadingBlock.Visibility = Visibility.Collapsed;
            WpfPlotGasData.Effect = null;
        }

        private void CalcBurnerStatsForVisible()
        {
            var minDate = DateTime.FromOADate(WpfPlotBoiler.plt.XAxis.Dims.Min);
            var maxDate = DateTime.FromOADate(WpfPlotBoiler.plt.XAxis.Dims.Max);

            var inRange = _flameActiveIntervals
                .Where(x => x.End >= minDate && x.End <= maxDate || x.Start >= minDate && x.Start <= maxDate || minDate >= x.Start && maxDate <= x.End)
                .ToList();

            var burnerDuration = TimeSpan.Zero;

            foreach (var interval in inRange)
            {
                if (interval.End >= minDate && interval.End <= maxDate && interval.Start >= minDate && interval.Start <= maxDate)
                {
                    burnerDuration += interval.End - interval.Start;
                }
                else
                {
                    if (interval.Start < minDate && interval.End > maxDate)
                        burnerDuration += maxDate - minDate;
                    else if (interval.Start < minDate)
                        burnerDuration += interval.End - minDate;
                    else if (interval.End > maxDate)
                        burnerDuration += maxDate - interval.Start;
                    else
                        throw new ApplicationException("Invalid interval");
                }
            }

            var totalDuration = maxDate - minDate;
            var burnerDurationTotalSeconds = 100d / totalDuration.TotalSeconds * burnerDuration.TotalSeconds;

            
            TextBlockSelectionInfo.Text = $"Burner active for {FormatTimeSpan(burnerDuration)} of {FormatTimeSpan(totalDuration)} ({burnerDurationTotalSeconds:F0} %)";
        }

        private static string FormatTimeSpan(TimeSpan sp) => $"{(int) sp.TotalHours}:{sp.Minutes:D2}";

        private void WpfPlot1_OnAxisChanged(object sender, EventArgs e)
        {
            CalcBurnerStatsForVisible();

            if (!_bypassAxisChange)
            {
                try
                {
                    _bypassScrollChange = true;
                    PlotScroll.Value = WpfPlotBoiler.plt.XAxis.Dims.Center;
                    PlotScroll.ViewportSize = (WpfPlotBoiler.plt.XAxis.Dims.Max - WpfPlotBoiler.plt.XAxis.Dims.Min) / (WpfPlotBoiler.plt.XAxis.Dims.UpperBound - WpfPlotBoiler.plt.XAxis.Dims.LowerBound);
                }
                finally
                {
                    _bypassScrollChange = false;
                }
            }


            WpfPlotRoom.plt.SetAxisLimits(WpfPlotBoiler.plt.XAxis.Dims.Min, WpfPlotBoiler.plt.XAxis.Dims.Max);
            WpfPlotRoom.Render();
            
            WpfPlotGasData.plt.SetAxisLimits(WpfPlotBoiler.plt.XAxis.Dims.Min, WpfPlotBoiler.plt.XAxis.Dims.Max);
            WpfPlotGasData.Render();
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _integralErrorPLot.IsVisible = false;
            WpfPlotBoiler.Render();
        }

        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _integralErrorPLot.IsVisible = true;
            WpfPlotBoiler.Render();
        }

        private void DatePicker_OnSelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            InitPlot(true);
        }

        private void PlotScroll_OnScroll(object sender, ScrollEventArgs e)
        {
            if (_bypassScrollChange)
                return;

            var range = WpfPlotBoiler.plt.XAxis.Dims.Span;

            var minX = PlotScroll.Value - range / 2;
            var maxX = PlotScroll.Value + range / 2;

            if (minX < WpfPlotBoiler.plt.XAxis.Dims.LowerBound)
                return;

            if (maxX > WpfPlotBoiler.plt.XAxis.Dims.UpperBound)
                return;

            try
            {
                _bypassAxisChange = true;
                WpfPlotBoiler.plt.SetAxisLimits(minX, maxX);
                WpfPlotBoiler.Render();

                WpfPlotRoom.plt.SetAxisLimits(minX, maxX);
                WpfPlotRoom.Render();

                CalcBurnerStatsForVisible();
            }
            finally
            {
                _bypassAxisChange = false;
            }
        }

        private void RefreshBtn_OnClick(object sender, RoutedEventArgs e)
        {
            InitPlot();
        }
    }

    internal class Interval
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }
}