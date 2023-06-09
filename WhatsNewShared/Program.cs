﻿using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WhatsNewShared
{
    internal static class GenericPluginLoader<T>
    {
        public static ICollection<T> LoadHandlers(string path)
        {
            string[] dllFileNames = null;

            if (Directory.Exists(path))
            {
                dllFileNames = Directory.GetFiles(path, "WnsHandler*.dll");

                ICollection<Assembly> assemblies = new List<Assembly>(dllFileNames.Length);
                foreach (string dllFile in dllFileNames)
                {
                    AssemblyName an = AssemblyName.GetAssemblyName(dllFile);
                    Assembly assembly = Assembly.Load(an);
                    assemblies.Add(assembly);
                }

                Type pluginType = typeof(T);
                ICollection<Type> pluginTypes = new List<Type>();
                foreach (Assembly assembly in assemblies)
                {
                    if (assembly != null)
                    {
                        Type[] types = assembly.GetTypes();

                        foreach (Type type in types)
                        {
                            if (type.IsInterface || type.IsAbstract)
                            {
                                continue;
                            }
                            else
                            {
                                if (type.GetInterface(pluginType.FullName) != null)
                                {
                                    pluginTypes.Add(type);
                                }
                            }
                        }
                    }
                }

                ICollection<T> handlers = new List<T>(pluginTypes.Count);
                foreach (Type type in pluginTypes)
                {
                    T plugin = (T)Activator.CreateInstance(type);
                    handlers.Add(plugin);
                    if (plugin is IParallelWnsHandler)
                    {
                        int instCount = ((IParallelWnsHandler)plugin).GetParallelInstancesCount();
                        for (int i = 2; i <= instCount; ++i)
                        {
                            T pluginDupe = (T)Activator.CreateInstance(type);
                            handlers.Add(pluginDupe);
                        }
                    }
                }

                return handlers;
            }

            return null;
        }
    }

    public class FeedState
    {
        public ulong HashCounter { get; set; }
        public List<string> Entries { get; set; }
    }

    public class ExportedReportData
    {
        public string ReportBody { get; set; }
        public string RootsList { get; set; }
        public string TechDetails { get; set; }
        public string Timestamp { get; set; }
        public bool Success { get; set; }
    }

    internal class UpdateChecker
    {
        //List<IWnsHandler> SpecialHandlers = null;
        Dictionary<string, List<IWnsHandler>> SpecialHandlers = new Dictionary<string, List<IWnsHandler>>();

        static string ExeDirectory = "";
        static string UsrDirectory = "";
        List<string> RootDirs = null;
        DateTime wayTooLongAgo;

        List<ReportRecord> Report = new List<ReportRecord>();
        string PageFooter = "";
        string PageHeader = "";

        string YdcSecret = "/y/upd4tez";

        bool SomethingHasFailed = false;

        public int ReportPeriod { get; set; }
        public bool Push2YDC { get; set; }
        public bool NoSizeLimit { get; set; }

        public UpdateChecker()
        {
            NoSizeLimit = false;
            Push2YDC = false;
        }

        public static string GetExecutingDirectoryName()
        {
            if (ExeDirectory != "") return ExeDirectory;
            var location = new Uri(System.Reflection.Assembly.GetEntryAssembly().GetName().CodeBase);
            return ExeDirectory = new FileInfo(location.AbsolutePath).Directory.FullName;
        }

        string GetVersion()
        {
            return "v.1.8.0.0";
        }

        void DigDrive()
        {
            wayTooLongAgo = DateTime.Now.ToUniversalTime().AddDays(-ReportPeriod);
            Console.WriteLine("Digging for files newer than " + wayTooLongAgo.ToString());

            Report.Clear();

            Dictionary<string, ConcurrentQueue<string>> rootsPerType = new Dictionary<string, ConcurrentQueue<string>>();

            foreach (string root in RootDirs)
            {
                var pieces = root.Split(new[] { ' ' }, 2);
                var key = pieces[0];
                if (key.Length <= 12 && key != "#")
                {
                    if (!rootsPerType.ContainsKey(key))
                    {
                        if (!SpecialHandlers.ContainsKey(key))
                        {
                            Console.WriteLine("Warning: no handler for type " + key);
                            continue;
                        }

                        rootsPerType[key] = new ConcurrentQueue<string>();

                        var handlers = SpecialHandlers[key];
                        foreach (var handler in handlers)
                        {
                            try
                            {
                                if (!handler.Initialized())
                                {
                                    handler.Initialize(UsrDirectory, GetExecutingDirectoryName(), wayTooLongAgo.AddDays(-1));
                                    if (!handler.Initialized())
                                    {
                                        SpecialHandlers[key].Clear();
                                        //SpecialHandlers.Add(key + "!errNotInit", handler);
                                        SomethingHasFailed = true;
                                        continue;
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Handler [" + key + "] failed with exception: " + e.Message);
                                SomethingHasFailed = true;
                            }
                        }
                    }
                    rootsPerType[key].Enqueue(pieces[1]);
                }
            }

            List<HandlerThread> hthrl = new List<HandlerThread>();
            foreach (var kvp in rootsPerType)
            {
                int hthrid = 0;
                var handlerType = kvp.Key;
                foreach (var handler in SpecialHandlers[handlerType])
                {
                    HandlerThread hthr = new HandlerThread();
                    hthr.name = handlerType + hthrid.ToString();
                    hthr.queue = kvp.Value;
                    hthr.wnsHandler = handler;
                    hthr.handlerType = handlerType;
                    hthr.wayTooLongAgo = wayTooLongAgo;
                    hthr.executableDirectory = GetExecutingDirectoryName();
                    hthrl.Add(hthr);
                    Console.WriteLine("Worker spawned: " + handlerType + hthrid.ToString());
                    ++hthrid;
                }
            }

            var processors = new List<Thread>();
            foreach (var hthr in hthrl)
            {
                Thread workerThread = new Thread(hthr.Execute);
                workerThread.Start();
                processors.Add(workerThread);
            }

            foreach (var p in processors)
            {
                p.Join();
            }

            foreach (var hthr in hthrl)
            {
                Report.AddRange(hthr.Report);
                SomethingHasFailed |= hthr.SomethingHasFailed;
            }

            foreach (var handlerPair in SpecialHandlers)
            {
                foreach (var handler in handlerPair.Value)
                {
                    SomethingHasFailed |= handler.HasFailed();
                }
            }
            
            Report.Sort((x, y) => y.UpdateFinished.CompareTo(x.UpdateFinished));
        }
        
        void UpdateFeed(List<string> atomRecordsList)
        {
            var atomRecordsListPrev = new List<string>();

            // state
            string recordsListPath = Path.Combine(UsrDirectory, "recordslist.json");
            string recordsListAtomPath = Path.Combine(UsrDirectory, "recordslist.atom.json");

            // Atom/XML templates
            string atomTemplateBodyPath = Path.Combine(ExeDirectory, "atombody.xml");
            string atomTemplateEntryPath = Path.Combine(ExeDirectory, "atomentry.xml");
            string atomTemplateBody = "";
            string atomTemplateEntry = "";
            // Atom/XML target path
            string atomResultPath = Path.Combine(UsrDirectory, "latest.atom.xml");
            // patch data target path
            string updateMarkerPath = Path.Combine(UsrDirectory, "recordslist.wnsm"); // data to be pushed to Discord

            if (File.Exists(atomTemplateBodyPath)) atomTemplateBody = File.ReadAllText(atomTemplateBodyPath);
            if (File.Exists(atomTemplateEntryPath)) atomTemplateEntry = File.ReadAllText(atomTemplateEntryPath);
            if (atomTemplateBody.Length > 0 && atomTemplateEntry.Length > 0)
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();

                if (File.Exists(recordsListPath))
                {
                    try
                    {
                        atomRecordsListPrev = serializer.Deserialize<List<string>>(File.ReadAllText(recordsListPath));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Atom :: Deserialization failed. Error: " + e.Message);
                        atomRecordsListPrev = new List<string>();
                    }
                }
                try
                {
                    File.WriteAllText(recordsListPath, serializer.Serialize(atomRecordsList));
                }
                catch (Exception e)
                {
                    Console.WriteLine("Atom ::Serialization failed. Error: " + e.Message);
                }

                var newRecs = atomRecordsList.Except(atomRecordsListPrev);

                if (newRecs.Count() > 0)
                {
                    Console.WriteLine("Atom :: New records count = " + newRecs.Count().ToString());

                    // read feed state
                    FeedState fs = null;
                    if (File.Exists(recordsListAtomPath))
                    {
                        try
                        {
                            fs = serializer.Deserialize<FeedState>(File.ReadAllText(recordsListAtomPath));
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Atom :: Deserialization failed. Error: " + e.Message);
                            fs = null;
                        }
                    }
                    if (fs == null)
                    {
                        fs = new FeedState();
                        fs.HashCounter = 1000;
                        fs.Entries = new List<string>();
                    }

                    // actualize
                    string feedInnerHtml = "";
                    foreach (string rec in newRecs)
                        feedInnerHtml += rec;
                    fs.HashCounter += 1;
                    fs.Entries.Insert(0,
                        string.Format(atomTemplateEntry,
                            fs.HashCounter.ToString(), //hash
                            DateTime.Now.ToUniversalTime().ToString("s") + "Z", //updated
                            feedInnerHtml //html
                        )
                    );
                    while (fs.Entries.Count() > 10)
                        fs.Entries.RemoveAt(fs.Entries.Count() - 1);

                    File.WriteAllText(updateMarkerPath, feedInnerHtml);

                    // write xml
                    string allXmlEntries = "";
                    foreach (var entry in fs.Entries)
                        allXmlEntries += entry;

                    File.WriteAllText(
                        atomResultPath,
                        String.Format(
                            atomTemplateBody,
                            DateTime.Now.ToUniversalTime().ToString("s") + "Z", //updated
                            allXmlEntries // entries
                        )
                    );

                    // dump feed state back
                    try
                    {
                        File.WriteAllText(recordsListAtomPath, serializer.Serialize(fs));
                        Console.WriteLine("Atom :: ok");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Atom :: Serialization failed. Error: " + e.Message);
                    }
                }
                else
                {
                    Console.WriteLine("Atom :: No changes, no need to update feed");
                }
            }
            // end of ATOM generation
        }

        void MakeReport()
        {
            // load previous report
            string oldReportPath = Path.Combine(UsrDirectory, "prevrep.json");
            List<ReportRecord> oldReport = null;
            if (File.Exists(oldReportPath))
            {
                try
                {
                    oldReport = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ReportRecord>>(File.ReadAllText(oldReportPath));
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR :: State deserialization failure!");
                    Console.WriteLine(e.Message);
                    oldReport = null;
                }
            }
            if (oldReport == null)
            {
                oldReport = new List<ReportRecord>();
            }

            // compare reports and produce a new one
            
            var oldReportActualRecords = new List<ReportRecord>();
            foreach (var oldRecord in oldReport)
            {
                // ignore old records that were already expired
                if (oldRecord.UpdateFinished < wayTooLongAgo)
                {
 //                   Console.WriteLine("dbg :: old rec expired");
                    continue;
                }
                // deep copy old rec's DateTime list 
                var oldRecordDateTimes = new List<DateTime>();
                oldRecordDateTimes.AddRange(oldRecord.FileDateTimes);
                // iterate through all new records and erase dates that were found in the new report
                foreach (var newRecord in Report)
                {
                    if ((newRecord.Flags & ReportFlags.NotNew) == ReportFlags.NotNew)
                    {
                        continue; // don't care for new recs already proven to be old news
                    }
                    if (newRecord.RootRec.UniqId == oldRecord.RootRec.UniqId // root uids must match
                        && newRecord.ParentUrl == oldRecord.ParentUrl // urls must match, though may both be ""
                        && (newRecord.ParentPath == oldRecord.ParentPath || newRecord.ParentUrl != "")
                        ) // good candidate for further checks (same folder path)
                    {
                        var datesIntersection = oldRecordDateTimes.IntersectAll(newRecord.FileDateTimes);
                        var newRecDateTimes = newRecord.FileDateTimes.ExceptAll(datesIntersection).ToList();
                        newRecDateTimes.Sort();
                        oldRecordDateTimes = oldRecordDateTimes.ExceptAll(datesIntersection).ToList();
                        if (newRecDateTimes.Count() > 0) // new record has more stuff than the old one
                        {
//                            Console.WriteLine("dbg :: new rec has more stuff than the old one");
                            newRecord.FileDateTimes = newRecDateTimes;
                            newRecord.UpdateFinished = newRecord.FileDateTimes.Last();
                            newRecord.NumberOfUpdates = newRecord.FileDateTimes.Count();
                        }
                        else // new is not really new
                        {
//                            Console.WriteLine("dbg :: new rec is deemed to be old news");
                            newRecord.Flags |= ReportFlags.NotNew;
                        }
                        // actualize names
                        oldRecord.RootRec.Rec = newRecord.RootRec.Rec;
                        oldRecord.ParentPath = newRecord.ParentPath;
                    }
                }
                oldRecord.Flags |= ReportFlags.NotNew;
                if (oldRecordDateTimes.Count == 0) // all entities within old record are also found in the new one(s)
                {
//                   Console.WriteLine("dbg :: old rec remains entirely");
                    oldReportActualRecords.Add(oldRecord);
                }
                else if (oldRecordDateTimes.Count == oldRecord.FileDateTimes.Count)
                {
 //                   Console.WriteLine("dbg :: old rec is erased entirely");
                    continue;
                }
                else
                {
                    oldRecord.FileDateTimes = oldRecord.FileDateTimes.ExceptAll(oldRecordDateTimes).ToList();
                    oldRecord.FileDateTimes.Sort();
                    oldRecord.UpdateFinished = oldRecord.FileDateTimes.Last();
                    oldRecord.NumberOfUpdates = oldRecord.FileDateTimes.Count();
                    if (oldRecord.UpdateFinished > wayTooLongAgo)
                        oldReportActualRecords.Add(oldRecord);
 //                   Console.WriteLine("dbg :: old rec remains partially");
                }
            }

            var actuallyNewRecords = new List<ReportRecord>();
            foreach (var newRecord in Report)
            {
                if ((newRecord.Flags & ReportFlags.NotNew) != ReportFlags.NotNew) // something actually new
                {
                    actuallyNewRecords.Add(newRecord);
                }
            }

            Report = actuallyNewRecords;
            Report.AddRange(oldReportActualRecords);

            if (!SomethingHasFailed)
            {
                try
                {
                    File.WriteAllText(oldReportPath, Newtonsoft.Json.JsonConvert.SerializeObject(Report));
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR :: Report serialization failed");
                    Console.WriteLine(e.Message);
                }
            }

            // make report page

            const string stFullRepMark = "<!--StOfReport-->";
            const string eoFullRepMark = "<!--EndOfReport-->";

            string rep = PageHeader + stFullRepMark;
            string repMainBody = "";

            // report body exceeds 500kb
            bool overflow2 = false;

            string group = "";
            DateTime prevDate = DateTime.Now;
            string prevRoot = "nil";
            int gCount = 0;
            
            // records formatted for Atom/rss
            List<string> atomRecordsList = new List<string>();

            foreach (var r in Report)
            {
                if (r.RootRec.Rec != prevRoot || prevDate.AddHours(-1) > r.UpdateFinished)
                {
                    if (r.UpdateFinished.Month != prevDate.Month)
                    {
                        repMainBody += Properties.Resources.ResourceManager.GetString("wns17_MonSeparator");
                    }
                    // new group. Write old one to the report
                    if (gCount >= 10)
                    {
                        repMainBody += String.Format(
                            Properties.Resources.ResourceManager.GetString("ReportGroupStart"), 
                            prevRoot)
                            + group
                            + Properties.Resources.ResourceManager.GetString("ReportGroupEnd");
                    }
                    else repMainBody += group;
                    // reset group-related stuff
                    group = ""; gCount = 0;
                }

                gCount++; prevDate = r.UpdateFinished; prevRoot = r.RootRec.Rec;

                // current report line
                string currentline = "";
                // :: body
                if (r.ParentPath != "")
                {
                    currentline = String.Format(
                            r.ParentUrl != ""
                                ? Properties.Resources.ResourceManager.GetString("wns13_ReportBodyUrl")
                                : Properties.Resources.ResourceManager.GetString("wns13_ReportBodySimple"),
                            r.ParentUrl, r.ParentPath
                        );
                }
                // :: counter
                if (r.NumberOfUpdates > 0)
                {
                    if (currentline != "") currentline += " ";
                    currentline += String.Format(
                        Properties.Resources.ResourceManager.GetString("wns13_ReportBodyCounter"),
                        r.NumberOfUpdates,
                        r.NumberOfUpdates == 1
                            ? ""
                            : Properties.Resources.ResourceManager.GetString("wns13_ReportBodyCounterPlural")
                    );
                }
                // :: join root and body
                if (currentline == "")
                    currentline = r.RootRec.Rec;
                else
                    currentline = r.RootRec.Rec + " " + currentline;
                // :: date
                string date = r.UpdateFinished.ToString("s") + "Z";
                string dateHumanReadable = r.UpdateFinished.ToString();
                string dateStub = Properties.Resources.ResourceManager.GetString("wns14_DateStub");
                // wrap in repLine div
                string wrappedLine = String.Format(
                    Properties.Resources.ResourceManager.GetString("wns14_ReportLine"),
                    currentline // line root+body
                );
                // :: date for HTML report
                group += wrappedLine.Replace(dateStub, date);
                // :: date for Atom/RSS
                if ((r.Flags & ReportFlags.NotNew) != ReportFlags.NotNew)
                {
                    atomRecordsList.Add(wrappedLine.Replace(dateStub, dateHumanReadable));
                }

                // too much stuff, ignore anything following
                int groupLen8 = Encoding.UTF8.GetByteCount(group);
                int repLen8 = Encoding.UTF8.GetByteCount(repMainBody);

                // limit the body of the report to 500kb unless -nolimit flag is used
                // this limits the damage caused by WNS bugs
                if (groupLen8 + repLen8 > 500000) { if (!NoSizeLimit) { overflow2 = true; break; } }
            }

            if (!SomethingHasFailed)
            {
                UpdateFeed(atomRecordsList);
            }
            else
            {
                Console.WriteLine("Error :: something has failed, not updating the feeds");
            }

            // add last group
            if (gCount >= 10)
            {
                repMainBody += String.Format(
                    Properties.Resources.ResourceManager.GetString("ReportGroupStart"),
                    prevRoot)
                    + group
                    + Properties.Resources.ResourceManager.GetString("ReportGroupEnd");
            }
            else
            {
                repMainBody += group;
            }

            // . . . in the end if the length of the report is just waaaaay too huge
            if (overflow2) repMainBody += Properties.Resources.ResourceManager.GetString("ReportOverflow");

            rep += repMainBody;
            rep += eoFullRepMark;
            
            string DrivesList = "";
            foreach (string drive in RootDirs)
                DrivesList += drive.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;") + "\n";
            
            string SystemStatus = String.Format("Updates checker {0} on {1}",
                GetVersion(),
                Environment.OSVersion.ToString());
            foreach (var shPair in SpecialHandlers)
            {
                foreach (var handler in shPair.Value)
                    SystemStatus += ("<br>Plugin[" + shPair.Key + ", "+ handler.GetVersion() +"]: " 
                        + handler.ReportStatus());
            }

            string cdt = DateTime.Now.ToUniversalTime().ToString("s") + "Z";
            rep += String.Format(
                PageFooter,
                DrivesList, cdt, SystemStatus);
           
            // save full report to filesystem just in case
            string pagepath = Path.Combine(UsrDirectory, "latest.htm");
            string pagepathbak = Path.Combine(UsrDirectory, "latest.bak.htm");
            if (System.IO.File.Exists(pagepathbak))
                System.IO.File.Delete(pagepathbak);
            if (System.IO.File.Exists(pagepath))
                System.IO.File.Copy(pagepath, pagepathbak);
            System.IO.File.WriteAllText(pagepath, rep, Encoding.UTF8);

            // save JSON report as well
            string expjsonpath = Path.Combine(UsrDirectory, "latest.json");
            ExportedReportData erd = new ExportedReportData
            {
                ReportBody = repMainBody,
                RootsList = DrivesList,
                TechDetails = SystemStatus,
                Timestamp = cdt,
                Success = !SomethingHasFailed
            };
            var serializedErd = JsonConvert.SerializeObject(erd);
            System.IO.File.WriteAllText(expjsonpath, serializedErd, Encoding.UTF8);

            if (Push2YDC)
            {
                HttpClient http = new HttpClient();
                http.DefaultRequestHeaders.Add("X-Yupdates-Secret", YdcSecret);
                //http.DefaultRequestHeaders.Add("Authorization", "Basic YWRmOmFkZg==");
                http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 WnsApp.NET");
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                var httpc = new StringContent(serializedErd, Encoding.UTF8, "application/json");
                var response = http.PostAsync("https://cat.yupdates.art/sys/push-upd-report", httpc)
                    .GetAwaiter().GetResult();
                try
                {
                    var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var contentObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
                    var succ = (bool)contentObj["success"];
                    if (succ)
                    {
                        Console.WriteLine("YDC publication succeeded");
                    }
                    else
                    {
                        throw new Exception("YDC API has rejected the request");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error :: YDC publication failed with exception (" + e.Message + ")");
                }
            }
            else
            {
                Console.WriteLine("YDC publication has not been enabled.");
            }
        }

        public void Run()
        {
            Console.WriteLine("WNS " + GetVersion() + " running from " + GetExecutingDirectoryName());

            #region reading config files and templates
            try
            {
                UsrDirectory = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.Personal);
                UsrDirectory = Path.Combine(UsrDirectory, ".wnsapp");

                RootDirs = new List<string>();
                List<string> allLinesRoots =
                    System.IO.File.ReadAllLines(Path.Combine(GetExecutingDirectoryName(), "roots.txt")).ToList();
                foreach (var line in allLinesRoots)
                {
                    if (line != "") RootDirs.Add(line);
                }
                Console.WriteLine("Found " + RootDirs.Count.ToString() + " root directories.");

                PageHeader =
                    System.IO.File.ReadAllText(Path.Combine(GetExecutingDirectoryName(), "header.txt"));
                PageFooter =
                    System.IO.File.ReadAllText(Path.Combine(GetExecutingDirectoryName(), "footer.txt"));
                YdcSecret =
                    System.IO.File.ReadAllText(Path.Combine(GetExecutingDirectoryName(), "ydc-secret.txt"));
                YdcSecret = Regex.Replace(YdcSecret, "[^A-Za-z0-9-]", "");
            }
            catch (Exception E)
            {
                Console.WriteLine("Reading configs failed!");
                Console.WriteLine(E.Message);
                return;
            }
            #endregion

            #region Loading special handlers plugins
            try
            {
                var handlers = GenericPluginLoader<IWnsHandler>.LoadHandlers(GetExecutingDirectoryName());
                foreach (var handler in handlers)
                {
                    var shortName = handler.GetShortName();
                    if (!SpecialHandlers.ContainsKey(shortName))
                    {
                        SpecialHandlers[shortName] = new List<IWnsHandler>();
                    }
                    SpecialHandlers[shortName].Add(handler);
                    Console.WriteLine("Handler loaded:" + handler.GetShortName());
                }
            }
            catch (Exception E)
            {
                Console.WriteLine("Special handlers initialization failed!");
                Console.WriteLine(E.Message);
                return;
            }
            #endregion
            
            #region crawling the drives
            try
            {
                DigDrive();
            }
            catch (Exception E)
            {
                Console.WriteLine("Something unexpected went wrong while digging dat drive!");
                Console.WriteLine(E.Message);
                SomethingHasFailed = true;
                return;
            }
            #endregion

            #region making a report
            try
            {
                MakeReport();
            }
            catch (Exception E)
            {
                Console.WriteLine("Report creation failed!");
                Console.WriteLine(E.Message);
                return;
            }
            #endregion

            Console.WriteLine("Done.");
            //Console.Read();
        }

    }
    
    class Program
    {
        static void Main(string[] args)
        {
            int reportPeriod = 32;

            UpdateChecker core = new UpdateChecker();

            foreach (string line in args)
            {
                if (line.Contains("-rp"))
                {
                    try { reportPeriod = int.Parse(line.Substring(3)); }
                    catch (Exception e) {
                        Console.WriteLine(e.Message);
                    }
                }
                if (line.Contains("-nolimit"))
                {
                    core.NoSizeLimit = true;
                }
                if (line.Contains("-push2ydc"))
                {
                    core.Push2YDC = true;
                }
            }

            core.ReportPeriod = reportPeriod;
            core.Run();
        }
    }
}