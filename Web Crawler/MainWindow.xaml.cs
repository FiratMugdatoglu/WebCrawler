using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HtmlAgilityPack;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static lecture_13.csHelperMethods;
using System.Net.Http;
using System.IO;
using Path = System.IO.Path;
using System.Security.Policy;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Server;

namespace lecture_13
{
    /// <summary>
    /// MainWindow.xaml etkileşim mantığı
    /// </summary>
    public partial class MainWindow : Window
    {

       private static int _irNumberOfTotalConcurrentCrawling = 20;
        private static int _irMaximumTryCount = 3;
        

        private ObservableCollection<string> _Results = new ObservableCollection<string>(); //ObservableCollection, verilerin değiştiğinde otomatik olarak ekranı güncelleme özelliğine sahiptir

        public ObservableCollection<string> UserLogs
        {
            get { return _Results; }
            set { _Results = value; }
        }


        public MainWindow()
        {
            InitializeComponent();
            ThreadPool.SetMaxThreads(100000, 100000);  //threadpools,  istediğiniz kadar iş parçacığı başlatmanıza olanak tanır
            ThreadPool.SetMinThreads(100000, 100000);
            ServicePointManager.DefaultConnectionLimit = 1000;//this increases your number of connections to per host at the same time
            listBoxResults.ItemsSource = UserLogs;
        }

        DateTime dtStartDate;

        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
            dtStartDate = DateTime.Now;   //Tarih ve saat olarak şimdiki zamanı dtStartDate değişkenine atar.
            using (DBCrawling db = new DBCrawling()) //veritabanı işlemlerini yapmak için DBCrawling sınıfını kullanır.
            {
                db.tblMainUrls.RemoveRange(db.tblMainUrls);  //Veritabanındaki değerleri siler.
                db.SaveChanges();                            //Veritabanındaki değerleri kaydeder.
                db.tblMainUrls.Add(new tblMainUrl { Url = "www.toros.edu.tr", ParentUrlHash = "www.toros.edu.tr", SourceCode = "gg", UrlHash = "ww" });  //Veritabanına kaydeder.
                db.SaveChanges();                            //Veritabanındaki değerleri kaydeder.
            }
        }

        private void clearDBandStart(object sender, RoutedEventArgs e)
        {
           clearDatabase();
            crawlPage(txtInputUrl.Text.normalizeUrl(), 0, txtInputUrl.Text.normalizeUrl(),DateTime.Now);
            checkingTimer();

        }



     










private void checkingTimer()
        {
            System.Windows.Threading.DispatcherTimer dispatcherTimer = new System.Windows.Threading.DispatcherTimer();//arka plandaki iş parçacıkları ile ekran güncellemeleri arasındaki işlemleri yönetmek için 
            dispatcherTimer.Tick += new EventHandler(startPollingAwaitingURLs);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0,0,1000);
            dispatcherTimer.Start();
        }

        private static object _lock_CrawlingSync = new object();
        private static bool blBeingProcessed = false;
        private static List<Task> lstCrawlingTasks = new List<Task>();
        private static List<string> lstCurrentlyCrawlingUrls = new List<string>();
        private void startPollingAwaitingURLs(object sender, EventArgs e)
        {
            lock(UserLogs)
            {
                string srPerMinCrawlingspeed=(irCrawledUrlCount.ToDouble() / (DateTime.Now - dtStartDate).TotalMinutes).ToString("N2");
                string srPerMinDiscoveredLinkspeed = (irDiscoveredUrlCount.ToDouble() / (DateTime.Now - dtStartDate).TotalMinutes).ToString("N2");
                string srPassedTime = (DateTime.Now - dtStartDate).TotalMinutes.ToString("N2");

                UserLogs.Insert(0,$"{DateTime.Now}polligin awaiting urls \t processing: {blBeingProcessed} \t number of crawling tasks: {lstCrawlingTasks.Count}");
                UserLogs.Insert(0, $"Total Time: {srPassedTime} Minutes \t Total Crawled Links Count: {irDiscoveredUrlCount.ToString("N0")} \t Crawling Speed Per Minute:{srPerMinCrawlingspeed} \t Total Discovered Links : {irDiscoveredUrlCount.ToString("N0")} \t Discovered Url Speed: {srPerMinDiscoveredLinkspeed}");
            }

            logMessage($"polligin awaiting urls \t processing: {blBeingProcessed} \t number of crawling tasks: {lstCrawlingTasks.Count}");



            if (blBeingProcessed)
                return;
           lock (_lock_CrawlingSync) //Lock bir nesnenin birden fazla iş parçacığı tarafından aynı anda erişilmesini engellemek için kullanılır.
            {
                blBeingProcessed= true;

                lstCrawlingTasks = lstCrawlingTasks.Where(pr => pr.Status!=TaskStatus.RanToCompletion && pr.Status!=TaskStatus.Faulted).ToList();

                int irTaskCountToStart = _irNumberOfTotalConcurrentCrawling - lstCrawlingTasks.Count;
                if(irTaskCountToStart > 0)
                using(DBCrawling db = new DBCrawling())
                {
                     //   "OrderBy" yöntemi, bir koleksiyon içindeki elemanları belirli bir kritere göre sıralamak için kullanılır.
                 var vrReturnedList =  db.tblMainUrls.Where(x => x.IsCrawled == false && x.CrawlTryCounter < _irMaximumTryCount).OrderBy(pr => pr.DiscoverDate).Select(x => new
                    {
                         x.Url,
                        x.LinkDepthLevel
                    }).Take(irTaskCountToStart * 2).ToList(); //"Take" yöntemi, bir koleksiyon içindeki elemanların belirli bir sayısını almak için kullanılır. 


                        logMessage(string.Join(",",vrReturnedList.Select(pr => pr.Url)));


                    foreach (var vrPerReturned in vrReturnedList)
                    {
                            var vrUrlToCrawl = vrPerReturned.Url;
                            int irDepth = vrPerReturned.LinkDepthLevel;
                            lock (lstCurrentlyCrawlingUrls) 
                            {
                                if (lstCurrentlyCrawlingUrls.Contains(vrUrlToCrawl))
                                {
                                    logMessage($"bypass url since already crawling: \t {vrUrlToCrawl}");
                                    continue;
                                }
                                lstCurrentlyCrawlingUrls.Add(vrUrlToCrawl);
                            }

                            logMessage($"starting crawling url: \t {vrUrlToCrawl}");

                            lock (UserLogs)
                            {
                                UserLogs.Insert(0, $"{DateTime.Now}starting crawling url: \t {vrUrlToCrawl}");
                            }


                            var vrStartedTask = Task.Factory.StartNew(() => { crawlPage(vrUrlToCrawl, irDepth, null, DateTime.MinValue); }).ContinueWith((pr) => { lock (lstCurrentlyCrawlingUrls){ lstCurrentlyCrawlingUrls.Remove(vrUrlToCrawl); logMessage($"removing url from list since task completed: \t {vrUrlToCrawl}"); } });
                            lstCrawlingTasks.Add(vrStartedTask);

                            if (lstCrawlingTasks.Count > _irNumberOfTotalConcurrentCrawling)
                                break;
                    }

                }
                blBeingProcessed= false;
            }
        }









        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string urlText = txtInputUrl.Text;
            string url = urlText;
            DownloadImages(url);
        }






        static void DownloadImages(string url)
        {
            HtmlWeb web = new HtmlWeb();
            HtmlDocument document = web.Load(url);

            var imageTags = document.DocumentNode.SelectNodes("//img");
            if (imageTags != null)
            {
                foreach (var imageTag in imageTags)
                {
                    string imageUrl = imageTag.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        if (!imageUrl.StartsWith("http"))
                        {
                            imageUrl = url + imageUrl;
                        }

                        string fileName = imageUrl.Substring(imageUrl.LastIndexOf("/") + 1);
                        DownloadImage(imageUrl, fileName);
                    }
                }
            }
        }




        static void DownloadImage(string url, string fileName)
        {
            string path = @"C:\Users\FIRAT\Desktop\Yazılım\C#\Images";
            string fullPath = Path.Combine(path, fileName);

            using (WebClient webClient = new WebClient())
            {
                webClient.DownloadFile(url, fullPath);
            }
        }




    }
}
