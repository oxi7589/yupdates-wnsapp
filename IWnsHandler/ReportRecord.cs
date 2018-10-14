using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WhatsNewShared
{
    public class RootRecord
    {
        public string Rec = "";
    }

    public class ReportRecord
    {
        public DateTime UpdateFinished;
        public string ParentUrl = "err";
        public string ParentPath = "err";
        public int NumberOfUpdates = -1;
        public bool ForceShowAll = false;
        public string CustomLine = "";
        public RootRecord RootRec;
    }
}
