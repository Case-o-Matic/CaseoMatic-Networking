using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Db.AWS
{
    public static class AWSServiceCredentials
    {
        private static AWSCredentials credentials;
        public static AWSCredentials Credentials
        {
            get { return credentials; }
            private set { credentials = value; }
        }

        public static void SetCredentials(AWSCredentials newCredentials)
        {
            credentials = newCredentials;
            // Check for validity?
        }
    }
}
