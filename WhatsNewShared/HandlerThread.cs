using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace WhatsNewShared
{
    class HandlerThread
    {
        public string name = "unnamed";

        public IWnsHandler wnsHandler;
        public string handlerType;
        public DateTime wayTooLongAgo;
        public string executableDirectory;
        public ConcurrentQueue<string> queue;

        public bool SomethingHasFailed = false;
        public List<ReportRecord> Report = new List<ReportRecord>();

        const string argsSplitter = "<<";

        void Process(string arg)
        {
            Console.WriteLine("[[" + name + "]] " + arg);
            var handler = wnsHandler;
            try
            {
                // extra arguments for the WNS engine itself
                var wayTooLongAgoLocal = wayTooLongAgo;
                string mirrorUrl = "";

                var crawlerArgs = arg.TrimEnd();

                // Handler arguments may be stored in file.
                // This is particularly useful when the drive is mirrored and we'd like to hide its true path
                if (crawlerArgs.StartsWith("$"))
                {
                    // first argument of the $somePath kind is expanded to the contents of somePath file
                    var splitArgs = crawlerArgs.Split(new char[] { ' ' }, 2);
                    // en-space can be used to encode regular spaces
                    // (paths actually containing en-spaces are out of luck)
                    var argsFilePath = System.IO.Path.Combine(
                        executableDirectory, 
                        splitArgs[0].Replace(" ", " ").Substring(1)
                    );
                    var argsFile = System.IO.File.ReadAllLines(argsFilePath).First();
                    splitArgs[0] = argsFile;
                    crawlerArgs = string.Join(" ", splitArgs);
                    Console.WriteLine("Arguments after $file expansion: " + crawlerArgs);
                }

                if (crawlerArgs.Contains(argsSplitter))
                {
                    var args = crawlerArgs.Split(new string[] { argsSplitter }, StringSplitOptions.RemoveEmptyEntries);
                    crawlerArgs = args[0].TrimEnd();
                    var wnsArgs = args[1].Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < wnsArgs.Count(); i++)
                    {
                        switch (wnsArgs[i])
                        {
                            case "NotBefore":
                                if (i + 1 < wnsArgs.Count())
                                {
                                    var ticks = long.Parse(wnsArgs[i + 1]);
                                    var notBefore = new DateTime(ticks);
                                    wayTooLongAgoLocal = wayTooLongAgoLocal > notBefore ?
                                        wayTooLongAgo : notBefore;
                                    Console.WriteLine("Overriding timeframe start point with " + notBefore.ToString()
                                        + ", new value: " + wayTooLongAgoLocal.ToString());
                                }
                                else
                                {
                                    throw new Exception("Parameter NotBefore requires one argument: (long)Ticks");
                                }
                                break;
                            case "Mirror":
                                if (i + 1 < wnsArgs.Count())
                                {
                                    mirrorUrl = wnsArgs[i + 1];
                                }
                                else
                                {
                                    throw new Exception("Parameter Mirror requires one argument: (string)MirrorUrl");
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }

                var crawlResults = handler.Crawl(crawlerArgs);
                if (crawlResults != null)
                {
                    foreach (var cr in crawlResults)
                    {
                        if (cr.UpdateFinished > wayTooLongAgoLocal)
                        {
                            if (mirrorUrl != "")
                            {
                                if (handler.CanBeMirrored())
                                {
                                    cr.RootRec.Rec = "<a class=\"rootl\" href=\""
                                        + mirrorUrl + "/" + "\">" + cr.RootRec.Label + "</a> /";
                                    cr.ParentUrl = mirrorUrl + "/" 
                                        + WebUtility.UrlEncode(cr.ParentPathMir)
                                            .Replace("+", "%20")
                                            .Replace("%2F", "%EF%BC%8F")
                                            .Replace("%20%08%20", "/")
                                        + "/";
                                }
                                else
                                {
                                    throw new Exception("Handler does not support drive mirroring.");
                                }
                            }
                            Report.Add(cr);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Handler [" + handlerType + "] failed with exception: " + e.Message);
                SomethingHasFailed = true;
            }
        }

        public void Execute()
        {
            while (queue.TryDequeue(out string arg))
            {
                Process(arg);
            }
            Console.WriteLine("[[" + name + "]] finished");
        }
    }
}
