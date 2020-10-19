using System;
using System.Collections.Generic;
using System.Text;

namespace AzureDurableFunction1.Models
{
    class EventModel
    {
        public Guid Id { get; set; }
        public string EventType { get; set; }
    }
}
