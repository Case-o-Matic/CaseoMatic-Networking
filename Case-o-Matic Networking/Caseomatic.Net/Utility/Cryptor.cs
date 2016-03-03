using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Caseomatic.Net.Utility
{
    public static class Cryptor
    {
        public static RSAParameters? rsaParams;

        public static byte[] Encrypt(byte[] bytes, bool isOAEP)
        {
            if (rsaParams == null)
                return null;

            try
            {
                byte[] encryptedData;
                using (var rsa = new RSACryptoServiceProvider())
                {
                    //Import the RSA Key information. This only needs
                    //to include the public key information.
                    rsa.ImportParameters(rsaParams.Value);

                    //Encrypt the passed byte array and specify OAEP padding.
                    encryptedData = rsa.Encrypt(bytes, isOAEP);
                }
                return encryptedData;
            }
            catch (CryptographicException e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }

        public static byte[] Decrypt(byte[] encrypedBytes, bool isOAEP)
        {
            if (rsaParams == null)
                return null;

            try
            {
                byte[] decryptedData;
                using (RSACryptoServiceProvider RSA = new RSACryptoServiceProvider())
                {
                    //Import the RSA Key information. This needs
                    //to include the private key information.
                    RSA.ImportParameters(rsaParams.Value);

                    //Decrypt the passed byte array and specify OAEP padding.
                    decryptedData = RSA.Decrypt(encrypedBytes, isOAEP);
                }
                return decryptedData;
            }
            catch (CryptographicException e)
            {
                Console.WriteLine(e.ToString());
                return null;
            }
        }
    }

    [Serializable]
    public struct RSASerializableParams
    {
        public readonly byte[] D;
        public readonly byte[] DP;
        public readonly byte[] DQ;
        public readonly byte[] Exponent;
        public readonly byte[] InverseQ;
        public readonly byte[] Modulus;
        public readonly byte[] P;
        public readonly byte[] Q;

        public RSASerializableParams(RSAParameters rsaParams)
        {
            D = rsaParams.D;
            DP = rsaParams.DP;
            DQ = rsaParams.DQ;
            Exponent = rsaParams.Exponent;
            InverseQ = rsaParams.InverseQ;
            Modulus = rsaParams.Modulus;
            P = rsaParams.P;
            Q = rsaParams.Q;
        }

        public static implicit operator RSAParameters(RSASerializableParams rsaParams)
        {
            return new RSAParameters()
            {
                D = rsaParams.D,
                DP = rsaParams.DP,
                DQ = rsaParams.DQ,
                Exponent = rsaParams.Exponent,
                InverseQ = rsaParams.InverseQ,
                Modulus = rsaParams.Modulus,
                P = rsaParams.P,
                Q = rsaParams.Q
            };
        }
        public static implicit operator RSASerializableParams(RSAParameters rsaParams)
        {
            return new RSASerializableParams(rsaParams);
        }
    }
}
