using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WhatsNewShared;

namespace WnsHandler.YaDisk
{
    /// <summary>
    /// WNSapp Yandex.Disk crawler
    /// 
    /// usage: YaDisk arg0 arg1
    /// arg0: folder hash (that is, XXX in https://yadi.sk/d/XXX)
    /// arg1: human readable name
    /// </summary>
    public class WnsYadisk : IWnsHandler
    {
        private string WorkingDirectory = "";
        private string ExecutableDirectory = "";
        private DateTime WayTooLongAgo;
        private bool InitState = false;
        private bool HandlerHasFailed = false;
        private string FootnoteReport = "unused";

        private HttpClient http = null;
        private JavaScriptSerializer serializer = null;


        private List<ReportRecord> Report = null;
        private RootRecord Root = null;

        private string RootHash = "";

        private string RootName = "untitled root (yadisk)";

        /*
        private string GetDirName(string node)
        {
            if (!Directories.ContainsKey(node)) return RootName;
            string pt = GetDirName(Directories[node].ParentId);
            return (pt != "" ? pt + " / " : "") + Directories[node].Name;
        }
        */

        private Queue<string> pathsQueue;

        private Dictionary<string, object> QueryApi(string path, string sort, int offset, int limit)
        {
            string query;
            using (var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]{
                new KeyValuePair<string, string>("public_key", "https://yadi.sk/d/" + RootHash),
                new KeyValuePair<string, string>("path", path),
                new KeyValuePair<string, string>("limit", limit.ToString()),
                new KeyValuePair<string, string>("offset", offset.ToString()),
                new KeyValuePair<string, string>("sort", sort)
            }))
            {
                query = content.ReadAsStringAsync().Result;
            }

            //HttpResponseMessage resp = null;
            int attempts = 0;
        b4reqest:
            try
            {
                //Console.WriteLine("GET " + "https://cloud-api.yandex.net:443/v1/disk/public/resources?" + query.ToString());
                var resp = http.GetStringAsync("https://cloud-api.yandex.net:443/v1/disk/public/resources?" + query).Result;
                System.Threading.Thread.Sleep(500);
                return serializer.Deserialize<Dictionary<string, object>>(resp);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                attempts += 1;
                if (attempts > 5) throw; // rethrow if it didn't get resolved on the fifth try
                System.Threading.Thread.Sleep((int)Math.Pow(2, attempts - 1) * 5000); // 5s 10s 20s 40s 80s
                goto b4reqest;
            }
        }

        public List<ReportRecord> Crawl(string parent)
        {
            Report = new List<ReportRecord>();
            pathsQueue = new Queue<string>();
            try
            {
                var pieces = parent.Split(new[] { ' ' }, 2);
                RootHash = pieces[0];
                pieces = pieces[1].Split(new[] { " -" }, StringSplitOptions.RemoveEmptyEntries);
                // pieces[1, 2...] are arguments with prefix dash removed
                RootName = pieces[0];

                Root = new RootRecord
                {
                    Rec =
                        "<a class=\"rootl\" href=\"https://yadi.sk/d/" + RootHash
                        + "\">" + RootName + "</a> /",
                    Label = RootName,
                    UniqId = RootHash
                };

                pathsQueue.Enqueue("/");

                const int Limit = 200;
                while (pathsQueue.Count > 0)
                {
                    string currentPath = pathsQueue.Dequeue();
                    Console.WriteLine("[YaDisk] path: " + currentPath);
                    int offset = 0;
                    int nodesFound = 0;
                    bool endReached = false;

                    ReportRecord rr = null;
                    DateTime prevTime = DateTime.UtcNow.AddYears(1);

                    while (!endReached)
                    {
                        var resp = QueryApi(currentPath, "-modified", offset, Limit);
                        var embedded = resp["_embedded"] as Dictionary<string, object>;
                        if (embedded != null)
                        {
                            int total = (int)embedded["total"];
                            var items = (System.Collections.ArrayList)embedded["items"];
                            nodesFound += items.Count;
                            offset += items.Count;
                            foreach (var item in items)
                            {
                                var itemDict = (Dictionary<string, object>)item;
                                string nodeName = (string)itemDict["name"];
                                string nodeType = (string)itemDict["type"];
                                string nodePath = (string)itemDict["path"];
                                if (nodeType == "dir")
                                {
                                    // directory
                                    pathsQueue.Enqueue(nodePath);
                                }
                                else
                                {
                                    // file
                                    DateTime nodeModDate = DateTime.Parse((string)itemDict["modified"]).ToUniversalTime();
                                    if (nodeModDate >= WayTooLongAgo)
                                    {
                                        if ((prevTime - nodeModDate).TotalMinutes > 60)
                                        {
                                            if (rr != null)
                                            {
                                                Report.Add(rr);
                                                Console.WriteLine("Add group");
                                            }
                                            rr = new ReportRecord
                                            {
                                                RootRec = Root,
                                                NumberOfUpdates = 0,
                                                ParentUrl = "https://yadi.sk/d/" + RootHash + currentPath,
                                                ParentPath = currentPath.Substring(1).Replace("/", " / "),
                                                ParentPathMir = currentPath.Substring(1).Replace("/", " \b "),
                                                FileDateTimes = new List<DateTime>()
                                            };
                                        }
                                        if (rr.NumberOfUpdates == 0)
                                        {
                                            rr.UpdateFinished = nodeModDate;
                                        }
                                        rr.NumberOfUpdates += 1;
                                        rr.FileDateTimes.Add(nodeModDate);
                                        prevTime = nodeModDate;
                                    }
                                    else
                                    {
                                        endReached = true;
                                        break;
                                    }
                                }
                            }
                            endReached = endReached || (offset >= total);
                        }
                        else throw new Exception("No _embedded in API response!");
                    }
                    if (rr != null)
                    {
                        Report.Add(rr);
                    }
                }

                Console.WriteLine("[YaDisk] Valid records: " + Report.Count);
            }
            catch (Exception e)
            {
                Console.WriteLine("[YaDisk] Error: " + e.Message);
                FootnoteReport = "completed with errors";
                HandlerHasFailed = true;
            }
            pathsQueue = null;
            return Report;
        }

        public string GetShortName()
        {
            return "YaDisk";
        }

        public void Initialize(string workDir, string exeDir, DateTime wayTooLongAgo)
        {
            WorkingDirectory = workDir;
            ExecutableDirectory = exeDir;
            WayTooLongAgo = wayTooLongAgo;

            http = new HttpClient();
            http.DefaultRequestHeaders.Clear();
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;

            serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024 * 8; // 8 MiB

            Report = new List<ReportRecord>();
            InitState = true;
            FootnoteReport = "ok";
        }

        public bool Initialized()
        {
            return InitState;
        }

        public string ReportStatus()
        {
            http = null;
            serializer = null;
            return FootnoteReport;
        }

        public string GetVersion()
        {
            return "v.0.3a";
        }

        public bool HasFailed()
        {
            return HandlerHasFailed;
        }

        public bool CanBeMirrored()
        {
            return true;
        }
    }
}
