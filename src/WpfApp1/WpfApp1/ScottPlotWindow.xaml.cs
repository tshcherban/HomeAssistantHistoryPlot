using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScottPlot;

namespace WpfApp1
{
    public partial class ScottPlotWindow
    {
        private PlottableScatter _integralErrorPLot;
        private bool _closed;
        private readonly List<Interval> _flameActiveIntervals;

        private readonly System.Drawing.Color _boilerTempColor;
        private readonly System.Drawing.Color _boilerTargetTempColor;

        public ScottPlotWindow()
        {
            _flameActiveIntervals = new List<Interval>();

            var boilerTargetTempColor = (System.Windows.Media.Color) System.Windows.Media.ColorConverter.ConvertFromString("#AE1313");
            _boilerTargetTempColor = System.Drawing.Color.FromArgb(boilerTargetTempColor.R, boilerTargetTempColor.G, boilerTargetTempColor.B);

            var boilerTempColor = (System.Windows.Media.Color) System.Windows.Media.ColorConverter.ConvertFromString("#13AEAE");
            _boilerTempColor = System.Drawing.Color.FromArgb(boilerTempColor.R, boilerTempColor.G, boilerTempColor.B);

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

        private TimeSpan DrawFlameStatus(double maxX, double minY, double maxY)
        {
            var dd = _haConnector.GetItems("sensor.ot_flame_enable", DatePickerFrom.SelectedDate.Value, DatePickerTo.SelectedDate.Value);

            var dataX = dd.Select(x => x.last_updated.ToOADate()).ToList();
            var dataYb = dd.Select(x => x.state == "1").ToList();

            if (dataYb.Last() && dataX.Last() < maxX)
            {
                dataYb.Add(false);
                dataX.Add(maxX);
            }

            //wpfPlot1.plt.PlotStep(dataX, dataY).color = Color.Red;
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

                        wpfPlot1.plt.PlotHSpan(xPrev.Value, x, System.Drawing.Color.IndianRed, 0.1);
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

                        if (date >= DatePickerFrom.SelectedDate.Value && date <= DatePickerTo.SelectedDate.Value)
                            ret.Add((date, value));
                    }
                }
            }

            return ret;
        }

        private void InitPlot(bool reset = false)
        {
            if (!DatePickerFrom.SelectedDate.HasValue || !DatePickerTo.SelectedDate.HasValue)
                return;

            if (reset)
            {
                wpfPlot1.Reset();
                wpfPlot2.Reset();
            }

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
            
            var dd = _haConnector.GetItems("sensor.ot_integral_error", DatePickerFrom.SelectedDate.Value, DatePickerTo.SelectedDate.Value).Where(x => x.state != "unknown").ToList();

            var dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            var dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            var minX = dataX.Min();
            var maxX = dataX.Max();
            var minY = dataY.Min();
            var maxY = dataY.Max();

            _integralErrorPLot = wpfPlot1.plt.PlotStep(dataX, dataY);
            _integralErrorPLot.visible = false;

            dd = _haConnector.GetItems("sensor.boiler_temperature", DatePickerFrom.SelectedDate.Value, DatePickerTo.SelectedDate.Value).Where(x => x.state != "unknown" && x.state != "0.00").ToList();

            dataX = dd.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd.Select(x => double.Parse(x.state, CultureInfo.InvariantCulture)).ToArray();

            minX = Math.Min(dataX.Min(), minX);
            maxX = Math.Max(dataX.Max(), maxX);
            minY = Math.Min(dataY.Min(), minY);
            maxY = Math.Max(dataY.Max(), maxY);

            if (!reset)
            {
                _boilerTempPlot.xs = dataX;
                _boilerTempPlot.ys = dataY;
            }
            else
            {
                _boilerTempPlot = wpfPlot1.plt.PlotStep(dataX, dataY, _boilerTempColor);
            }

            dd = _haConnector.GetItems("sensor.boiler_target_temperature", DatePickerFrom.SelectedDate.Value, DatePickerTo.SelectedDate.Value).Where(x => x.state != "unknown" && x.state != "0.00").ToList();

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

            wpfPlot1.plt.PlotStep(dataX, dataY, _boilerTargetTempColor);

            var burnerActive = DrawFlameStatus(maxX, minY, maxY);
            var totalDuration = DateTime.FromOADate(maxX) - DateTime.FromOADate(minX);
            var perc = 100d / totalDuration.TotalSeconds * burnerActive.TotalSeconds;

            string fts(TimeSpan sp) => $"{(int) sp.TotalHours}:{sp.Minutes:D2}";

            InfoTextBlock.Text = $"Burner active for {fts(burnerActive)} of {fts(totalDuration)} ({perc:F0} %)";

            var marginX = 0.01 * (maxX - minX);
            var marginY = 0.05 * (maxY - minY);

            wpfPlot1.plt.AxisBounds(minX - marginX, maxX + marginX, minY - marginY, maxY + marginY);
            wpfPlot1.plt.Ticks(dateTimeX: true);
            wpfPlot1.Render();

            PlotScroll.Minimum = minX - marginX;
            PlotScroll.Maximum = maxX + marginX;

            dd = _haConnector.GetItems("sensor.living_temp", DatePickerFrom.SelectedDate.Value, DatePickerTo.SelectedDate.Value).Where(x => x.state != "unknown").ToList();

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

            wpfPlot2.plt.PlotStep(dataX, dataY);

            var dd1 = _haConnector.GetItems("climate.test_ot", DatePickerFrom.SelectedDate.Value, DatePickerTo.SelectedDate.Value).Where(x => x.attributes.temperature != null).Select(x => (last_updated: x.last_updated, state: (double) x.attributes.temperature)).ToList();

            if (dd1.Last().last_updated.ToOADate() < maxX)
            {
                dd1.Add((last_updated: DateTime.FromOADate(maxX), state: dd1.Last().state));
            }

            dataX = dd1.Select(x => x.last_updated.ToOADate()).ToArray();
            dataY = dd1.Select(x => x.state).ToArray();
            wpfPlot2.plt.PlotStep(dataX, dataY);

            wpfPlot2.plt.AxisBounds(minX - marginX, maxX + marginX);
            wpfPlot2.plt.Ticks(dateTimeX: true);
            wpfPlot2.Render();
        }

        private bool _bypassAxisChange;
        private bool _bypassScrollChange;
        private PlottableScatter _boilerTempPlot;
        private readonly HomeAssistantConnector _haConnector;

        private void CalcBurnerStatsForVisible()
        {
            var set = wpfPlot1.plt.GetSettings();

            var minDate = DateTime.FromOADate(set.axes.x.min);
            var maxDate = DateTime.FromOADate(set.axes.x.max);

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
            var perc = 100d / totalDuration.TotalSeconds * burnerDuration.TotalSeconds;

            string fts(TimeSpan sp) => $"{(int) sp.TotalHours}:{sp.Minutes:D2}";
            TextBlockSelectionInfo.Text = $"Burner active for {fts(burnerDuration)} of {fts(totalDuration)} ({perc:F0} %)";
        }

        private void WpfPlot1_OnAxisChanged(object sender, EventArgs e)
        {
            var set = wpfPlot1.plt.GetSettings();

            CalcBurnerStatsForVisible();

            if (!_bypassAxisChange)
            {
                try
                {
                    _bypassScrollChange = true;
                    PlotScroll.Value = set.axes.x.center;
                    PlotScroll.ViewportSize = (set.axes.x.max - set.axes.x.min) / (set.axes.x.boundMax - set.axes.x.boundMin);
                }
                finally
                {
                    _bypassScrollChange = false;
                }
            }


            //TextBlockSelectionInfo2.Text = $"{(set.axes.x.max - set.axes.x.min) /(set.axes.x.boundMax - set.axes.x.boundMin)}";

            wpfPlot2.plt.Axis(set.axes.x.min, set.axes.x.max);
            wpfPlot2.Render();
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _integralErrorPLot.visible = false;
            wpfPlot1.Render();
        }

        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _integralErrorPLot.visible = true;
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

            var set = wpfPlot1.plt.GetSettings();
            var range = set.axes.x.max - set.axes.x.min;

            var minX = PlotScroll.Value - range / 2;
            var maxX = PlotScroll.Value + range / 2;

            if (minX < set.axes.x.boundMin)
                return;

            if (maxX > set.axes.x.boundMax)
                return;

            try
            {
                _bypassAxisChange = true;
                wpfPlot1.plt.Axis(minX, maxX);
                wpfPlot1.Render();

                wpfPlot2.plt.Axis(minX, maxX);
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