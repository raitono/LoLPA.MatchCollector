using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLPA.MatchCollector
{
    public class RiotApiOptions
    {
        public string ApiKey { get; set; }
        public int LimitPer10s { get; set; }
        public int LimitPer10m { get; set; }
    }
}