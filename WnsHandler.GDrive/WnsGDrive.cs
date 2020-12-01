using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WhatsNewShared;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.IO;
using System.Threading;
using System.Net;

namespace WnsHandler.GDrive
{
    public class WnsGDrive : IWnsHandler
    {
        private string WorkingDirectory = "";
        private string ExecutableDirectory = "";
        private DateTime WayTooLongAgo;
        private bool InitState = false;
        private bool HandlerHasFailed = false;
        private string FootnoteReport = "unused";
        private string Mirror = "";

        static string[] Scopes = { DriveService.Scope.DriveReadonly };
        static string ApplicationName = "WNS App";
        Google.Apis.Drive.v3.DriveService GdService;
        
        private List<ReportRecord> Report = null;
        private RootRecord Root = null;

        private Dictionary<string, string> DirectoryNames = new Dictionary<string, string>();

        void DigFolders(string folders)
        {
            Console.WriteLine("[GDrive] Q: " + folders);
            string pageToken = null;
            Dictionary<string, List<DateTime>> fileModDatesByFolder = new Dictionary<string, List<DateTime>>();
            List<string> subfolders = new List<string>();
            do
            {
                //Google.Apis.Drive.v3.FilesResource.files
                var request = GdService.Files.List();
                request.Q = folders;
                request.Spaces = "drive";
                request.IncludeItemsFromAllDrives = true;
                request.SupportsAllDrives = true;
                request.Fields = "nextPageToken, files(id, name, parents, mimeType, modifiedTime, createdTime)";
                request.PageToken = pageToken;
                request.PageSize = 500;

                FileList result = new FileList();
                int reqAttempts = 0;
                bool reqFailed = false;
                while (true)
                {
                    try
                    {
                        result = request.Execute();
                        break;
                    }
                    catch (Exception e)
                    {
                        reqAttempts++;
                        Console.WriteLine("Request failed, attempt " + reqAttempts.ToString());
                        if (reqAttempts == 4)
                        {
                            Console.WriteLine("Total fuckup :<");
                            Console.WriteLine(e.Message);
                            FootnoteReport = "completed with errors";
                            HandlerHasFailed = true;
                            reqFailed = true;
                            break;
                        }
                        else
                        {
                            // 20 sec, 40 sec, 80 sec
                            Thread.Sleep(10000 * (int)Math.Pow(2.0, reqAttempts));
                        }
                    }
                }
                if (reqFailed) break;
                foreach (var file in result.Files)
                {
                    var parentsList = file.Parents;
                    if (parentsList.Count == 0)
                    {
                        Console.WriteLine("[GDrive] Warning: folder is parentless, ignoring it");
                        continue;
                    }
                    string parent = "err";
                    foreach (string parentCand in parentsList)
                    {
                        if (DirectoryNames.ContainsKey(parentCand))
                        {
                            parent = parentCand;
                            break; // just pick one parent we are aware of, in case there somehow are many
                        }
                    }
                    if (parent == "err")
                    {
                        Console.WriteLine("[GDrive] Error: unknown parent!");
                        continue;
                    }
                    // is folder
                    if (file.MimeType.Contains("application/vnd.google-apps.folder"))
                    {
                        DirectoryNames[file.Id] =
                            (DirectoryNames[parent] == "" ? file.Name :
                            DirectoryNames[parent] + " \b " + file.Name);
                        subfolders.Add(file.Id);
                    }
                    // is file
                    else
                    {
                        var modTime = file.ModifiedTime.HasValue ? file.ModifiedTime.Value.ToUniversalTime() : new DateTime(2000, 1, 1);
                        var creatTime = file.CreatedTime.HasValue ? file.CreatedTime.Value.ToUniversalTime() : new DateTime(2000, 1, 1);
                        if (creatTime > modTime) modTime = creatTime;
                        //if (file.ModifiedTime.HasValue)
                        {
                            if (modTime < WayTooLongAgo)
                            {
                                continue; // too old.
                            }
                            if (!fileModDatesByFolder.ContainsKey(parent))
                            {
                                fileModDatesByFolder[parent] = new List<DateTime>();
                            }
                            fileModDatesByFolder[parent].Add(modTime);
                        }
                    }
                }
                pageToken = result.NextPageToken;
            } while (pageToken != null);
            // add files to report
            foreach (var parentDatesListPair in fileModDatesByFolder)
            {
                var fileModifyDates = parentDatesListPair.Value;
                fileModifyDates.Sort();
                int firstIndex = 0;
                int cnt = 0;
                for (int i = 0; i < fileModifyDates.Count; i++)
                {
                    cnt++;
                    if (i + 1 == fileModifyDates.Count || (fileModifyDates[i + 1] - fileModifyDates[i]).TotalMinutes > 60)
                    {
                        ReportRecord rr = new ReportRecord
                        {
                            NumberOfUpdates = cnt,
                            UpdateFinished = fileModifyDates[i],
                            ParentUrl = (
                                Mirror == ""
                                ? "https://drive.google.com/drive/folders/" + parentDatesListPair.Key
                                : Mirror + "/" + WebUtility.UrlEncode(DirectoryNames[parentDatesListPair.Key]).Replace("+", "%20").Replace("%2F", "%EF%BC%8F").Replace("%20%08%20", "/") + "/"
                            ),
                            ParentPath = DirectoryNames[parentDatesListPair.Key]
                                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                                .Replace(" \b ", " / "),
                            RootRec = Root,
                            FileDateTimes = fileModifyDates.GetRange(firstIndex, cnt)
                        };
                        Report.Add(rr);
                        cnt = 0;
                        firstIndex = i + 1;
                    }
                }
            }
            int fc = 0;
            const int foldersPerRequest = 1; // unless google fixes their shit it should be one
            folders = "";
            foreach (string sub in subfolders)
            {
                if (fc % foldersPerRequest == 0)
                    folders = "'" + sub + "' in parents";
                else
                    folders += " or '" + sub +"' in parents";
                fc++;
                if (fc % foldersPerRequest == 0)
                {
                    DigFolders(folders);
                    folders = "";
                }
            }
            if (folders != "")
                DigFolders(folders);
        }

        public List<ReportRecord> Crawl(string parent)
        {
            Report = new List<ReportRecord>();
            try
            {
                var pieces = parent.Split(new[] { ' ' }, 3);
                Mirror = "";
                if (pieces.Length > 2)
                {
                    var prms = parent.Split(new[] { ' ' });
                    foreach (var param in prms)
                    {
                        if (param.Contains("-mirror="))
                        {
                            Mirror = param.Replace("-mirror=", "");
                            Console.WriteLine("GDrive :: Mirror = " + Mirror);
                        }
                    }
                }
                if (pieces[0].Contains("$"))
                {
                    string hashFileName = Path.Combine(ExecutableDirectory, pieces[0].Replace("$", ""));
                    var hashFile = System.IO.File.ReadAllLines(hashFileName);
                    pieces[0] = hashFile[0];
                    Console.WriteLine("GDrive :: real hash = " + pieces[0]);
                }
                Console.WriteLine("GDrive :: Processing " + pieces[0]);
                DirectoryNames[pieces[0]] = "";//pieces[1]; // root name
                if (Mirror == "")
                    Root = new RootRecord
                    {
                        Rec =
                            "<a class=\"rootl\" href=\"https://drive.google.com/drive/folders/"
                            + pieces[0] + "\">" + pieces[1] + "</a> / "
                    };
                else
                    Root = new RootRecord
                    {
                        Rec =
                            "<a class=\"rootl\" href=\"" + Mirror + "/"
                            + "\">" + pieces[1] + "</a> / "
                    };
                DigFolders("'" + pieces[0] + "' in parents");
                
                Console.WriteLine("GDrive :: Valid records: " + Report.Count);
            }
            catch (Exception e)
            {
                Console.WriteLine("GDrive :: Error: " + e.Message);
                FootnoteReport = "completed with errors (unhandled exception)";
                HandlerHasFailed = true;
            }
            return Report;
        }

        public string GetShortName()
        {
            return "GDrive";
        }

        public void Initialize(string workDir, string exeDir, DateTime wayTooLongAgo)
        {
            InitState = false;

            WorkingDirectory = workDir;
            ExecutableDirectory = exeDir;
            WayTooLongAgo = wayTooLongAgo;
            
            UserCredential credential;

            using (var stream =
                new FileStream(Path.Combine(ExecutableDirectory, "client_id.json"),
                    FileMode.Open, FileAccess.Read))
            {
                string credPath = Path.Combine(WorkingDirectory, "gdrive.json");
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("GDrive :: Credentials file: " + credPath);
            }

            // Create Drive API service.
            int authAttempts = 0;
            while (true)
            { 
                GdService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
                if (GdService == null)
                {
                    Console.WriteLine("GDrive :: Authorization failed.");
                    authAttempts++;
                    if (authAttempts == 3)
                    {
                        Console.WriteLine("GDrive :: A complete authorization fuckup, giving up");
                        FootnoteReport = "authorization failure";
                        HandlerHasFailed = true;
                        break;
                    }
                    else
                    {
                        Thread.Sleep(15000);
                    }
                }
                else
                {
                    Console.WriteLine("GDrive :: Authorized.");
                    FootnoteReport = "ok";
                    InitState = true;
                    break;
                }
            }
        }

        public bool Initialized()
        {
            return InitState;
        }

        public string ReportStatus()
        {
            return FootnoteReport;
        }

        public string GetVersion()
        {
            return "v.1.7";
        }

        public bool HasFailed()
        {
            return HandlerHasFailed;
        }
    }
}
