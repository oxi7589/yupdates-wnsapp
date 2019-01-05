using System;
using System.Collections.Generic;
using System.Text;

namespace WhatsNewShared
{
    public class RootRecord
    {
        public string Rec = "";
    }

    public class ReportRecord
    {
        public DateTime UpdateFinished;
        public string ParentUrl = "#";
        public string ParentPath = "#";
        public int NumberOfUpdates = -1;
        public bool ForceShowAll = false;
        public RootRecord RootRec;
    }
}
