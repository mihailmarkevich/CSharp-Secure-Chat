using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Models
{
    public class BanPayload
    {
        public string? Message { get; set; }
        public int? RetryAfterSeconds { get; set; }
    }

}
