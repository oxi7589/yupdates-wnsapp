using CG.Web.MegaApiClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WhatsNewShared;

namespace WnsHandler.MEGA
{
    /// <summary>
    /// WNSapp MEGA.nz crawler
    /// 
    /// usage: MEGA arg0 arg1
    /// arg0: folder hash
    /// arg1: human readable name
    /// </summary>
    public class WnsMEGA : IWnsHandler
    {
        private string WorkingDirectory = "";
        private string ExecutableDirectory = "";
        private DateTime WayTooLongAgo;
        private bool InitState = false;
        private bool HandlerHasFailed = false;
        private string FootnoteReport = "unused";

        MegaApiClient ApiClient = null;

        private List<ReportRecord> Report = null;
        private string RootName = "untitled root (mega)";
        private RootRecord Root = null;
        private Dictionary<string, INode> Directories = new Dictionary<string, INode>();
        private Dictionary<string, string> DirectoryPaths = new Dictionary<string, string>();

        private string GetDirName(string node)
        {
            if (!Directories.ContainsKey(node)) return RootName;
            string pt = GetDirName(Directories[node].ParentId);
            return  (pt != "" ? pt+" / " : "") + Directories[node].Name;
        }

        public List<ReportRecord> Crawl(string parent)
        {
            Report = new List<ReportRecord>();
            try
            {
                var pieces = parent.Split(new[] { ' ' }, 2);
                Uri folderLink = new Uri("https://mega.nz/#" + pieces[0]);
                Console.WriteLine("[MEGA] Processing " + pieces[0]);
                RootName = "";//pieces[1];
                Root = new RootRecord
                {
                    Rec =
                        "<a class=\"rootl\" href=\"/mg/?" + pieces[0]
                        + "\">" + pieces[1] + "</a> / "
                };
                IEnumerable<INode> nodes = ApiClient.GetNodesFromLink(folderLink);
                Dictionary<string, List<DateTime>> fileDates = new Dictionary<string, List<DateTime>>();

                foreach (INode node in nodes.Where(x => x.Type == NodeType.File))
                    if (node.ModificationDate.HasValue)
                    {
                        var modtime = node.ModificationDate.Value.ToUniversalTime();
                        var crtime = node.CreationDate.ToUniversalTime();
                        if (crtime > modtime)
                            modtime = crtime;
                        if (modtime > WayTooLongAgo)
                        {
                            if (!fileDates.ContainsKey(node.ParentId))
                                fileDates[node.ParentId] = new List<DateTime>();
                            fileDates[node.ParentId].Add(modtime);
                        }
                    }
                foreach (INode node in nodes.Where(x => x.Type == NodeType.Directory))
                    Directories[node.Id] = node;
                foreach (var nodePair in Directories)
                    DirectoryPaths[nodePair.Key] = GetDirName(nodePair.Key);
                foreach (var fdlist in fileDates)
                {
                    var fileModifyDates = fdlist.Value;
                    fileModifyDates.Sort();
                    int cnt = 0;
                    int firstIndex = 0;
                    for (int i = 0; i < fileModifyDates.Count; i++)
                    {
                        cnt++;
                        if (i + 1 == fileModifyDates.Count || (fileModifyDates[i + 1] - fileModifyDates[i]).TotalMinutes > 60)
                        {
                            ReportRecord rr = new ReportRecord
                            {
                                NumberOfUpdates = cnt,
                                UpdateFinished = fileModifyDates[i],
                                ParentUrl = "https://yupdates.neocities.org/mg/?" + pieces[0] + "!" + fdlist.Key,
                                ParentPath =
                                    (DirectoryPaths.ContainsKey(fdlist.Key) ? DirectoryPaths[fdlist.Key] : RootName)
                                    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"),
                                RootRec = Root,
                                FileDateTimes = fileModifyDates.GetRange(firstIndex, cnt)
                            };
                            Report.Add(rr);
                            cnt = 0;
                            firstIndex = i + 1;
                        }
                    }
                }
                Console.WriteLine("[MEGA] Valid records: " + Report.Count);
            }
            catch (Exception e)
            {
                Console.WriteLine("[MEGA] Error: " + e.Message);
                FootnoteReport = "completed with errors";
                HandlerHasFailed = true;
            }
            return Report;
        }

        public string GetShortName()
        {
            return "MEGA";
        }

        public void Initialize(string workDir, string exeDir, DateTime wayTooLongAgo)
        {
            WorkingDirectory = workDir;
            ExecutableDirectory = exeDir;
            WayTooLongAgo = wayTooLongAgo;

            ApiClient = new MegaApiClient();
            ApiClient.LoginAnonymous();
            if (ApiClient.IsLoggedIn)
            {
                Console.WriteLine("MEGA :: Logged in as anonymous");
                Report = new List<ReportRecord>();
                InitState = true;
                FootnoteReport = "ok";
            }
            else
            {
                Console.WriteLine("MEGA :: Not logged in!");
                FootnoteReport = "Not logged in.";
                HandlerHasFailed = true;
            }
        }

        public bool Initialized()
        {
            return InitState;
        }

        public string ReportStatus()
        {
            if (InitState) ApiClient.Logout();
            return FootnoteReport;
        }

        public string GetVersion()
        {
            return "v.0.4";
        }

        public bool HasFailed()
        {
            return HandlerHasFailed;
        }
    }
}
