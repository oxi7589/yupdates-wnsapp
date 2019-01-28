using System;
using System.Collections.Generic;
using System.Text;

namespace WhatsNewShared
{
    public class RootRecord
    {
        public string Rec = "";
    }

    [Flags]
    public enum ReportFlags
    {
        None = 0,
        NotNew = 256
    }

    public class ReportRecord
    {
        public DateTime UpdateFinished; // latest updated file date in this batch
        public List<DateTime> FileDateTimes = new List<DateTime>(); // individual files' update times
        public string ParentUrl = "#";
        public string ParentPath = "#";
        public int NumberOfUpdates = -1;
        public ReportFlags Flags = ReportFlags.None;
        public RootRecord RootRec; // reference to a root record object
    }
}
