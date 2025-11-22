using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Models
{
    public class MessageDto
    {
        public string? Id { get; set; }
        public string? UserName { get; set; }
        public string? Text { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

}
