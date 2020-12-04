using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WhatsNewShared
{
    public interface IWnsHandler
    {
        bool Initialized();
        void Initialize(string workDir, string exeDir, DateTime wayTooLongAgo);
        List<ReportRecord> Crawl(string parent);
        string ReportStatus();
        string GetShortName();
        string GetVersion();
        bool HasFailed();
    }

    public interface IParallelWnsHandler
    {
        int GetParallelInstancesCount();
    }
}
