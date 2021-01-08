using System;

namespace WpfApp1
{
    public class DataPoint
    {
        public string entity_id { get; set; }
        public string state { get; set; }

        public DateTime last_changed { get; set; }
        public DateTime last_updated { get; set; }

        public dynamic attributes { get; set; }
    }
}