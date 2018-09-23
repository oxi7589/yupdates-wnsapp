using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
                            handler.Initialize(UsrDirectory, GetExecutingDirectoryName(), wayTooLongAgo);
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

            foreach (var r in Report)
            {
                if (r.RootRec.Rec != prevRoot || r.UpdateFinished.AddHours(-1) > prevDate)
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

                group += String.Format(
                    Properties.Resources.ResourceManager.GetString("ReportLineStrresStart"),
                    r.UpdateFinished.ToString("s") + "Z", // record date
                    r.RootRec.Rec // root (or the only only) link
                );
                if (r.ParentPath!="")
                {
                    group += String.Format(
                        Properties.Resources.ResourceManager.GetString("ReportLineStrresPath"),
                        r.ParentUrl,
                        r.ParentPath
                    );
                }
                if (r.NumberOfUpdates > 0)
                {
                    group += String.Format(
                        Properties.Resources.ResourceManager.GetString("ReportLineStrresCount"),
                        r.NumberOfUpdates
                    );
                }
                group += Properties.Resources.ResourceManager.GetString("ReportLineStrresEnd");

                // too much stuff, ignore anything following
                if (group.Length + rep.Length > 48000 && overflow == 0) { overflow = 1; }
                if (group.Length + rep.Length > 100000) { overflow2 = true; break; }
            }

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

            string SystemStatus = String.Format("ok [v.1.2.2 on {0}]", Environment.OSVersion.ToString());
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
            Console.WriteLine("WNS 1.2.2 running from " + GetExecutingDirectoryName());

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