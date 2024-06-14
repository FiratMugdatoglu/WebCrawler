using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Xml;
using HtmlAgilityPack;
using System.Security.Policy;
using System.Security.Cryptography;
using System.Data.Entity;
using System.Data.Entity.Migrations;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Data.Entity.Core.Objects;
using System.Reflection;
using System.Threading;
using System.Runtime.ConstrainedExecution;

namespace lecture_13
{
    public static class csHelperMethods
    {
        public static int irCrawledUrlCount = 0;
        public static int irDiscoveredUrlCount = 0;






        //Bu kod, "DBCrawling" adlı veritabanını  kullanarak "tblMainUrls" adlı tablonun içeriğini tamamen siler. İçindeki "using" bloku veritabanı bağlantısını yönetmek için Entity Framework kullanır ve veritabanındaki değişiklikleri kaydetmek için "SaveChanges" metodunu kullanmaz. "truncate table" SQL komutu tablo içeriğini tamamen siler, ancak tablo yapısını etkilemez.
        public static void clearDatabase()
        {
            using (var context = new DBCrawling())
            {
                var ctx = ((System.Data.Entity.Infrastructure.IObjectContextAdapter)context).ObjectContext;
                ctx.ExecuteStoreCommand("truncate table tblMainUrls");
            }
        }





        //Bu kod, "tblMainUrl" adlı tablo tabanlı "crawlingResults" adlı bir sınıf tanımlar. "crawlingResults" sınıfı, "tblMainUrl" sınıfından türer ve onun bazı özelliklerini içerir. Ayrıca, sınıfın içinde, "LastCrawlingDate", "IsCrawled", "CompressionPercent", "FetchTimeMS", "LinkDepthLevel", "PageTitle", "SourceCode", "Url", "UrlHash", "DiscoverDate", "ParentUrlHash" ve "CrawlTryCounter" gibi veritabanındaki alanları tanımlar ve bunların başlangıç değerlerini belirler. Ayrıca, "blcrawlSuccess" adlı bir bool değişken ve "lstDiscoveredLinks" adlı bir string listesi de tanımlanır.
        public class crawlingResults : tblMainUrl
        {

            public crawlingResults()
            {
                this.LastCrawlingDate = new DateTime(1900, 1, 1);
                this.IsCrawled = false;
                this.CompressionPercent= 0;
                this.FetchTimeMS= 0;
                this.LinkDepthLevel = 0;
                this.PageTile = null;
                this.SourceCode= null;
                this.Url= "";
                this.UrlHash = "";
                this.DiscoverDate = DateTime.Now;
                this.ParentUrlHash = "";
                this.CrawlTryCounter = 0;

            }

            public bool blcrawlSuccess = true;
            public List<string> lstDiscoveredLinks = new List<string>();

        }







        //Kod, bir web sayfasını taramak için dört parametre alan crawlPage adlı bir yöntemi tanımlar. Bir crawlingResults nesnesi oluşturur ve iletilen parametrelere dayalı olarak bazı özellikleri ayarlar. Ardından, tarama süresini ölçmek için bir kronometre başlatır. Verilen URL'nin HTML içeriğini indirmek için HtmlAgilityPack'ten HtmlWeb nesnesini kullanır. İndirme başarılı olursa, HTML içeriğini depolar ve tarama sonuçlarını veritabanına kaydetmek için bir yöntem çağırır. Tarama başarılı olursa, HTML içeriğinden bağlantıları çıkarır ve bunları gelecekteki taramalar için veritabanına kaydeder. Son olarak, belleği boşaltır ve geri döner.

        public static void crawlPage(string srUrlToCrawl, int irUrlDepthLevel, string _srParentUrl, DateTime _dtDiscoverDate)
        {
            var vrLocalUrl = srUrlToCrawl;
            crawlingResults crawlResult = new crawlingResults();
            crawlResult.Url = vrLocalUrl;
            if(!string.IsNullOrEmpty(_srParentUrl) )
            crawlResult.ParentUrlHash = _srParentUrl;
            if(_dtDiscoverDate!=DateTime.MinValue)
            crawlResult.DiscoverDate = _dtDiscoverDate;

            Stopwatch swTimerCrawling = new Stopwatch();
            swTimerCrawling.Start();

            HtmlWeb wbClient = new HtmlWeb();//You should use httpwebrewuest for more control and better performance
            wbClient.AutoDetectEncoding = true;
            wbClient.BrowserTimeout = new TimeSpan(0,2,0);
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();

            try
            {
                doc = wbClient.Load(crawlResult.Url);
                crawlResult.SourceCode = doc.Text;
            }
            catch (Exception E)
            {
                crawlResult.blcrawlSuccess = false;
                logError(E, "crawlPage");

            }
            Interlocked.Increment(ref irCrawledUrlCount);  // değişkenin değerini artırır.  //Interlocked.Increment veri çakışmalarını  önlemek için kullanılır.

            Interlocked.Increment(ref irCrawledUrlCount);  // değişkenin değerini artırır.  //Interlocked.Increment veri çakışmalarını  önlemek için kullanılır.


            swTimerCrawling.Stop();
            crawlResult.FetchTimeMS = Convert.ToInt32(swTimerCrawling.ElapsedMilliseconds);
            crawlResult.LastCrawlingDate = DateTime.Now;

            saveCrawlInDatebase(crawlResult);

            if (crawlResult.blcrawlSuccess)
            {
                extractLinks(crawlResult, doc);
                
                
                
                saveDiscoveredLinksInDatabaseForFutureCrawling(crawlResult);
            }

            doc = null;

           

           
        }


        private static object _lockDatabaseAdd = new object();

       




        private static void saveDiscoveredLinksInDatabaseForFutureCrawling(crawlingResults crawlResult )
        {
            lock (_lockDatabaseAdd)
            {
                using (var context = new DBCrawling())
                {

                    HashSet<string> hsProcessedUrls = new HashSet<string>();//bir kümedeki öğeleri saklamak için kullanılan bir veri yapısıdır.
                  

                    foreach (var vrPerLink in crawlResult.lstDiscoveredLinks) 
                    {
                        var vrHashedLink = vrPerLink.ComputeHashOfOurSystem();
                        if (hsProcessedUrls.Contains(vrHashedLink)) //Her bağlantı için, ComputeHashOfOurSystem yöntemini kullanarak karmasını hesaplar ve hash'in HashSet'te zaten var olup olmadığını kontrol eder.
                            continue;
                      
                        var vrResult = context.tblMainUrls.Any(databaseRecord => databaseRecord.UrlHash == vrHashedLink);
                        if (vrResult == false )
                        {
                            crawlingResults newLinkCrawlingResult = new crawlingResults();
                            newLinkCrawlingResult.Url = vrPerLink.normalizeUrl();
                            newLinkCrawlingResult.HostUrl = newLinkCrawlingResult.Url.returnRootUrl();
                            newLinkCrawlingResult.UrlHash = vrPerLink.ComputeHashOfOurSystem();
                            newLinkCrawlingResult.ParentUrlHash = crawlResult.UrlHash;
                            newLinkCrawlingResult.LinkDepthLevel = (short)(crawlResult.LinkDepthLevel + 1);
                            context.tblMainUrls.Add(newLinkCrawlingResult.convertToBaseMainUrlClass());//convertToBaseMainUrlClass yöntemini kullanarak yeni nesneyi tblMainUrls tablosuna ekler.
                            hsProcessedUrls.Add(vrHashedLink);
                            Interlocked.Increment(ref irDiscoveredUrlCount); // Interlocked.Increment içine yazılanın değerini arttırır


                        }
                    }
                    context.SaveChanges();
                }
            }
        }

        //Bu bir veritabanına tarama sonuçlarını kaydetme fonksiyonudur. Bu fonksiyon, veritabanına kaydetmek için DBCrawling context nesnesini kullanır. Eğer tarama başarılı ise, tarama sonucu veritabanına kaydedilir. Eğer veritabanında zaten aynı URL'nin kaydı varsa, var olan kayıt güncellenir. Eğer tarama başarısız ise, tarama deneme sayacı bir artırılır. Bu fonksiyon veritabanına kaydedilen veriyi kilitler ve diğer tarama fonksiyonlarından etkilenmemesini sağlar.
        private static void saveCrawlInDatebase(crawlingResults crawledResult)
        {

            lock (_lockDatabaseAdd)
            {
                using (var context = new DBCrawling())
                {




                    crawledResult.UrlHash = crawledResult.Url.ComputeHashOfOurSystem();
                    crawledResult.HostUrl = crawledResult.Url.returnRootUrl();
                    var vrResult = context.tblMainUrls.SingleOrDefault(b => b.UrlHash == crawledResult.UrlHash);
                    crawledResult.ParentUrlHash = crawledResult.ParentUrlHash.ComputeHashOfOurSystem();

                    if (crawledResult.blcrawlSuccess == true)
                    {
                        crawledResult.IsCrawled = true;

                        if(!string.IsNullOrEmpty(crawledResult.SourceCode))
                        {

                       


                        double dblOriginalSourceCodeLenght = crawledResult.SourceCode.Length;
                        crawledResult.SourceCode = crawledResult.SourceCode.CompressString();
                        crawledResult.CompressionPercent = Convert.ToByte(Math.Floor(((crawledResult.SourceCode.Length.ToDouble() / dblOriginalSourceCodeLenght) * 100)));
                        }
                        crawledResult.CrawlTryCounter = 0;
                    }



                    tblMainUrl finalObject = crawledResult.convertToBaseMainUrlClass();
                    //this approach brings extra overhead to the server with deleting from server first
                    //therefore will use copy properties of object to another object wthout changing reference
                    //if (vrResult != null)
                    //{
                    //    context.tblMainUrls.Remove(vrResult);
                    //    context.SaveChanges();
                    //}

                  


                    if (vrResult != null)
                    {
                        finalObject.DiscoverDate = vrResult.DiscoverDate;
                        finalObject.LinkDepthLevel = vrResult.LinkDepthLevel;
                        finalObject.CrawlTryCounter=vrResult.CrawlTryCounter;
                        if (crawledResult.blcrawlSuccess == false)
                            finalObject.CrawlTryCounter++;
                        finalObject.CopyProperties(vrResult);
                    }
                    else
                        context.tblMainUrls.Add(finalObject);







                    var gg = context.SaveChanges();


                }
                }
            }
        //Bu metod, tblMainUrl tipindeki bir nesnenin JSON string'ine serileştirilmesini ve sonra tekrar tblMainUrl tipinde bir nesne olarak deserileştirilmesini sağlar. Bu, orijinal nesnenin farklı bir referansa sahip bir kopyasını oluşturur. Bu metodun amacı, orijinal nesnenin bir başka, ayıklanmış örneğini oluşturmak 
        private static tblMainUrl convertToBaseMainUrlClass(this tblMainUrl finalObject)
        {
             return JsonConvert.DeserializeObject<tblMainUrl>(JsonConvert.SerializeObject(finalObject));
        }




        //Bu metod, kaynak ve hedef nesneler arasındaki özellikleri eşler. Eğer kaynak veya hedef nesne null ise, bir hata fırlatılır. Kaynak ve hedef nesne türleri alınır ve geçerli özellikler toplanır. Daha sonra, eşleşen özellikler hedef nesneye kopyalanır.
        public static void CopyProperties(this object source, object destination)
        {
            // If any this null throw an exception
            if (source == null || destination == null)
                throw new Exception("Source or/and Destination Objects are null");
            // Getting the Types of the objects
            Type typeDest = destination.GetType();
            Type typeSrc = source.GetType();
            // Collect all the valid properties to map
            var results = from srcProp in typeSrc.GetProperties()
                          let targetProperty = typeDest.GetProperty(srcProp.Name)
                          where srcProp.CanRead
                          && targetProperty != null
                          && (targetProperty.GetSetMethod(true) != null && !targetProperty.GetSetMethod(true).IsPrivate)
                          && (targetProperty.GetSetMethod().Attributes & MethodAttributes.Static) == 0
                          && targetProperty.PropertyType.IsAssignableFrom(srcProp.PropertyType)
                          select new { sourceProperty = srcProp, targetProperty = targetProperty };
            //map the properties
            foreach (var props in results)
            {
                props.targetProperty.SetValue(destination, props.sourceProperty.GetValue(source, null), null);
            }
        }




        //Metod, dizi girdisi olarak bir dize alır ve URL dizesinin çözülmüş sürümünü döndürür.
        private static string decodeUrl(this string srUrl)
        {
            return HtmlEntity.DeEntitize(srUrl);//HtmlEntity.DeEntitize() yöntemi, giriş dizesindeki tüm HTML varlıklarını karşılık gelen karakterlerle değiştirir.
        }




        //Bu metod, verilen "myCrawlingResult" nesnesine ait URL'den belirtilen HTML belgesindeki bağlantıları çıkarır. Bağlantılar, "baseUri" değişkenine dayanarak absolüt URL'lere dönüştürülür ve sonuç olarak "lstDiscoveredLinks" adlı bir listeye eklenir. Aynı zamanda, belge başlığı da "vrDocTitle" değişkenine atanır ve son olarak "PageTitle" özelliğine atanır.
        private static void extractLinks(crawlingResults myCrawlingResult, HtmlDocument doc)
        {


            var baseUri = new Uri(myCrawlingResult.Url);//bu nesne ile URL'nin farklı parçalarına erişebilirsiniz.






            // extracting all links
            var vrNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (vrNodes != null)
                foreach (HtmlNode link in vrNodes)//xpath notation
                {
                    HtmlAttribute att = link.Attributes["href"];
                    //this is used to convert from relative path to absolute path
                    var absoluteUri = new Uri(baseUri, att.Value.ToString().decodeUrl());

                    if (!absoluteUri.ToString().StartsWith("http://") && !absoluteUri.ToString().StartsWith("https://"))
                        continue;

                    myCrawlingResult.lstDiscoveredLinks.Add(absoluteUri.ToString().Split('#').FirstOrDefault());
                }

            myCrawlingResult.lstDiscoveredLinks = myCrawlingResult.lstDiscoveredLinks.Distinct().Where(pr=>pr.Length<201).ToList();

            var vrDocTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.ToString().Trim();
            vrDocTitle = System.Net.WebUtility.HtmlDecode(vrDocTitle);

            myCrawlingResult.PageTile = vrDocTitle;
            
           
           
        }








        //Bu kod bir dosya ismi "error_logs.txt" olan bir StreamWriter nesnesi oluşturur. Nesne, verileri UTF8 kodlamasıyla dosyaya yazmak için kullanılır ve dosyaya ekleme yapması için "append" parametresi true olarak ayarlanır.
        private static StreamWriter swErrorLogs = new StreamWriter("error_logs.txt", append: true, encoding: Encoding.UTF8);

        
        private static object _lock_swErrorLogs = new object();




        static csHelperMethods()
        {
            swErrorLogs.AutoFlush = true;//her yapılan yazma işleminde dosyaya  yazılan veriler anında kaydedilir
            swLog.AutoFlush= true;
        }


        public static void logError(Exception E, string callingMethodName)
        {
            lock (_lock_swErrorLogs)//ı am using lock methodoloy to synchornize access to a non-thread safe object streamwriter
            {
                swErrorLogs.WriteLine("error happened in:" + callingMethodName + "\t" + DateTime.Now);
                swErrorLogs.WriteLine();
                swErrorLogs.WriteLine(E.Message);
                swErrorLogs.WriteLine(E?.InnerException?.Message);
                swErrorLogs.WriteLine();
                swErrorLogs.WriteLine(E?.StackTrace);
                swErrorLogs.WriteLine();
                swErrorLogs.WriteLine(E?.InnerException?.StackTrace);
                swErrorLogs.WriteLine();
                swErrorLogs.WriteLine("*****************************");
                swErrorLogs.WriteLine();
            }
        }

        //Bu fonksiyon, verilen URL dizesini "en-US" kültürüne göre küçük harf olarak düzenler ve başındaki ve sonundaki boşlukları siler.
        public static string normalizeUrl(this string srUrl)
        {
            return srUrl.ToLower(new System.Globalization.CultureInfo("en-US")).Trim();
        }


        //Bu bir .NET metodu ve SHA256 şifreleme algoritması kullanarak bir verinin hash değerini hesaplamayı amaçlar. Metod, verilen ham veriyi (rawData) UTF8 kodlamasıyla byte dizisine dönüştürür ve daha sonra SHA256 hash fonksiyonu kullanarak verinin hash değerini hesaplar. Hesaplanan hash değerinin byte dizisi, tek tek hexadecimal şekline dönüştürülür ve son olarak string bir değer olarak döndürülür.
        private static string ComputeSha256Hash(this string rawData)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }



        //Bu fonksiyon, verilen URL'yi normalize eder (büyük küçük harf farkını ortadan kaldırarak) ve sonuçta SHA256 hash'i hesaplar. Bu hash, sistemimiz tarafından verilen URL'nin benzersiz bir kimliğini temsil eder.
        static string ComputeHashOfOurSystem(this string srUrl)
        {
            return srUrl.normalizeUrl().ComputeSha256Hash();//SHA-256, güvenliği artırmak amacıyla kullanılan bir şifresel hash fonksiyonudur.
        }


        //Bu fonksiyon verilen URL'nin kök URL'sini döndürür. Örneğin, "https://www.example.com/pages/about" URL'si için döndürülecek olan "www.example.com" olacaktır
        public static string returnRootUrl(this string srUrl)
        {
            var uri = new Uri(srUrl);
            return uri.Host;
        }

        //Bu kod, dosya adı "logs.txt" olan bir StreamWriter nesnesi oluşturur. StreamWriter, veriyi yazmak için kullanılan bir dosya çıkışı nesnesidir. "true" değeri ile birlikte "append" parametresi, yazılan verinin dosyanın sonunda eklenmesini sağlar. "Encoding.UTF8" değeri, verinin UTF-8 kodlaması ile yazılmasını garantiler.
        private static StreamWriter swLog = new StreamWriter("logs.txt", true,Encoding.UTF8);


        private static object _lock_swLogs = new object();



        //Bu yöntem, verilen mesajı veritabanına yazmak veya log dosyasına yazmak için kullanılan bir yöntemdir. Mesaj, belirtilen zaman ve tarihle birlikte dosyaya yazılır ve bir veri kilidi kullanılarak diğer işlemlerin aynı anda yapılmaması sağlanır.
        public static void logMessage(string srMsg)
        {
            lock (_lock_swLogs)
            {
                swLog.WriteLine($"{DateTime.Now}\t\t{srMsg}");
            }
        }



    }
}
