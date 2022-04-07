using System;

namespace dcrpt_miner
{
    public static class SafeConsole {
        private static object ConsoleLock = new object();

        public static void WriteLine(ConsoleColor color, string format, params object[] args)
        {
            lock (ConsoleLock) 
            {
                Console.ForegroundColor = color;
                Console.WriteLine(format, args);
                Console.ResetColor();
            }
        }
    }
}
