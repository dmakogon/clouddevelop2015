using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using System;
using System.Linq;
using System.Configuration;

namespace clouddevelopquery
{
    class Program
    {
        static readonly string endpoint = ConfigurationManager.AppSettings["endpointUrl"];
        static readonly string authKey = ConfigurationManager.AppSettings["authKey"];
        static readonly string databaseId = ConfigurationManager.AppSettings["databaseId"];
        static readonly string collectionId = ConfigurationManager.AppSettings["collectionId"];

        static void Main(string[] args)
        {
            var query = "SELECT * FROM c";

            using (var client = new DocumentClient(new Uri(endpoint), authKey))
            {
                var collLink = UriFactory.CreateDocumentCollectionUri(databaseId, collectionId);
                var querySpec = new SqlQuerySpec { QueryText = query };

                var itr = client.CreateDocumentQuery(collLink, querySpec).AsDocumentQuery();
                var response = itr.ExecuteNextAsync<Document>().Result;
                var charge = response.RequestCharge;
                Console.WriteLine("Request charge: {0}", charge);

                foreach (var doc in response.AsEnumerable())
                {
                    Console.WriteLine(doc.ToString());
                }
            }

            Console.ReadLine();
        }
    }
}
