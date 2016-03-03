using Amazon.DynamoDBv2;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Db.AWS
{
    public class DynamoDBClient
    {
        private readonly AmazonDynamoDBClient client;

        public DynamoDBClient()
        {
            client = new AmazonDynamoDBClient(AWSServiceCredentials.Credentials);
        }
    }
}
