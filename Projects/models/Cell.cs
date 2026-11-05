using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Projects.models
{
    public class Cell
    {
        public int Id { get; init; }
        public int DiagnosticIterations { get; init; }
        public int ChargeDurationMs { get; init; }
        public int CalibrationDurationMs { get; init; }

        public CellStatus Status { get; set; } = CellStatus.Pending;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
