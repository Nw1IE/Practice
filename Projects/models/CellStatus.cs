using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Projects.models
{
    public enum CellStatus
    {
        Pending,
        Diagnostics,
        WaitingForCharger,
        Charging,
        WaitingForCalibration,
        Calibration,
        Completed,
        Canceled,
        Faulted
    }
}
