using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.KeyManagementService.Model.Internal.MarshallTransformations;
using Amazon.S3;
using Amazon.S3.Model;
using LambdaSharp;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace My.QA.QueryStability {
    
    public class DbObject
    {
        [Newtonsoft.Json.JsonProperty("test")] 
        public string Test;

        [Newtonsoft.Json.JsonProperty("suite")] 
        public string Suite;

        [Newtonsoft.Json.JsonProperty("stability")]
        public double Stability;
    }

    public class FormattedObj
    {
        [Newtonsoft.Json.JsonProperty("title")] 
        public string Title;
        
        [Newtonsoft.Json.JsonProperty("fullTitle")] 
        public string FullTitle;
        
        [Newtonsoft.Json.JsonProperty("timedOut")] 
        public string TimedOut;
        
        [Newtonsoft.Json.JsonProperty("duration")] 
        public string Duration;
        
        [Newtonsoft.Json.JsonProperty("state")] 
        public double State;
        
        [Newtonsoft.Json.JsonProperty("speed")] 
        public string Speed;
      
        [Newtonsoft.Json.JsonProperty("pass")] 
        public Boolean Pass;
        
        [Newtonsoft.Json.JsonProperty("fail")] 
        public Boolean Fail;
        
        [Newtonsoft.Json.JsonProperty("pending")] 
        public Boolean Pending;
        
        [Newtonsoft.Json.JsonProperty("code")] 
        public string Code;
    }

    public class FinalObj
    {
        [Newtonsoft.Json.JsonProperty("stats")] 
        public object Stats;
        
        [Newtonsoft.Json.JsonProperty("allFailures")] 
        public List<FormattedObj> AllFailures;
    }

    public class Stats
    {
        [Newtonsoft.Json.JsonProperty("suites")] 
        public int Suites;
        
        [Newtonsoft.Json.JsonProperty("tests")] 
        public int Tests;
        
        [Newtonsoft.Json.JsonProperty("passes")] 
        public int Passes;
        
        [Newtonsoft.Json.JsonProperty("pending")] 
        public int Pending;
        
        [Newtonsoft.Json.JsonProperty("failures")] 
        public int Failures;
        
        [Newtonsoft.Json.JsonProperty("skipped")] 
        public int Skipped;
    }

    public class Function : ALambdaFunction<DynamoDBEvent, string>
    {

        //--- Fields ---
        private IAmazonDynamoDB _dynamoDbClient;
        private IAmazonS3 _s3Client;
        private string _bucket { get; } = "mindtouch-qa-test-2";
//        private string _bucket2 { get; } = "alexa-qa-stability-my-qa-alexatemporaryqabucket-mm3v7awzf5tp";
        private string _tableName { get; } = "sentinelStage";
        private string _suitesPath { get; } = "vital-components/containers.json";
        private string[] _ignore { get; } = new string[] {"idf3-release", "mt4-release", "responsive-release",
        "modular", "mt4-monitoring", "responsive-monitoring",
        "mt4-ss", "responsive-ss", "mt4-ss-functional", "responsive-ss-functional",
        "responsive-kcs", "responsive-looker", "responsive-performance", "responsive-simple-titles"};

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // Initialize AWS clients
            _dynamoDbClient = new AmazonDynamoDBClient();
            _s3Client = new AmazonS3Client();
        }

        public override async Task<string> ProcessMessageAsync(DynamoDBEvent evt)
        {

            LogInfo("Running Function");

            // TO-DO: add business logic
            var gitR = Environment.GetEnvironmentVariable("GIT_REVISION");

            var suites = await GetSuites();
            Console.WriteLine("SUITES: " + suites);
            var testNamesAll = await GetTestNames(suites);
            Console.WriteLine("TEST OBJ ALL: " + testNamesAll);
            var dbObjectAll = await GetDBObjects(testNamesAll, gitR);
            Console.WriteLine("DB OBJ ALL: " + dbObjectAll);
            var dbObjectSorted = SortByStability(dbObjectAll);
            var desired = GrabDesired(dbObjectSorted, Convert.ToInt16(Environment.GetEnvironmentVariable("DESIRED_COUNT")));
            Console.WriteLine("DESIRED: " + JsonConvert.SerializeObject(desired));
            var finalObj = FormatSentinel(desired);
            Console.WriteLine("FINAL OBJECT: " + JsonConvert.SerializeObject(finalObj.Stats));

            string path = $"generated-reports/analyzed-runs/full-report/responsive/responsive-unstable/{gitR}.json";
            await UploadObject(path, JsonConvert.SerializeObject(finalObj));
            Console.WriteLine("TEST AFTER UPLOAD OBJECT");
            return "Success";
    }

        public async Task<string> GetObject(string key) 
        {
            try
            {
                // Create GetObject request
                var request = new Amazon.S3.Model.GetObjectRequest
                {
                    BucketName = _bucket,
                    Key = key
                };
                
                // Issue GetObject request
                using (GetObjectResponse response = await _s3Client.GetObjectAsync(request))

                // View GetObject response
                using (Stream responseStream = response.ResponseStream)
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch(Exception e) 
            {
                Console.WriteLine($"Error getting object {key} from bucket {_bucket}. Make sure it exists and your bucket is in the same region as this function.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }

        public async Task UploadObject(string key, string body)
        {
            Console.WriteLine("SERIALIZED BODY TO UPLOAD: " + body);
            Console.WriteLine("BUCKET NAME: " + _bucket);
            try
            {
                // Create UploadObject request
                var request = new Amazon.S3.Model.PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = key,
                    ContentBody = body,
//                    ContentType = "application/json"
                };
                Console.WriteLine("REQUEST: " + request);
                Console.WriteLine("REQUEST: " + JsonConvert.SerializeObject(request));
                // Issue UploadObject request
                await _s3Client.PutObjectAsync(request);
                
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error putting object {key} into bucket {_bucket}. Make sure it exist and your bucket is in the same region as this function.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }

        public async Task<string> GetItem(string testName, string container, string gitRevision)
        {
            try
            {
                // Define item key
//                Dictionary<string, AttributeValue> key = new Dictionary<string, AttributeValue>
//                {
//                    {"testNameVersion", new AttributeValue {S = $"{testName}::{container}"}},
//                    {"gitRevision", new AttributeValue {S = gitRevision}}
//                };
//
//                // Create GetItem request
//                GetItemRequest request = new GetItemRequest
//                {
//                    TableName = _tableName,
//                    Key = key
//                };
//                Console.WriteLine("REQUEST: " + JsonConvert.SerializeObject(request));

                // Issue GetItem request
//                var response = await _dynamoDbClient.GetItemAsync(request);

                var response = await _dynamoDbClient.GetItemAsync(_tableName, new Dictionary<string, AttributeValue>
                {
                    {"testNameVersion", new AttributeValue {S = $"{testName}::{container}"}},
                    {"gitRevision", new AttributeValue {S = gitRevision}}
                });

                // View GetItem response
                Dictionary<string, AttributeValue> item = response.Item;
                var stability = "1";
                foreach (var keyValuePair in item)
                {
                    if (keyValuePair.Key == "stability")
                    {
                        stability = keyValuePair.Value.N;
                    }
//                    Console.WriteLine("THIS IS A LOG: {0} : S={1}, N={2}, SS=[{3}], NS=[{4}]",
//                        keyValuePair.Key,
//                        keyValuePair.Value.S,
//                        keyValuePair.Value.N,
//                        string.Join(", ", keyValuePair.Value.SS ?? new List<string>()),
//                        string.Join(", ", keyValuePair.Value.NS ?? new List<string>()));
                }
               
                return stability;
            }
            catch
            {
                throw;
            }
        }

        public async Task<List<string>> GetSuites()
        {
            try
            {
//                var test = await GetObject(_suitesPath);
//                Console.Write(test);
                var suitesJson = JsonConvert.DeserializeObject<Models.Suites>(await GetObject(_suitesPath));
                var suites = suitesJson.Containers;
                return suites;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting object from {_bucket}/{_suitesPath}. Make sure it exists and your bucket is in the same region as this function.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }

        public async Task<List<object>> GetTestNames(List<string> suites)
        {
            try
            {
                var testNamesAll = new List<object>();
                foreach (var suite in suites)
                {
                    if (((IList) _ignore).Contains(suite))
                    {
                        continue;
                    }

                    var testNamesPath = $"test-names/{suite}.json";
                    List<string> testNames = new List<string>();
                    try
                    {
                        testNames = JsonConvert.DeserializeObject<List<string>>(await GetObject(testNamesPath));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error getting object from {_bucket}/{testNamesPath}. Make sure it exists and your bucket is in the same region as this function.");
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                        throw;
                    }

                    foreach (var testName in testNames)
                    {
                        Dictionary<string, string> testObj = new Dictionary<string, string>
                        {
                            {"name", testName},
                            {"suite", suite}
                        };
                        testNamesAll.Add(testObj);
                    }
                }

                return testNamesAll;
            }
            catch (Exception e)
            {
                Console.WriteLine("Error retrieving test names.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }

//        public async Task<String> GetLatestRevision()
//        {
//            try
//            {
//                var configPath = "generated-reports/configurations/responsive-navbar-configuration.json";
//                var configJson = JsonConvert.DeserializeObject<Models.Config>(await GetObject(configPath));
//                var latestDeployment = configJson.Deployments[configJson.Deployments.Count - 1];
//                var latestRevision = latestDeployment.Revisions[latestDeployment.Revisions.Count - 1];
//                return latestRevision.Id;
//            }
//            catch (Exception e)
//            {
//                Console.WriteLine("Error retrieving latest revision.");
//                Console.WriteLine(e.Message);
//                Console.WriteLine(e.StackTrace);
//                throw;
//            }
//            
//        }

        public async Task<string> FormatSuite(string suite)
        {
            try
            {
                if (suite.Contains("-"))
                {
                    string[] arr = suite.Split("-");
                    var version = arr[0].ToUpper().ToCharArray()[0] + arr[0].Substring(1);
                    var specific = arr[1];
                    for (int i = 1; i < arr.Length; i++)
                    {
                        if (i != 1)
                        {
                            specific = specific + "-" + arr[i];
                        }
                    }

                    var append = "";
                    if (arr.Length > 1)
                    {
                        append = " (" + specific + ")";
                    }

                    return "MindTouch " + version + append;
                }
                else
                {
                    return "MindTouch " + suite;
                }
                
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error formatting suite {suite}.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }

        public async Task<List<DbObject>> GetDBObjects(List<object> testNamesAll, string gitRevision)
        {
            List<DbObject> dbObjectAll = new List<DbObject>();
            foreach (Dictionary<string, string> testObj in testNamesAll)
            {
                string testName = "";
                string suite = "";
                foreach (KeyValuePair<string, string> kvp in testObj)
                {
                    if (kvp.Key == "name")
                    {
                        testName = kvp.Value;
                    }
                    else if(kvp.Key == "suite")
                    {
                        suite = kvp.Value;
                    }
                }
                string suiteFormatted = await FormatSuite(suite);
                double stability = 1;
                if (suiteFormatted == "MindTouch Responsive (pages)")
                {
                    var response = await GetItem(testName, suiteFormatted, gitRevision);
                    stability = Convert.ToDouble(response);
                }

                dbObjectAll.Add(new DbObject
                {
                    Test = testName,
                    Suite = suiteFormatted,
                    Stability = stability
                });
                
//                foreach(DbObject dbObj in dbObjectAll)
//                {
//                    Console.WriteLine("TEST: " + dbObj.Test);
//                    Console.WriteLine("SUITE: " + dbObj.Suite);
//                    Console.WriteLine("STABILITY: " + dbObj.Stability);
//                }

//                    var serialized = JsonConvert.SerializeObject(dbItem);
//                    var deserialized = JsonConvert.DeserializeObject<Models.DbItem>(serialized);
//                    var test = JsonConvert.SerializeObject(deserialized.Items);
//                    var stability = JsonConvert.DeserializeObject<Models.Item>(test);
//                    
//                    Dictionary<string, AttributeValue> item = stability.N;
//                    
//                    Console.WriteLine("STABILITY: " + stability);

//                    Type type = dbItem.GetType();
//                    Console.WriteLine("TYPE: " + type);
//
//                    int attributeCount = 0;
//                    foreach (PropertyInfo property in type.GetProperties())
//                    {
//                        Console.WriteLine("PROPERTY: " + property);
//                        var customAttributes = property.GetCustomAttributes();
//                        Console.WriteLine("CUSTOM ATTRIBUTES: " + customAttributes);
//                        foreach(Attribute attr in customAttributes)
//                        {
//                            Console.WriteLine("CUSTOM ATTRIBUTE: " + attr);
//                        }
//                        attributeCount += property.GetCustomAttributes(false).Length;
//                        Console.WriteLine("ATTRIBUTE COUNT: " + attributeCount);
//                    }
//                }

//                if (attributeCount >= 1)
//                {
//                    var dbStability = serialized;
//                }
            }

            return dbObjectAll;
        }

        public List<DbObject> SortByStability(List<DbObject> dbObjectAll)
        {
            List<DbObject> dbObjectSorted = dbObjectAll; 
            dbObjectSorted.Sort(
                delegate(DbObject p1, DbObject p2) { return p1.Stability.CompareTo(p2.Stability); });
//            foreach(DbObject dbObj in dbObjectSorted){
//                Console.Write("TEST: " + dbObj.Test);
//                Console.WriteLine("STABILITY: " + dbObj.Stability);
//            }
            return dbObjectSorted;
        }
        
//        public List<DbObject> SortByStability(List<DbObject> arr)
//        {
//            var sorted = new List<DbObject>();
//            Console.WriteLine("INSIDE SORT BY STABILITY");
//            Console.WriteLine("STABILITY COUNT: " + arr.Count);
//            sorted = MergeSort(arr, 0, arr.Count - 1);
//            // Console.WriteLine("Sorted Values:");
//            // foreach(dbObject dbObj in sorted){
//            //     Console.Write("TEST: " + dbObj.Test);
//            //     Console.WriteLine("STABILITY: " + dbObj.Stability);
//            // }
//            // Console.WriteLine("SORTBYSTABILITY: " + sorted.Count);
//            return sorted;
//        }
        
//        private List<DbObject> Merge(List<DbObject> input, int left, int middle, int right)
//        {
//            double[] leftArrayStab = new double[middle - left + 1];
//            string[] leftArrayTest = new string[middle - left + 1];
//            string[] leftArraySuite = new string[middle - left + 1];
//            double[] rightArrayStab = new double[right - middle];
//            string[] rightArrayTest = new string[right - middle];
//            string[] rightArraySuite = new string[right - middle];
//            double[] stabilities = new double[input.Count];
//            string[] testNames = new string[input.Count];
//            string[] suites = new string[input.Count];
//            for(int x = 0; x < input.Count; x++){
//                stabilities[x] = input[x].Stability;
//                testNames[x] = input[x].Test;
//                suites[x] = input[x].Suite;
//            } 
//            // left array
//            Array.Copy(stabilities, left, leftArrayStab, 0, middle - left + 1);
//            Array.Copy(testNames, left, leftArrayTest, 0, middle - left + 1);
//            Array.Copy(suites, left, leftArraySuite, 0, middle - left + 1);
//            // right array
//            Array.Copy(stabilities, middle + 1, rightArrayStab, 0, right - middle);
//            Array.Copy(testNames, middle + 1, rightArrayTest, 0, right - middle);
//            Array.Copy(suites, middle + 1, rightArraySuite, 0, right - middle);
//            int i = 0;
//            int j = 0;
//            for (int k = left; k < right + 1; k++)
//            {
//                if (i == leftArrayStab.Length)
//                {
//                    stabilities[k] = rightArrayStab[j];
//                    testNames[k] = rightArrayTest[j];
//                    suites[k] = rightArraySuite[j];
//                    j++;
//                }
//                else if (j == rightArrayStab.Length)
//                {
//                    stabilities[k] = leftArrayStab[i];
//                    testNames[k] = leftArrayTest[i];
//                    suites[k] = leftArraySuite[i];
//                    i++;
//                }
//                else if (leftArrayStab[i] <= rightArrayStab[j])
//                {
//                    stabilities[k] = leftArrayStab[i];
//                    testNames[k] = leftArrayTest[i];
//                    suites[k] = leftArraySuite[i];
//                    i++;
//                }
//                else
//                {
//                    stabilities[k] = rightArrayStab[j];
//                    testNames[k] = rightArrayTest[j];
//                    suites[k] = rightArraySuite[j];
//                    j++;
//                }
//            }
//            var dbObjectSorted = new List<DbObject>();
//            // Console.WriteLine("STABILITIES LENGTH: " + stabilities.Length);
//            for (int a = 0; a < stabilities.Length; a++){
//                dbObjectSorted.Add(new DbObject { Test = testNames[a], Suite = suites[a], Stability = stabilities[a] });
//            }
//            return dbObjectSorted;
//        }
//        
//        private List<DbObject> MergeSort(List<DbObject> input, int left, int right)
//        {
//        
//            var output = new List<DbObject>();
//            if (left < right)
//            {
//                int middle = (left + right) / 2;
//                MergeSort(input, left, middle);
//                MergeSort(input, middle + 1, right);
//                output = Merge(input, left, middle, right);
//                // Console.WriteLine("OUTPUT.COUNT" + output.Count);
//            }
//            return output;
//        }

        public List<DbObject> GrabDesired(List<DbObject> dbObjectSorted, int count)
        {
            List<DbObject> desired = new List<DbObject>();
            for(int i = 0; i < count; i++) {
                desired.Add(dbObjectSorted[i]);
            }
            return desired;
        }
        
        public FinalObj FormatSentinel(List<DbObject> desired)
        {
            var formatted = new List<FormattedObj>();
            foreach (DbObject dbObj in desired)
            {
                string stabilityShortened = "";
                if (dbObj.Stability != 1 && dbObj.Stability != 0)
                {
                    stabilityShortened = Convert.ToString(dbObj.Stability).Substring(0, 3);
                }
                else
                {
                    stabilityShortened = Convert.ToString(dbObj.Stability);
                }
                
                formatted.Add(new FormattedObj
                {
                     Title = $"{stabilityShortened} | {dbObj.Test}::{dbObj.Suite}",
                     FullTitle = $"{stabilityShortened} | {dbObj.Test}::{dbObj.Suite}",
                     TimedOut = null,
                     Duration = null,
                     State = dbObj.Stability,
                     Speed = null,
                     Pass = false,
                     Fail = true,
                     Pending = false,
                     Code = null
                });
            }

            FinalObj finalObj = new FinalObj()
            {
                Stats = new Stats()
                {
                    Suites = 1,
                    Tests = formatted.Count,
                    Passes = 0,
                    Pending = 0,
                    Failures = formatted.Count,
                    Skipped = 0
                },
                AllFailures = formatted
            };
            Type t = finalObj.GetType(); // Where obj is object whose properties you need.
            PropertyInfo [] pi = t.GetProperties();
            foreach (PropertyInfo p in pi)
            {
                Console.WriteLine(p.Name + " : " + p.GetType());
            }
            return finalObj;
        }
    }
}















































