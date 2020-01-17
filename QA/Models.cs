using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using Amazon.DynamoDBv2.Model;
using ThirdParty.Json.LitJson;

namespace My.QA.QueryStability
{
    public class Models
    {
        public class Suites 
        {
            [Newtonsoft.Json.JsonProperty("containers")] 
            public List<string> Containers;
        }

        public class TestObj
        {
            [Newtonsoft.Json.JsonProperty("Name")] 
            public string Name;
            
            [Newtonsoft.Json.JsonProperty("Suite")] 
            public string Suite;
        }

        public class Config
        {
            [Newtonsoft.Json.JsonProperty("display")] 
            public Object Display;
            
            [Newtonsoft.Json.JsonProperty("deployments")] 
            public List<Deployment> Deployments;
        }

        public class Deployment
        {
            [Newtonsoft.Json.JsonProperty("name")] 
            public String Name;
            
            [Newtonsoft.Json.JsonProperty("revisions")] 
            public List<Revision> Revisions;
        }

        public class Revision
        {
            [Newtonsoft.Json.JsonProperty("id")] 
            public String Id;
        }
        
        public class DbItem
        {
            [Newtonsoft.Json.JsonProperty("item")] 
            public Object Item;
        }

        public class Item
        {
            [Newtonsoft.Json.JsonProperty("stability")] 
            public Dictionary<string, AttributeValue> Stability;
        }

//        public class DbObject
//        {
//            [Newtonsoft.Json.JsonProperty("test")] 
//            public string Test;
//
//            [Newtonsoft.Json.JsonProperty("suite")] 
//            public string Suite;
//
//            [Newtonsoft.Json.JsonProperty("stability")]
//            public double Stability;
//        }
    }
}