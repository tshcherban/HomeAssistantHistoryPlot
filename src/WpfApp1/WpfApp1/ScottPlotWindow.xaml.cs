using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScottPlot;
using ScottPlot.Plottable;

namespace WpfApp1
{
    public partial class ScottPlotWindow
    {
        private ScatterPlot _integralErrorPLot;
        private bool _closed;
        private readonly List<Interval> _flameActiveIntervals;

        private readonly System.Drawing.Color _boilerTempColor;
        private readonly System.Drawing.Color _boilerTargetTempColor;
        private readonly System.Drawing.Color _flameActiveColor;

        private static System.Drawing.Color ColorFromRGBString(string rgbHex)
        {
            var colorObj = System.Windows.Media.ColorConverter.ConvertFromString(rgbHex);
            if (colorObj == null)
                throw new ApplicationException($"Failed to convert '{rgbHex}' to color");

            var mediaColor = (System.Windows.Media.Color) colorObj;
            var ret = System.Drawing.Color.FromArgb(mediaColor.R, mediaColor.G, mediaColor.B);

            return ret;
        }

        public ScottPlotWindow()
        {
            _flameActiveIntervals = new List<Interval>();

            _boilerTargetTempColor = ColorFromRGBString("#AE1313");
            _boilerTempColor = ColorFromRGBString("#13AEAE");
            _flameActiveColor = System.Drawing.Color.FromArgb(25, System.Drawing.Color.IndianRed);

            _haConnector = new HomeAssistantConnector();

            InitializeComponent();

            wpfPlot1.Configure(lowQualityWhileDragging: false, lowQualityOnScrollWheel: false, lowQualityAlways: false);

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

        private TimeSpan DrawFlameStatus(double maxX)
        {
            var dd = _haConnector.GetItems("sensor.ot_flame_enable", _minDate, _maxDate);

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

                        wpfPlot1.plt.AddHorizontalSpan(xPrev.Value, x, _flameActiveColor);
                    }
                }

                xPrev = x;
                prev = y;
            }

            return total;
        }

        private List<(DateTime date, double value)> GetGasConsumption()
        {
            var ret = new List<(DateTime date, double value)>();

            using (var httpClient = new HttpClient())
            {
                const string resUrl = "";
                using (var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, resUrl))
                {
                    var resp = httpClient.SendAsync(httpRequestMessage).Result;
                    var jsonStr = resp.Content.ReadAsStringAsync().Result;
                    
                    var data = JsonConvert.DeserializeObject<JArray>(jsonStr);
                    foreach (var dd in data)
                    {
                        var dateStr = dd[0].Value<string>();
                        var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        var value = dd[1].Value<double>();

                        if (date >= _minDate && date <= _maxDate)
                            ret.Add((date, value));
                    }
                }
            }

            return ret;
        }

        private static ScatterPlot PlotStep(Plot plot, double[] x, double[]y, System.Drawing.Color? color = null)
        {
             var scatter = plot.AddScatter(x, y, color, markerSize: 0F, markerShape:MarkerShape.none);
            scatter.StepDisplay = true;

            return scatter;
        }

        private void InitPlot(bool reset = false)
        {
            if (!DatePickerFrom.SelectedDate.HasValue || !DatePickerTo.SelectedDate.HasValue)
                return;

            _minDate = DatePickerFrom.SelectedDate.Value;
            _maxDate = DatePickerTo.SelectedDate.Value;

            if (reset)
            {
                wpfPlot1.Reset();
                wpfPlot2.Reset();
            }

            var spans = wpfPlot1.plt.GetPlottables().OfType<HSpan>().ToList();
            foreach (var sp in spans)
                wpfPlot1.plt.Remove(sp);

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

            var dd = _haConnector.GetItems("sensor.ot_integral_error", _minDate, _maxDate).Where(x => x.state != "unknown").ToList();

            var dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            var dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            var minX = dataX.Min();
            var maxX = dataX.Max();
            var minY = dataY.Min();
            var maxY = dataY.Max();

            _integralErrorPLot = PlotStep(wpfPlot1.plt, dataX, dataY);
            _integralErrorPLot.IsVisible = false;

            dd = _haConnector.GetItems("sensor.boiler_temperature", _minDate, _maxDate).Where(x => x.state != "unknown" && x.state != "0.00").ToList();

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
                _boilerTempPlot = PlotStep(wpfPlot1.plt, dataX, dataY, _boilerTempColor);
            }

            dd = _haConnector.GetItems("sensor.boiler_target_temperature", _minDate, _maxDate).Where(x => x.state != "unknown" && x.state != "0.00").ToList();

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

            PlotStep(wpfPlot1.plt, dataX, dataY, _boilerTargetTempColor);

            var burnerActive = DrawFlameStatus(maxX);
            var totalDuration = DateTime.FromOADate(maxX) - DateTime.FromOADate(minX);
            var burnerActiveTotalSeconds = 100d / totalDuration.TotalSeconds * burnerActive.TotalSeconds;

            InfoTextBlock.Text = $"Burner active for {FormatTimeSpan(burnerActive)} of {FormatTimeSpan(totalDuration)} ({burnerActiveTotalSeconds:F0} %)";

            var marginX = 0.01 * (maxX - minX);
            var marginY = 0.05 * (maxY - minY);

            wpfPlot1.plt.XAxis.Dims.SetBounds(minX - marginX, maxX + marginX);
            wpfPlot1.plt.YAxis.Dims.SetBounds(minY - marginY, maxY + marginY);

            wpfPlot1.plt.XAxis.TickLabelFormat(null, true);
            wpfPlot1.Render();

            PlotScroll.Minimum = minX - marginX;
            PlotScroll.Maximum = maxX + marginX;

            dd = _haConnector.GetItems("sensor.living_temp", _minDate, _maxDate).Where(x => x.state != "unknown").ToList();

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

            PlotStep(wpfPlot2.plt, dataX, dataY);

            var dd1 = _haConnector.GetItems("climate.test_ot", _minDate, _maxDate).Where(x => x.attributes.temperature != null).Select(x => (last_updated: x.last_updated, state: (double) x.attributes.temperature)).ToList();

            if (dd1.Last().last_updated.ToOADate() < maxX)
            {
                dd1.Add((last_updated: DateTime.FromOADate(maxX), state: dd1.Last().state));
            }

            dataX = dd1.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd1.Select(x => x.state).ToArray();
            PlotStep(wpfPlot2.plt, dataX, dataY);

            wpfPlot2.plt.XAxis.Dims.SetBounds(minX - marginX, maxX + marginX);
            wpfPlot2.plt.XAxis.TickLabelFormat("g", true);
            wpfPlot2.Render();
        }

        private bool _bypassAxisChange;
        private bool _bypassScrollChange;
        private ScatterPlot _boilerTempPlot;
        private readonly HomeAssistantConnector _haConnector;
        private DateTime _minDate;
        private DateTime _maxDate;

        private void CalcBurnerStatsForVisible()
        {
            var minDate = DateTime.FromOADate(wpfPlot1.plt.XAxis.Dims.Min);
            var maxDate = DateTime.FromOADate(wpfPlot1.plt.XAxis.Dims.Max);

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
                    PlotScroll.Value = wpfPlot1.plt.XAxis.Dims.Center;
                    PlotScroll.ViewportSize = (wpfPlot1.plt.XAxis.Dims.Max - wpfPlot1.plt.XAxis.Dims.Min) / (wpfPlot1.plt.XAxis.Dims.UpperBound - wpfPlot1.plt.XAxis.Dims.LowerBound);
                }
                finally
                {
                    _bypassScrollChange = false;
                }
            }


            wpfPlot2.plt.SetAxisLimits(wpfPlot1.plt.XAxis.Dims.Min, wpfPlot1.plt.XAxis.Dims.Max);
            wpfPlot2.Render();
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _integralErrorPLot.IsVisible = false;
            wpfPlot1.Render();
        }

        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _integralErrorPLot.IsVisible = true;
            wpfPlot1.Render();
        }

        private void DatePicker_OnSelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            InitPlot(true);
        }

        private void PlotScroll_OnScroll(object sender, ScrollEventArgs e)
        {
            if (_bypassScrollChange)
                return;

            var range = wpfPlot1.plt.XAxis.Dims.Span;

            var minX = PlotScroll.Value - range / 2;
            var maxX = PlotScroll.Value + range / 2;

            if (minX < wpfPlot1.plt.XAxis.Dims.LowerBound)
                return;

            if (maxX > wpfPlot1.plt.XAxis.Dims.UpperBound)
                return;

            try
            {
                _bypassAxisChange = true;
                wpfPlot1.plt.SetAxisLimits(minX, maxX);
                wpfPlot1.Render();

                wpfPlot2.plt.SetAxisLimits(minX, maxX);
                wpfPlot2.Render();

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