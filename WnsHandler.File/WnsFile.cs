using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WhatsNewShared;

namespace WnsHandler.File
{
    /// <summary>
    /// WNSapp File plugin
    /// 
    /// usage: File arg0
    /// arg0: file to read from (relative path, first EXEDIR+arg0 is checked, then WRKDIR+arg0)
    /// Records in file must be sorted by date, newest first.
    /// </summary>
    public class WnsMEGA : IWnsHandler
    {
        private string WorkingDirectory = "";
        private string ExecutableDirectory = "";
        private DateTime WayTooLongAgo;
        private bool InitState = false;
        private string FootnoteReport = "unused";

        public List<ReportRecord> Crawl(string parent)
        {
            List<ReportRecord> report = new List<ReportRecord>();
            try
            {
                System.IO.StreamReader streamReader = null;
                string filepath = parent;
                if (System.IO.File.Exists(filepath = ExecutableDirectory + System.IO.Path.DirectorySeparatorChar + parent))
                    streamReader = System.IO.File.OpenText(filepath);
                else if (System.IO.File.Exists(filepath = WorkingDirectory + System.IO.Path.DirectorySeparatorChar + parent))
                    streamReader = System.IO.File.OpenText(filepath);
                else throw new Exception("File not found!");

                Console.WriteLine("File :: reading from " + filepath);

                ReportRecord rr = null;
                RootRecord root = null;
                string lastRootContents = "";
                bool hasUrl = false;
                bool working = true;

                while (!streamReader.EndOfStream && working)
                {
                    string line = streamReader.ReadLine();
                    
                    string command = "";
                    if (line.Length > 0 && line[0] == '#')
                        command = line.TrimStart('#');
                    if (command != "")
                    {
                        switch (command)
                        {
                            case "rec":
                                rr = new ReportRecord();
                                hasUrl = false;
                                break;
                            case "date":
                                string date = streamReader.ReadLine();
                                DateTimeOffset dto;
                                if (!DateTimeOffset.TryParse(date, null as IFormatProvider,
                                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                                    out dto))
                                {
                                    Console.WriteLine("File :: Error: bad time!");
                                    throw new Exception("Bad record time: " + date);
                                }
                                if (rr != null) rr.UpdateFinished = dto.DateTime;
                                break;
                            case "root":
                                string newRootStr = streamReader.ReadLine();
                                if (newRootStr != lastRootContents || root == null)
                                {
                                    lastRootContents = newRootStr;
                                    root = new RootRecord
                                    {
                                        Rec = "<span class=\"roots\">" + newRootStr + "</span>",
                                        UniqId = newRootStr
                                    };
                                }
                                if (rr != null) rr.RootRec = root;
                                break;
                            case "body":
                                string body = streamReader.ReadLine();
                                if (rr != null) rr.ParentPath = body;
                                break;
                            case "url":
                                string url = streamReader.ReadLine();
                                hasUrl = true;
                                if (rr != null) rr.ParentUrl = url;
                                break;
                            case "end":
                                if (rr != null)
                                {
                                    if (rr.UpdateFinished < WayTooLongAgo)
                                    {
                                        working = false;
                                    }
                                    else
                                    {
                                        if (rr.RootRec == null)
                                            throw new Exception("Rootless records are not allowed!");
                                        if (!hasUrl)
                                            rr.ParentUrl = "";
                                        rr.FileDateTimes.Add(rr.UpdateFinished);
                                        report.Add(rr);
                                        rr = null;
                                    }
                                }
                                break;
                            case "showall":
                            case "nowrap":
                                // obsolete
                                break;
                            case "rem":
                                break;
                        }
                    }
                }
                FootnoteReport = "ok";
            }
            catch (Exception E)
            {
                FootnoteReport = "completed with errors";
                Console.WriteLine("File :: Error:");
                Console.WriteLine(E.Message);
            }
            return report;
        }

        public string GetShortName()
        {
            return "File";
        }

        public string GetVersion()
        {
            return "v.1.5";
        }

        public void Initialize(string workDir, string exeDir, DateTime wayTooLongAgo)
        {
            WorkingDirectory = workDir;
            ExecutableDirectory = exeDir;
            WayTooLongAgo = wayTooLongAgo;
            InitState = true;
        }

        public bool Initialized()
        {
            return InitState;
        }

        public string ReportStatus()
        {
            return FootnoteReport;
        }

        public bool HasFailed()
        {
            return false;
        }
    }
}
