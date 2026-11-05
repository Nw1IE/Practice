using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Projects.models
{
    public class ProcessingResult
    {
        public int CellId { get; init; }
        public CellStatus FinalStatus { get; init; }
        public DateTime? StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
