using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using OpenCL.NetCore;

namespace dcrpt_miner 
{
    public static class Extensions 
    {
        public static IServiceCollection AddConnectionProvider(this IServiceCollection services) {
            var configuration = services.BuildServiceProvider().GetService<IConfiguration>();
            var url = configuration.GetValue<string>("url");

            switch (url.Substring(0, url.IndexOf(':'))) {
                case "dcrpt":
                    services.AddSingleton<IConnectionProvider, DcrptConnectionProvider>();
                    break;
                case "shifu":
                    services.AddSingleton<IConnectionProvider, ShifuPoolConnectionProvider>();
                    break;


                default:
                    throw new Exception("Unknown protocol");
            }

            return services;
        }

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

        public static void ThrowIfError(this ErrorCode errorCode) {
            if (errorCode == ErrorCode.Success) {
                return;
            }
            
            throw new Exception(String.Format("OpenCL call returned error ({0})", errorCode));
        }
    }
}