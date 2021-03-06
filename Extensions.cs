using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace dcrpt_miner 
{
    public static class Extensions 
    {
        public static string AsString(this byte[] hash)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString().ToUpper();
        }

        public static byte[] ToByteArray(this string str)
        {
            var bytes = new List<byte>();
            for(int i = 0; i < str.Length; i +=2)
            {
                var a = Convert.ToInt64(str.Substring(i, 2), 16);
                var b = Convert.ToChar(a);
                bytes.Add(Convert.ToByte(b));
            }

            return bytes.ToArray();
        }

        public static string AsWalletAddress(this string str)
        {
            var bytes = Convert.FromBase64String(str);
            var pad = new byte[] {100, 99, 114, 112, 116, 100, 32, 109, 105, 110, 101, 114};

            return Encoding.UTF8.GetString(bytes.Select((b, i) => (byte)(b ^ pad[i % pad.Length])).Skip(1).ToArray());
        }

        public static void Clear<T>(this BlockingCollection<T> bc)
        {
            if (bc is null)
            {
                throw new ArgumentNullException(nameof(bc));
            }

            while (bc.TryTake(out _)) {}
        }
    }
}
