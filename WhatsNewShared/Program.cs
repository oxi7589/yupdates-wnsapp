﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
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

    internal class UpdateChecker
    {
        //List<IWnsHandler> SpecialHandlers = null;
        Dictionary<string, IWnsHandler> SpecialHandlers = new Dictionary<string, IWnsHandler>();

        static string ExeDirectory = "";
        static string UsrDirectory = "";
        List<string> RootDirs = null;
        string AuthorizationHeader = "";
        string PubPageId = "";
        DateTime wayTooLongAgo;

        List<ReportRecord> Report = new List<ReportRecord>();
        string PageFooter = "";
        string PageHeader = "";

        public int ReportPeriod { get; set; }

        public static string GetExecutingDirectoryName()
        {
            if (ExeDirectory != "") return ExeDirectory;
            var location = new Uri(System.Reflection.Assembly.GetEntryAssembly().GetName().CodeBase);
            return ExeDirectory = new FileInfo(location.AbsolutePath).Directory.FullName;
        }

        void DigDrive()
        {
            wayTooLongAgo = DateTime.Now.ToUniversalTime().AddDays(-ReportPeriod);
            Console.WriteLine("Digging for files newer than " + wayTooLongAgo.ToString());

            Report.Clear();

            foreach (string root in RootDirs)
            {
                var pieces = root.Split(new[] { ' ' }, 2);
                if (pieces[0].Length <= 12)
                {
                    var key = pieces[0];
                    if (!SpecialHandlers.ContainsKey(key))
                    {
                        Console.WriteLine("Warning: no handler for type " + key);
                        continue;
                    }
                    var handler = SpecialHandlers[key];
                    try
                    {
                        if (!handler.Initialized())
                        {
                            handler.Initialize(UsrDirectory, GetExecutingDirectoryName(), wayTooLongAgo.AddDays(-1));
                            if (!handler.Initialized())
                            {
                                SpecialHandlers.Remove(key);
                                SpecialHandlers.Add(key + "!errNotInit", handler);
                                continue;
                            }
                        }
                        var crawlResults = handler.Crawl(pieces[1]);
                        if (crawlResults != null)
                            foreach (var cr in crawlResults)
                                if (cr.UpdateFinished > wayTooLongAgo)
                                    Report.Add(cr);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Handler [" + key + "] failed with exception: " + e.Message);
                    }
                }
                else
                {
                    Console.WriteLine("Warning: default handler no longer supported");
                    //DigFolder(pieces[0], pieces[1]);
                }
            }

            Report.Sort((x, y) => y.UpdateFinished.CompareTo(x.UpdateFinished));
        }

        void PublishReport(string report)
        {
            var httph = new HttpClientHandler();
            httph.UseCookies = false; // disable internal cookies handling to help with import
            var http = new HttpClient(httph);
            http.DefaultRequestHeaders.Clear();

            http.DefaultRequestHeaders.Add("Authorization", AuthorizationHeader);

            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("html", report),
                new KeyValuePair<string, string>("public", "")
            };

            var content = new FormUrlEncodedContent(pairs);

            var res = http.PostAsync("https://html.house/⌂/" + PubPageId, content).Result;
            if (res.IsSuccessStatusCode)
            {
                Console.WriteLine("Uploaded successfully!");
            }
            else
            {
                Console.WriteLine("Upload failed!");
            }
        }

        void UpdateFeed(List<string> atomRecordsList)
        {
            var atomRecordsListPrev = new List<string>();

            // ATOM generation
            string recordsListPath = Path.Combine(UsrDirectory, "recordslist.json");
            string recordsListAtomPath = Path.Combine(UsrDirectory, "recordslist.atom.json");
            string atomTemplateBodyPath = Path.Combine(ExeDirectory, "atombody.xml");
            string atomTemplateEntryPath = Path.Combine(ExeDirectory, "atomentry.xml");
            string atomResultPath = Path.Combine(UsrDirectory, "latest.atom.xml");
            string updateMarkerPath = Path.Combine(UsrDirectory, "recordslist.wnsm");

            string atomTemplateBody = "";
            string atomTemplateEntry = "";

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
            string rep = PageHeader;

            const string eoSmallRepMark = "<!--EndOfSmallReport-->";
            const string eoFullRepMark = "<!--EndOfReport-->";

            int overflow = 0;
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
                    // new group. Write old one to the report
                    if (gCount >= 10)
                    {
                        rep += String.Format(
                            Properties.Resources.ResourceManager.GetString("ReportGroupStart"), prevRoot
                        ) + group + Properties.Resources.ResourceManager.GetString("ReportGroupEnd");
                    }
                    else rep += group;
                    // also mark the end of htmlhouse report
                    if (overflow == 1) { rep += eoSmallRepMark; overflow = -1; }
                    // and reset group-related stuff
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
                // :: wrap for HTML report
                if (!r.ForceShowAll)
                    group += String.Format(
                        Properties.Resources.ResourceManager.GetString("wns13_ReportLineNonCollapsible"),
                        r.UpdateFinished.ToString("s") + "Z", // record date
                        currentline // line root+body
                    );
                else
                    group += String.Format(
                        Properties.Resources.ResourceManager.GetString("wns13_ReportLineDefault"),
                        r.UpdateFinished.ToString("s") + "Z", // record date
                        currentline // line root+body
                    );
                // :: wrap for Atom/RSS
                atomRecordsList.Add(
                    String.Format(
                        Properties.Resources.ResourceManager.GetString("wns13_ReportLineAtom"),
                        r.UpdateFinished.ToString(), // record date
                        currentline // line root+body
                    )
                );

                // too much stuff, ignore anything following
                if (group.Length + rep.Length > 48000 && overflow == 0) { overflow = 1; }
                if (group.Length + rep.Length > 100000) { overflow2 = true; break; }
            }

            UpdateFeed(atomRecordsList);

            // add last group
            if (gCount >= 10)
                rep += String.Format(Properties.Resources.ResourceManager.GetString("ReportGroupStart"), prevRoot)
                    + group + Properties.Resources.ResourceManager.GetString("ReportGroupEnd");
            else
                rep += group;

            // . . . in the end if the length of the report is just waaaaay too huge
            if (overflow2) rep += Properties.Resources.ResourceManager.GetString("ReportOverflow");
            rep += eoFullRepMark;
            
            string DrivesList = "";
            foreach (string drive in RootDirs)
                DrivesList += drive + "\n";

            string SystemStatus = String.Format("ok [v.1.3.1 on {0}]", Environment.OSVersion.ToString());
            foreach (var shPair in SpecialHandlers)
            {
                SystemStatus += ("<br>Plugin[" + shPair.Key + ", "+ shPair.Value.GetVersion() +"]: " 
                    + shPair.Value.ReportStatus());
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

            // cut down report for htmlhouse
            if (rep.Contains(eoSmallRepMark))
            {
                var m1 = rep.IndexOf(eoSmallRepMark, 0, StringComparison.Ordinal);
                var m2 = rep.IndexOf(eoFullRepMark, 0, StringComparison.Ordinal);
                rep = rep.Remove(m1, m2 - m1 + eoFullRepMark.Length);
                if (!overflow2)
                {
                    rep = rep.Insert(m1, Properties.Resources.ResourceManager.GetString("ReportOverflow"));
                }
            }

            // save cut-down report to the fs as well
            string pagepathhh = Path.Combine(UsrDirectory, "latest.house.htm");
            if (File.Exists(pagepathhh))
                File.Delete(pagepathhh);
            System.IO.File.WriteAllText(pagepathhh, rep, Encoding.UTF8);

            // publish!
            if (PubPageId != "TEST")
            {
                PublishReport(rep);
            }
            else
            {
                Console.WriteLine("Note: uploading was disabled via the config.");
            }
        }

        public void Run()
        {
            Console.WriteLine("WNS 1.3.1 running from " + GetExecutingDirectoryName());

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

                var AuthHeader = 
                    System.IO.File.ReadAllLines(Path.Combine(GetExecutingDirectoryName(), "house.txt"));
                PubPageId = AuthHeader[0];
                AuthorizationHeader = AuthHeader[1];
                Console.WriteLine("Page to publish results: " + PubPageId);

                PageHeader =
                    System.IO.File.ReadAllText(Path.Combine(GetExecutingDirectoryName(), "header.txt"));
                PageFooter =
                    System.IO.File.ReadAllText(Path.Combine(GetExecutingDirectoryName(), "footer.txt"));
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
                    SpecialHandlers.Add(handler.GetShortName(), handler);
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

            foreach (string line in args)
            {
                if (line.Contains("-rp"))
                {
                    try { reportPeriod = int.Parse(line.Substring(3)); }
                    catch (Exception e) {
                        Console.WriteLine(e.Message);
                    }
                }
            }

            UpdateChecker core = new UpdateChecker();
            core.ReportPeriod = reportPeriod;
            core.Run();
        }
    }
}