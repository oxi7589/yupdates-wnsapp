using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
                var crawlerArgs = arg.TrimEnd();
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
                            default:
                                break;
                        }
                    }
                }

                var crawlResults = handler.Crawl(crawlerArgs);
                if (crawlResults != null)
                    foreach (var cr in crawlResults)
                        if (cr.UpdateFinished > wayTooLongAgoLocal)
                            Report.Add(cr);
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
        }
    }
}
